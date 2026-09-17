using System;
using System.Collections.Concurrent;
using System.Linq;
using System.Numerics;
using ArcheCore.Server.World.Core.Entities;
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
    /// (AiInterval / SpawnerScanInterval) keeps the actual AI/scan work
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
        private DateTime _lastAiTick = DateTime.MinValue;

        private static readonly TimeSpan AiInterval = TimeSpan.FromMilliseconds(300);
        private static readonly TimeSpan SpawnerScanInterval = TimeSpan.FromSeconds(1);

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
            _lastAiTick = DateTime.MinValue;
            Logger.Info("NpcAiManager ready (AI tick every {0}ms, spawner scan every {1}s, driven by the main tick loop).",
                AiInterval.TotalMilliseconds, SpawnerScanInterval.TotalSeconds);
        }

        public void Stop()
        {
            // Nothing to tear down - there's no background thread anymore.
            // Kept so WorldServer.StopAsync doesn't need to change.
        }

        /// <summary>
        /// Called once per tick from WorldServer's tick loop, on the same
        /// thread as everything else that touches InterestManager/
        /// SpatialGrid. Internally rate-limited by AiInterval/
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

                if (now - _lastAiTick >= AiInterval)
                {
                    var delta = _lastAiTick == DateTime.MinValue ? AiInterval : now - _lastAiTick;
                    _lastAiTick = now;
                    RunAiStep(delta);
                }
            }
            catch (Exception ex)
            {
                Logger.Error(ex, "[NpcAiManager] Unhandled exception in Tick - continuing.");
            }
        }

        // --- Spawner activation (background thread computes, main thread applies) ---

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

        // --- Wander AI (background thread) ---

        private void RunAiStep(TimeSpan delta)
        {
            foreach (var state in _active.Values)
                StepOne(state, delta);
        }

        private void StepOne(NpcAiState state, TimeSpan delta)
        {
            if (state.WanderTarget is not { } target ||
                Vector3.Distance(state.CurrentPosition, target) < ArriveDistance)
            {
                double angle = _rng.NextDouble() * Math.PI * 2;
                double dist = _rng.NextDouble() * LeashRadius;
                target = state.SpawnOrigin + new Vector3(
                    (float)(Math.Cos(angle) * dist), 0,
                    (float)(Math.Sin(angle) * dist));
                state.WanderTarget = target;
            }

            var toTarget = target - state.CurrentPosition;
            var distance = toTarget.Length();
            if (distance <= 0.01f)
                return;

            var step = Math.Min(distance, WanderSpeed * (float)delta.TotalSeconds);
            var newPos = state.CurrentPosition + Vector3.Normalize(toTarget) * step;
            state.CurrentPosition = newPos;

            int id = state.NetworkId;
            _playerManager.EnqueueAction(() => ApplyMove(id, newPos));
        }

        // Main thread only (enqueued). Mirrors PlayerMovementBroadcaster.BroadcastPosition,
        // but for an NPC - no sender peer, and only players in entered/left get packets.
        private void ApplyMove(int networkId, Vector3 newPosition)
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

            var knownByPeers = _interest.GetKnownBy(networkId)
                .Where(id => !SpawnManager.IsNpcId(id))
                .Select(id => _playerManager.TryGetPeer(id, out var p) ? p : null)
                .Where(p => p != null);

            W2CNpcPositionPacketSender.SendUnreliable(_replication, knownByPeers, networkId, newPosition);
        }

        private void BroadcastSpawnToNearbyPlayers(NpcEntity npc)
        {
            // Registers the NPC in the shared grid and immediately tells
            // whichever players are already standing nearby - a brand new
            // NPC has no previous grid entry, so everything in "entered"
            // here is a player discovering it for the first time.
            var (entered, _) = _interest.UpdatePosition(npc.NetworkId, npc.Position);

            foreach (var otherId in entered)
            {
                if (SpawnManager.IsNpcId(otherId)) continue;
                if (!_playerManager.TryGetPeer(otherId, out var peer)) continue;
                W2CSpawnNpcPacketSender.Send(_replication, peer, npc);
            }
        }

        private void RemoveNpcInterest(int networkId)
        {
            var knownByPeers = _interest.GetKnownBy(networkId)
                .Where(id => !SpawnManager.IsNpcId(id))
                .Select(id => _playerManager.TryGetPeer(id, out var p) ? p : null)
                .Where(p => p != null)
                .ToList();

            _interest.Remove(networkId);

            W2CNpcDespawnPacketSender.Send(_replication, knownByPeers, networkId);
        }
    }
}