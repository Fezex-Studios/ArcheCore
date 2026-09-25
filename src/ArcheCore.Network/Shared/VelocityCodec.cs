using System;

namespace ArcheCore.Network.Shared
{
    /// <summary>
    /// One velocity component in one signed byte, shared by the server's
    /// snapshot writer and the client's snapshot reader so the two can't
    /// disagree (audit M8).
    ///
    /// The old encoding was linear at 1/4 u/s, which capped every axis at
    /// 31.75 u/s: a long fall (the validator allows 80), a glider or a ship
    /// extrapolated too slowly. This one is a square-root curve - fine steps
    /// where characters actually move, coarse steps where only falls,
    /// gliders and boats go - reaching MaxSpeed in the same byte:
    ///
    ///     speed        step between codes
    ///     ~1 u/s        ~0.1 u/s
    ///     5 u/s (run)   ~0.35 u/s
    ///     10 u/s        ~0.5 u/s
    ///     50 u/s        ~1.1 u/s
    ///     100 u/s       ~1.6 u/s
    ///
    /// Velocity only ever extrapolates across a gap of a few hundred ms, so
    /// even the coarse end is centimetres of drift that the next real update
    /// corrects.
    /// </summary>
    public static class VelocityCodec
    {
        /// <summary>Largest speed per axis that encodes without clamping, units/second.</summary>
        public const float MaxSpeed = 100f;

        public static sbyte Encode(float unitsPerSecond)
        {
            if (float.IsNaN(unitsPerSecond)) return 0;

            float magnitude = Math.Min(Math.Abs(unitsPerSecond), MaxSpeed);
            int code = (int)Math.Round(127.0 * Math.Sqrt(magnitude / MaxSpeed));
            if (code > 127) code = 127;

            return (sbyte)(unitsPerSecond < 0 ? -code : code);
        }

        public static float Decode(sbyte encoded)
        {
            float t = Math.Abs((int)encoded) / 127f;
            float magnitude = t * t * MaxSpeed;
            return encoded < 0 ? -magnitude : magnitude;
        }
    }
}
