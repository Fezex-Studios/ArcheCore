# ArcheCore — scaling to 20k without zone servers

You do not need zone servers. You need bytes-per-player down and the global
lock gone. This is the work, in order, with the code for the first half
already written.

---

## The number that decides everything

```
bandwidth = players × entities_in_AOI × update_rate × bytes_per_update
```

Your current path, roughly:

| | now | after this change |
|---|---|---|
| bytes per entity update | ~40 (MessagePack floats) | **12** |
| datagrams per tick | 1 per observer per mover | **1 per observer** |
| updates for a 150-unit-away entity | 10/sec | **1/sec** |
| AOI recompute | every movement packet | **on cell crossing only** |

At 20k × 150 entities × 10 Hz × 40 bytes that is ~9.6 Gbps and it does not
fit on one machine. At an effective ~15 KB/s per player after LOD tiering
and quantization it is ~2.4 Gbps, which fits comfortably on a 10GbE NIC.
**That difference is the entire zone-server question.**

---

## Files in this drop

| File | Replaces | What it does |
|---|---|---|
| `Core/Replication/EntityStateCodec.cs` | new | Fixed-point quantization. Position → 3× int16, yaw → 1 byte |
| `Core/Replication/SnapshotWriter.cs` | new | Manual binary writer, pooled buffers, MTU-bounded |
| `Core/Replication/SnapshotDispatcher.cs` | most of `PlayerMovementBroadcast.cs` | One datagram per observer per tick, LOD tiers, distance cap |
| `Core/Managers/SpatialGrid.cs` | `Core/Managers/Spatialgrid.cs` | Lock removed, reports cell transitions, allocation-free queries |
| `Core/Managers/InterestManager.cs` | same path | Skips the set-difference unless the entity crossed a cell |
| `Client/SnapshotReader.cs` | new | Unity-side decoder |

---

## Wiring it up

### 1. Add the opcode

In `ArcheCore_Network/Shared` add `W2CWorldSnapshot` to `Opcodes`. It does
**not** go through ProtoCLI — the whole point is that it bypasses
MessagePack.

### 2. Construct the dispatcher in `WorldServer.StartAsync`

```csharp
_interestManager = new InterestManager();
_snapshotDispatcher = new SnapshotDispatcher(
    _sessionManager, _interestManager, (ushort)Opcodes.W2CWorldSnapshot);
```

Register it in `RegisterPackets()`'s `ServiceContainer` alongside the rest.

### 3. Rewrite the tick loop

Your current loop only drains actions and polls events. It needs a real
phase separation, and it needs to own the tick counter the dispatcher
depends on.

```csharp
private async Task RunTickLoopAsync(CancellationToken ct)
{
    var interval = TimeSpan.FromMilliseconds(1000.0 / _world.TickRate);
    using var timer = new PeriodicTimer(interval);
    uint tick = 0;

    while (!ct.IsCancellationRequested && await timer.WaitForNextTickAsync(ct))
    {
        try
        {
            tick++;

            _server?.PollEvents();          // 1. input
            _playerManager.DrainActions();  // 2. simulate
            _npcAiManager.Tick(tick);       //    (now in-loop, see step 5)
            _snapshotDispatcher.Flush(tick);// 3. replicate — the ONLY send point
        }
        catch (Exception ex)
        {
            Logger.Error(ex, "[TickLoop] Unhandled exception in tick — continuing.");
        }
    }
}
```

### 4. Gut `PlayerMovementBroadcaster.BroadcastPosition`

Keep the enter/leave half. Delete the position-broadcast half — that whole
`GetKnownBy().Where().Select()` tail at the bottom is the thing that does
not scale, and it allocated three LINQ enumerators per movement packet.

```csharp
public void BroadcastPosition(NetPeer sender, int networkId, Vector3 position, float yaw, uint tick)
{
    if (sender.Tag is PlayerSession s) s.Position = position;

    var (entered, left) = _interest.UpdatePosition(networkId, position);

    // Spawns/despawns stay RELIABLE and unchanged — loop over entered/left
    // exactly as you do today.
    HandleEntered(sender, networkId, position, entered);
    HandleLeft(sender, networkId, left);

    // Position replication is no longer sent here.
    _snapshots.SetTransform(networkId, position, yaw, isNpc: false, tick);
}
```

