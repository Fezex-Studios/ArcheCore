namespace ArcheCore.Movement
{
    /// <summary>
    /// Every tunable that describes how one KIND of thing moves. Data, not
    /// code, and deliberately so: a player, a mount, a deer, a ship and a
    /// car differ almost entirely in these numbers, and the ones that differ
    /// in behaviour differ by which LocomotionMode they use - not by having
    /// their own controller.
    ///
    /// This is also what the server validates against. Because both sides
    /// run the same motor with the same profile, "is this move legal" stops
    /// being a guess about plausible speeds and becomes "did the client's
    /// result match what the same code produced here". A mount doesn't need
    /// anyone to remember to raise a speed cap; it has its own profile and
    /// the server already knows it.
    ///
    /// Load these from your game data alongside NPC templates and items.
    /// Nothing here should ever be hardcoded in the motor.
    /// </summary>
    public sealed class MovementProfile
    {
        // --- Capsule ---

        /// <summary>Total height including both hemispherical caps.</summary>
        public float Height = 2f;
        public float Radius = 0.5f;

        /// <summary>
        /// Collision inset. Every cast is done with a slightly shrunken
        /// capsule and the remainder is kept as a gap, so the character
        /// never rests exactly on a surface. Resting exactly on one means
        /// every subsequent query is a tie, and ties resolve inconsistently.
        /// Keep at roughly 2% of radius; too large and the character
        /// visibly floats, too small and it jitters against walls.
        /// </summary>
        public float SkinWidth = 0.01f;

        // --- Ground locomotion ---

        public float WalkSpeed = 2.5f;
        public float RunSpeed = 5f;
        public float SprintSpeed = 8f;

        /// <summary>
        /// Units/second^2 toward the desired velocity while grounded. Finite
        /// rather than instant so that direction changes have weight;
        /// set very high for a twitchy arcade feel.
        /// </summary>
        public float GroundAcceleration = 40f;
        public float GroundFriction = 30f;

        /// <summary>Steepest surface that counts as ground, in degrees.
        /// Anything steeper is a wall to slide along, not a floor to stand
        /// on - this is what stops characters walking up cliffs.</summary>
        public float SlopeLimit = 50f;

        /// <summary>Tallest obstacle that can be walked over without a
        /// jump. Stairs, kerbs, roots.</summary>
        public float StepHeight = 0.4f;

        /// <summary>
        /// How far below the feet to look for ground when already grounded.
        /// This is what keeps a character glued to a descending slope
        /// instead of launching off every bump and arriving as a series of
        /// small hops. Must exceed the distance gravity moves you in one
        /// step, or fast descents break free anyway.
        /// </summary>
        public float GroundSnapDistance = 0.5f;

        // --- Air ---

        public float Gravity = -20f;

        /// <summary>Peak height of a standing jump. Impulse is derived from
        /// this and Gravity, so changing either keeps them consistent.</summary>
        public float JumpHeight = 1.5f;

        /// <summary>
        /// Fraction of ground acceleration usable in the air. Zero is
        /// realistic and feels terrible; 1 lets players fly. ~0.3 is the
        /// range most action games sit in.
        /// </summary>
        public float AirControl = 0.3f;

        public float TerminalVelocity = -60f;

        /// <summary>
        /// Grace period after walking off a ledge during which a jump is
        /// still allowed. Players press jump slightly late constantly and
        /// perceive the failure as the game dropping input, not as their own
        /// timing.
        /// </summary>
        public float CoyoteTime = 0.12f;

        /// <summary>
        /// How long a jump press is remembered if pressed just before
        /// landing. The mirror of coyote time, for pressing slightly early.
        /// </summary>
        public float JumpBufferTime = 0.12f;

        // --- Water ---

        public float SwimSpeed = 3f;
        public float SwimAcceleration = 15f;

        /// <summary>
        /// Depth, measured from the feet, at which the character leaves
        /// ground locomotion for swimming. Below this it wades - still
        /// grounded, just slower.
        /// </summary>
        public float SwimDepthThreshold = 1.3f;

        /// <summary>Speed multiplier while wading rather than swimming.</summary>
        public float WadeSpeedScale = 0.6f;

        public float Buoyancy = 4f;

        // --- Derived ---

        /// <summary>Upward speed needed to reach JumpHeight under Gravity.</summary>
        public float JumpImpulse =>
            (float)System.Math.Sqrt(JumpHeight * -2f * Gravity);

        public float HalfHeight => Height * 0.5f;

        /// <summary>Distance from the capsule centre to the centre of each
        /// hemispherical cap. Zero for a sphere.</summary>
        public float CapsuleSegment =>
            System.Math.Max(0f, HalfHeight - Radius);

        public MovementProfile Clone() => (MovementProfile)MemberwiseClone();
    }
}
