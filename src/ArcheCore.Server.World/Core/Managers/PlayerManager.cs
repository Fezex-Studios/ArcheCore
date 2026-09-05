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

        // Reverse indexes only - these can't live on PlayerSession because
        // they map FROM an id TO a peer, not the other way around.
        private readonly Dictionary<int, NetPeer> idToPeer = new();
        private readonly Dictionary<int, NetPeer> accountToPeer = new();

        private readonly ConcurrentQueue<Action> pendingActions = new();
        private readonly SpawnManager _spawnManager;
        private readonly ReplicationManager _replication;
        private readonly InterestManager _interest = new();
        private readonly LuaEngine luaEngine = new();
        private readonly WorldServerConfig _worldConfig;
        private readonly PersistenceClient _persistence;
        private readonly DemoManager _demoManager;
        public InterestManager Interest => _interest;

        private static readonly Logger Logger =
            LogManager.GetCurrentClassLogger();
        public static string Lua =>
            Path.Combine(AppContext.BaseDirectory, "Lua", "Server");

        public bool TryGetPeer(int networkId, out NetPeer peer) =>
            idToPeer.TryGetValue(networkId, out peer);

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

        // --- Session lookup helpers ---
        // All per-player state now lives on peer.Tag as a PlayerSession.
        // These are the only places that touch peer.Tag directly - every
        // handler goes through one of these instead of casting Tag itself.

        public bool TryGetSession(NetPeer peer, out PlayerSession session)
        {
            if (peer.Tag is PlayerSession s)
            {
                session = s;
                return true;
            }

            session = null;
            return false;
        }

        private bool TryGetSessionByNetworkId(int networkId, out PlayerSession session)
        {
            if (idToPeer.TryGetValue(networkId, out var peer) &&
                peer.Tag is PlayerSession s)
            {
                session = s;
                return true;
            }

            session = null;
            return false;
        }

        public bool TryGetNetworkId(NetPeer peer, out int networkId)
        {
            if (peer.Tag is PlayerSession { NetworkId: int id })
            {
                networkId = id;
                return true;
            }

            networkId = -1;
            return false;
        }
        public IEnumerable<NetPeer> GetAllConnectedPeers()
        {
            return idToPeer.Values;
        }

        public bool TryGetPeerByName(string name, out NetPeer peer)
        {
            foreach (var kvp in idToPeer)
            {
                if (kvp.Value.Tag is PlayerSession s &&
                    string.Equals(s.Name, name, System.StringComparison.OrdinalIgnoreCase))
                {
                    peer = kvp.Value;
                    return true;
                }
            }

            peer = null;
            return false;
        }

        public bool TryGetPosition(int networkId, out Vector3 position)
        {
            if (TryGetSessionByNetworkId(networkId, out var session))
            {
                position = session.Position;
                return true;
            }

            position = default;
            return false;
        }

        // --- Login flow: authenticated, not yet in-world ---

        /// <summary>
        /// Call once a token has been validated. Creates a session for this
        /// peer with no NetworkId yet - that's what "pending" means. Covers
        /// both the "no characters yet, show create" and "pick a character"
        /// states, since both just need to know which account owns the peer
        /// before anything spawns.
        /// </summary>
        public void TrackPendingSelection(NetPeer peer, int accountId)
        {
            peer.Tag = new PlayerSession { AccountId = accountId };
        }

        /// <summary>
        /// True only for a peer that authenticated but hasn't spawned yet
        /// (session exists, NetworkId is still null). A peer that's already
        /// in-world, or never authenticated at all, returns false here.
        /// </summary>
        public int? GetPendingAccountId(NetPeer peer)
        {
            return peer.Tag is PlayerSession { NetworkId: null } session
                ? session.AccountId
                : null;
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
            // Peer never made it past character select (or never
            // authenticated at all) - nothing spawned, nothing to clean up.
            // Tag either holds no session, or a pending one with no
            // NetworkId; either way it's dropped when the peer object is,
            // no explicit removal required.
            if (peer.Tag is not PlayerSession { NetworkId: int networkId } session)
                return;

            if (save)
            {
                _ = SaveCharacterAsync(
                    session.CharacterId,
                    session.AccountId,
                    session.Name,
                    session.Level,
                    session.Position);
            }

            if (accountToPeer.TryGetValue(
                    session.AccountId,
                    out var registered)
                && registered == peer)
            {
                accountToPeer.Remove(session.AccountId);
            }

            var knownByPeers =
                _interest.GetKnownBy(networkId)
                    .Select(id => TryGetPeer(id, out var p) ? p : null)
                    .Where(p => p != null)
                    .ToList();

            _interest.Remove(networkId);

            idToPeer.Remove(networkId);
            peer.Tag = null;

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
            if (sender.Tag is PlayerSession senderSession)
                senderSession.Position = position;

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

                TryGetPosition(otherId, out Vector3 otherPos);

                W2CSpawnPlayerPacketSender.Send(
                    _replication,
                    sender,
                    otherId,
                    otherPos,
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

            // Session was created back in TrackPendingSelection; fill in
            // everything that was missing until now. Assigning NetworkId
            // here is what turns "pending" into "in-world" - nothing else
            // needs to explicitly clear the pending state.
            var session = peer.Tag as PlayerSession
                ?? new PlayerSession { AccountId = accountId };

            session.NetworkId   = networkId;
            session.CharacterId = character.CharacterId;
            session.Name        = character.Name;
            session.Level       = character.Level;
            session.Position    = spawn;

            peer.Tag = session;

            idToPeer[networkId] = peer;

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

                TryGetPosition(otherId, out Vector3 otherPos);

                W2CSpawnPlayerPacketSender.Send(
                    _replication,
                    peer,
                    otherId,
                    otherPos,
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
            long characterId, int accountId, string name, int level, Vector3 pos)
        {
            try
            {
                await _persistence.W2PCharacter.Save(
                    characterId, accountId, name, level, pos.X, pos.Y, pos.Z);

                Logger.Info($"[Save] CharacterId={characterId} saved successfully.");
            }
            catch (Exception ex)
            {
                Logger.Error(ex, $"[Save] FAILED to save CharacterId={characterId}");
            }
        }

        public int GetLevel(NetPeer peer) =>
            peer.Tag is PlayerSession session ? session.Level : -1;

        public long GetCharacterId(NetPeer peer) =>
            peer.Tag is PlayerSession session ? session.CharacterId : -1;

        // --- Interaction system ---

        public LuaPlayer CreateLuaPlayer(NetPeer peer)
        {
            if (peer.Tag is not PlayerSession { NetworkId: int networkId } session)
                return null;

            return new LuaPlayer(peer, networkId, session.AccountId, _replication);
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