using MessagePack;

namespace ArcheCore.Network.Shared.Packets.W2C
{
    /// <summary>
    /// "Entity N left the ground at P with velocity V." Sent once, RELIABLE,
    /// when a character starts a jump. The observing client integrates the
    /// arc from here; no further packets are needed to describe it.
    ///
    /// WHY AN EVENT AND NOT SAMPLES
    ///
    /// A jump lasts about 0.77 seconds. SnapshotDispatcher's far tier sends
    /// every 10th tick - 2Hz - so a distant jump was being described by one
    /// or two position samples for the whole arc. The interpolator has
    /// nothing to interpolate between and the result reads as a stutter or
    /// as the character barely leaving the ground. Raising the sample rate
    /// fixes it by spending bandwidth proportional to (observers x jumps),
    /// on a trajectory that is completely determined by its first instant.
    ///
    /// One packet instead, and the motion is then independent of tick rate,
    /// LOD tier and packet loss. This is the same reason you'd send "cast
    /// fireball at T" rather than streaming the projectile's position: if
    /// the receiver can derive it, deriving beats transmitting.
    ///
    /// RELIABLE, unlike snapshots, and that is not optional. A dropped
    /// snapshot costs one frame of smoothness because another is 50ms
    /// behind it. A dropped jump event means the observer never starts the
    /// arc at all and watches the character skate along the ground until
    /// the next positional update yanks it upward.
    ///
    /// The velocity is SERVER-AUTHORED - see MovementConstants.JumpVelocity.
    /// The client reports that it jumped; it does not get to say how hard.
    /// Horizontal velocity IS taken from the client, because it's bounded
    /// by MovementValidator's speed budget already and a lie there is
    /// caught by the same machinery that catches speed hacking.
    /// </summary>
    [MessagePackObject(true)]
    public class W2CJumpEventPacket
    {
        /// <summary>Who jumped.</summary>
        public int NetworkId { get; set; }

        /// <summary>
        /// Where the jump started. Full float precision rather than the
        /// quantized form snapshots use: this is a rare packet, the arc is
        /// integrated from it for most of a second, and quantization error
        /// at the origin becomes a visible offset at the apex. The three
        /// extra bytes per axis cost nothing at this frequency.
        /// </summary>
        public float OriginX { get; set; }
        public float OriginY { get; set; }
        public float OriginZ { get; set; }

        /// <summary>
        /// Horizontal velocity at takeoff, world units/second. Client
        /// reported, already inside the validator's speed budget.
        /// </summary>
        public float VelocityX { get; set; }
        public float VelocityZ { get; set; }

        /// <summary>
        /// Upward velocity at takeoff. Server-authored from
        /// MovementConstants.JumpVelocity, NOT from the client's report.
        /// Sent explicitly rather than left implicit so that per-character
        /// jump height (buffs, races, mounts) can land later without a
        /// packet format change - the client already reads it from here.
        /// </summary>
        public float VelocityY { get; set; }

        /// <summary>
        /// Server tick the jump began on. Not used by the current client
        /// simulation, which starts from "now", but it's the only way to
        /// account for the packet's own flight time, and a client that
        /// wants to start the arc already partway through needs it.
        /// </summary>
        public long Tick { get; set; }
    }
}