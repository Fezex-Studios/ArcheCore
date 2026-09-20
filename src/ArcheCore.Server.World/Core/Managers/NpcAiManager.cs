using System;
using System.Collections.Concurrent;
using System.Numerics;
using ArcheCore.Server.World.Core.Entities;
using ArcheCore.Network.Shared;
using ArcheCore.Server.World.Networking.W2C;
using LiteNetLib;
using NLog;

namespace ArcheCore.Server.World.Managers
{
    /// <summary>
    /// Runs NPC wander AI and spawner-radius activation. Used to run on its
    /// own background thread (mirroring AAEmu's ActiveRegionTick split off
    /// the main loop); that model is retired now that SpatialGrid and
    /// InterestManager have dropped their internal locking in favor of
    /// single-thread ownership (see those classes' remarks) - a background
    /// AI thread reading the grid concurrently with the main thread's
    /// player-movement writes would corrupt it. Instead, WorldServer's tick
    /// loop calls Tick() once per tick, and the interval gating below
    /// (DecisionInterval / SpawnerScanInterval) keeps waypoint and scan work
    /// from running every tick - same cadence as before, just on the main
    /// thread instead of a second one.
    ///
    /// Threading contract, updated for the single-thread model:
    ///   - InterestManager/SpatialGrid are NOT thread-safe and are only
    ///     ever touched from Tick(), which only WorldServer's tick loop
    ///     calls. Do not call Tick() from anywhere else.
    ///   - The _pendingActions queue (PlayerManager.EnqueueAction) is no
    ///     longer bridging two threads here - it's kept as-is so
    ///     ApplyActivate/ApplyDeactivate/ApplyMove still run through the
    ///     same drain-once-per-tick path as everything else, deferred to
    ///     the start of next tick rather than applied inline. That one
    ///     tick of latency is harmless and keeps this diff small; it can
    ///     be inlined later if it's ever worth removing.
    ///   - Per-NPC wander state (NpcAiState) is touched only from Tick(),
    ///     so the ConcurrentDictionary is no longer required for
    ///     correctness, just left in place since a plain Dictionary buys
    ///     nothing here and this isn't the hot path that needs it.
    ///
    /// CHANGED: NPC movement now goes through SnapshotDispatcher, the same
    /// path players use, instead of ApplyMove fanning out one
    /// W2CNpcPosition datagram per observer per step.
    ///
    /// That fan-out was the exact pattern SnapshotDispatcher was built to
    /// replace - cost scaling with (moving NPCs x observers of each) - and
    /// it was still here because the player migration didn't touch this
    /// file. It also meant NPCs got no LOD treatment at all: an NPC 300
    /// units away cost a distant player the same bandwidth as one standing
    /// next to them, while every player in the world was already being
    /// rate-limited by distance.
    ///
    /// Two consequences worth knowing about:
    ///
    ///   - W2CNpcPositionPacketSender and the client's
    ///     W2CNpcPositionHandler are now dead code on the movement path.
    ///     Left in place rather than deleted; they cost nothing idle and
    ///     the opcode stays valid if something else ever needs a
    ///     single-NPC positional nudge.
    ///   - Removal is now load-bearing. SnapshotDispatcher keeps a flat
    ///     transform dictionary keyed by network id and nothing else prunes
    ///     it, so an NPC written in at spawn and not removed at despawn
    ///     leaks an entry permanently. Spawners activate and deactivate
    ///     continuously as players move around, so that leak would be
    ///     unbounded over a long uptime, not a fixed cost. RemoveNpcInterest
    ///     is the single choke point for NPC despawn and does it there.
    /// </summary>
    public class NpcAiManager
    {
        private static readonly Logger Logger = LogManager.GetCurrentClassLogger();
        private static readonly Random _rng = new();

        private readonly SpawnManager _spawnManager;
        private readonly InterestManager _interest;
        private readonly ReplicationManager _replication;
        private readonly PlayerManager _playerManager;

        private readonly ConcurrentDictionary<int, NpcAiState> _active = new();

        private DateTime _lastSpawnerScan = DateTime.MinValue;
        private DateTime _lastDecisionTick = DateTime.MinValue;
        private DateTime _lastMoveTick = DateTime.MinValue;

