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
    ///
    /// ADDED: optional velocity, three SIGNED BYTES at 1/4 unit-per-second
    /// resolution. Deliberately the cheapest useful encoding rather than
    /// matching position's precision, for two reasons. Velocity is only
    /// ever used to extrapolate across a gap of at most a few hundred
    /// milliseconds, so an error of 0.25 u/s is a couple of centimetres of
    /// drift that the next real update corrects anyway. And it is optional
    /// per entry (see EntryFlags.Velocity) precisely so the dispatcher can
    /// spend those three bytes only on near-tier entities, where
    /// extrapolation is actually visible, and keep far-tier entries at the
    /// original 12 bytes.
    /// </summary>
    public static class EntityStateCodec
    {
        /// <summary>Fixed-point scale. 64 = 1/64 unit = ~1.6cm precision.</summary>
        public const float PositionScale = 64f;

        /// <summary>Max representable offset from origin, in world units.</summary>
        public const float MaxOffset = short.MaxValue / PositionScale; // ~511.98

        /// <summary>
        /// SUPERSEDED (audit M8): velocity is now VelocityCodec's square-root
        /// curve, up to VelocityCodec.MaxSpeed per axis. Kept for reference.
        /// Velocity fixed-point scale. 4 = 1/4 unit/sec precision in a
        /// signed byte, giving a representable range of +/-31.75 u/s.
        /// Walk is 5 u/s and a jump leaves the ground at ~7.7 u/s, so the
        /// only thing that realistically saturates this is terminal
        /// velocity on a long fall — which clamps, and a clamped downward
        /// velocity extrapolates a falling body slightly too slowly for a
        /// fraction of a second. That is a much better failure than
        /// spending twice the bytes on every entry to represent a case the
        /// player barely sees.
        /// </summary>
        public const float VelocityScale = 4f;

        /// <summary>Max representable speed per axis, in units/second.</summary>
        public const float MaxVelocity = ArcheCore.Network.Shared.VelocityCodec.MaxSpeed;

        [Flags]
        public enum EntryFlags : byte
        {
            None     = 0,
            Position = 1 << 0,
            Yaw      = 1 << 1,
            IsNpc    = 1 << 2,

            /// <summary>
            /// Entry carries three velocity bytes after the state byte. Set
            /// only for near-tier entities; the reader must branch on this
            /// rather than assuming a fixed entry size, or it will walk off
            /// into the next entry.
            /// </summary>
            Velocity = 1 << 3,

            /// <summary>
            /// Entry carries two extra rotation bytes (pitch, roll) after
            /// the velocity block. Set only when the entity is actually
            /// tilted - a character standing upright on flat ground has
            /// pitch and roll of zero and pays nothing for the fact that
            /// gliders exist. Same reasoning as Velocity: optional fields
            /// keep the common entry small, at the cost of the reader
            /// having to derive entry length from the flags rather than a
            /// constant.
            /// </summary>
            Tilt = 1 << 4,
        }

        /// <summary>
        /// Below this, in radians, an entity counts as upright and the two
        /// tilt bytes are skipped. ~2 degrees - under the 1.4 degree
        /// quantization bucket's own noise floor by enough that skipping is
        /// never visible, and it means ordinary ground characters (which
        /// have exactly zero pitch and roll) never trip it.
        /// </summary>
        public const float TiltEpsilon = 0.035f;

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
        /// Velocity component to a signed byte. Clamps rather than wraps -
        /// a velocity past the representable range is a fast fall, and the
        /// nearest representable fast fall is a far better answer than a
        /// wrapped one pointing the opposite direction.
        /// </summary>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static sbyte QuantizeVelocity(float unitsPerSecond) =>
            ArcheCore.Network.Shared.VelocityCodec.Encode(unitsPerSecond);

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static float DequantizeVelocity(sbyte quantized) =>
            ArcheCore.Network.Shared.VelocityCodec.Decode(quantized);

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