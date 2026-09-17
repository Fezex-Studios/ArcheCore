using ArcheCore.Server.World.Networking.W2C;
using ArcheCore.Server.World.Replication;
using LiteNetLib;
using System.Numerics;

namespace ArcheCore.Server.World.Managers
{
    /// <summary>
    /// Everything to do with telling other players where a player is -
    /// interest-list crossing (enter/leave) plus movement replication.
    ///
    /// CHANGED: the bottom of BroadcastPosition used to loop every peer in
    /// this player's interest set and send them an immediate, uncapped,
    /// full-rate position packet on EVERY movement packet received - one
    /// SendUnreliable call per observer per move. At 500 clustered bots
    /// that was ~355,000 packets/sec and ~28KB/s/bot; the whole point of
    /// SnapshotDispatcher is that none of that fan-out happens here
    /// anymore. This method now only records where the mover is
    /// (SetTransform - a dictionary write, no send) and handles
    /// enter/leave, which stay reliable and immediate because a spawn or
    /// despawn is exactly the kind of event that must never be dropped.
    /// SnapshotDispatcher.Flush, called once per tick from WorldServer,
    /// is the only place a position packet actually goes out now.
    /// </summary>
    public class PlayerMovementBroadcaster
    {
        private readonly SessionManager _sessions;
        private readonly InterestManager _interest;
        private readonly ReplicationManager _replication;
        private readonly SpawnManager _spawnManager;
        private readonly SnapshotDispatcher _snapshots;
        private readonly TickClock _clock;

        public PlayerMovementBroadcaster(
            SessionManager sessions,
            InterestManager interest,
            ReplicationManager replication,
            SpawnManager spawnManager,
            SnapshotDispatcher snapshots,
            TickClock clock)
        {
            _sessions = sessions;
            _interest = interest;
            _replication = replication;
            _spawnManager = spawnManager;
            _snapshots = snapshots;
            _clock = clock;
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

        public void BroadcastPosition(NetPeer sender, int networkId, Vector3 position, float yaw = 0f)
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

            // Replaces the old immediate SendUnreliable fan-out. No send
            // happens here - just a dictionary write. SnapshotDispatcher
            // picks this up on the next Flush(tick) and decides who
            // actually needs to hear about it, at what rate, per observer.
            _snapshots.SetTransform(networkId, position, yaw, isNpc: false, _clock.Current);
        }
    }
}