        /// <summary>
        /// How often an NPC picks a NEW WAYPOINT. A decision, not a
        /// movement - 300ms is fine for choosing where to wander next,
        /// and re-rolling it every tick would produce jitter, not smoother
        /// motion.
        /// </summary>
        private static readonly TimeSpan DecisionInterval = TimeSpan.FromMilliseconds(300);

        private static readonly TimeSpan SpawnerScanInterval = TimeSpan.FromSeconds(1);

        /// <summary>
        /// Upper bound on the movement delta, so a long hitch - a GC pause,
        /// a debugger breakpoint, the process being suspended - doesn't
        /// teleport every NPC across its leash radius in one step when the
        /// tick loop resumes.
        /// </summary>
        private static readonly TimeSpan MaxMoveDelta = TimeSpan.FromMilliseconds(250);

        private const float WanderSpeed = 2.0f;   // world units/sec
        private const float LeashRadius = 15f;    // max wander distance from spawn origin
        private const float ArriveDistance = 0.5f;

        private class NpcAiState
        {
            public int NetworkId;
            public Vector3 SpawnOrigin;
            public Vector3 CurrentPosition;
            public Vector3? WanderTarget;
        }

        public NpcAiManager(
            SpawnManager spawnManager,
            InterestManager interest,
            ReplicationManager replication,
            PlayerManager playerManager)
        {
            _spawnManager = spawnManager;
            _interest = interest;
            _replication = replication;
            _playerManager = playerManager;
        }

        public void Start()
        {
            _lastSpawnerScan = DateTime.MinValue;
            _lastDecisionTick = DateTime.MinValue;
            _lastMoveTick = DateTime.MinValue;
            Logger.Info("NpcAiManager ready (movement every tick, waypoint decisions every {0}ms, spawner scan every {1}s, driven by the main tick loop).",
                DecisionInterval.TotalMilliseconds, SpawnerScanInterval.TotalSeconds);
        }

        public void Stop()
        {
            // Nothing to tear down - there's no background thread anymore.
            // Kept so WorldServer.StopAsync doesn't need to change.
        }

        /// <summary>
        /// Called once per tick from WorldServer's tick loop, on the same
        /// thread as everything else that touches InterestManager/
        /// SpatialGrid. Movement runs every tick; waypoint decisions and
        /// spawner scans are rate-limited by DecisionInterval/
        /// SpawnerScanInterval so this stays cheap on ticks where neither
        /// is due yet.
        /// </summary>
        public void Tick()
        {
            try
            {
                var now = DateTime.UtcNow;

                if (now - _lastSpawnerScan >= SpawnerScanInterval)
                {
                    _lastSpawnerScan = now;
                    RunSpawnerScan();
                }

                // DECISIONS: pick a new waypoint for any NPC that has
                // arrived. Rate-limited, because "where shall I wander
                // next" does not need re-answering 20 times a second.
                if (now - _lastDecisionTick >= DecisionInterval)
                {
                    _lastDecisionTick = now;
                    RunDecisionStep();
                }

                // MOVEMENT: advance every NPC toward its current waypoint,
                // EVERY TICK.
                //
                // This used to run on the same 300ms clock as the decision
                // above, and that was the bug. An NPC's position only
                // changed once every 6 ticks, so SnapshotDispatcher had the
                // same coordinates to send on the other five - and the
                // client, receiving six samples one tick apart of which
                // five were identical, rendered the NPC standing still for
                // 250ms and then covering 300ms of travel in a single 50ms
                // segment. Stand, dash, stand, dash.
                //
                // Deciding and moving are different questions at different
                // rates. Every tick the NPC takes one tick's worth of a
                // step; every 300ms it reconsiders where it's heading.
                //
                // It also fixes something subtler: the server's own idea of
                // where an NPC is was a staircase, up to 300ms stale.
                // Nothing reads it yet, but melee range, aggro radius and
                // AoE overlap all will, and none of them should be working
                // from a position the NPC left a third of a second ago.
                var moveDelta = _lastMoveTick == DateTime.MinValue
                    ? TimeSpan.Zero
                    : now - _lastMoveTick;

                _lastMoveTick = now;

                if (moveDelta > MaxMoveDelta)
                    moveDelta = MaxMoveDelta;

                if (moveDelta > TimeSpan.Zero)
                    RunMoveStep(moveDelta);
            }
            catch (Exception ex)
            {
                Logger.Error(ex, "[NpcAiManager] Unhandled exception in Tick - continuing.");
            }
        }

        // --- Spawner activation (scan computes, enqueued apply mutates) ---

