using System;
using System.Numerics;

namespace ArcheCore.Movement
{
    /// <summary>
    /// One tick of intent. The ONLY thing the client sends once movement is
    /// server-authoritative: the server replays these through the same motor
    /// and derives the position itself, rather than being told a position
    /// and asked whether to believe it.
    ///
    /// Intent, never outcome. WishDirection is "which way I am pressing",
    /// not "how fast I am going" - speed comes from the profile, so a client
    /// that inflates this cannot make itself faster, only make itself press
    /// harder in a direction. That distinction is most of why replaying
    /// input is more defensible than validating positions.
    ///
    /// Sequence is what makes reconciliation possible: the server echoes the
    /// last sequence it processed, and the client replays everything after
    /// it from the corrected state.
    /// </summary>
    public struct MoveInput
    {
        /// <summary>Monotonic per-connection tick counter. Never reused,
        /// never reordered - the client keeps unacknowledged inputs in a
        /// queue keyed by this.</summary>
        public uint Sequence;

        /// <summary>
        /// Desired direction in WORLD space, horizontal, normalized (or
        /// zero). World rather than local because the camera basis it was
        /// built from lives only on the client - resolving it there means
        /// the server never needs to know where the camera was pointing.
        /// Magnitude above 1 is clamped: it is a direction, not a throttle.
        /// </summary>
        public Vector3 WishDirection;

        /// <summary>Facing in radians. Separate from WishDirection because a
        /// character can face somewhere it isn't walking.</summary>
        public float Yaw;

        public float Pitch;

        public MoveButtons Buttons;

        public bool Jump    => (Buttons & MoveButtons.Jump)    != 0;
        public bool Sprint  => (Buttons & MoveButtons.Sprint)  != 0;
        public bool Walk    => (Buttons & MoveButtons.Walk)    != 0;
        public bool Crouch  => (Buttons & MoveButtons.Crouch)  != 0;
        public bool Ascend  => (Buttons & MoveButtons.Ascend)  != 0;
        public bool Descend => (Buttons & MoveButtons.Descend) != 0;

        /// <summary>
        /// Rejects anything a legitimate client cannot produce, BEFORE it
        /// reaches the motor. Cheap, and it is the only place non-finite
        /// values can be stopped - every comparison against NaN is false, so
        /// a NaN that gets past here passes every later check too and ends
        /// up in the spatial grid.
        /// </summary>
        public void Sanitize()
        {
            if (!IsFinite(WishDirection))
                WishDirection = Vector3.Zero;

            WishDirection.Y = 0f;

            float lengthSq = WishDirection.LengthSquared();
            if (lengthSq > 1f)
                WishDirection /= (float)Math.Sqrt(lengthSq);
            else if (lengthSq < 1e-6f)
                WishDirection = Vector3.Zero;

            if (!float.IsFinite(Yaw)) Yaw = 0f;
            if (!float.IsFinite(Pitch)) Pitch = 0f;
        }

        private static bool IsFinite(Vector3 v) =>
            float.IsFinite(v.X) && float.IsFinite(v.Y) && float.IsFinite(v.Z);
    }

    [Flags]
    public enum MoveButtons : ushort
    {
        None    = 0,
        Jump    = 1 << 0,
        Sprint  = 1 << 1,
        Walk    = 1 << 2,
        Crouch  = 1 << 3,

        /// <summary>Swim up / gain altitude while gliding or flying.</summary>
        Ascend  = 1 << 4,
        Descend = 1 << 5,

        /// <summary>Context action: deploy glider, mount, board, take the helm.</summary>
        Interact = 1 << 6,
    }
}
