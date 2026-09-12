using System.Linq;
using System.Numerics;
using ArcheCore.Server.World.Networking.W2C;
using LiteNetLib;

namespace ArcheCore.Server.World.Managers
{
    /// <summary>
    /// Everything to do with telling other players where a player is -
    /// interest-list crossing (enter/leave) plus the unreliable position
    /// tick. Split out of PlayerManager, which used to interleave this
    /// with spawning, session bookkeeping, persistence, and Lua events.
    ///
    /// NPCs live in the same InterestManager/SpatialGrid as players (see
    /// SpawnManager.NpcIdBase), so when a player walks toward an NPC that
    /// was already active before they arrived, that NPC shows up in
    /// `entered` here exactly like another player would. SpawnManager is
    /// used only to tell the two apart (IsNpcId) and look the NpcEntity up
    /// so the right packet type goes out.
    /// </summary>
    public class PlayerMovementBroadcaster
    {
        private readonly SessionManager _sessions;
        private readonly InterestManager _interest;
        private readonly ReplicationManager _replication;
        private readonly SpawnManager _spawnManager;

        public PlayerMovementBroadcaster(
            SessionManager sessions,
            InterestManager interest,
            ReplicationManager replication,
            SpawnManager spawnManager)
        {
            _sessions = sessions;
            _interest = interest;
            _replication = replication;
            _spawnManager = spawnManager;
        }

        public bool TryGetPosition(int networkId, out Vector3 position)
        {
            if (_sessions.TryGetSessionByNetworkId(networkId, out var session))
            {
                position = session.Position;
                return true;
            }

            position = default;
            return false;
        }

        public void BroadcastPosition(NetPeer sender, int networkId, Vector3 position)
        {
            if (sender.Tag is PlayerSession senderSession)
                senderSession.Position = position;

            var (entered, left) = _interest.UpdatePosition(networkId, position);

            foreach (var otherId in entered)
            {
                // NPCs already active nearby - tell the mover about them,
                // but there's no NPC peer to tell about the mover.
                if (SpawnManager.IsNpcId(otherId))
                {
                    if (_spawnManager.TryGetNpc(otherId, out var npc))
                        W2CSpawnNpcPacketSender.Send(_replication, sender, npc);
                    continue;
                }

                if (!_sessions.TryGetPeer(otherId, out var otherPeer))
                    continue;

                W2CSpawnPlayerPacketSender.Send(_replication, otherPeer, networkId, position, false);

                TryGetPosition(otherId, out Vector3 otherPos);

                W2CSpawnPlayerPacketSender.Send(_replication, sender, otherId, otherPos, false);
            }

            foreach (var otherId in left)
            {
                if (SpawnManager.IsNpcId(otherId))
                {
                    W2CNpcDespawnPacketSender.Send(_replication, new[] { sender }, otherId);
                    continue;
                }

                if (!_sessions.TryGetPeer(otherId, out var otherPeer))
                    continue;

                W2CPlayerLeavePacketSender.Send(_replication, new[] { otherPeer }, networkId);
                W2CPlayerLeavePacketSender.Send(_replication, new[] { sender }, otherId);
            }

            var knownByPeers = _interest.GetKnownBy(networkId)
                .Where(id => !SpawnManager.IsNpcId(id))
                .Select(id => _sessions.TryGetPeer(id, out var p) ? p : null)
                .Where(p => p != null);

            W2CPlayerPositionPacketSender.SendUnreliable(
                _replication, knownByPeers, sender, networkId, position);
        }
    }
}
