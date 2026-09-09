# Networking & Packets

This doc covers the two transports and their wire formats. For the
step-by-step of *adding* a new packet, `ArcheCore_Server_World/Docs/ADDING_PACKETSv2.md`
is the canonical reference — read that for process, read this for the
underlying mechanics and for how the two systems differ.

## Two transports, two opcode enums

| | Client ↔ WorldServer | WorldServer ↔ PersistenceServer |
|---|---|---|
| Transport | LiteNetLib (UDP, reliable/sequenced by default) | Raw TCP |
| Port | 7777 | 7778 |
| Opcode enum | `Opcodes` (`ArcheCore.Network/Shared/Opcodes.cs`) | `PServerOpcodes` (C#) / `ProtocolPersistence` (TS) — must stay numerically identical by hand |
| Framing | LiteNetLib handles message boundaries | Manual length-prefix (see below) |
| Registration | Attribute + reflection (`AutoRegister`) on the server side; **manual** on the client side today | Fully manual in both languages |
| Dispatcher | `PacketDispatcher` (server) / `ClientPacketDispatcher` (client) | `PersistenceDispatcher` (C#) / `PacketDispatcher.ts` (Node) |

### Client ↔ WorldServer wire format

`WorldServer.OnNetworkReceive`:

```csharp
public void OnNetworkReceive(NetPeer peer, NetPacketReader reader, byte channel, DeliveryMethod delivery)
{
    Opcodes packet = (Opcodes)reader.GetUShort();
    _packetDispatcher.Handle(packet, peer, reader);
    reader.Recycle();
}
```

Every message is a `ushort` opcode followed by the MessagePack-encoded
payload for that opcode's packet class. LiteNetLib itself handles message
boundaries, so there's no length prefix needed here — that's only required
on the raw-TCP persistence link (see below). The client's
`ClientNetwork.OnNetworkReceive` is the mirror image.

Connection uses a shared string key rather than any real auth at the
transport layer:

```csharp
public void OnConnectionRequest(ConnectionRequest request)
{
    request.AcceptIfKey(ConnectionKey); // ConnectionKey = "MMO"
}
```

Real authentication happens one level up, after connection, via the
`Authenticate` opcode and `AuthService.ValidateToken` — see
[06-AUTH-AND-SESSION-FLOW.md](06-AUTH-AND-SESSION-FLOW.md).

### WorldServer ↔ PersistenceServer wire format

This link has no framing help from the transport, so `PersistenceClient`
(C#) and `index.ts` (Node) both hand-roll a 4-byte little-endian length
prefix around a MessagePack-encoded envelope:

```csharp
// PersistenceClient.Send<T>
PersistencePacket persistencePacket = new PersistencePacket
{
    Opcode  = (ushort)opcode,
    Payload = MessagePackSerializer.Serialize(payload)
};

byte[] packetBytes = MessagePackSerializer.Serialize(persistencePacket);
byte[] lengthBytes = BitConverter.GetBytes(packetBytes.Length);

await stream.WriteAsync(lengthBytes);
await stream.WriteAsync(packetBytes);
```

```ts
// index.ts — reassembling that same framing on the Node side
while (buffer.length >= 4) {
    const length = buffer.readInt32LE(0);
    if (buffer.length < length + 4) break;         // wait for more data

    const packetBytes = buffer.subarray(4, 4 + length);
    buffer = buffer.subarray(4 + length);

    const packet: Packet = decode(packetBytes) as Packet;
    await Dispatch(socket, packet.Opcode, packet.Payload);
}
```

`PersistencePacket` (the outer envelope: `{ Opcode: ushort, Payload: byte[] }`)
is distinct from every individual request/response packet class — the
opcode tells the dispatcher which class to deserialize `Payload` as next.
The Node side mirrors this with the `Packet` type
(`Shared/Packet.ts`, `{ Opcode: number, Payload: Uint8Array }`).

## Server-side dispatch: attribute + reflection

`PacketDispatcher.AutoRegister` (`ArcheCore.Network/Utils/Worldserver/WSPacketDispatcher.cs`)
is what makes adding a C2W handler a one-file change:

```csharp
public void AutoRegister(Func<Type, object> resolve, Assembly assembly)
{
    var handlerTypes = assembly.GetTypes()
        .Where(t => typeof(IPacketHandler).IsAssignableFrom(t) && !t.IsAbstract);

    foreach (var type in handlerTypes)
    {
        var attr = type.GetCustomAttribute<PacketOpcodeAttribute>();
        if (attr == null)
        {
            Console.WriteLine($"[PacketDispatcher] WARNING: {type.Name} implements IPacketHandler " +
                               "but has no [PacketOpcode] attribute - skipped.");
            continue;
        }

        var ctor = type.GetConstructors().Single();
        var args = ctor.GetParameters().Select(p => resolve(p.ParameterType)).ToArray();

        Register(attr.Opcode, (IPacketHandler)Activator.CreateInstance(type, args));
    }
}
```

Two things to internalize:

- **Every handler must have exactly one public constructor.** `.Single()`
  throws at startup if that's ever violated — which is the point: you find
  out at boot, not the first time a player triggers the packet.
- **Every constructor parameter must be resolvable** via the `resolve`
  function passed in, which in practice is `ServiceContainer.Resolve` (see
  `WorldServer.RegisterPackets`). If you add a handler that asks for a type
  nobody registered, you get a clear `InvalidOperationException` naming the
  missing type, at boot — not a `NullReferenceException` three requests
  later.

`ServiceContainer` itself is deliberately not a general DI container:

```csharp
public class ServiceContainer
{
    private readonly Dictionary<Type, object> _services = new();
    public void Register<T>(T instance) => _services[typeof(T)] = instance;
    public object Resolve(Type type)
    {
        if (_services.TryGetValue(type, out var instance)) return instance;
        throw new InvalidOperationException(
            $"[ServiceContainer] No instance registered for {type.Name}. " +
            "Add a services.Register(...) call for it in WorldServer.RegisterPackets.");
    }
}
```

It exists specifically to guarantee every handler that asks for, say,
`InterestManager` gets the *same* instance — see the class doc-comment,
which references a real bug this fixed (a handler accidentally constructed
with its own private empty `InterestManager`).

## Client-side dispatch: this is NOT auto-registered today

This is worth calling out explicitly because it contradicts what you'd
expect from `ClientPacketDispatcher.AutoRegister` existing at all.
`ClientNetwork.RegisterHandlers()` (Unity side) does this by hand:

```csharp
private void RegisterHandlers()
{
    dispatcher.Register(Opcodes.MOTD,           new W2CMOTDHandler());
    dispatcher.Register(Opcodes.SpawnPlayer,    new W2CSpawnPlayerHandler());
    dispatcher.Register(Opcodes.PlayerPosition, new W2CPlayerPositionHandler());
    dispatcher.Register(Opcodes.PlayerLeave,    new W2CPlayerLeaveHandler());
    dispatcher.Register(Opcodes.Announcement,   new W2CAnnouncementHandler());
    dispatcher.Register(Opcodes.SpawnNpc,       new W2CSpawnNpcHandler());
    // ...one line per opcode, all the way down
}
```

None of the `W2CHandlers` classes carry a `[PacketOpcode]` attribute — they
don't need to, because nothing calls `ClientPacketDispatcher.AutoRegister`
on the client. That method is fully implemented and would work (client
handlers are all parameterless, which is exactly the case it optimizes
for), it's just not wired up. This is exactly the class of bug
`ADDING_PACKETSv2.md` describes `AutoRegister` as having eliminated on the
server — on the client it's still possible today to add a `W2CHandler` and
forget the `dispatcher.Register(...)` line, and the packet will silently
print `[ClientPacketDispatcher] Unhandled packet: X` with no compile-time
signal. See [09-KNOWN-GAPS-AND-NEXT-STEPS.md](09-KNOWN-GAPS-AND-NEXT-STEPS.md)
for the concrete fix.

## Persistence-side dispatch: fully manual, in two languages

Neither `PersistenceDispatcher` (C#, world-server side, handling P2W
replies) nor `PacketDispatcher.ts` (Node side, handling W2P requests) has
any reflection or attribute system. Every opcode needs a manual
registration call:

```csharp
// PersistenceClient.RegisterHandlers()
dispatcher.Register(PServerOpcodes.P2WConnectResponse, new P2WConnectResponseHandler());
dispatcher.Register(PServerOpcodes.CharacterLoad, new P2WCharacterLoadHandler(this));
dispatcher.Register(PServerOpcodes.P2WCharacterCreateResponse, new P2WCharacterCreateResponseHandler(this));
dispatcher.Register(PServerOpcodes.P2WCharacterListResponse, new P2WCharacterListResponseHandler(this));
```

```ts
// RegisterHandlers.ts — the ONE place Node-side opcodes get wired up
export function RegisterHandlers() {
    RegisterHandler(ProtocolPersistence.CharacterSave, W2PCharacterSaveHandler);
    RegisterHandler(ProtocolPersistence.CharacterLoad, W2PCharacterLoadHandler);
    RegisterHandler(ProtocolPersistence.W2PConnectRequest, W2PConnectHandler);
    RegisterHandler(ProtocolPersistence.HelloWorld, W2PHelloWorldHandler);
    RegisterHandler(ProtocolPersistence.CharacterCreate, W2PCharacterCreateHandler);
    RegisterHandler(ProtocolPersistence.CharacterList, W2PCharacterListHandler);
}
```

Missing a registration on either side doesn't crash anything — it just
prints `Unknown Opcode N` (Node) or leaves the request permanently pending
(C#, since nothing ever calls `Resolve(...)`/`ResolveList(...)` on the
`TaskCompletionSource` — see `PersistenceClient.pendingLoads` /
`pendingCreates` / `pendingLists`). If a persistence round-trip just hangs
forever with no error, check `RegisterHandlers` on both sides first.

## The stale `Opcodes.ts` file

`ArcheCore.Server.Persistence/src/Shared/Opcodes.ts` is a second, older copy
of the same enum name (`ProtocolPersistence`) as the canonical
`protocol.persistence.ts`, and it's already drifted:

```ts
// Opcodes.ts (stale, unused)
export enum ProtocolPersistence {
    W2PConnectRequest = 1,
    CharacterSave = 2,
    CharacterLoad = 3,
    P2WConnectResponse = 4,
    P2WErrorResponse = 6,   // <- doesn't exist in the real one at all
}

// protocol.persistence.ts (canonical — this is what RegisterHandlers.ts imports)
export enum ProtocolPersistence {
    W2PConnectRequest = 1,
    CharacterSave = 2,
    CharacterLoad = 3,
    P2WConnectResponse = 4,
    HelloWorld = 5,
    CharacterCreate = 6,
    P2WCharacterCreateResponse = 7,
    CharacterList = 8,
    P2WCharacterListResponse = 9,
}
```

Nothing currently imports `Opcodes.ts` (grep the repo before relying on
that staying true), but it's exactly the trap `ADDING_PACKETSv2.md` warns
about in its worked examples — safe to delete, or worth a comment marking
it dead, before it confuses a future edit.

## Correlation pattern for request/response over the persistence link

Fire-and-forget sends (`W2PHelloWorldSender`, `W2CMOTDPacketSender`) need
nothing extra. Anything that needs a reply routed back to the specific
caller that asked (character load/create/list) uses a
`ConcurrentDictionary<TKey, TaskCompletionSource<TResponse>>` keyed by
`AccountId`:

```csharp
// PersistenceClient
internal readonly ConcurrentDictionary<int, TaskCompletionSource<P2WCharacterLoadResponse>>
    pendingLoads = new();

public void ResolveLoad(P2WCharacterLoadResponse response)
{
    if (pendingLoads.TryRemove(response.AccountId, out var tcs))
        tcs.SetResult(response);
    else
        Logger.Warn($"[PersistenceClient] No pending load for AccountId={response.AccountId}");
}
```

`AccountId`-keying only works because there's never more than one in-flight
request per account for load/create/list today. If you add a request type
a player could trigger multiple times in quick succession (a "check shop
price" call, for instance), key it by a fresh `Guid` generated at the call
site instead, echoed back unchanged in the response — see
`ADDING_PACKETSv2.md`'s edge-case notes for the `Guid`-as-`string`-across-the-
C#/TS-boundary caveat, which applies here too.

Every C2W handler that awaits a persistence round-trip
(`C2WAuthenticateHandler`, `C2WCreateCharacterHandler`,
`C2WSelectCharacterHandler`) wraps everything after the `await` in
`playerManager.EnqueueAction(() => { ... })` to get back onto the tick
thread — see `C2WAuthenticateHandler.ValidateAndConnect` in
[06-AUTH-AND-SESSION-FLOW.md](06-AUTH-AND-SESSION-FLOW.md) for a complete
worked example of this pattern.
