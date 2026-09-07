using System.Linq;
using System.Numerics;
using ArcheCore.Network.Shared.Packets.PersistenceServer.P2W;
using ArcheCore.Server.World.Lua.Scripting;
using ArcheCore.Server.World.Lua.Scripting.Bindings;
using ArcheCore.Server.World.Networking.W2C;
using ArcheCore.Server.World.Utils.Config;
using LiteNetLib;
using NLog;

namespace ArcheCore.Server.World.Managers
{
    /// <summary>
    /// Named PlayerSpawnManager (not just "SpawnManager") to avoid clashing
    /// with the existing SpawnManager, which spawns world objects (NPCs,
    /// pickups, etc.) rather than players.
    ///
    /// Owns the full connect/disconnect lifecycle for a player: turning a
    /// pending, authenticated peer into a spawned character in the world,
    /// and cleanly tearing that down again on disconnect (including the
    /// save-on-disconnect call and interest-list cleanup).
    /// </summary>
    public class PlayerSpawnManager
    {
        private static readonly Logger Logger = LogManager.GetCurrentClassLogger();

        private readonly SessionManager _sessions;
        private readonly CharacterPersistence _persistence;
        private readonly ReplicationManager _replication;
        private readonly InterestManager _interest;
        private readonly LuaEngine _luaEngine;
        private readonly WorldServerConfig _worldConfig;
        private readonly DemoManager _demoManager;
        private readonly SpawnManager _worldSpawnManager;

        public PlayerSpawnManager(
            SessionManager sessions,
            CharacterPersistence persistence,
            ReplicationManager replication,
            InterestManager interest,
            LuaEngine luaEngine,
            WorldServerConfig worldConfig,
            DemoManager demoManager,
            SpawnManager worldSpawnManager)
        {
            _sessions = sessions;
            _persistence = persistence;
            _replication = replication;
            _interest = interest;
            _luaEngine = luaEngine;
            _worldConfig = worldConfig;
            _demoManager = demoManager;
            _worldSpawnManager = worldSpawnManager;
        }

        public void HandlePlayerConnected(
            NetPeer peer,
            int accountId,
            P2WCharacterLoadResponse character)
        {
            if (_sessions.TryGetAccountPeer(accountId, out var existingPeer))
            {
                Logger.Info($"Duplicate login Account={accountId}");

                CleanupPeer(existingPeer, true);
                existingPeer.Disconnect();
            }

            _sessions.RegisterAccountPeer(accountId, peer);

            W2CMOTDPacketSender.Send(peer, _worldConfig.MOTD);

            int newId = SpawnPlayer(peer, accountId, character);

            var luaPlayer = new LuaPlayer(peer, newId, accountId, _replication);
            _luaEngine.FireEvent(PlayerEvent.OnConnect, luaPlayer);

            _worldSpawnManager.SendWorldToPeer(peer);

            _demoManager.OnPlayerJoin(peer);
        }

        public void HandlePlayerDisconnected(NetPeer peer)
        {
            CleanupPeer(peer, true);
        }

        public void CleanupPeer(NetPeer peer, bool save)
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
                _persistence.SaveCharacterAsync(
                    session.CharacterId,
                    session.AccountId,
                    session.Name,
                    session.Level,
                    session.Position);
            }

            _sessions.UnregisterAccountPeerIfCurrent(session.AccountId, peer);

            var knownByPeers = _interest.GetKnownBy(networkId)
                .Select(id => _sessions.TryGetPeer(id, out var p) ? p : null)
                .Where(p => p != null)
                .ToList();

            _interest.Remove(networkId);

            _sessions.UnregisterNetworkId(networkId);
            peer.Tag = null;

            Logger.Info($"Disconnected player {networkId}");

            W2CPlayerLeavePacketSender.Send(_replication, knownByPeers, networkId);
        }

        private int SpawnPlayer(
            NetPeer peer,
            int accountId,
            P2WCharacterLoadResponse character)
        {
            int networkId = _sessions.NextNetworkId();

            Vector3 spawn = new(character.X, character.Y, character.Z);

            // Session was created back in TrackPendingSelection; fill in
            // everything that was missing until now. Assigning NetworkId
            // here is what turns "pending" into "in-world" - nothing else
            // needs to explicitly clear the pending state.
            var session = peer.Tag as PlayerSession
                ?? new PlayerSession { AccountId = accountId };

            session.NetworkId = networkId;
            session.CharacterId = character.CharacterId;
            session.Name = character.Name;
            session.Level = character.Level;
            session.Position = spawn;

            peer.Tag = session;

            _sessions.RegisterNetworkId(networkId, peer);

            W2CSpawnPlayerPacketSender.Send(_replication, peer, networkId, spawn, true);

            var (entered, _) = _interest.UpdatePosition(networkId, spawn);

            foreach (var otherId in entered)
            {
                if (!_sessions.TryGetPeer(otherId, out var otherPeer))
                    continue;

                _sessions.TryGetSessionByNetworkId(otherId, out var otherSession);
                var otherPos = otherSession?.Position ?? default;

                W2CSpawnPlayerPacketSender.Send(_replication, peer, otherId, otherPos, false);
                W2CSpawnPlayerPacketSender.Send(_replication, otherPeer, networkId, spawn, false);
            }

            Logger.Info($"Spawned {character.Name}");

            return networkId;
        }
    }
}
