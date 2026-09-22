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
    ///
    /// CHANGED AGAIN: velocity and yaw now come in from the client and get
    /// recorded with the position. Neither is used for simulation here -
    /// they exist purely so observers can render the mover smoothly.
    /// Velocity lets them extrapolate through a dropped snapshot instead of
    /// freezing, and yaw is carried separately from the direction of travel
    /// because a player with mouse-look held faces the camera, not the way
    /// they're moving, and a player turning on the spot has a facing that
    /// changes while velocity stays zero.
    ///
    /// CHANGED AGAIN: pitch, roll and a movement-state byte ride along on
    /// the same path. None of it is simulated here either; state is what
    /// observing clients pick an animation from, and pitch/roll are for
    /// anything whose body isn't upright. Note this method is only reached
    /// for positions MovementValidator already accepted - the handler drops
    /// rejected ones before they get here, so nothing downstream (the
    /// interest grid, the transform store, the session's saved position)
    /// ever sees a position the server didn't believe.
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

        /// <param name="velocity">
        /// Client-reported, world units per second. Trusted the same amount
        /// as the position is - which is to say, not at all in a security
        /// sense, but it is only ever used for other clients' rendering, so
        /// a lie here makes a cheater's character look wrong to other
        /// people rather than giving them an advantage. See the note in
        /// C2WMovementHandler about where real validation belongs.
        /// </param>
        /// <param name="yaw">Facing, in radians.</param>
        /// <param name="pitch">Nose up/down, radians. Zero for an upright character.</param>
        /// <param name="roll">Bank, radians. Zero for an upright character.</param>
        /// <param name="state">MovementState bitfield - what the character is doing.</param>
        public void BroadcastPosition(
            NetPeer sender,
            int networkId,
            Vector3 position,
            Vector3 velocity = default,
            float yaw = 0f,
            float pitch = 0f,
            float roll = 0f,
            byte state = 0)
        {
            if (sender.Tag is PlayerSession senderSession)
                senderSession.Position = position;

            var (entered, left) = _interest.UpdatePosition(networkId, position);

            foreach (var otherId in entered)
            {
                // NPCs, harvest nodes, anything non-player nearby - tell the
                // mover about it; there's no peer to tell about the mover.
                if (SpawnManager.IsNpcId(otherId))
                {
                    _spawnManager.TrySendSpawnTo(_replication, sender, otherId);
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
            _snapshots.SetTransform(
                networkId, position, velocity,
                yaw, pitch, roll, state,
                isNpc: false, _clock.Current);
        }
    }
}