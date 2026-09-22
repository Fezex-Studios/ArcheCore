using System.Linq;
using System.Numerics;
using ArcheCore.Network.Shared.Packets.PersistenceServer.P2W;
using ArcheCore.Network.Shared.Packets.W2C;
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
            if (peer.ConnectionState != ConnectionState.Connected)
            {
                Logger.Info($"[Spawn] Account={accountId} disconnected before spawning - skipped.");
                return;
            }

            if (peer.Tag is PlayerSession { NetworkId: not null })
            {
                Logger.Warn($"[Spawn] Account={accountId} is already in world - duplicate spawn ignored.");
                return;
            }

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
            session.Gold = character.Gold;

            // Rebuild the dense 20-slot array from the persistence
            // server's sparse (occupied-slots-only) response. A new
            // character has Inventory == null (nothing created yet) -
            // treated the same as an empty array, not a null-ref.
            session.Inventory = new InventorySlot[InventoryConstants.SlotCount];

            if (character.Inventory != null)
            {
                foreach (var dto in character.Inventory)
                {
                    if (dto.Slot < 0 || dto.Slot >= InventoryConstants.SlotCount)
                    {
                        // Data from a slot count change (e.g. SlotCount
                        // shrank after rows already existed for higher
                        // slots) or a bad row - drop it rather than
                        // crash the spawn. Worth grepping this log if it
                        // ever fires for a real player.
                        Logger.Warn(
                            $"[Spawn] CharacterId={character.CharacterId}: ignoring out-of-range " +
                            $"inventory slot {dto.Slot} from persistence.");
                        continue;
                    }

                    session.Inventory[dto.Slot] = new InventorySlot
                    {
                        ItemTemplateId = dto.ItemTemplateId,
                        Quantity       = dto.Quantity
                    };
                }
            }

            session.Position = spawn;
            session.SpawnRequested = true;

            peer.Tag = session;

            if (isNewCharacter)
            {
                // New character - nothing to mark "already matches the
                // database" yet (inventory is empty either way), so this
                // just writes the real spawn point instead of waiting for
                // the first autosave, same as before the inventory pass.
                _persistence.SaveInBackground(session);
            }
            else
            {
                // Just loaded - memory (including the inventory array we
                // just built) matches the database by construction.
                session.MarkSaved();
            }

            _sessions.RegisterNetworkId(networkId, peer);
            _snapshots.SetTransform(
                networkId, spawn, velocity: Vector3.Zero,
                yaw: 0f, pitch: 0f, roll: 0f, state: 0,
                isNpc: false, _clock.Current);

            W2CSpawnPlayerPacketSender.Send(_replication, peer, networkId, spawn, true);

            // Everything the client needs to render itself, in one atomic
            // send: name/level, gold, and full inventory. Pushed blind, no
            // handshake - same approach Gold/Inventory always used, now
            // covering CharacterData too instead of that being a separate
            // client-request round trip (see the old PlayerSpawned opcode,
            // retired).
            W2CEnterWorldPacketSender.Send(
                peer,
                new CharacterData { Level = session.Level, Name = session.Name },
                session.Gold,
                session.Inventory);

            var (entered, _) = _interest.UpdatePosition(networkId, spawn);

            foreach (var otherId in entered)
            {
                // NPCs, harvest nodes, anything non-player already here.
                if (SpawnManager.IsNpcId(otherId))
                {
                    _worldSpawnManager.TrySendSpawnTo(_replication, peer, otherId);
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
