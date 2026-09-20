using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Numerics;
using System.Threading.Tasks;
using ArcheCore.Network.Shared.Packets.PersistenceServer.P2W;
using ArcheCore.Server.World.Core.Interaction;
using ArcheCore.Server.World.Lua.Scripting;
using ArcheCore.Server.World.Lua.Scripting.Bindings;
using ArcheCore.Server.World.Networking.W2C;
using ArcheCore.Server.World.Replication;
using ArcheCore.Server.World.Utils.Config;
using LiteNetLib;
using NLog;
using Worldserver.ArcheCore.PersistenceServer.Scripts;

namespace ArcheCore.Server.World.Managers
{
    public class PlayerManager
    {
        private readonly ConcurrentQueue<Action> _pendingActions = new();
        private readonly LuaEngine _luaEngine = new();
        private readonly ReplicationManager _replication;

        private readonly SessionManager _sessions;
        private readonly InterestManager _interest;
        private readonly PlayerMovementBroadcaster _movement;
        private readonly PlayerSpawnManager _spawn;
        private readonly CharacterPersistence _persistence;
        private readonly AutosaveScheduler _autosave;
        private readonly SnapshotDispatcher _snapshots;
        private readonly TickClock _clock;
        private readonly MovementValidator _validator;
        private readonly JumpEventBroadcaster _jumps;

        public InterestManager Interest => _interest;

        /// <summary>
        /// Exposed so WorldServer can hand it to the packet dispatcher's
        /// service container - C2WMovementHandler takes it as a constructor
        /// parameter.
        /// </summary>
        public JumpEventBroadcaster Jumps => _jumps;

        /// <summary>
        /// Exposed so WorldServer can call ConfigureFromInterest on it at
        /// startup - see the class comment on SnapshotDispatcher.
        /// </summary>
        public SnapshotDispatcher Snapshots => _snapshots;

        private static readonly Logger Logger = LogManager.GetCurrentClassLogger();

        public static string Lua =>
            System.IO.Path.Combine(AppContext.BaseDirectory, "Lua", "Server");

        public PlayerManager(
            SpawnManager spawnManager,
            ReplicationManager replication,
            WorldServerConfig worldConfig,
            PersistenceClient persistence,
            DemoManager demoManager,
            InterestManager interest)
        {
            _replication = replication;
            _interest = interest;

            _sessions = new SessionManager();
            _persistence = new CharacterPersistence(persistence, EnqueueAction);
            _autosave = new AutosaveScheduler(
                _sessions, _persistence, worldConfig.AutosaveIntervalSeconds, worldConfig.TickRate);
            _clock = new TickClock();

            _snapshots = new SnapshotDispatcher(
                _sessions, _interest, (ushort)ArcheCore.Library.Net.Worldserver.Opcodes.W2CWorldSnapshot);

            _validator = new MovementValidator(_replication);

            _jumps = new JumpEventBroadcaster(_sessions, _interest, _replication, _clock);

            _movement = new PlayerMovementBroadcaster(
                _sessions, _interest, _replication, spawnManager, _snapshots, _clock);

            _spawn = new PlayerSpawnManager(
                _sessions, _persistence, _replication, _interest,
                _luaEngine, worldConfig, demoManager, spawnManager,
                _snapshots, _clock, _jumps);
        }

        public void InitializeScripts() => _luaEngine.LoadAllScripts(Lua);

        public void EnqueueAction(Action action) => _pendingActions.Enqueue(action);

        public void DrainActions()
        {
            while (_pendingActions.TryDequeue(out var action))
            {
                try
                {
                    action();
                }
                catch (Exception ex)
                {
                    Logger.Error(ex);
                }
            }
        }

        // --- Tick hooks (called by WorldServer.RunTickLoopAsync) ---

        public void AdvanceTick(uint tick) => _clock.Advance(tick);

        public void FlushSnapshots(uint tick) => _snapshots.Flush(tick);

        public void RunAutosave(uint tick) => _autosave.Tick(tick);

        /// <summary>
        /// Shutdown only - call AFTER the tick loop has stopped (this touches
        /// sessions from the calling thread). Saves every in-world character
        /// that has unsaved changes and waits for all of them.
        /// </summary>
        public async Task SaveAllAsync()
        {
            var saves = new List<Task<bool>>();

            foreach (var peer in _sessions.GetAllConnectedPeers())
            {
                if (peer.Tag is not PlayerSession { NetworkId: not null } s || !s.IsDirty)
                    continue;

                saves.Add(_persistence.SaveAsync(
                    s.CharacterId, s.AccountId, s.Name, s.Level, s.Position, s.Gold));
                s.MarkSaved();
            }

            if (saves.Count == 0)
                return;

            Logger.Info($"[Shutdown] Saving {saves.Count} character(s)...");
            var results = await Task.WhenAll(saves);

            int failed = 0;
            foreach (var ok in results)
                if (!ok) failed++;

            if (failed == 0) Logger.Info("[Shutdown] All characters saved.");
            else             Logger.Error($"[Shutdown] {failed} of {results.Length} character save(s) FAILED.");
        }

        // --- Session lookups (delegated to SessionManager) ---

        public bool TryGetSession(NetPeer peer, out PlayerSession session) =>
            _sessions.TryGetSession(peer, out session);

