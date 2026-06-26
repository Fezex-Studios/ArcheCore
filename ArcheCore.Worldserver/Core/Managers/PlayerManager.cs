using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using ArcheCore.WorldServer;
using ArcheCore.WorldServer.Lua.Scripting;
using ArcheCore.WorldServer.Lua.Scripting.Bindings;
using ArcheCore.WorldServer.Networking.W2C;
using ArcheCore.WorldServer.PersistenceServer;
using ArcheCore.WorldServer.ServerConfig;
using LiteNetLib;
using Shared;
using Shared.Components;
using UnityEngine;
using Worldserver.ArcheCore.PersistenceServer.Scripts;

namespace ArcheCore.WorldServer.Managers
{
    public class PlayerManager
    {
        private int nextNetworkId = 1;

        private readonly Dictionary<NetPeer, int>  peerToId        = new();
        private readonly Dictionary<int, int>      idToAccount     = new();
        private readonly Dictionary<int, Vector3>  positions       = new();
        private readonly Dictionary<int, long>     idToCharacterId = new();
        private readonly Dictionary<int, string>   idToName        = new();
        private readonly Dictionary<int, int>      idToLevel       = new();

        // Tracks one peer per account — used to detect and kick duplicate logins
        private readonly Dictionary<int, NetPeer>  accountToPeer   = new();

        private readonly ConcurrentQueue<Action> pendingActions = new();

        private readonly SpawnManager       _spawnManager;
        private readonly ReplicationManager _replication;
        private readonly LuaEngine          luaEngine = new();

        public IReadOnlyDictionary<NetPeer, int> PeerToId  => peerToId;
        public Dictionary<int, Vector3>          Positions => positions;

        public PlayerManager(
            SpawnManager spawnManager,
            ReplicationManager replication)
        {
            _spawnManager = spawnManager;
            _replication  = replication;
        }

        public void DrainActions()
        {
            while (pendingActions.TryDequeue(out Action action))
            {
                try   { action(); }
                catch (Exception e) { Debug.LogException(e); }
            }
        }

        /// <summary>
        /// Loads every script in the Lua/Server directory once and lets them
        /// register their event hooks. Call exactly once at server start,
        /// before any players can connect.
        /// </summary>
        public void InitializeScripts()
        {
            luaEngine.LoadAllScripts(ServerPaths.Lua);
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
            // ── Duplicate login check ─────────────────────────────────────────
            // If this account is already connected on another peer, kick that
            // peer before letting the new one in. Same behavior as most MMOs:
            // "Your account has been logged in from another location."
            if (accountToPeer.TryGetValue(accountId, out NetPeer existingPeer))
            {
                WorldLogger.Warning(
                    $"[PlayerManager] AccountId={accountId} already connected — kicking existing peer {existingPeer.Address}");

                // Clean up the old peer's data first, then disconnect it.
                // We call our own cleanup directly rather than relying on
                // OnPeerDisconnected firing, because Disconnect() is async
                // and we need the old data gone before spawning the new player.
                CleanupPeer(existingPeer, save: true);
                existingPeer.Disconnect();
            }

            // Register the new peer for this account
            accountToPeer[accountId] = peer;

            W2CMOTDPacketSender.Send(_replication, peer, ConfigService.Config.MOTD);

            int newId = SpawnPlayer(peer, accountId, character);

            LuaPlayer luaPlayer = new LuaPlayer(peer, newId, accountId, _replication);
            luaEngine.FireEvent(PlayerEvent.OnConnect, luaPlayer);

            _spawnManager.SendCubesToPeer(peer);

            foreach (var kvp in peerToId)
            {
                int existingId = kvp.Value;
                if (existingId == newId) continue;

                W2CSpawnPlayerPacketSender.Send(
                    _replication,
                    peer,
                    existingId,
                    positions[existingId],
                    false);
            }
        }

        public void HandlePlayerDisconnected(NetPeer peer)
        {
            CleanupPeer(peer, save: true);
        }

        /// <summary>
        /// Removes all state for a peer. Called on normal disconnect and on
        /// duplicate-login kick. Pass save=true to persist character data.
        /// </summary>
        private void CleanupPeer(NetPeer peer, bool save)
        {
            if (!peerToId.TryGetValue(peer, out int networkId))
                return;

            // Save character if requested and we have data for it
            if (save && idToCharacterId.TryGetValue(networkId, out long characterId))
            {
                string  name  = idToName.GetValueOrDefault(networkId, "Unknown");
                int     level = idToLevel.GetValueOrDefault(networkId, 1);
                Vector3 pos   = positions.GetValueOrDefault(networkId, Vector3.zero);

                _ = SaveCharacterAsync(characterId, name, level, pos);
            }

            // Remove from accountToPeer only if this peer is still the registered one.
            // If a duplicate login already replaced it, don't remove the new peer's entry.
            if (idToAccount.TryGetValue(networkId, out int accountId))
            {
                if (accountToPeer.TryGetValue(accountId, out NetPeer registeredPeer)
                    && registeredPeer == peer)
                {
                    accountToPeer.Remove(accountId);
                }
            }

            peerToId.Remove(peer);
            idToAccount.Remove(networkId);
            positions.Remove(networkId);
            idToCharacterId.Remove(networkId);
            idToName.Remove(networkId);
            idToLevel.Remove(networkId);

            WorldLogger.Info($"[PlayerManager] Player {networkId} (AccountId={accountId}) disconnected");

            W2CPlayerLeavePacketSender.Send(_replication, peerToId.Keys, networkId);
        }

        public void BroadcastPosition(
            NetPeer sender,
            int     networkId,
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
            int     networkId     = nextNetworkId++;
            Vector3 spawnPosition = new Vector3(character.X, character.Y, character.Z);

            peerToId[peer]             = networkId;
            idToAccount[networkId]     = accountId;
            positions[networkId]       = spawnPosition;
            idToCharacterId[networkId] = character.CharacterId;
            idToName[networkId]        = character.Name;
            idToLevel[networkId]       = character.Level;

            foreach (var kvp in peerToId)
            {
                W2CSpawnPlayerPacketSender.Send(
                    _replication,
                    kvp.Key,
                    networkId,
                    spawnPosition,
                    kvp.Key == peer);
            }

            WorldLogger.Info(
                $"[PlayerManager] Spawned player {networkId} (AccountId={accountId}, Name={character.Name})");

            return networkId;
        }

        private async Task SaveCharacterAsync(
            long characterId, string name, int level, Vector3 pos)
        {
            var persistence = PersistenceClient.Instance;

            if (persistence == null)
            {
                Debug.LogWarning("[PlayerManager] PersistenceClient.Instance is null — skipping save");
                return;
            }

            await persistence.W2PCharacter.Save(characterId, name, level, pos.x, pos.y, pos.z);
            WorldLogger.Info($"[PlayerManager] Saved character {characterId} ({name})");
        }
    }
}