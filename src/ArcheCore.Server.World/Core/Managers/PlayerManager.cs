using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Numerics;
using ArcheCore.Network.Shared.Packets.PersistenceServer.P2W;
using ArcheCore.Server.World.Core.Interaction;
using ArcheCore.Server.World.Lua.Scripting;
using ArcheCore.Server.World.Lua.Scripting.Bindings;
using ArcheCore.Server.World.Utils.Config;
using LiteNetLib;
using NLog;
using Worldserver.ArcheCore.PersistenceServer.Scripts;

namespace ArcheCore.Server.World.Managers
{
    /// <summary>
    /// What PlayerManager used to be a 448-line god object doing session
    /// tracking, spawning, movement broadcast, character saving, Lua event
    /// firing, and interaction-player creation, all in one class.
    ///
    /// It's now a thin coordinator: it owns instances of the four classes
    /// each responsibility was split into (SessionManager,
    /// PlayerSpawnManager, PlayerMovementBroadcaster, CharacterPersistence)
    /// and delegates to whichever one actually owns the data. Every public
    /// method that existed before still exists here with the same
    /// signature, so no handler or call site anywhere else needed to
    /// change - this is the "thin facade during the transition" the split
    /// was designed around.
    ///
    /// What's left directly on PlayerManager itself: the pending-action
    /// queue, the Lua engine, and the two bits of interaction glue
    /// (CreateLuaPlayer / FireInteractEvent) that don't cleanly belong to
    /// any one of the four extracted pieces since they touch session data,
    /// replication, and Lua all at once.
    /// </summary>
    public class PlayerManager
    {
        private readonly ConcurrentQueue<Action> _pendingActions = new();
        private readonly LuaEngine _luaEngine = new();
        private readonly ReplicationManager _replication;

        private readonly SessionManager _sessions;
        private readonly InterestManager _interest = new();
        private readonly PlayerMovementBroadcaster _movement;
        private readonly PlayerSpawnManager _spawn;
        private readonly CharacterPersistence _persistence;

        public InterestManager Interest => _interest;

        private static readonly Logger Logger = LogManager.GetCurrentClassLogger();

        public static string Lua =>
            System.IO.Path.Combine(AppContext.BaseDirectory, "Lua", "Server");

        public PlayerManager(
            SpawnManager spawnManager,
            ReplicationManager replication,
            WorldServerConfig worldConfig,
            PersistenceClient persistence,
            DemoManager demoManager)
        {
            _replication = replication;

            _sessions = new SessionManager();
            _persistence = new CharacterPersistence(persistence);
            _movement = new PlayerMovementBroadcaster(_sessions, _interest, _replication);
            _spawn = new PlayerSpawnManager(
                _sessions, _persistence, _replication, _interest,
                _luaEngine, worldConfig, demoManager, spawnManager);
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

        public int GetLevel(NetPeer peer) => _sessions.GetLevel(peer);

        public long GetCharacterId(NetPeer peer) => _sessions.GetCharacterId(peer);

        // --- Spawn/connect lifecycle (delegated to PlayerSpawnManager) ---

        public void HandlePlayerConnected(
            NetPeer peer, int accountId,
            P2WCharacterLoadResponse character) =>
            _spawn.HandlePlayerConnected(peer, accountId, character);

        public void HandlePlayerDisconnected(NetPeer peer) =>
            _spawn.HandlePlayerDisconnected(peer);

        // --- Movement (delegated to PlayerMovementBroadcaster) ---

        public bool TryGetPosition(int networkId, out Vector3 position) =>
            _movement.TryGetPosition(networkId, out position);

        public void BroadcastPosition(NetPeer sender, int networkId, Vector3 position) =>
            _movement.BroadcastPosition(sender, networkId, position);

        // --- Interaction system ---
        // Kept here rather than in one of the four extracted classes:
        // this touches session data, the replication manager, and Lua all
        // at once, so no single split-out class owns everything it needs.

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
    }
}
