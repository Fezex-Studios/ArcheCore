# Building a Feature End-to-End: Client ↔ WorldServer ↔ PersistenceServer

The other docs describe each layer in isolation. This one is the missing
piece: a complete trace of **every file you'd touch and every packet
that'd fly**, in both directions, for a real feature — from a
UI button press on the client, through the WorldServer, down to the
PersistenceServer's database, and all the way back. The running example is
a **shop system** (buy an item from an NPC vendor), since that's a natural
next step on top of the interaction system that already exists, and it
needs all three processes and both opcode systems to do anything real.

Read [02-NETWORKING-AND-PACKETS.md](02-NETWORKING-AND-PACKETS.md) and
`ADDING_PACKETSv2.md` first if you haven't — this doc assumes you know
what a C2W/W2C/W2P/P2W packet is and just walks the *whole* round trip for
one feature instead of one packet at a time.

## The two directions this feature needs

1. **Client → WorldServer → back to Client** (fast path, no persistence
   involved): asking a vendor NPC what it sells, and getting a denial if
   you're out of range or can't afford something.
2. **Client → WorldServer → PersistenceServer → WorldServer → Client**
   (slow path): the actual purchase, which has to durably deduct currency
   and grant an item — data that must survive a disconnect, so it can't
   live only in server memory.

## Direction 1: "what does this vendor sell" (no persistence needed)

This reuses the existing interaction system wholesale — a shop is just a
new `IInteractable` kind whose `OnInteract` hook sends a new packet instead
of dialogue text.

### 1. Opcode + packet class (shared, `ArcheCore.Network`)

```csharp
// Shared/Opcodes.cs — append, never renumber existing values
ChatMessage    = 22,
W2CShopOpen    = 23,   // world server -> client: here's what this vendor sells
C2WShopPurchase = 24,  // client -> world server: buy this item
W2CShopResult  = 25,   // world server -> client: purchase succeeded/failed
```

```csharp
// Shared/Packets/W2C/W2CShopOpenPacket.cs
[MessagePackObject(true)]
public class W2CShopOpenPacket
{
    public int VendorNetworkId { get; set; }
    public ShopEntry[] Items { get; set; }
}

[MessagePackObject(true)]
public class ShopEntry
{
    public int ItemId { get; set; }
    public string Name { get; set; }
    public int Price { get; set; }
}
```

### 2. New `InteractableKind` + entity (server, `ArcheCore.Server.World`)

```csharp
// Core/Interaction/IInteractable.cs
public enum InteractableKind
{
    Npc = 1,
    Vendor = 2,   // new
}
```

