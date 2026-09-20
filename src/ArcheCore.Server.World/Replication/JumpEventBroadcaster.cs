using System.Collections.Generic;
using System.Numerics;
using ArcheCore.Network.Shared;
using ArcheCore.Network.Shared.Packets.W2C;
using ArcheCore.Network.Worldserver;
using ArcheCore.Server.World.Managers;
using ArcheCore.Server.World.Networking.W2C;
using LiteNetLib;

namespace ArcheCore.Server.World.Replication
{
    /// <summary>
    /// Turns "this character's Jumping bit just went from clear to set"
    /// into a single reliable event sent to everyone who can see them.
    ///
    /// WHY THE RISING EDGE AND NOT A DEDICATED C2W OPCODE
    ///
    /// The state byte already arrives on every movement packet, and it
    /// already carries a Jumping bit. Adding a C2WJump opcode would create
    /// a second source of truth about the same fact, and the two can
    /// disagree - a client that sends C2WJump without setting the bit, or
    /// sets the bit without sending the opcode, produces a character whose
    /// animation and whose trajectory tell different stories. Deriving the
    /// event from the byte means there is exactly one thing to be wrong
    /// about.
    ///
    /// It also means no client change is needed to PRODUCE jumps. The
    /// client already sets the bit.
    ///
    /// WHAT THE CLIENT IS AND ISN'T TRUSTED WITH
    ///
    /// Trusted: that a jump happened, and the horizontal velocity at
    /// takeoff. Horizontal is already inside MovementValidator's speed
    /// budget, so a lie there is caught by machinery that exists.
    ///
    /// Not trusted: the vertical velocity. That comes from
    /// MovementConstants.JumpVelocity. This matters more than the usual
    /// "a lie only makes the liar look wrong", because observers SIMULATE
    /// this number for most of a second - a client reporting 40 would have
    /// everyone else faithfully render it rocketing into the sky. The
    /// client says that it jumped; the server says how high.
    ///
    /// ONE PACKET PER JUMP PER OBSERVER. At the moment of takeoff only.
    /// Nothing further is sent for the arc - snapshots continue as normal
    /// underneath it, and the client reconciles.
    ///
    /// NOT THREAD SAFE. Called from the tick thread inside the movement
    /// handler, same as everything else that touches the interest manager.
    /// </summary>
    public sealed class JumpEventBroadcaster
    {
        private readonly SessionManager _sessions;
        private readonly InterestManager _interest;
        private readonly ReplicationManager _replication;
        private readonly TickClock _clock;

        /// <summary>
        /// Last state byte seen per entity, so the rising edge can be
        /// detected. Only entities that have moved at least once appear
        /// here; a missing entry reads as MovementState.None, which makes
        /// a first packet that already has Jumping set count as an edge.
        /// That's correct - it's a character that entered our awareness
        /// mid-jump.
        /// </summary>
        private readonly Dictionary<int, MovementState> _lastState = new(capacity: 4096);

        /// <summary>Reused. Never allocate on the movement path.</summary>
        private readonly List<NetPeer> _observerScratch = new(capacity: 128);

        public JumpEventBroadcaster(
            SessionManager sessions,
            InterestManager interest,
            ReplicationManager replication,
            TickClock clock)
        {
            _sessions = sessions;
            _interest = interest;
            _replication = replication;
            _clock = clock;
        }

        /// <summary>
        /// Call on every accepted movement packet, AFTER validation and
        /// BEFORE or after BroadcastPosition - order doesn't matter, since
        /// this reads the interest set rather than modifying it.
        ///
        /// Cheap in the common case: one dictionary lookup and a bit test.
        /// </summary>
        /// <param name="velocity">
        /// Client-reported. Only X and Z are used; Y is replaced with the
        /// server's own jump velocity.
        /// </param>
        public void OnMovement(
            int networkId,
            Vector3 position,
            Vector3 velocity,
            byte stateByte)
        {
            var state = (MovementState)stateByte;

            _lastState.TryGetValue(networkId, out var previous);
            _lastState[networkId] = state;

            // Rising edge only. Holding the bit set for the whole arc -
            // which is what the client does - must not re-fire the event
            // every tick, or the observer restarts the arc 20 times a
            // second and the character never leaves the ground.
            bool wasJumping = (previous & MovementState.Jumping) != 0;
            bool isJumping  = (state    & MovementState.Jumping) != 0;

            if (!isJumping || wasJumping)
                return;

            Broadcast(networkId, position, velocity);
        }

        /// <summary>
        /// Forget an entity. MUST be called on despawn, or the dictionary
        /// grows for the life of the process and a recycled network id
        /// inherits a stale Jumping bit - which would swallow that new
        /// character's first real jump.
        /// </summary>
        public void Remove(int networkId) => _lastState.Remove(networkId);

        private void Broadcast(int networkId, Vector3 position, Vector3 velocity)
        {
            var observers = _interest.GetKnownBy(networkId);

            _observerScratch.Clear();

            for (int i = 0; i < observers.Count; i++)
            {
                var otherId = observers[i];

                // NPCs are in the interest set but have no peer to tell.
                if (SpawnManager.IsNpcId(otherId))
                    continue;

                if (_sessions.TryGetPeer(otherId, out var peer))
                    _observerScratch.Add(peer);
            }

            // Note the jumper is NOT in this list. Their own client already
            // simulated the jump locally the instant they pressed the key -
            // sending them their own event would make them re-start an arc
            // they're already a quarter of the way through.
            if (_observerScratch.Count == 0)
                return;

            var packet = new W2CJumpEventPacket
            {
                NetworkId = networkId,
                OriginX   = position.X,
                OriginY   = position.Y,
                OriginZ   = position.Z,
                VelocityX = velocity.X,
                VelocityZ = velocity.Z,

                // Server-authored. See the class comment.
                VelocityY = MovementConstants.JumpVelocity,

                Tick = _clock.Current
            };

            W2CJumpEventPacketSender.Send(_replication, _observerScratch, packet);
        }
    }
}