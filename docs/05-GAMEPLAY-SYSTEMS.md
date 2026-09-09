# Gameplay Systems

This covers the manager classes that make up the actual simulation:
session tracking, spatial awareness, movement replication, and the
interaction system. All of these live under
`ArcheCore.Server.World/Core/Managers/` (plus `Core/Interaction/`) and are
coordinated by the thin `PlayerManager` facade.

## `PlayerManager` — a facade, not a god object (anymore)

The class comment on `PlayerManager` is worth reading verbatim because it
explains the shape of everything else in this doc: it used to be a
448-line class doing session tracking, spawning, movement broadcast,
character saving, and Lua event firing all at once, and has since been
split into four owner classes plus a thin facade:

```csharp
public class PlayerManager
{
    private readonly SessionManager _sessions;
    private readonly InterestManager _interest = new();
    private readonly PlayerMovementBroadcaster _movement;
    private readonly PlayerSpawnManager _spawn;
    private readonly CharacterPersistence _persistence;
    // + the pending-action queue and the LuaEngine, kept here directly
}
```

Every method that existed on the old god-object still exists on
`PlayerManager` with the same signature — it just delegates. This matters
if you're deciding where to add a new method: **add it to the class that
actually owns the relevant state**, then expose it through `PlayerManager`
only if a packet handler needs to call it (handlers only ever get
`PlayerManager` injected, not the four inner classes directly — see
`ServiceContainer` in
[02-NETWORKING-AND-PACKETS.md](02-NETWORKING-AND-PACKETS.md)).

| Responsibility | Owner class |
|---|---|
| "which peer is which player" (peer.Tag access) | `SessionManager` |
| connect → spawn → disconnect → cleanup lifecycle | `PlayerSpawnManager` |
| position updates, interest-list crossing broadcasts | `PlayerMovementBroadcaster` |
| asking the persistence server to save a character | `CharacterPersistence` |
| pending-action queue (tick-thread re-entry), Lua engine, interaction glue | `PlayerManager` itself |

## Sessions: `PlayerSession` lives on `NetPeer.Tag`

`SessionManager` is the **only** class allowed to read/write `peer.Tag`
directly — every other class goes through one of its methods instead of
casting `Tag` itself. This is enforced by convention, not the compiler, so
keep it that way when adding new code.

A session has two states, distinguished by whether `NetworkId` is set:

```csharp
// "pending" — authenticated, but hasn't picked/created a character yet
public void TrackPendingSelection(NetPeer peer, int accountId) =>
    peer.Tag = new PlayerSession { AccountId = accountId };

// non-null only while pending (NetworkId is still null)
public int? GetPendingAccountId(NetPeer peer) =>
    peer.Tag is PlayerSession { NetworkId: null } session ? session.AccountId : null;
```

`PlayerSpawnManager.SpawnPlayer` is what turns "pending" into "in-world" —
assigning `session.NetworkId` is the entire state transition, nothing else
needs to explicitly clear the pending flag:

```csharp
var session = peer.Tag as PlayerSession ?? new PlayerSession { AccountId = accountId };
session.NetworkId = networkId;
session.CharacterId = character.CharacterId;
// ...
peer.Tag = session;
```

Two reverse-index dictionaries live alongside the peer-keyed lookups,
because you sometimes need to go the other direction:

```csharp
private readonly Dictionary<int, NetPeer> _idToPeer = new();      // networkId -> peer
private readonly Dictionary<int, NetPeer> _accountToPeer = new(); // accountId -> peer, for duplicate-login detection
```

## Spatial awareness: `SpatialGrid` + `InterestManager`

Two layers here, each with a distinct job:

- **`SpatialGrid`** — pure bucketing. Divides the XZ plane into
  `cellSize`-sized cells (default `50f`) and answers "who's near this
  position" as a handful of dictionary lookups instead of an O(n) scan:

  ```csharp
  private (int, int) CellOf(Vector3 position) => (
      (int)MathF.Floor(position.X / _cellSize),
      (int)MathF.Floor(position.Z / _cellSize));

  public IEnumerable<int> GetNearby(int id, int radiusCells = 1)
  {
      // 3x3 neighborhood at radiusCells=1 -> up to ~150 units of awareness
      // range with the default 50-unit cell size
  }
  ```

  Explicitly **not thread-safe** — call it only from the tick thread /
  wherever `PlayerManager`'s state is already being accessed, same
  assumption the rest of the manager layer makes.

- **`InterestManager`** — wraps `SpatialGrid` with the bookkeeping
  `PlayerManager` actually needs: not just "who's nearby right now" but
  "what changed since the last update", so the caller knows who needs a
  fresh spawn packet versus a despawn/leave packet:

  ```csharp
  public (List<int> entered, List<int> left) UpdatePosition(int networkId, Vector3 position)
  {
      _grid.Update(networkId, position);
      var nearby = new HashSet<int>(_grid.GetNearby(networkId));

      _known.TryGetValue(networkId, out var previouslyKnown);
      previouslyKnown ??= new HashSet<int>();

      var entered = nearby.Except(previouslyKnown).ToList();
      var left = previouslyKnown.Except(nearby).ToList();

      _known[networkId] = nearby;
      // ... also updates the OTHER side's _known set symmetrically,
      // so if B enters A's awareness, A is added to B's too
      return (entered, left);
  }
  ```

  The awareness relationship is kept symmetric deliberately — when `other`
  enters `networkId`'s set, `networkId` is added to `other`'s set too, in
  the same call. `GetKnownBy(networkId)` — "who currently knows about this
  entity" — is what routes position-tick and leave packets; see
  `PlayerMovementBroadcaster.BroadcastPosition` below for the consumer.

## Movement replication: `PlayerMovementBroadcaster`