A `VendorEntity` mirrors `NpcEntity` — implements `IInteractable`, gets
registered/unregistered with `InteractionRegistry` on spawn/despawn exactly
like an NPC does (see
[05-GAMEPLAY-SYSTEMS.md](05-GAMEPLAY-SYSTEMS.md#the-interaction-system)).
Notice `C2WInteractHandler` needs **zero changes** for this — that's the
entire reason `IInteractable` exists as an interface instead of a type
check.

### 3. Lua hook reacts to the new kind

```lua
-- shop_vendor_example.lua
-- PlayerEvent.OnInteract = 6, targetKind now includes 2 = Vendor
Server:RegisterPlayerEvent(6, function(player, targetTemplateId, targetKind)
    if targetKind == 2 then
        player:OpenShop(targetTemplateId)   -- new LuaPlayer method, see below
    end
end)
```

```csharp
// Core/Lua/Bindings/LuaPlayer.cs — new method, same pattern as SendDialogue
public void OpenShop(int vendorTemplateId)
{
    var items = ShopCatalog.GetItemsForVendor(vendorTemplateId);  // wherever your shop data lives
    W2CShopOpenPacketSender.Send(peer, vendorTemplateId, items);
}
```

### 4. Sender + client handler (the return trip)

```csharp
// Core/Packets/W2CSenders/W2CShopOpenPacketSender.cs — server side, mirrors W2CInteractDialoguePacketSender
public static class W2CShopOpenPacketSender
{
    public static void Send(NetPeer peer, int vendorId, ShopEntry[] items) =>
        WorldserverPacketSender.SendPacket(peer, Opcodes.W2CShopOpen,
            new W2CShopOpenPacket { VendorNetworkId = vendorId, Items = items });
}
```

```csharp
// Assets/ArcheCore.Client/Networking/W2CHandlers/W2CShopOpenHandler.cs — client side
public class W2CShopOpenHandler : IClientPacketHandler
{
    public void Handle(NetPacketReader reader)
    {
        var packet = MessagePackSerializer.Deserialize<W2CShopOpenPacket>(reader.GetRemainingBytes());
        ShopUI.Instance.Show(packet.VendorNetworkId, packet.Items);   // your new UI class
    }
}
```

```csharp
// ClientNetwork.RegisterHandlers() — REMEMBER THIS LINE
// (see 02-NETWORKING-AND-PACKETS.md: client dispatch is manual today,
// there's no attribute-based auto-registration on this side yet)
dispatcher.Register(Opcodes.W2CShopOpen, new W2CShopOpenHandler());
```

That last line is the one step that's easy to forget precisely *because*
the server-side equivalent (`[PacketOpcode(...)]` + `AutoRegister`) doesn't
need it — see
[08-CLIENT-ARCHITECTURE.md](08-CLIENT-ARCHITECTURE.md#the-dispatch-gotcha)
and
[09-KNOWN-GAPS-AND-NEXT-STEPS.md](09-KNOWN-GAPS-AND-NEXT-STEPS.md) for why
this asymmetry exists and how to close it.

At this point, direction 1 is done: interact with a vendor, get a shop
window, no database touched at all — the catalog can live in server memory
or in `worldserver.db`'s existing `Items` table (`GameData/Items/ItemTable.cs`).

## Direction 2: the actual purchase (needs the PersistenceServer)

Buying something has to durably change two things that outlive the current
connection: the player's currency and their inventory/character row. That
means this packet's handler needs a full C2W → W2P → (Node DB write) →
P2W → W2C round trip, following the exact request/response + `TaskCompletionSource`
pattern `W2PCharacterSender` already establishes for character load/create.

### 1. Client sends the purchase request

```csharp
// Assets/ArcheCore.Client/Networking/C2WSenders/C2WShopPurchasePacketSender.cs
public static class C2WShopPurchasePacketSender
{
    public static void Send(NetPeer peer, int vendorNetworkId, int itemId) =>
        ClientPacketSender.SendPacket(peer, Opcodes.C2WShopPurchase,
            new C2WShopPurchasePacket { VendorNetworkId = vendorNetworkId, ItemId = itemId },
            DeliveryMethod.ReliableOrdered);   // a purchase must not be silently dropped
}
```

### 2. WorldServer handler validates, then asks the PersistenceServer to do the transaction

```csharp
[PacketOpcode(Opcodes.C2WShopPurchase)]
public class C2WShopPurchaseHandler : IPacketHandler
{
    private readonly PlayerManager _playerManager;
    private readonly InteractionRegistry _interactions;
    private readonly PersistenceClient _persistence;

    public C2WShopPurchaseHandler(PlayerManager playerManager, InteractionRegistry interactions, PersistenceClient persistence)
    {
        _playerManager = playerManager;
        _interactions = interactions;
        _persistence = persistence;
    }

    public void Handle(NetPeer peer, NetPacketReader reader)
    {
        var packet = MessagePackSerializer.Deserialize<C2WShopPurchasePacket>(reader.GetRemainingBytes());

        // Same authoritative checks C2WInteractHandler already does —
        // existence + range — reused here, not re-invented:
        if (!_interactions.TryGet(packet.VendorNetworkId, out var vendor) ||
            vendor.Kind != InteractableKind.Vendor)
        {
            W2CShopResultPacketSender.Send(peer, success: false, reason: "Vendor not found.");
            return;
        }

        if (!_playerManager.TryGetNetworkId(peer, out int playerId) ||
            !_playerManager.TryGetPosition(playerId, out var playerPos) ||
            Vector3.Distance(playerPos, vendor.Position) > vendor.InteractRange)
        {
            W2CShopResultPacketSender.Send(peer, success: false, reason: "Too far away.");
            return;
        }

        long characterId = _playerManager.GetCharacterId(peer);
        _ = ProcessPurchase(peer, characterId, packet.ItemId);
    }

    private async Task ProcessPurchase(NetPeer peer, long characterId, int itemId)
    {
        // New method on W2PCharacterSender, same TCS-correlation shape as
        // Load/Create/LoadList — see 02-NETWORKING-AND-PACKETS.md
        P2WPurchaseResponse result = await _persistence.W2PCharacter.Purchase(characterId, itemId);

        _playerManager.EnqueueAction(() =>   // mandatory: we're off the tick thread after this await
        {
            W2CShopResultPacketSender.Send(peer, result.Success, result.Reason);

            if (result.Success)
                W2CInventoryUpdatePacketSender.Send(peer, result.UpdatedInventory);  // new packet, not detailed here
        });
    }
}
```

### 3. New W2P/P2W packets (shared, both languages need matching enum values)

```csharp
// ArcheCore.Network/Shared/PServerOpcodes.cs — C# side
public enum PServerOpcodes : ushort
{
    // ...existing values...
    CharacterList = 8,
    P2WCharacterListResponse = 9,
    Purchase = 10,             // NEW
    P2WPurchaseResponse = 11,  // NEW
}
```

```ts
// ArcheCore.Server.Persistence/src/Shared/protocol.persistence.ts — TS side
// MUST use the same numbers as above, in the same commit
export enum ProtocolPersistence {
    // ...existing values...
    CharacterList = 8,
    P2WCharacterListResponse = 9,
    Purchase = 10,
    P2WPurchaseResponse = 11,
}
```

### 4. Sender on the WorldServer side (mirrors `W2PCharacterSender.Create`)

```csharp
// Add to W2PCharacterSender.cs
public async Task<P2WPurchaseResponse> Purchase(long characterId, int itemId)
{
    var tcs = new TaskCompletionSource<P2WPurchaseResponse>(TaskCreationOptions.RunContinuationsAsynchronously);
    _client.pendingPurchases[characterId] = tcs;   // new ConcurrentDictionary on PersistenceClient,
                                                     // same pattern as pendingLoads/pendingCreates/pendingLists

    await _client.Send(PServerOpcodes.Purchase, new W2PPurchaseRequest { CharacterId = characterId, ItemId = itemId });
    return await tcs.Task;
}
```

Remember `characterId` is a `long` on the C# side — if you key the pending
dictionary by it instead of by `int accountId` (the existing convention),
that's fine, but double check `PersistenceClient.ResolvePurchase` removes
from the matching dictionary type. Also remember the `Guid`-vs-`string`
caveat from `ADDING_PACKETSv2.md` if you ever move to a `Guid` correlation
key instead — MessagePack for C# serializes `Guid` fine, `@msgpack/msgpack`
on the Node side has no matching extension type for it, so use `string`
across that boundary specifically.

### 5. Node handler does the actual transaction

This is the one step that's genuinely new territory versus what
`persistence.db`'s schema supports today — see
[03-DATABASE-AND-PERSISTENCE.md](03-DATABASE-AND-PERSISTENCE.md#persistencedb--the-bunTypescript-character-store)
for why: right now `characters` has no currency or inventory columns at
all. You'd add them (or a separate `inventory` table) the same
unmigrated, `CREATE TABLE IF NOT EXISTS` way `Database.ts` already does it:

```ts
// Database.ts — add alongside the existing characters table
db.exec(`
    CREATE TABLE IF NOT EXISTS characters (
        character_id INTEGER PRIMARY KEY AUTOINCREMENT,
        account_id   INTEGER NOT NULL,
        name         TEXT    NOT NULL,
        level        INTEGER NOT NULL DEFAULT 1,
        pos_x        REAL    NOT NULL DEFAULT 0,
        pos_y        REAL    NOT NULL DEFAULT 2,
        pos_z        REAL    NOT NULL DEFAULT 0,
        gold         INTEGER NOT NULL DEFAULT 0      -- NEW
    )
`);

db.exec(`
    CREATE TABLE IF NOT EXISTS inventory (            -- NEW
        character_id INTEGER NOT NULL,
        item_id      INTEGER NOT NULL,
        quantity     INTEGER NOT NULL DEFAULT 1,
        PRIMARY KEY (character_id, item_id)
    )
`);
```

```ts
// Networking/W2P/W2PPurchaseHandler.ts
import { db } from "../../Database/Database";
import { W2PPurchaseRequest } from "../../Shared/Packets/Requests/W2PPurchaseRequest";
import { SendP2WPurchaseResponse } from "../P2W/P2WPurchaseResponseSender";

const PRICES: Record<number, number> = { 101: 25, 102: 100 };  // or its own table — see note below

export async function W2PPurchaseHandler(socket: net.Socket, payload: Uint8Array) {
    const request = decode(payload) as W2PPurchaseRequest;
    const price = PRICES[request.ItemId];

    const character = db.prepare(`SELECT gold FROM characters WHERE character_id = ?`)
        .get(request.CharacterId) as { gold: number } | undefined;

    if (!character || price === undefined || character.gold < price) {
        SendP2WPurchaseResponse(socket, { CharacterId: request.CharacterId, Success: false, Reason: "Insufficient gold." });
        return;
    }

    // Two writes that must succeed or fail together — see the transaction
    // note right below this snippet before shipping this as-is.
    db.prepare(`UPDATE characters SET gold = gold - ? WHERE character_id = ?`)
        .run(price, request.CharacterId);

    db.prepare(`
        INSERT INTO inventory (character_id, item_id, quantity) VALUES (?, ?, 1)
        ON CONFLICT(character_id, item_id) DO UPDATE SET quantity = quantity + 1
    `).run(request.CharacterId, request.ItemId);

    SendP2WPurchaseResponse(socket, { CharacterId: request.CharacterId, Success: true, Reason: null });
}
```

```ts
// Networking/RegisterHandlers.ts — the manual registration this whole
// side of the protocol requires (see 02-NETWORKING-AND-PACKETS.md)
RegisterHandler(ProtocolPersistence.Purchase, W2PPurchaseHandler);
```

**Do the gold-deduct and the inventory-insert in one `bun:sqlite`
transaction**, not as two independent `.run()` calls like the sketch
above — `bun:sqlite` supports `db.transaction(fn)` for exactly this. As
written, a crash between the two `.run()` calls would deduct gold without
granting the item. This is the first place in the codebase where a
multi-statement write actually needs atomicity — every existing handler
(`W2PCharacterSaveHandler`, etc.) is a single statement, so there's no
existing pattern to copy; reach for `bun:sqlite`'s transaction API rather
than inventing manual rollback logic.

### 6. Response flows back exactly like Load/Create/List does

```csharp
// Core/PersistenceServer/.../Networking/P2WHandlers/P2WPurchaseResponseHandler.cs
public class P2WPurchaseResponseHandler : IPersistencePacketHandler
{
    private readonly PersistenceClient _client;
    public P2WPurchaseResponseHandler(PersistenceClient client) => _client = client;

    public void Handle(PersistencePacket packet)
    {
        var response = MessagePackSerializer.Deserialize<P2WPurchaseResponse>(packet.Payload);
        _client.ResolvePurchase(response);   // new method on PersistenceClient, mirrors ResolveLoad/ResolveCreate/ResolveList
    }
}
```

```csharp
// PersistenceClient.cs — register alongside the existing four
dispatcher.Register(PServerOpcodes.P2WPurchaseResponse, new P2WPurchaseResponseHandler(this));
```

That `Resolve` call completes the `TaskCompletionSource` sitting in
`ProcessPurchase`'s `await`, which resumes inside
`playerManager.EnqueueAction(...)` back on step 2, sends
`W2CShopResultPacketSender`, and the client's `W2CShopResultHandler` (new,
mirrors `W2CInteractDialogueHandler`) updates the shop UI.

## The complete round trip, both directions, at a glance

```
Client                    WorldServer                      PersistenceServer
------                    -----------                      -----------------
[interact with vendor — reuses the EXISTING interaction system, no persistence]
C2WInteractPacket    -->  C2WInteractHandler
                            -> range/existence check (IInteractable)
                            -> LuaEngine.FireEvent(OnInteract, player, templateId, Vendor)
                                 -> shop_vendor_example.lua -> player:OpenShop(id)
                                      -> W2CShopOpenPacketSender.Send
W2CShopOpenPacket    <--  ................................

[buy an item — NEW round trip, needs persistence]
C2WShopPurchasePacket -->  C2WShopPurchaseHandler
                             -> re-check range/existence (same pattern as interact)
                             -> W2PCharacter.Purchase(characterId, itemId) --> W2PPurchaseHandler
                                                                                  transaction {
                                                                                    UPDATE characters SET gold...
                                                                                    INSERT INTO inventory...
                                                                                  }
                             <----------------------------------------------- P2WPurchaseResponse
                             EnqueueAction(() => send result)
W2CShopResultPacket   <--  ................................
```

## The checklist this example generalizes to

Whenever a new feature needs to reach the database, you're really building
two independent things that happen to share a name:

1. **A same-process round trip** (client ↔ WorldServer only) for anything
   that's pure gameplay logic and doesn't need to survive a disconnect —
   follow the C2W/W2C steps in `ADDING_PACKETSv2.md`, reuse
   `InteractionRegistry`/`IInteractable` if it's interact-triggered.
2. **A cross-process round trip** (WorldServer ↔ PersistenceServer) for
   anything that must be durable — follow the `W2PCharacterSender`
   pattern: new opcode value in **both** `PServerOpcodes` and
   `ProtocolPersistence` in the same commit, a `TaskCompletionSource`
   dictionary on `PersistenceClient` if it's request/response, a Node
   handler registered in `RegisterHandlers.ts`, and — if it's more than one
   SQL statement — an actual transaction, since nothing before the
   shop-purchase example needed one.

And regardless of which of those two you're building, don't forget the one
step that has no compiler or attribute to catch it for you: **the client-
side `dispatcher.Register(...)` line in `ClientNetwork.RegisterHandlers()`
for any new W2C packet.** See
[08-CLIENT-ARCHITECTURE.md](08-CLIENT-ARCHITECTURE.md) for exactly why that
one's on you today.