        private void RunSpawnerScan()
        {
            var (toActivate, toDeactivate) = _spawnManager.ScanSpawners();

            foreach (var pending in toActivate)
            {
                int spawnerId = pending.SpawnerId;
                _playerManager.EnqueueAction(() => ApplyActivate(spawnerId));
            }

            foreach (var spawnerId in toDeactivate)
            {
                int id = spawnerId;
                _playerManager.EnqueueAction(() => ApplyDeactivate(id));
            }
        }

        // Main thread only (enqueued).
        private void ApplyActivate(int spawnerId)
        {
            var spawned = _spawnManager.ApplyActivate(spawnerId);

            foreach (var npc in spawned)
            {
                _active[npc.NetworkId] = new NpcAiState
                {
                    NetworkId = npc.NetworkId,
                    SpawnOrigin = npc.SpawnOrigin,
                    CurrentPosition = npc.Position
                };

                BroadcastSpawnToNearbyPlayers(npc);
            }
        }

        // Main thread only (enqueued).
        private void ApplyDeactivate(int spawnerId)
        {
            var removedIds = _spawnManager.ApplyDeactivate(spawnerId);

            foreach (var id in removedIds)
            {
                _active.TryRemove(id, out _);
                RemoveNpcInterest(id);
            }
        }

        // --- Wander AI ---

        /// <summary>
        /// Give a new waypoint to every NPC that has arrived at its last
        /// one, or never had one. Runs on DecisionInterval.
        ///
        /// Note an NPC that arrives between decision passes simply stands
        /// still until the next one - up to 300ms of idle at each waypoint.
        /// That reads as deliberate pausing rather than as a stall, and
        /// removing it would mean re-rolling targets every tick, which is
        /// twitchier, not smoother.
        /// </summary>
        private void RunDecisionStep()
        {
            foreach (var state in _active.Values)
            {
                if (state.WanderTarget is { } existing &&
                    Vector3.Distance(state.CurrentPosition, existing) >= ArriveDistance)
                {
                    continue; // still travelling - leave it alone
                }

                double angle = _rng.NextDouble() * Math.PI * 2;
                double dist = _rng.NextDouble() * LeashRadius;

                state.WanderTarget = state.SpawnOrigin + new Vector3(
                    (float)(Math.Cos(angle) * dist), 0,
                    (float)(Math.Sin(angle) * dist));
            }
        }

        /// <summary>
        /// Advance every NPC one tick's worth toward its waypoint. Runs
        /// EVERY tick - see the comment in Tick().
        /// </summary>
        private void RunMoveStep(TimeSpan delta)
        {
            foreach (var state in _active.Values)
                StepOne(state, delta);
        }

        private void StepOne(NpcAiState state, TimeSpan delta)
        {
            // No waypoint yet - the next decision pass will give it one.
            // Nothing is reported for this NPC on this tick, which is
            // correct: it genuinely hasn't moved, and SnapshotDispatcher's
            // staleness window lets it go quiet rather than repeating a
            // position nobody needs told about twice.
            if (state.WanderTarget is not { } target)
                return;

            var toTarget = target - state.CurrentPosition;
            var distance = toTarget.Length();
            if (distance <= 0.01f)
                return;

            var direction = Vector3.Normalize(toTarget);
            var step = Math.Min(distance, WanderSpeed * (float)delta.TotalSeconds);
            var newPos = state.CurrentPosition + direction * step;
            state.CurrentPosition = newPos;

            // Velocity for the client to extrapolate along through a
            // dropped snapshot. Movement now runs at tick rate, same as a
            // player's, so an NPC is no longer a special case here - the
            // gap an observer interpolates across is whatever its LOD tier
            // gives it, exactly as for a player.
            //
            // Zero on the step that ARRIVES, which is the NPC equivalent of
            // the client's stop packet: the NPC is about to pick a new
            // random heading, so predicting it onward in the old direction
            // is predicting it somewhere it's specifically not going. The
            // alternative - reporting WanderSpeed right up to the waypoint -
            // walks every observer's copy up to half a unit past the turn
            // and then snaps it back.
            var arriving = distance - step <= ArriveDistance;
            var velocity = arriving ? Vector3.Zero : direction * WanderSpeed;

            // Facing, in radians, matching EntityStateCodec.QuantizeYaw.
            // NPCs previously reported nothing here and the client derived
            // facing from the motion vector as a workaround; sending the
            // real heading costs one atan2 per NPC per 300ms and is correct
            // at the moment of arrival, where the derived version has no
            // motion to work from.
            var yaw = MathF.Atan2(direction.X, direction.Z);

            // NPCs are always on the ground and never jump, so the only
            // bit that applies is Moving - and it's off on the arriving
            // step, matching the zero velocity, so an observer plays the
            // idle animation during the pause at each waypoint instead of
            // a run cycle on the spot.
            var moveState = arriving ? MovementState.None : MovementState.Moving;

            int id = state.NetworkId;
            _playerManager.EnqueueAction(() => ApplyMove(id, newPos, velocity, yaw, (byte)moveState));
        }