        public bool TryGetNetworkId(NetPeer peer, out int networkId) =>
            _sessions.TryGetNetworkId(peer, out networkId);

        public bool TryGetPeer(int networkId, out NetPeer peer) =>
            _sessions.TryGetPeer(networkId, out peer);

        public bool TryGetPeerByName(string name, out NetPeer peer) =>
            _sessions.TryGetPeerByName(name, out peer);

        public IEnumerable<NetPeer> GetAllConnectedPeers() =>
            _sessions.GetAllConnectedPeers();

        public void TrackPendingSelection(NetPeer peer, int accountId) =>
            _sessions.TrackPendingSelection(peer, accountId);

        public int? GetPendingAccountId(NetPeer peer) =>
            _sessions.GetPendingAccountId(peer);

        public bool TryBeginSpawn(NetPeer peer) =>
            _sessions.TryBeginSpawn(peer);

        public int GetLevel(NetPeer peer) => _sessions.GetLevel(peer);

        public long GetCharacterId(NetPeer peer) => _sessions.GetCharacterId(peer);

        public string GetName(NetPeer peer) => _sessions.GetName(peer);

        // --- Spawn/connect lifecycle (delegated to PlayerSpawnManager) ---

        public void HandlePlayerConnected(
            NetPeer peer, int accountId,
            P2WCharacterLoadResponse character,
            bool isNewCharacter = false) =>
            _spawn.HandlePlayerConnected(peer, accountId, character, isNewCharacter);

        public void HandlePlayerDisconnected(NetPeer peer) =>
            _spawn.HandlePlayerDisconnected(peer);

        // --- Movement (delegated to PlayerMovementBroadcaster) ---

        public bool TryGetPosition(int networkId, out Vector3 position) =>
            _movement.TryGetPosition(networkId, out position);

        public void BroadcastPosition(
            NetPeer sender, int networkId, Vector3 position,
            Vector3 velocity = default, float yaw = 0f,
            float pitch = 0f, float roll = 0f, byte state = 0) =>
            _movement.BroadcastPosition(sender, networkId, position, velocity, yaw, pitch, roll, state);

        public bool TryAcceptMovement(NetPeer peer, PlayerSession session, Vector3 position, Vector3 velocity) =>
            _validator.Validate(peer, session, position, velocity) == MovementValidator.Result.Accepted;

        public void NotifyAuthoritativeMove(PlayerSession session, Vector3 position) =>
            _validator.NotifyAuthoritativeMove(session, position);

        // --- Replication surface for non-player entities ---

        public void SetNpcTransform(
            int networkId, Vector3 position, Vector3 velocity,
            float yaw, byte state) =>
            _snapshots.SetTransform(
                networkId, position, velocity,
                yaw, pitch: 0f, roll: 0f, state,
                isNpc: true, _clock.Current);

        public void RemoveReplicatedEntity(int networkId)
        {
            _snapshots.Remove(networkId);
            _jumps.Remove(networkId);
        }

        // --- Interaction system ---

        public LuaPlayer CreateLuaPlayer(NetPeer peer)
        {
            if (!_sessions.TryGetSession(peer, out var session) || session.NetworkId is not int networkId)
                return null;

            return new LuaPlayer(peer, networkId, session.AccountId, _replication);
        }

        public void FireInteractEvent(LuaPlayer player, IInteractable target)
        {
            _luaEngine.FireEvent(
                PlayerEvent.OnInteract,
                player,
                target.TemplateId,
                (int)target.Kind);
        }

        public int LevelUp(NetPeer peer)
        {
            if (!TryGetSession(peer, out var session) || session.NetworkId == null) return -1;
            session.Level += 1;

            _persistence.SaveInBackground(session);
            return session.Level;
        }

        // --- Currency ---

        /// <summary>
        /// The ONLY place gold changes. Every system that grants or spends
        /// gold - shop, quest reward, loot sale, this debug command -
        /// calls this and nothing else touches session.Gold directly.
        ///
        /// Refuses to go negative rather than clamping to zero, because a
        /// caller asking to remove more gold than the player has is a bug
        /// upstream (a shop that let a purchase through it shouldn't
        /// have), and silently clamping would hide that bug behind a
        /// balance that's merely wrong instead of a failed transaction
        /// that's visibly wrong. Callers must check the return value and
        /// not apply whatever the gold was "for" when it comes back false.
        /// </summary>
        /// <param name="delta">Positive to add, negative to spend.</param>
        /// <returns>False if delta was negative and would have taken the
        /// balance below zero. The balance is unchanged in that case.</returns>
        public bool TryAddGold(NetPeer peer, int delta)
        {
            if (!TryGetSession(peer, out var session) || session.NetworkId == null)
                return false;

            if (delta < 0 && session.Gold + delta < 0)
                return false;

            session.Gold += delta;

            // Marks the session dirty via IsDirty (Gold != SavedGold), so
            // this rides the existing autosave cycle - no separate save
            // call needed here. Sent immediately anyway, for the same
            // reason a chat message doesn't wait for autosave: the number
            // on screen should match the number the server just decided,
            // now, not up to AutosaveIntervalSeconds from now.
            W2CGoldUpdatePacketSender.Send(peer, session.Gold);

            return true;
        }
    }
}