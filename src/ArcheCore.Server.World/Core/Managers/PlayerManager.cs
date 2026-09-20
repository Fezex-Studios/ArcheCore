using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Numerics;
using System.Threading.Tasks;
using ArcheCore.Network.Shared.Packets.PersistenceServer.P2W;
using ArcheCore.Server.World.Core.Interaction;
using ArcheCore.Server.World.Lua.Scripting;
using ArcheCore.Server.World.Lua.Scripting.Bindings;
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
        ///
        /// Built in here rather than registered in ServerBootStrap because
        /// it needs SessionManager and TickClock, and both of those are
        /// created with `new` in this constructor rather than coming from
        /// DI. Registering it in DI would mean it couldn't be resolved.
        /// </summary>
        public JumpEventBroadcaster Jumps => _jumps;

        /// <summary>
        /// Exposed so WorldServer can call ConfigureFromInterest on it at
        /// startup.
        ///
        /// The snapshot LOD tier boundaries and InterestManager's
        /// spawn/despawn radii describe the same thing - how far away an
        /// entity is - from two directions, and they used to be independent
        /// constants in two files. They drifted: MidRange sat at 80 while
        /// DespawnRadius was 85, so entities in the 80-85 hysteresis band
        /// were still being rendered while falling into the slowest
        /// replication tier. Deriving one from the other makes that class
        /// of mistake impossible rather than merely unlikely.
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

            // One dispatcher, shared by movement (writes transforms), spawn
            // (writes the initial transform + removes on disconnect), and
            // now NpcAiManager via SetNpcTransform/RemoveReplicatedEntity.
            _snapshots = new SnapshotDispatcher(
                _sessions, _interest, (ushort)ArcheCore.Library.Net.Worldserver.Opcodes.W2CWorldSnapshot);

            // Owns the validator so C2WMovementHandler doesn't need a
            // ReplicationManager of its own just to send a correction -
            // which would mean touching the handler's DI registration.
            _validator = new MovementValidator(_replication);

            // Turns the rising edge of MovementState.Jumping into one
            // reliable W2CJumpEvent per observer. Must be constructed after
            // _sessions and _clock, which it depends on.
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

        /// <summary>Before PollEvents: sets "now" for SetTransform calls made mid-tick.</summary>
        public void AdvanceTick(uint tick) => _clock.Advance(tick);

        /// <summary>After PollEvents: the only place position packets leave the server.</summary>
        public void FlushSnapshots(uint tick) => _snapshots.Flush(tick);

        /// <summary>Once per tick: saves this tick's slice of dirty characters.</summary>
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

                saves.Add(_persistence.SaveAsync(s.CharacterId, s.AccountId, s.Name, s.Level, s.Position));
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

        /// <summary>See SessionManager.TryBeginSpawn - one select/create per peer.</summary>
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

        /// <param name="velocity">
        /// World units/second, client-reported. Note the parameter ORDER:
        /// velocity comes before yaw. Both are presentation state for other
        /// clients and neither feeds simulation.
        /// </param>
        public void BroadcastPosition(
            NetPeer sender, int networkId, Vector3 position,
            Vector3 velocity = default, float yaw = 0f,
            float pitch = 0f, float roll = 0f, byte state = 0) =>
            _movement.BroadcastPosition(sender, networkId, position, velocity, yaw, pitch, roll, state);

        /// <summary>
        /// Gate in front of BroadcastPosition. Returns false when the
        /// reported position wasn't believed, in which case the caller must
        /// NOT broadcast it - a rejected position that still reaches the
        /// interest grid and the transform store defeats the whole point of
        /// rejecting it.
        /// </summary>
        public bool TryAcceptMovement(NetPeer peer, PlayerSession session, Vector3 position, Vector3 velocity) =>
            _validator.Validate(peer, session, position, velocity) == MovementValidator.Result.Accepted;

        /// <summary>
        /// Tell the validator the SERVER moved this character. Must be
        /// called for teleports, respawns and displacement skills, or the
        /// client's next honest report reads as a teleport and gets snapped
        /// back - undoing the server's own move.
        /// </summary>
        public void NotifyAuthoritativeMove(PlayerSession session, Vector3 position) =>
            _validator.NotifyAuthoritativeMove(session, position);

        // --- Replication surface for non-player entities ---
        //
        // NpcAiManager needs to write into the same transform store players
        // use, but SnapshotDispatcher and TickClock are both constructed and
        // owned here. These two methods are the whole surface rather than
        // exposing the dispatcher, so "what tick is it" stays a detail of
        // this class and callers can't accidentally write a stale timestamp.

        /// <summary>
        /// Record an NPC's transform for the next snapshot flush. Cheap by
        /// design - one dictionary write, no sends.
        /// </summary>
        /// <param name="yaw">Facing, in radians.</param>
        public void SetNpcTransform(
            int networkId, Vector3 position, Vector3 velocity,
            float yaw, byte state) =>
            _snapshots.SetTransform(
                networkId, position, velocity,
                yaw, pitch: 0f, roll: 0f, state,
                isNpc: true, _clock.Current);

        /// <summary>
        /// Drop an entity from the transform store. MUST be called when any
        /// replicated entity despawns - the store has no other pruning, so a
        /// missed call is a permanent leak, and for NPCs (whose spawners
        /// cycle continuously as players move) an unbounded one.
        ///
        /// The jump broadcaster is pruned here for the same reason, plus a
        /// second one: it keeps the last state byte per entity to detect
        /// the rising edge of a jump, and LiteNetLib recycles network ids.
        /// A recycled id that inherited a stale Jumping bit would have its
        /// first real jump swallowed, because the edge would look like it
        /// had already happened.
        /// </summary>
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
    }
}