Called from `C2WMovementHandler` on every position update the client
sends. Does three things per call:

```csharp
public void BroadcastPosition(NetPeer sender, int networkId, Vector3 position)
{
    if (sender.Tag is PlayerSession senderSession)
        senderSession.Position = position;                 // 1. update authoritative position

    var (entered, left) = _interest.UpdatePosition(networkId, position);

    foreach (var otherId in entered)                        // 2. spawn packets both ways
    {
        // sends otherId's existing position to sender, AND sender's new
        // position to otherId — a fresh mutual introduction
    }

    foreach (var otherId in left)                           // 2b. leave packets both ways
    {
        W2CPlayerLeavePacketSender.Send(_replication, new[] { otherPeer }, networkId);
        W2CPlayerLeavePacketSender.Send(_replication, new[] { sender }, otherId);
    }

    var knownByPeers = _interest.GetKnownBy(networkId)...;
    W2CPlayerPositionPacketSender.SendUnreliable(       // 3. routine position tick
        _replication, knownByPeers, sender, networkId, position);
}
```

Note the delivery method split: entering/leaving the interest set uses
reliable sends (`W2CSpawnPlayerPacketSender.Send`,
`W2CPlayerLeavePacketSender.Send`) because missing a spawn/despawn is a
correctness bug (a ghost or a permanently-missing player), while the
routine per-tick position update uses
`ReplicationManager.SendUnreliable` — dropping one position packet is
harmless, the next tick supersedes it.

## `ReplicationManager` — the only class that calls the packet sender directly

Thin on purpose:

```csharp
public class ReplicationManager
{
    public void Broadcast<T>(Opcodes opcode, T payload, IEnumerable<NetPeer> peers);
    public void BroadcastExcept<T>(Opcodes opcode, T payload, IEnumerable<NetPeer> peers, NetPeer except);
    public void Send<T>(Opcodes opcode, T payload, NetPeer peer);
    public void SendUnreliable<T>(Opcodes opcode, T payload, IEnumerable<NetPeer> peers, NetPeer except);
}
```

Every `W2C*Sender` class takes a `ReplicationManager` and calls one of
these rather than touching `WorldserverPacketSender`/LiteNetLib directly —
if you're writing a new sender, follow that pattern rather than reaching
for a lower-level API, so delivery-method choices stay centralized.

## The interaction system

Three small pieces, deliberately kept generic so new interactable types
don't require touching the handler:

```csharp
// IInteractable.cs
public interface IInteractable
{
    Vector3 Position { get; }
    int TemplateId { get; }
    InteractableKind Kind { get; }
    float InteractRange { get; }
}

public enum InteractableKind
{
    Npc = 1,
    // Lootable = 2, QuestObject = 3, ResourceNode = 4, ... add as they're built
}
```

```csharp
// InteractionRegistry.cs — networkId -> IInteractable lookup table
public class InteractionRegistry
{
    public void Register(int networkId, IInteractable interactable);
    public void Unregister(int networkId);
    public bool TryGet(int networkId, out IInteractable interactable);
}
```

`NpcSpawner.SpawnFromTemplate` registers every spawned NPC here
(`interactions.Register(networkId, npc)`), and `DespawnNpc` unregisters it
— that's the entire lifecycle contract a new interactable type needs to
honor to become a valid `C2WInteractPacket` target.

`C2WInteractHandler` is the single reader of this registry, and does a
straightforward authoritative check before firing the Lua hook — existence,
then range (server-side distance check against `InteractionConstants.MaxRange`
= 5 units, independent of whatever range value the target itself reports),
then delegates everything else to Lua:

```csharp
[PacketOpcode(Opcodes.Interact)]
public class C2WInteractHandler : IPacketHandler
{
    public void Handle(NetPeer peer, NetPacketReader reader)
    {
        var packet = MessagePackSerializer.Deserialize<C2WInteractPacket>(reader.GetRemainingBytes());

        if (!playerManager.TryGetNetworkId(peer, out int playerId)) return;

        if (!interactions.TryGet(packet.TargetNetworkId, out var target))
        {
            W2CInteractDeniedPacketSender.Send(peer, "That's no longer there.");
            return;
        }

        if (!playerManager.TryGetPosition(playerId, out Vector3 playerPos)) return;

        float distance = Vector3.Distance(playerPos, target.Position);
        if (distance > target.InteractRange)
        {
            W2CInteractDeniedPacketSender.Send(peer, "Too far away.");
            return;
        }

        var luaPlayer = playerManager.CreateLuaPlayer(peer);
        if (luaPlayer == null) return;

        playerManager.FireInteractEvent(luaPlayer, target);
    }
}
```

Everything past the range check is opaque to C# — what actually happens on
interact (dialogue, a loot placeholder, eventually a shop window) is
entirely a Lua concern, dispatched by `targetTemplateId`. See
[04-LUA-SCRIPTING.md](04-LUA-SCRIPTING.md) for the `npc_guard_example.lua`
worked example, and note the client's raycast that decides *which*
NetworkId to send in `C2WInteractPacket` is UX-only — the range check above
is what actually matters, per `InteractionConstants.MaxRange`'s doc
comment.

### Adding a new interactable kind (e.g. a lootable resource node for a shop system)

1. Add a value to `InteractableKind` (`Lootable = 2`, per the existing
   comment).
2. Create an entity class implementing `IInteractable` (mirror
   `NpcEntity` — `Core/Entities/NpcEntity.cs`).
3. Register/unregister instances with `InteractionRegistry` at
   spawn/despawn time (mirror `NpcSpawner`).
4. Handle the new `targetKind` value in whichever `OnInteract` Lua scripts
   should react to it — `C2WInteractHandler` needs **no changes** for a
   new kind, that's the point of the interface.
