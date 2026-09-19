using System;
using System.Numerics;

namespace ArcheCore.Movement
{
    /// <summary>
    /// Everything the motor needs to continue a character's motion, and
    /// nothing else. Deliberately a plain struct with no references: it gets
    /// copied per tick into a client-side prediction history, compared field
    /// by field against the server's version during reconciliation, and
    /// serialized into snapshots.
    ///
    /// The rule that keeps that working: if the motor reads it across ticks,
    /// it belongs here. A timer kept as a private field in the motor would
    /// not survive a reconciliation replay, so the replay would diverge from
    /// the original simulation for reasons invisible in the diff. That
    /// includes the unglamorous ones - coyote time, jump buffer, airborne
    /// duration all live here for exactly that reason.
    /// </summary>
    public struct MoveState
    {
        // --- Transform ---

        /// <summary>
        /// Position of the capsule CENTRE, not the feet.
        ///
        /// Centre because every collision query is expressed around it and
        /// converting at each one is where sign errors breed. The feet are
        /// available as FeetPosition. Note this differs from the convention
        /// most Unity setups use, so the Unity adapter converts once, at the
        /// boundary, rather than scattering half-height arithmetic.
        /// </summary>
        public Vector3 Position;

        public Vector3 Velocity;

        public float Yaw;
        public float Pitch;
        public float Roll;

        // --- Ground ---

        public bool IsGrounded;

        /// <summary>Surface normal under the feet. Only meaningful while
        /// IsGrounded; drives slope-aware movement.</summary>
        public Vector3 GroundNormal;

        /// <summary>
        /// Entity id of what is being stood on, or 0 for the world.
        /// Non-zero means the character rides that thing's reference frame -
        /// a ship deck, a cart, a platform. See ReferenceFrame.
        /// </summary>
        public int GroundEntity;

        // --- Mode ---

        public LocomotionMode Mode;

        /// <summary>
        /// Id of the entity whose frame this character's position is
        /// expressed in, or 0 for world space. When non-zero, Position is
        /// LOCAL to that entity. This is the whole mechanism behind standing
        /// on a moving ship: the character is simulated in the ship's space
        /// and the ship's own motion is not the character's problem.
        /// </summary>
        public int ParentEntity;

        // --- Timers (must persist across ticks; see the class remarks) ---

        public float TimeSinceGrounded;
        public float JumpBufferRemaining;
        public float TimeInAir;

        /// <summary>Set for one tick on the step where ground contact was
        /// regained. Animation and audio hooks read it; the motor clears it
        /// at the start of every step.</summary>
        public bool LandedThisStep;

        public bool JumpedThisStep;

        public Vector3 FeetPosition(MovementProfile p) =>
            Position - new Vector3(0f, p.HalfHeight, 0f);

        public void SetFeetPosition(Vector3 feet, MovementProfile p) =>
            Position = feet + new Vector3(0f, p.HalfHeight, 0f);

        public float HorizontalSpeed =>
            (float)Math.Sqrt(Velocity.X * Velocity.X + Velocity.Z * Velocity.Z);

        public static MoveState AtFeet(Vector3 feet, MovementProfile p, float yaw = 0f)
        {
            var s = new MoveState
            {
                Velocity = Vector3.Zero,
                Yaw = yaw,
                GroundNormal = Vector3.UnitY,
                Mode = LocomotionMode.Grounded
            };
            s.SetFeetPosition(feet, p);
            return s;
        }
    }

    /// <summary>
    /// Which set of rules is currently driving the character. Exclusive, so
    /// an enum rather than flags - the observable STATE that goes on the
    /// wire (MovementState) is a bitfield because a character can be several
    /// things at once for animation purposes, but only one rule set can be
    /// integrating its velocity.
    ///
    /// Adding a mode is the intended way to extend this. Ships and cars are
    /// Piloting plus a vehicle profile; they do not need their own motor.
    /// </summary>
    public enum LocomotionMode : byte
    {
        Grounded = 0,
        Airborne = 1,
        Swimming = 2,

        /// <summary>Airborne with lift - glider, wingsuit.</summary>
        Gliding = 3,

        /// <summary>Riding something that moves itself. Input is ignored;
        /// position comes from the parent's frame.</summary>
        Riding = 4,

        /// <summary>Driving something. Input steers the VEHICLE, and the
        /// character's own position is pinned to its seat.</summary>
        Piloting = 5,

        /// <summary>Server-driven displacement: knockback, leap, cutscene.
        /// The motor still resolves collision but ignores input.</summary>
        Scripted = 6,
    }
}
