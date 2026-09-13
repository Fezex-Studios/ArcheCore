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

        public InterestManager Interest => _interest;

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
            _persistence = new CharacterPersistence(persistence);
            _movement = new PlayerMovementBroadcaster(_sessions, _interest, _replication, spawnManager);
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

        // NEW — needed so CharacterData responses can include the name.
        public string GetName(NetPeer peer) => _sessions.GetName(peer);

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
            if (!TryGetSession(peer, out var session)) return -1;
            session.Level += 1;

            _persistence.SaveCharacterAsync(session.CharacterId, session.AccountId, session.Name, session.Level, session.Position);
            return session.Level;
        }
    }
}
