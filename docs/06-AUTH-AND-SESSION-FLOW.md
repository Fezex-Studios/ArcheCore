# Auth & Session Flow, End to End

This traces one player from clicking "connect" to standing in the world,
across all four processes, with the actual packets and method calls at
each step. Everything referenced here is expanded in more depth in
[02-NETWORKING-AND-PACKETS.md](02-NETWORKING-AND-PACKETS.md),
[03-DATABASE-AND-PERSISTENCE.md](03-DATABASE-AND-PERSISTENCE.md), and
[05-GAMEPLAY-SYSTEMS.md](05-GAMEPLAY-SYSTEMS.md).

## Step 0 — before any LiteNetLib connection exists

The Unity client logs in against the **AuthServer** over plain HTTP (not
part of this snapshot, but its two contracts are used by name throughout
the client and WorldServer config): it gets back a session token, and
separately, `GameDataBootstrap` checks/downloads `gamedata.db` (see
[03-DATABASE-AND-PERSISTENCE.md](03-DATABASE-AND-PERSISTENCE.md)). The
token is stashed in `SessionManager.Token` (the client-side static, not to
be confused with the server's `Core/Managers/SessionManager`) before the
LiteNetLib connection is ever opened.

## Step 1 — LiteNetLib connect

```csharp
// ClientNetwork.Connect(ip) — client
client = new NetManager(this);
client.Start();
client.Connect(ip, 7777, "MMO");
```

```csharp
// WorldServer.OnConnectionRequest — world server
public void OnConnectionRequest(ConnectionRequest request)
{
    request.AcceptIfKey(ConnectionKey);  // ConnectionKey = "MMO"
}
```

This is a shared-secret handshake at the transport level, not real
authentication — anyone who knows the string `"MMO"` can open a connection.
Real auth happens next, over that connection.

## Step 2 — client sends `Authenticate` the moment the peer connects

```csharp
// ClientNetwork.OnPeerConnected — fires as soon as LiteNetLib accepts the connection
public void OnPeerConnected(NetPeer peer)
{
    ServerPeer = peer;
    C2WAuthenticatePacket.Send(peer, SessionManager.Token);
}
```

```csharp
// C2WAuthenticatePacket.Send — client-side sender
public static void Send(NetPeer peer, string token)
{
    ClientPacketSender.SendPacket(peer, Opcodes.Authenticate,
        new C2WAuthenticateRequest { Token = token });
}
```

## Step 3 — WorldServer validates the token against the AuthServer

```csharp
[PacketOpcode(Opcodes.Authenticate)]
public class C2WAuthenticateHandler : IPacketHandler
{
    public void Handle(NetPeer peer, NetPacketReader reader)
    {
        var request = MessagePackSerializer.Deserialize<C2WAuthenticateRequest>(reader.GetRemainingBytes());
        _ = ValidateAndConnect(peer, request.Token);   // fire-and-forget from the sync Handle() entrypoint
    }

    private async Task ValidateAndConnect(NetPeer peer, string token)
    {
        int accountId = await authService.ValidateToken(token);
        if (accountId == -1)
        {
            playerManager.EnqueueAction(() => peer.Disconnect());
            return;
        }
        // ...continues in Step 4
    }
}
```

`AuthService.ValidateToken` is a plain HTTP POST to the AuthServer:

```csharp
string url  = $"{_worldConfig.AuthServerUrl}/validate-session";   // http://127.0.0.1:3000 by default
string body = JsonConvert.SerializeObject(new { Token = token });

var request = new HttpRequestMessage(HttpMethod.Post, url) {
    Content = new StringContent(body, Encoding.UTF8, "application/json")
};
request.Headers.Add("x-internal-secret", _worldConfig.InternalSecret);  // must match AuthServer's own INTERNAL_SECRET

var result = JsonConvert.DeserializeObject<ValidateResponse>(json);  // { Valid: bool, AccountId: int }
return result.Valid ? result.AccountId : -1;
```

A `-1` account id (bad token, expired, or the HTTP call itself failing)
disconnects the peer immediately.

## Step 4 — WorldServer asks the PersistenceServer for the account's character roster

Still inside `ValidateAndConnect`, once `accountId` is known:

```csharp
P2WCharacterListResponse list = await persistence.W2PCharacter.LoadList(accountId);

playerManager.EnqueueAction(() =>
{
    playerManager.TrackPendingSelection(peer, accountId);   // session exists, NetworkId still null = "pending"

    WorldserverPacketSender.SendPacket(peer, Opcodes.W2CCharacterList,
        new W2CCharacterListPacket { Characters = list.Characters ?? Array.Empty<CharacterSummary>() });
});
```

`W2PCharacter.LoadList` is a full round trip over the *second* protocol
(WorldServer ↔ PersistenceServer, raw TCP, port 7778):

```
WorldServer                                    PersistenceServer (Node)
------------                                   ------------------------
W2PCharacterSender.LoadList(accountId)
  -> pendingLists[accountId] = new TCS
  -> PersistenceClient.Send(PServerOpcodes.CharacterList,
       new W2PCharacterListRequest { AccountId = accountId })
        |
        v  (4-byte length prefix + MessagePack PersistencePacket)
                                                W2PCharacterListHandler(socket, payload)
                                                  const rows = db.prepare(
                                                    "SELECT * FROM characters WHERE account_id = ?"
                                                  ).all(request.AccountId)
                                                  SendP2WCharacterListResponse(socket, {
                                                    AccountId, Characters: rows.map(...)
                                                  })
        |
        v
P2WCharacterListResponseHandler.Handle(response)
  -> client.ResolveList(response)
       -> pendingLists.TryRemove(accountId, out tcs)
       -> tcs.SetResult(response)
  -> the `await` in LoadList() above now returns
```

Notice the `EnqueueAction` wrap around everything after the `await` — this
is the mandatory pattern any time a C2W handler awaits a persistence
round-trip. The continuation after an `await` resumes on whatever thread
the TCP `ReceiveLoop` happened to complete on, **not** the tick thread that
owns `PlayerManager`'s state, so touching session/interest/replication
state directly here would be a race condition. Every handler in this doc
that awaits `persistence.*` follows the same shape:
`await ...; playerManager.EnqueueAction(() => { /* touch shared state here */ });`

## Step 5 — client shows create-or-select UI, sends one of two requests

The client reacts to `W2CCharacterListPacket` via
`PlayerUIEvents.RaiseCharacterListReceived(...)` (see
`W2CCharacterListHandler.cs`), which the `CharacterSelectUI`/
`CharacterCreateUI` MonoBehaviours listen for. Depending on whether the
roster is empty, the player either creates a character:

```csharp
// C2WCreateCharacterPacket.Send (client) -> Opcodes.C2WCreateCharacterRequest
```

or selects an existing one:

```csharp
// C2WSelectCharacterPacket.Send (client) -> Opcodes.C2WSelectCharacter
```

### Branch A — `C2WCreateCharacterHandler`

```csharp
[PacketOpcode(Opcodes.C2WCreateCharacterRequest)]
public class C2WCreateCharacterHandler : IPacketHandler
{
    public void Handle(NetPeer peer, NetPacketReader reader)
    {
        var request = MessagePackSerializer.Deserialize<C2WCreateCharacterRequest>(reader.GetRemainingBytes());

        var accountId = _playerManager.GetPendingAccountId(peer);
        if (accountId is null) { peer.Disconnect(); return; }     // never authenticated / already spawned

        string name = request.Name?.Trim() ?? string.Empty;
        if (name.Length < 2 || name.Length > 20) { peer.Disconnect(); return; }  // only validation today

        _ = CreateAndSpawn(peer, accountId.Value, name);
    }

    private async Task CreateAndSpawn(NetPeer peer, int accountId, string name)
    {
        P2WCreateCharacterResponse response = await _persistence.W2PCharacter.Create(accountId, name);
        if (!response.Success) { _playerManager.EnqueueAction(() => peer.Disconnect()); return; }

        // Persistence only returns the new CharacterId/Name — the handler
        // fabricates the rest of a P2WCharacterLoadResponse locally
        // (Level 1, spawn at origin) rather than doing a second round trip
        // to load what it just created.
        var characterData = new P2WCharacterLoadResponse {
            Found = true, AccountId = accountId, CharacterId = response.CharacterId,
            Name = response.Name, Level = 1, X = 0, Y = 2, Z = 0
        };

        _playerManager.EnqueueAction(() =>
            _playerManager.HandlePlayerConnected(peer, accountId, characterData));
    }
}
```

Name validation (`2 <= length <= 20`) happens **only** on the WorldServer
side today — nothing in `W2PCharacterCreateHandler.ts` re-validates it, so
a modified client bypassing the length check would still be caught here,
but a bug in this specific check has no second line of defense. Worth
keeping in mind if you add more create-time fields (a starting class or
appearance choice, say) for a shop/character-customization feature.

### Branch B — `C2WSelectCharacterHandler`

```csharp
private async Task LoadAndSpawn(NetPeer peer, int accountId, long characterId)
{
    P2WCharacterLoadResponse character = await _persistence.W2PCharacter.Load(accountId, characterId);

    if (!character.Found)
    {
        // Either the id doesn't exist, or belongs to a different account —
        // the persistence query filters on both, so this also catches a
        // tampered client trying to load someone else's character.
        _playerManager.EnqueueAction(() => peer.Disconnect());
        return;
    }

    _playerManager.EnqueueAction(() =>
        _playerManager.HandlePlayerConnected(peer, accountId, character));
}
```

Both branches converge on the exact same call:
`playerManager.HandlePlayerConnected(peer, accountId, characterData)`.
Neither branch has an explicit "clear pending" step — the pending state
disappears the moment `SpawnPlayer` (inside `HandlePlayerConnected`)
assigns the peer a `NetworkId`.

## Step 6 — spawning into the world

```csharp
// PlayerSpawnManager.HandlePlayerConnected
public void HandlePlayerConnected(NetPeer peer, int accountId, P2WCharacterLoadResponse character)
{
    if (_sessions.TryGetAccountPeer(accountId, out var existingPeer))
    {
        // Duplicate login — the account is already spawned on another peer.
        CleanupPeer(existingPeer, true);   // saves + despawns the OLD connection
        existingPeer.Disconnect();
    }

    _sessions.RegisterAccountPeer(accountId, peer);
    W2CMOTDPacketSender.Send(peer, _worldConfig.MOTD);

    int newId = SpawnPlayer(peer, accountId, character);   // assigns NetworkId, sends W2CSpawnPlayerPacket,
                                                              // runs InterestManager.UpdatePosition for the
                                                              // initial mutual-introduction spawn packets

    var luaPlayer = new LuaPlayer(peer, newId, accountId, _replication);
    _luaEngine.FireEvent(PlayerEvent.OnConnect, luaPlayer);   // fires on_player_connect.lua etc.

    _worldSpawnManager.SendWorldToPeer(peer);                 // static world objects / NPCs
    _demoManager.OnPlayerJoin(peer);
}
```

At this point the player is fully in-world: `Opcodes.PlayerMove` /
`Opcodes.PlayerPosition` traffic starts flowing (see
[05-GAMEPLAY-SYSTEMS.md](05-GAMEPLAY-SYSTEMS.md) for
`PlayerMovementBroadcaster`), and `Opcodes.Interact` becomes valid for
anything the `InteractionRegistry` knows about.

## Step 7 — disconnect

```csharp
// WorldServer.OnPeerDisconnected
public void OnPeerDisconnected(NetPeer peer, DisconnectInfo info) =>
    _playerManager.HandlePlayerDisconnected(peer);
```

```csharp
// PlayerSpawnManager.CleanupPeer(peer, save: true)
if (peer.Tag is not PlayerSession { NetworkId: int networkId } session) return; // never spawned — nothing to do

if (save)
    _persistence.SaveCharacterAsync(session.CharacterId, session.AccountId,
        session.Name, session.Level, session.Position);

_sessions.UnregisterAccountPeerIfCurrent(session.AccountId, peer);
var knownByPeers = _interest.GetKnownBy(networkId)...;   // snapshot BEFORE removing from InterestManager
_interest.Remove(networkId);
_sessions.UnregisterNetworkId(networkId);
peer.Tag = null;

W2CPlayerLeavePacketSender.Send(_replication, knownByPeers, networkId);
```

`SaveCharacterAsync` is fire-and-forget (`async void`) — sends
`PServerOpcodes.CharacterSave` to the persistence server with no reply
expected, which `W2PCharacterSaveHandler.ts` handles with a plain
`INSERT OR REPLACE`. There's no confirmation path back to the WorldServer
that the save actually succeeded before the peer object is discarded — if
you need save-acknowledgment for something higher-stakes than position/
level (say, a shop transaction that debited currency), that would need a
request/response pair with a `TaskCompletionSource`, not this fire-and-
forget send.

## The whole thing as one diagram

```
Client                  WorldServer                         PersistenceServer
------                  -----------                         -----------------
connect (UDP, "MMO") -> OnConnectionRequest (accept)
C2WAuthenticatePacket -> C2WAuthenticateHandler
                           -> AuthService.ValidateToken (HTTP to AuthServer)
                           -> W2PCharacter.LoadList(accountId) ------------> W2PCharacterListHandler
                                                                               SELECT * FROM characters
                           <----------------------------------------------- P2WCharacterListResponse
                         TrackPendingSelection(peer, accountId)
W2CCharacterListPacket <- SendPacket(...)

[choose one:]
C2WCreateCharacterRequest -> C2WCreateCharacterHandler
                                -> W2PCharacter.Create(accountId, name) ---> W2PCharacterCreateHandler
                                                                                INSERT INTO characters
                                <------------------------------------------ P2WCreateCharacterResponse
        or
C2WSelectCharacterRequest -> C2WSelectCharacterHandler
                                -> W2PCharacter.Load(accountId, id) -------> W2PCharacterLoadHandler
                                                                                SELECT ... WHERE id AND account_id
                                <------------------------------------------ P2WCharacterLoadResponse

                         PlayerSpawnManager.HandlePlayerConnected
                           -> SpawnPlayer (assigns NetworkId, ends "pending")
                           -> LuaEngine.FireEvent(OnConnect, luaPlayer)
                           -> SpawnManager.SendWorldToPeer
W2CSpawnPlayerPacket   <- ...
W2CMOTDPacket          <- ...
[player is now in-world]

...gameplay (PlayerMove / Interact / Chat / etc.)...

disconnect             -> OnPeerDisconnected -> CleanupPeer
                           -> CharacterPersistence.SaveCharacterAsync ----> W2PCharacterSaveHandler
                                                                              INSERT OR REPLACE INTO characters
W2CPlayerLeavePacket   <- (to everyone who knew about this player)
```
