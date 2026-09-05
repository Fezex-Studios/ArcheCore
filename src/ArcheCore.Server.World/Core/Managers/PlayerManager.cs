using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Linq;
using System.Numerics;
using System.Threading.Tasks;

using ArcheCore.Network.Shared.Packets.PersistenceServer.P2W;
using ArcheCore.Server.World.Core.Interaction;
using ArcheCore.Server.World.Lua.Scripting;
using ArcheCore.Server.World.Lua.Scripting.Bindings;
using ArcheCore.Server.World.Networking.W2C;
using ArcheCore.Server.World.Utils.Config;
using LiteNetLib;
using Microsoft.Extensions.Options;
using NLog;
using Worldserver.ArcheCore.PersistenceServer.Scripts;

namespace ArcheCore.Server.World.Managers
{
    public class PlayerManager
    {
        private int nextNetworkId = 1;

        private readonly Dictionary<NetPeer, int> peerToId = new();
        private readonly Dictionary<int, NetPeer> idToPeer = new();
        private readonly Dictionary<int, int> idToAccount = new();
        private readonly Dictionary<int, Vector3> positions = new();
        private readonly Dictionary<int, long> idToCharacterId = new();
        private readonly Dictionary<int, string> idToName = new();
        private readonly Dictionary<int, int> idToLevel = new();
        private readonly Dictionary<int, NetPeer> accountToPeer = new();
        private readonly ConcurrentQueue<Action> pendingActions = new();
        private readonly SpawnManager _spawnManager;
        private readonly ReplicationManager _replication;
        private readonly InterestManager _interest = new();
        private readonly LuaEngine luaEngine = new();
        private readonly WorldServerConfig _worldConfig;
        private readonly PersistenceClient _persistence;
        private readonly DemoManager _demoManager;

        // Authenticated, but not yet in-world: peer -> accountId. Covers
        // both the "no characters yet, show create" and "pick a character"
        // states — both need to know which account owns the peer before
        // anything spawns.
        private readonly Dictionary<NetPeer, int> _pendingSelection = new();

        private static readonly Logger Logger =
            LogManager.GetCurrentClassLogger();
        public static string Lua =>
            Path.Combine(AppContext.BaseDirectory, "Lua", "Server");

        public IReadOnlyDictionary<NetPeer, int> PeerToId => peerToId;

        public bool TryGetPeer(int networkId, out NetPeer peer) =>
            idToPeer.TryGetValue(networkId, out peer);

        public Dictionary<int, Vector3> Positions => positions;

        public PlayerManager(
            SpawnManager spawnManager,
            ReplicationManager replication,
            WorldServerConfig worldConfig,
            PersistenceClient persistence,
            DemoManager demoManager
            )
        {
            _spawnManager = spawnManager;
            _replication = replication;
            _worldConfig = worldConfig;
            _persistence = persistence;
            _demoManager = demoManager;
        }

