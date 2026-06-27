using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Numerics;
using System.Threading.Tasks;

using ArcheCore.Net.Shared.Packets.PersistenceServer.P2W;
using ArcheCore.WorldServer.Lua.Scripting;
using ArcheCore.WorldServer.Lua.Scripting.Bindings;
using ArcheCore.WorldServer.Networking.W2C;
using ArcheCore.Worldserver.Utils.Config;
using LiteNetLib;
using Microsoft.Extensions.Options;
using NLog;
using Worldserver.ArcheCore.PersistenceServer.Scripts;

namespace ArcheCore.WorldServer.Managers
{
    public class PlayerManager
    {
        private int nextNetworkId = 1;

        private readonly Dictionary<NetPeer, int> peerToId = new();
        private readonly Dictionary<int, int> idToAccount = new();
        private readonly Dictionary<int, Vector3> positions = new();
        private readonly Dictionary<int, long> idToCharacterId = new();
        private readonly Dictionary<int, string> idToName = new();
        private readonly Dictionary<int, int> idToLevel = new();
        private readonly Dictionary<int, NetPeer> accountToPeer = new();
        private readonly ConcurrentQueue<Action> pendingActions = new();
        private readonly SpawnManager _spawnManager;
        private readonly ReplicationManager _replication;
        private readonly LuaEngine luaEngine = new();
        private readonly WorldServerConfig _worldConfig;

        private static readonly Logger Logger =
            LogManager.GetCurrentClassLogger();
        public static string Lua =>
            Path.Combine(AppContext.BaseDirectory, "Lua","Server");

        public IReadOnlyDictionary<NetPeer, int> PeerToId => peerToId;

        public Dictionary<int, Vector3> Positions => positions;

        public PlayerManager(
            SpawnManager spawnManager,
            ReplicationManager replication,
            WorldServerConfig worldConfig)
        {
            _spawnManager = spawnManager;
            _replication = replication;
            _worldConfig = worldConfig;
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
                _replication,
                peer,
                _worldConfig.MOTD);

            int newId =
                SpawnPlayer(peer, accountId, character);

            var luaPlayer =
                new LuaPlayer(peer, newId, accountId, _replication);

            luaEngine.FireEvent(
                PlayerEvent.OnConnect,
                luaPlayer);

            _spawnManager.SendCubesToPeer(peer);

            foreach (var kvp in peerToId)
            {
                if (kvp.Value == newId)
                    continue;

                W2CSpawnPlayerPacketSender.Send(
                    _replication,
                    peer,
                    kvp.Value,
                    positions[kvp.Value],
                    false);
            }
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

            peerToId.Remove(peer);
            idToAccount.Remove(networkId);
            positions.Remove(networkId);
            idToCharacterId.Remove(networkId);
            idToName.Remove(networkId);
            idToLevel.Remove(networkId);

            Logger.Info(
                $"Disconnected player {networkId}");

            W2CPlayerLeavePacketSender.Send(
                _replication,
                peerToId.Keys,
                networkId);
        }

        public void BroadcastPosition(
            NetPeer sender,
            int networkId,
            Vector3 position)
        {
            positions[networkId] = position;

            W2CPlayerPositionPacketSender.SendUnreliable(
                _replication,
                peerToId.Keys,
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

            idToAccount[networkId] = accountId;

            positions[networkId] = spawn;

            idToCharacterId[networkId] =
                character.CharacterId;

            idToName[networkId] =
                character.Name;

            idToLevel[networkId] =
                character.Level;

            foreach (var p in peerToId)
            {
                W2CSpawnPlayerPacketSender.Send(
                    _replication,
                    p.Key,
                    networkId,
                    spawn,
                    p.Key == peer);
            }

            Logger.Info(
                $"Spawned {character.Name}");

            return networkId;
        }

        private async Task SaveCharacterAsync(
            long characterId,
            string name,
            int level,
            Vector3 pos)
        {
            var persistence =
                PersistenceClient.Instance;

            if (persistence == null)
            {
                Logger.Warn(
                    "Persistence unavailable");

                return;
            }

            await persistence.W2PCharacter.Save(
                characterId,
                name,
                level,
                pos.X,
                pos.Y,
                pos.Z);

            Logger.Info(
                $"Saved character {characterId}");
        }
    }
}