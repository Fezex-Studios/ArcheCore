using MessagePack;

namespace ArcheCore.Network.Shared.Packets.C2W
{
    /// <summary>
    /// Client -> World movement report.
    ///
    /// Started as position only, which is not enough for observers to
    /// render a character: this packet and the snapshot that carries it
    /// onward are both UNRELIABLE, so when one is dropped an observer with
    /// nothing but a stale position has no way to fill the gap and the
    /// remote character freezes.
    ///
    /// What each field is for:
    ///
    ///   POSITION - where. Obvious, and the only thing here that was
    ///   originally sent.
    ///
    ///   VELOCITY - lets an observer extrapolate through a dropped or late
    ///   update rather than stalling. Zero is a positive statement that the
    ///   character is stationary, not a missing value.
    ///
    ///   YAW - carried separately from the direction of travel, because a
    ///   player with mouse-look held faces the camera rather than the way
    ///   they're moving, and a player turning on the spot has a facing that
    ///   changes while velocity stays zero. Deriving facing from motion
    ///   gets both wrong.
    ///
    ///   PITCH and ROLL - for anything whose body isn't upright: gliders,
    ///   swimming, a mount leaning into a slope. Nothing in the client
    ///   produces non-zero values for these yet, so today they cost nothing
    ///   (the snapshot writer flag-gates them and skips the bytes when
    ///   they're flat). They're plumbed end to end so that when something
    ///   does tilt the character, it replicates without another wire change.
    ///
    ///   STATE - what the character is doing. See MovementState. This is
    ///   what an observing client selects an animation from; without it
    ///   remote characters can only ever be posed, never animated.
    ///
    /// Units: world units, world units/second, and RADIANS for all three
    /// rotation axes, to match EntityStateCodec. Unity's eulerAngles are
    /// degrees, so the sender converts.
    /// </summary>
    [MessagePackObject(true)]
    public class C2WPlayerMovePacket
    {
        public float x;
        public float y;
        public float z;

        public float vx;
        public float vy;
        public float vz;

        /// <summary>Facing, in radians.</summary>
        public float yaw;

        /// <summary>Nose up/down, in radians. Zero for an upright character.</summary>
        public float pitch;

        /// <summary>Bank left/right, in radians. Zero for an upright character.</summary>
        public float roll;

        /// <summary>MovementState bitfield. Client-reported - see the trust note on that enum.</summary>
        public byte state;
    }
}