        public void DrainActions()
        {
            while (pendingActions.TryDequeue(out var action))
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

        public void InitializeScripts()
        {
            luaEngine.LoadAllScripts(Lua);
        }

        public void EnqueueAction(Action action)
        {
            pendingActions.Enqueue(action);
        }

        public void TrackPendingSelection(NetPeer peer, int accountId)
        {
            _pendingSelection[peer] = accountId;
        }

        public bool TryGetPendingAccountId(NetPeer peer, out int accountId)
        {
            return _pendingSelection.TryGetValue(peer, out accountId);
        }

        public void ClearPendingCreation(NetPeer peer)
        {
            _pendingSelection.Remove(peer);
        }

        public void HandlePlayerConnected(
            NetPeer peer,
            int accountId,
            P2WCharacterLoadResponse character)
        {
            if (accountToPeer.TryGetValue(accountId, out var existingPeer))
            {
                Logger.Info(
                    $"Duplicate login Account={accountId}");

                CleanupPeer(existingPeer, true);
                existingPeer.Disconnect();
            }

            accountToPeer[accountId] = peer;

            W2CMOTDPacketSender.Send(
                peer,
                _worldConfig.MOTD);

            int newId =
                SpawnPlayer(peer, accountId, character);

            var luaPlayer =
                new LuaPlayer(peer, newId, accountId, _replication);

            luaEngine.FireEvent(
                PlayerEvent.OnConnect,
                luaPlayer);

            _spawnManager.SendWorldToPeer(peer);

            _demoManager.OnPlayerJoin(peer);
        }

        public void HandlePlayerDisconnected(NetPeer peer)
        {
            CleanupPeer(peer, true);
        }

        private void CleanupPeer(
            NetPeer peer,
            bool save)
        {
            if (!peerToId.TryGetValue(peer, out int networkId))
                return;

            int accountId =
                idToAccount.GetValueOrDefault(networkId);

            if (save &&
                idToCharacterId.TryGetValue(
                    networkId,
                    out long characterId))
            {
                string name =
                    idToName.GetValueOrDefault(
                        networkId,
                        "Unknown");

                int level =
                    idToLevel.GetValueOrDefault(
                        networkId,
                        1);

                Vector3 pos =
                    positions.GetValueOrDefault(
                        networkId,
                        Vector3.Zero);

                _ = SaveCharacterAsync(
                    characterId,
                    accountId,
                    name,
                    level,
                    pos);
            }

            if (accountToPeer.TryGetValue(
                    accountId,
                    out var registered)
                && registered == peer)
            {
                accountToPeer.Remove(accountId);
            }

            var knownByPeers =
                _interest.GetKnownBy(networkId)
                    .Select(id => TryGetPeer(id, out var p) ? p : null)
                    .Where(p => p != null)
                    .ToList();

            _interest.Remove(networkId);

            peerToId.Remove(peer);
            idToPeer.Remove(networkId);
            idToAccount.Remove(networkId);
            positions.Remove(networkId);
            idToCharacterId.Remove(networkId);
            idToName.Remove(networkId);
            idToLevel.Remove(networkId);

            Logger.Info(
                $"Disconnected player {networkId}");

            W2CPlayerLeavePacketSender.Send(
                _replication,
                knownByPeers,
                networkId);
        }

        public void BroadcastPosition(
            NetPeer sender,
            int networkId,
            Vector3 position)
        {
            positions[networkId] = position;

            var (entered, left) =
                _interest.UpdatePosition(networkId, position);

            foreach (var otherId in entered)
            {
                if (!TryGetPeer(otherId, out var otherPeer))
                    continue;

                W2CSpawnPlayerPacketSender.Send(
                    _replication,
                    otherPeer,
                    networkId,
                    position,
                    false);

                W2CSpawnPlayerPacketSender.Send(
                    _replication,
                    sender,
                    otherId,
                    positions.GetValueOrDefault(otherId),
                    false);
            }

            foreach (var otherId in left)
            {
                if (!TryGetPeer(otherId, out var otherPeer))
                    continue;

                W2CPlayerLeavePacketSender.Send(
                    _replication,
                    new[] { otherPeer },
                    networkId);

                W2CPlayerLeavePacketSender.Send(
                    _replication,
                    new[] { sender },
                    otherId);
            }

            var knownByPeers =
                _interest.GetKnownBy(networkId)
                    .Select(id => TryGetPeer(id, out var p) ? p : null)
                    .Where(p => p != null);

            W2CPlayerPositionPacketSender.SendUnreliable(
                _replication,
                knownByPeers,
                sender,
                networkId,
                position);
        }

        private int SpawnPlayer(
            NetPeer peer,
            int accountId,
            P2WCharacterLoadResponse character)
        {
            int networkId = nextNetworkId++;

            Vector3 spawn =
                new(
                    character.X,
                    character.Y,
                    character.Z);

            peerToId[peer] = networkId;
            idToPeer[networkId] = peer;

            idToAccount[networkId] = accountId;

            positions[networkId] = spawn;

            idToCharacterId[networkId] =
                character.CharacterId;

            idToName[networkId] =
                character.Name;

            idToLevel[networkId] =
                character.Level;

            W2CSpawnPlayerPacketSender.Send(
                _replication,
                peer,
                networkId,
                spawn,
                true);

            var (entered, _) =
                _interest.UpdatePosition(networkId, spawn);

            foreach (var otherId in entered)
            {
                if (!TryGetPeer(otherId, out var otherPeer))
                    continue;

                W2CSpawnPlayerPacketSender.Send(
                    _replication,
                    peer,
                    otherId,
                    positions[otherId],
                    false);

                W2CSpawnPlayerPacketSender.Send(
                    _replication,
                    otherPeer,
                    networkId,
                    spawn,
                    false);
            }

            Logger.Info(
                $"Spawned {character.Name}");

            return networkId;
        }

        private async Task SaveCharacterAsync(
            long characterId,
            int accountId,
            string name,
            int level,
            Vector3 pos)
        {
            await _persistence.W2PCharacter.Save(
                characterId,
                accountId,
                name,
                level,
                pos.X,
                pos.Y,
                pos.Z
            );
        }

        public int GetLevel(NetPeer peer)
        {
            if (peerToId.TryGetValue(peer, out var id) &&
                idToLevel.TryGetValue(id, out var level))
            {
                return level;
            }

            return -1;
        }

        public long GetCharacterId(NetPeer peer)
        {
            if (peerToId.TryGetValue(peer, out var networkId) &&
                idToCharacterId.TryGetValue(networkId, out var characterId))
            {
                return characterId;
            }

            return -1;
        }

        // --- Interaction system ---

        public LuaPlayer CreateLuaPlayer(NetPeer peer)
        {
            if (!peerToId.TryGetValue(peer, out int networkId))
                return null;

            int accountId = idToAccount.GetValueOrDefault(networkId, -1);

            return new LuaPlayer(peer, networkId, accountId, _replication);
        }

        public void FireInteractEvent(LuaPlayer player, IInteractable target)
        {
            luaEngine.FireEvent(
                PlayerEvent.OnInteract,
                player,
                target.TemplateId,
                (int)target.Kind);
        }
    }
}