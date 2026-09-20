using System.Linq;
using System.Numerics;
using ArcheCore.Network.Shared.Packets.PersistenceServer.P2W;
using ArcheCore.Server.World.Lua.Scripting;
using ArcheCore.Server.World.Lua.Scripting.Bindings;
using ArcheCore.Server.World.Networking.W2C;
using ArcheCore.Server.World.Replication;
using ArcheCore.Server.World.Utils.Config;
using LiteNetLib;
using NLog;

namespace ArcheCore.Server.World.Managers
{
    /// <summary>
    /// Owns the full connect/disconnect lifecycle for a player: turning a
    /// pending, authenticated peer into a spawned character in the world,
    /// and tearing that down again on disconnect (including the
    /// save-on-disconnect call, interest cleanup and snapshot removal).
    ///
    /// Tick thread only.
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
        private readonly SnapshotDispatcher _snapshots;
        private readonly TickClock _clock;
        private readonly JumpEventBroadcaster _jumps;

        public PlayerSpawnManager(
            SessionManager sessions,
            CharacterPersistence persistence,
            ReplicationManager replication,
            InterestManager interest,
            LuaEngine luaEngine,
            WorldServerConfig worldConfig,
            DemoManager demoManager,
            SpawnManager worldSpawnManager,
            SnapshotDispatcher snapshots,
            TickClock clock,
            JumpEventBroadcaster jump
            
            
            
            )
        {
            _sessions = sessions;
            _persistence = persistence;
            _replication = replication;
            _interest = interest;
            _luaEngine = luaEngine;
            _worldConfig = worldConfig;
            _demoManager = demoManager;
            _worldSpawnManager = worldSpawnManager;
            _snapshots = snapshots;
            _clock = clock;
            _jumps = jump;
        }

        public void HandlePlayerConnected(
            NetPeer peer,
            int accountId,
            P2WCharacterLoadResponse character,
            bool isNewCharacter)
        {
            // The peer may have disconnected while the character was loading
            // from the persistence server. Spawning it now would create a
            // ghost that nothing ever cleans up.
            if (peer.ConnectionState != ConnectionState.Connected)
            {
                Logger.Info($"[Spawn] Account={accountId} disconnected before spawning - skipped.");
                return;
            }

            // Already in world (duplicate request slipped through) - never spawn twice.
            if (peer.Tag is PlayerSession { NetworkId: not null })
            {
                Logger.Warn($"[Spawn] Account={accountId} is already in world - duplicate spawn ignored.");
                return;
            }

            // Same account logged in from a DIFFERENT connection: kick the old one.
            if (_sessions.TryGetAccountPeer(accountId, out var existingPeer) && existingPeer != peer)
            {
                Logger.Info($"Duplicate login Account={accountId} - disconnecting previous connection");

                CleanupPeer(existingPeer, true);
                existingPeer.Disconnect();
            }

            _sessions.RegisterAccountPeer(accountId, peer);

            W2CMOTDPacketSender.Send(peer, _worldConfig.MOTD);

            int newId = SpawnPlayer(peer, accountId, character, isNewCharacter);

            var luaPlayer = new LuaPlayer(peer, newId, accountId, _replication);
            _luaEngine.FireEvent(PlayerEvent.OnConnect, luaPlayer);

            _demoManager.OnPlayerJoin(peer);
        }

        public void HandlePlayerDisconnected(NetPeer peer)
        {
            CleanupPeer(peer, true);
        }

        public void CleanupPeer(NetPeer peer, bool save)
        {
            // Never spawned (or never authenticated) - nothing to clean up.
            if (peer.Tag is not PlayerSession { NetworkId: int networkId } session)
                return;

            if (save && session.IsDirty)
                _persistence.SaveInBackground(session);

            _sessions.UnregisterAccountPeerIfCurrent(session.AccountId, peer);

            var knownByPeers = _interest.GetKnownBy(networkId)
                .Where(id => !SpawnManager.IsNpcId(id))
                .Select(id => _sessions.TryGetPeer(id, out var p) ? p : null)
                .Where(p => p != null)
                .ToList();

            _interest.Remove(networkId);
            _snapshots.Remove(networkId);
            _jumps.Remove(networkId);

            _sessions.UnregisterNetworkId(networkId);
            peer.Tag = null;

            Logger.Info($"Disconnected player {networkId}");

            W2CPlayerLeavePacketSender.Send(_replication, knownByPeers, networkId);
        }

        private int SpawnPlayer(
            NetPeer peer,
            int accountId,
            P2WCharacterLoadResponse character,
            bool isNewCharacter)
        {
            int networkId = _sessions.NextNetworkId();

            Vector3 spawn = new(character.X, character.Y, character.Z);

            var session = peer.Tag as PlayerSession
                ?? new PlayerSession { AccountId = accountId };

            session.NetworkId = networkId;
            session.CharacterId = character.CharacterId;
            session.Name = character.Name;
            session.Level = character.Level;
            session.Position = spawn;
            session.SpawnRequested = true;

            peer.Tag = session;

            if (isNewCharacter)
            {
                // The DB row was created with a placeholder position; write the
                // real spawn point now instead of waiting for the first autosave.
                _persistence.SaveInBackground(session);
            }
            else
            {
                // Just loaded - memory matches the database.
                session.MarkSaved();
            }

            _sessions.RegisterNetworkId(networkId, peer);
            _snapshots.SetTransform(
                networkId, spawn, velocity: Vector3.Zero,
                yaw: 0f, pitch: 0f, roll: 0f, state: 0,
                isNpc: false, _clock.Current);

            W2CSpawnPlayerPacketSender.Send(_replication, peer, networkId, spawn, true);

            var (entered, _) = _interest.UpdatePosition(networkId, spawn);

            foreach (var otherId in entered)
            {
                if (SpawnManager.IsNpcId(otherId))
                {
                    if (_worldSpawnManager.TryGetNpc(otherId, out var npc))
                        W2CSpawnNpcPacketSender.Send(_replication, peer, npc);
                    continue;
                }

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
