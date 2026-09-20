namespace ArcheCore.Network.Shared
{
    /// <summary>
    /// Physics constants that the client SIMULATES and the server
    /// AUTHORS. Lives in the shared network library for the same reason
    /// MovementState does: a client-side copy that drifts from the
    /// server's is a silent wire break, not a compile error.
    ///
    /// WHY THESE HAVE TO BE SHARED
    ///
    /// A jump event says "this character left the ground at position P
    /// with velocity V at tick T". Everything after that is derived, not
    /// transmitted - the observing client integrates the arc itself. That
    /// only works if both ends integrate the SAME arc, which means the
    /// same gravity and the same jump impulse.
    ///
    /// If the client uses -9.81 and the server authors its events assuming
    /// -20, every remote jump lands in the wrong place and the correction
    /// when the next snapshot arrives is a visible snap. The failure is
    /// subtle, because small mismatches just look like "netcode feels a
    /// bit off" rather than like a bug.
    ///
    /// WHERE THIS SHOULD END UP
    ///
    /// Sent by the server at spawn, not compiled into the client. The
    /// moment you want a low-gravity zone, a jump-height buff, or per-race
    /// movement, these stop being constants and become per-character
    /// values the server owns and transmits. This file is the honest
    /// interim: one definition, shared, easy to find - but still a
    /// constant the client trusts.
    ///
    /// UNITY NOTE: Unity's own Physics.gravity default is -9.81. If your
    /// player controller uses that rather than this value, change the
    /// controller, not this file. The server has to be the authority on
    /// how high a character can jump, because MovementValidator's vertical
    /// budget is derived from it.
    /// </summary>
    public static class MovementConstants
    {
        /// <summary>
        /// Downward acceleration in world units per second squared. Must
        /// match the gravity your CharacterController applies to the local
        /// player, or a remote jump and a local jump of the same character
        /// take different amounts of time.
        /// </summary>
        public const float Gravity = -20f;

        /// <summary>
        /// Upward velocity at the instant a jump begins, in units per
        /// second.
        ///
        /// SERVER-AUTHORED, deliberately. The jump event does NOT carry
        /// the client's reported vertical velocity, because a client that
        /// reports 40 would have every other player watch it rocket into
        /// the sky - the one case where the "a lie only makes the liar
        /// look wrong" argument fails, since the lie is about a trajectory
        /// other clients then simulate faithfully. The client says only
        /// THAT it jumped; the server says how high.
        ///
        /// At -20 gravity this gives an apex of about 1.48 units after
        /// ~0.385s, and a ~0.77s round trip on flat ground.
        /// </summary>
        public const float JumpVelocity = 7.7f;

        /// <summary>
        /// Longest a client will keep simulating a jump arc before giving
        /// up and returning to ordinary snapshot interpolation.
        ///
        /// A jump off a cliff has no landing within any predictable time,
        /// and a simulation with no exit runs a character through the
        /// world floor forever if the landing packet is lost. Snapshots
        /// keep arriving during the arc regardless, so falling back to
        /// them is always safe - this just bounds how long the prettier
        /// path is allowed to win.
        /// </summary>
        public const float MaxJumpSimulationSeconds = 3f;

        public const float ServerTickRate = 20f;
    }
}