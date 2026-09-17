using System;
using System.Numerics;
using System.Runtime.CompilerServices;

namespace ArcheCore.Server.World.Replication
{
    /// <summary>
    /// Wire format for per-tick entity movement. This is the single hottest
    /// thing on the server at scale: bytes-per-entity-update multiplied by
    /// entities-in-AOI multiplied by observers multiplied by tick rate is
    /// the number that decides whether 20k concurrent players fits on one
    /// machine or needs five.
    ///
    /// MessagePack was costing ~40 bytes for a position update (three IEEE
    /// floats plus map keys plus a type header). This gets the same update
    /// to 12 bytes, and a despawn to 5, by:
    ///
    ///   - encoding position RELATIVE to a per-packet origin, so the
    ///     absolute magnitude of the world coordinate stops mattering
    ///   - fixed-point at 1/64 of a unit instead of float32. That's ~1.6cm
    ///     of precision, which is well under what a player can perceive on
    ///     a remote entity that is being interpolated client-side anyway.
    ///   - yaw as a single byte (1.4 degree buckets). Pitch and roll are
    ///     not replicated for ground entities at all; the client derives
    ///     them from terrain.
    ///
    /// The origin is the observer's own position rounded to whole units.
    /// AOI radius is ~150 units, and int16 at 1/64 scale covers +/-512
    /// units, so every entity a player can see is representable with a lot
    /// of headroom. Anything outside that range is clamped, which is
    /// correct: it's about to leave the interest set anyway.
    /// </summary>
    public static class EntityStateCodec
    {
        /// <summary>Fixed-point scale. 64 = 1/64 unit = ~1.6cm precision.</summary>
        public const float PositionScale = 64f;

        /// <summary>Max representable offset from origin, in world units.</summary>
        public const float MaxOffset = short.MaxValue / PositionScale; // ~511.98

        [Flags]
        public enum EntryFlags : byte
        {
            None     = 0,
            Position = 1 << 0,
            Yaw      = 1 << 1,
            IsNpc    = 1 << 2,
            // Reserved for later: velocity hint, state/anim id, dead flag.
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static short Quantize(float value, int origin)
        {
            var scaled = (value - origin) * PositionScale;

            if (scaled > short.MaxValue) return short.MaxValue;
            if (scaled < short.MinValue) return short.MinValue;

            return (short)scaled;
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static float Dequantize(short quantized, int origin)
        {
            return origin + (quantized / PositionScale);
        }

        /// <summary>
        /// Yaw in radians to a single byte. Wraps rather than clamps, since
        /// rotation is cyclic - a yaw of 7 radians is a valid heading, not
        /// an out-of-range value.
        /// </summary>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static byte QuantizeYaw(float radians)
        {
            const float TwoPi = MathF.PI * 2f;

            var normalized = radians % TwoPi;
            if (normalized < 0) normalized += TwoPi;

            return (byte)(normalized / TwoPi * 256f);
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static float DequantizeYaw(byte packed)
        {
            return packed / 256f * (MathF.PI * 2f);
        }

        /// <summary>
        /// True if this entity is close enough to the origin to be encoded
        /// without clamping. Callers use this to decide whether an entity
        /// belongs in the snapshot at all rather than silently shipping a
        /// clamped, wrong position.
        /// </summary>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static bool IsEncodable(Vector3 position, Vector3 origin)
        {
            return MathF.Abs(position.X - origin.X) < MaxOffset
                && MathF.Abs(position.Y - origin.Y) < MaxOffset
                && MathF.Abs(position.Z - origin.Z) < MaxOffset;
        }
    }
}