Same change in `NpcAiManager` — NPC movement calls `SetTransform` instead
of `W2CNpcPositionPacketSender`.

Call `_snapshots.Remove(networkId)` wherever you call `_interest.Remove`.

### 5. Move NPC AI onto the tick loop

The new `SpatialGrid` has no lock, so `NpcAiManager` **cannot** keep
running on its own thread. Convert `Start()` / its internal loop into a
`Tick(uint tick)` called from the tick loop. Its current 300ms cadence
becomes `if (tick % 3 != 0) return;` at 10 Hz.

This looks like a step backwards. It isn't: one background thread bought
you one extra core at the cost of a lock that caps you at one core
forever. Ownership-per-worker (step 7) buys you all of them.

### 6. Client side

Route `W2CWorldSnapshot` to `SnapshotReader.Read`. Two rules:

- **Track the highest tick applied per entity and drop older ones.**
  Unreliable delivery reorders; applying a stale snapshot is the classic
  source of remote-player stutter.
- **Absence from a snapshot is not a despawn.** It means slower LOD tier or
  the per-packet cap. Despawn only arrives on the reliable channel.

Interpolate toward the received position over ~100–150ms rather than
snapping. Mid-tier entities update at 3 Hz, so without interpolation they
will visibly step.

---

## Then: load test before anything else

Build a headless bot client — a console app using the same
`ArcheCore_Network` shared assembly, connecting, selecting a character, and
walking a random path. Run 500, then 2000, then 5000.

Measure **only** these four:

1. Outbound bytes/sec ÷ connected players — the number from the top of this doc
2. Tick duration p50 / p99 — when p99 exceeds your tick interval you are behind
3. Gen2 collections per minute — target zero at steady state
4. CPU per core — if one core is pegged and the rest idle, you are lock-bound

Do not tune the constants in `SnapshotDispatcher` before you have these.
Every one of them is a guess until you have measured your own world.

---

## Step 7, only after load testing: workers

Partition the world. One `WorldWorker` owns one region, one thread, its own
`SpatialGrid` and `InterestManager`, and never touches another worker's
state. Communication is messages only:

```csharp
public interface IWorkerTransport
{
    ValueTask SendAsync(int workerId, WorkerMessage msg);
    ChannelReader<WorkerMessage> Inbox { get; }
}
```

- `InProcessTransport` → `Channel<WorkerMessage>` (threads on one box)
- `RemoteTransport` → LiteNetLib between processes (many boxes)

Worker code never knows which one it has. **That interface is the whole
zone-server migration.** Write it now; it costs almost nothing today and
means you never have to answer the zone-server question as an architecture
question again — it becomes a config flag.

Two things go with it:

- **Border ghosting.** Each worker publishes a read-only snapshot of its
  edge cells once per tick; neighbours consume it so players near a
  boundary see across it without either worker touching the other's grid.
- **Handoff.** Source serializes the entity, sends `TransferEntity`,
  destination acks, source removes. No reconnect, no loading screen.

## Step 8, only if one box saturates: the gateway

If you do split across machines, clients must **not** connect to world
processes directly. Put a gateway in front that owns all client sockets and
routes to the right worker.

```
Clients → Gateway(s) → WorldOrchestrator → WorldWorkers (regions)
                                              ↕
                                         GameServices
```

Zone crossing needs no reconnect, regions can move between machines live,
one public IP, and your simulation processes never face the internet.

---

## Honest expectations

ArcheAge's live shards ran roughly 2–3k concurrent. 20k in one seamless
world is EVE territory, and EVE gets there with time dilation and heavy
queuing. It is a fine target for the *architecture* — everything above is
the right shape regardless — but set milestones at 500 → 2k → 5k and let
profiling, not planning, decide when you need a second process.