        // Main thread only (enqueued). Mirrors PlayerMovementBroadcaster.BroadcastPosition,
        // but for an NPC - no sender peer, and only players in entered/left get packets.
        private void ApplyMove(int networkId, Vector3 newPosition, Vector3 velocity, float yaw, byte state)
        {
            if (!_spawnManager.TryGetNpc(networkId, out var npc))
                return; // despawned since this move was queued - drop it

            npc.Position = newPosition;

            var (entered, left) = _interest.UpdatePosition(networkId, newPosition);

            foreach (var otherId in entered)
            {
                if (SpawnManager.IsNpcId(otherId)) continue;
                if (!_playerManager.TryGetPeer(otherId, out var peer)) continue;
                W2CSpawnNpcPacketSender.Send(_replication, peer, npc);
            }

            foreach (var otherId in left)
            {
                if (SpawnManager.IsNpcId(otherId)) continue;
                if (!_playerManager.TryGetPeer(otherId, out var peer)) continue;
                W2CNpcDespawnPacketSender.Send(_replication, new[] { peer }, networkId);
            }

            // Replaces the per-observer W2CNpcPosition fan-out. Like the
            // player path, this is a dictionary write and nothing more -
            // SnapshotDispatcher.Flush decides who hears about it and at
            // what rate. Enter/leave above stay immediate and reliable,
            // because a spawn or despawn is exactly the kind of event an
            // unreliable snapshot must never be the only delivery of.
            _playerManager.SetNpcTransform(networkId, newPosition, velocity, yaw, state);
        }

        private void BroadcastSpawnToNearbyPlayers(NpcEntity npc)
        {
            // Registers the NPC in the shared grid and immediately tells
            // whichever players are already standing nearby - a brand new
            // NPC has no previous grid entry, so everything in "entered"
            // here is a player discovering it for the first time.
            var (entered, _) = _interest.UpdatePosition(npc.NetworkId, npc.Position);

            // Seed the transform store at spawn. Without this an NPC is
            // invisible to every snapshot until its first AI step lands,
            // which for a freshly activated spawner is up to 300ms of the
            // NPC existing on the client at its spawn packet's position
            // with nothing confirming it.
            _playerManager.SetNpcTransform(
                npc.NetworkId, npc.Position, Vector3.Zero,
                yaw: 0f, state: (byte)MovementState.None);

            foreach (var otherId in entered)
            {
                if (SpawnManager.IsNpcId(otherId)) continue;
                if (!_playerManager.TryGetPeer(otherId, out var peer)) continue;
                W2CSpawnNpcPacketSender.Send(_replication, peer, npc);
            }
        }

        private void RemoveNpcInterest(int networkId)
        {
            var knownByPeers = new System.Collections.Generic.List<NetPeer>();

            foreach (var id in _interest.GetKnownBy(networkId))
            {
                if (SpawnManager.IsNpcId(id)) continue;
                if (!_playerManager.TryGetPeer(id, out var p)) continue;
                knownByPeers.Add(p);
            }

            _interest.Remove(networkId);

            // MUST happen, and must happen here. SnapshotDispatcher's
            // transform store is a flat dictionary with no other pruning -
            // an entry written at spawn and never removed stays forever.
            // Spawners activate and deactivate continuously as players move
            // through the world, so this is an unbounded leak over uptime
            // rather than a bounded one, and a stale transform would also
            // keep feeding a despawned NPC into snapshots (harmless on the
            // client, which ignores unknown ids, but it's wasted bytes in
            // every observer's packet).
            _playerManager.RemoveReplicatedEntity(networkId);

            W2CNpcDespawnPacketSender.Send(_replication, knownByPeers, networkId);
        }
    }
}