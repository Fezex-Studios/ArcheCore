using MessagePack;

namespace ArcheCore.Network.Shared.Packets.C2W
{
    /// <summary>
    /// Client -> World movement report.
    ///
    /// CHANGED: used to be position only. Position alone is not enough for
    /// the receiving clients to render remote players smoothly, because
    /// this packet is UNRELIABLE and the snapshot that carries it onward is
    /// too. When one is dropped the observer has nothing to fill the gap
    /// with and the remote character freezes until the next one lands.
    ///
    /// Velocity is what lets an observer extrapolate through a dropped or
    /// late update instead of stalling. Yaw is separate from velocity
    /// because a player can turn on the spot (velocity zero, facing
    /// changing) and because with mouse-look held the character faces the
    /// camera rather than the direction of travel — deriving facing from
    /// the movement vector gets both of those wrong.
    ///
    /// Units: world units, world units/second, and RADIANS for yaw. Radians
    /// to match EntityStateCodec.QuantizeYaw, which is what this ends up in.
    /// Unity's transform.eulerAngles.y is degrees, so the sender multiplies
    /// by Mathf.Deg2Rad before it goes on the wire.
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
    }
}