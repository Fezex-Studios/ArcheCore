using System;
using System.Numerics;

namespace ArcheCore.Movement
{
    /// <summary>
    /// The movement simulation. One pure-ish function: given a state, an
    /// input and a fixed timestep, produce the next state.
    ///
    /// THREE RULES THAT MAKE THIS WORK, AND BREAK IT IF VIOLATED
    ///
    /// 1. FIXED TIMESTEP ONLY. Step() must be called with a constant dt.
    ///    Not Time.deltaTime. Variable dt means two machines running the
    ///    same inputs produce different results, which makes client
    ///    prediction and server reconciliation impossible - not
    ///    inaccurate, impossible. Accumulate frame time and step a whole
    ///    number of times; render between steps by interpolating.
    ///
    /// 2. NO STATE OUTSIDE MoveState. This class holds no per-character
    ///    fields. Anything the motor remembers between ticks lives in
    ///    MoveState, because reconciliation replays inputs from an older
    ///    state and any hidden field would not be rewound with it - so the
    ///    replay would silently diverge from the original run.
    ///
    /// 3. NO ENGINE CALLS. Everything about the world arrives through
    ///    ICollisionWorld.
    ///
    /// Obey those and the same code runs in Unity for prediction and in the
    /// world server for authority, which is the entire point of the design.
    ///
    /// COLLISION APPROACH
    ///
    /// Iterative collide-and-slide. Sweep the capsule along the remaining
    /// displacement; on contact, advance to just before the surface, project
    /// what's left onto the contact plane, repeat. Bounded iterations, and
    /// each frame's displacement is substepped so no single sweep is longer
    /// than a fraction of the radius - which is what actually prevents
    /// tunnelling through terrain at fall speed, rather than hoping the
    /// engine's CCD catches it.
    /// </summary>
    public sealed class CharacterMotor
    {
        private const int MaxSlideIterations = 5;

        /// <summary>
        /// Longest single sweep, as a fraction of radius. Anything further
        /// is split. The failure this prevents is the classic one: a fast
        /// fall moves further in one step than the capsule is thick, the
        /// sweep starts below the floor it should have hit, and the
        /// character arrives under the world.
        /// </summary>
        private const float MaxSweepFraction = 0.5f;

        /// <summary>Leftover displacement below this is discarded rather
        /// than chased through another iteration.</summary>
        private const float MinDisplacement = 1e-4f;

        private readonly ICollisionWorld _world;

        public CharacterMotor(ICollisionWorld world)
        {
            _world = world ?? throw new ArgumentNullException(nameof(world));
        }

        /// <param name="dt">FIXED. See rule 1.</param>
        public MoveState Step(MoveState state, MoveInput input, MovementProfile profile, float dt)
        {
            input.Sanitize();

            state.LandedThisStep = false;
            state.JumpedThisStep = false;

            // Riding and piloting are resolved by whatever owns the vehicle;
            // the character's own position is pinned to a seat in the
            // parent's frame, so there is nothing here to integrate.
            if (state.Mode == LocomotionMode.Riding || state.Mode == LocomotionMode.Piloting)
            {
                state.Yaw = input.Yaw;
                state.Pitch = input.Pitch;
                return state;
            }

            // An overlapping start pose makes every sweep below meaningless,
            // so fix it first. Characters get here through teleports into
            // bad data, spawn points authored underground, and geometry
            // moving into them.
            state = ResolveOverlap(state, profile);

            state = UpdateMode(state, profile, dt);

            switch (state.Mode)
            {
                case LocomotionMode.Swimming:
                    state = StepSwimming(state, input, profile, dt);
                    break;
                case LocomotionMode.Gliding:
                    state = StepGliding(state, input, profile, dt);
                    break;
                case LocomotionMode.Scripted:
                    state = StepScripted(state, profile, dt);
                    break;
                default:
                    state = StepGround(state, input, profile, dt);
                    break;
            }

            if (state.Mode != LocomotionMode.Scripted)
                state.Yaw = input.Yaw;

            return state;
        }

        // ---------------------------------------------------------------
        // Mode selection
        // ---------------------------------------------------------------

        private MoveState UpdateMode(MoveState state, MovementProfile profile, float dt)
        {
            if (state.Mode == LocomotionMode.Scripted)
                return state;

            float water = _world.SampleWaterLevel(state.Position);

            if (water > float.MinValue)
            {
                float feetY = state.FeetPosition(profile).Y;
                float depth = water - feetY;

                if (depth >= profile.SwimDepthThreshold)
                {
                    if (state.Mode != LocomotionMode.Swimming)
                    {
                        state.Mode = LocomotionMode.Swimming;
                        // Entering water kills fall speed. Without this, a
                        // dive carries terminal velocity into a medium with
                        // no gravity and the character rockets to the bed.
                        state.Velocity *= 0.25f;
                    }
                    state.IsGrounded = false;
                    return state;
                }
            }

            if (state.Mode == LocomotionMode.Swimming)
                state.Mode = state.IsGrounded ? LocomotionMode.Grounded : LocomotionMode.Airborne;

            if (state.Mode == LocomotionMode.Gliding)
                return state;

            state.Mode = state.IsGrounded ? LocomotionMode.Grounded : LocomotionMode.Airborne;
            return state;
        }

        // ---------------------------------------------------------------
        // Ground / air
        // ---------------------------------------------------------------

        private MoveState StepGround(MoveState state, MoveInput input, MovementProfile profile, float dt)
        {
            // --- Timers. Kept in state; see rule 2. ---

            if (state.IsGrounded)
            {
                state.TimeSinceGrounded = 0f;
                state.TimeInAir = 0f;
            }
            else
            {
                state.TimeSinceGrounded += dt;
                state.TimeInAir += dt;
            }

            state.JumpBufferRemaining = input.Jump
                ? profile.JumpBufferTime
                : Math.Max(0f, state.JumpBufferRemaining - dt);

            // --- Horizontal intent ---

            float targetSpeed =
                input.Walk   ? profile.WalkSpeed :
                input.Sprint ? profile.SprintSpeed :
                               profile.RunSpeed;

            Vector3 wish = input.WishDirection * targetSpeed;

            // On a walkable slope, move ALONG the surface rather than
            // horizontally. Without this, walking uphill drives the capsule
            // into the slope and relies on collision response to redirect
            // it, which reads as stuttering; downhill it walks off the
            // surface every step and relies on ground snap to catch it.
            if (state.IsGrounded && state.GroundNormal != Vector3.Zero)
                wish = ProjectOnPlane(wish, state.GroundNormal);

            float accel = state.IsGrounded
                ? profile.GroundAcceleration
                : profile.GroundAcceleration * profile.AirControl;

            Vector3 horizontalVelocity = new Vector3(state.Velocity.X, 0f, state.Velocity.Z);
            Vector3 horizontalWish = new Vector3(wish.X, 0f, wish.Z);

            if (horizontalWish.LengthSquared() > 1e-6f)
            {
                horizontalVelocity = MoveToward(horizontalVelocity, horizontalWish, accel * dt);
            }
            else if (state.IsGrounded)
            {
                horizontalVelocity = MoveToward(horizontalVelocity, Vector3.Zero, profile.GroundFriction * dt);
            }

            state.Velocity.X = horizontalVelocity.X;
            state.Velocity.Z = horizontalVelocity.Z;

            // --- Jump ---

            bool canJump =
                state.JumpBufferRemaining > 0f &&
                (state.IsGrounded || state.TimeSinceGrounded <= profile.CoyoteTime);

            if (canJump)
            {
                state.Velocity.Y = profile.JumpImpulse;
                state.IsGrounded = false;
                state.JumpedThisStep = true;
                state.JumpBufferRemaining = 0f;

                // Consume the coyote window, or one late press off a ledge
                // buys a second jump in mid-air.
                state.TimeSinceGrounded = profile.CoyoteTime + 1f;
                state.GroundEntity = 0;
            }
            else if (state.IsGrounded)
            {
                // Small downward bias keeps the capsule in contact so that
                // ground queries stay decided rather than flickering. Any
                // controller that does not do something equivalent has
                // isGrounded oscillating on flat floors.
                if (state.Velocity.Y < 0f)
                    state.Velocity.Y = -2f;
            }

            if (!state.IsGrounded)
            {
                state.Velocity.Y += profile.Gravity * dt;
                if (state.Velocity.Y < profile.TerminalVelocity)
                    state.Velocity.Y = profile.TerminalVelocity;
            }

            // --- Integrate and collide ---

            bool wasGrounded = state.IsGrounded;

            state = SweepMove(state, state.Velocity * dt, profile);

            state = ProbeGround(state, profile, wasGrounded);

            if (!wasGrounded && state.IsGrounded)
                state.LandedThisStep = true;

            return state;
        }

        private MoveState StepSwimming(MoveState state, MoveInput input, MovementProfile profile, float dt)
        {
            Vector3 wish = input.WishDirection * profile.SwimSpeed;

            if (input.Ascend)  wish.Y += profile.SwimSpeed;
            if (input.Descend) wish.Y -= profile.SwimSpeed;

            // Drift upward when not actively descending, so a floating
            // character surfaces instead of hanging at whatever depth it
            // stopped at.
            if (!input.Descend)
            {
                float water = _world.SampleWaterLevel(state.Position);
                if (water > float.MinValue && state.Position.Y < water - profile.HalfHeight)
                    wish.Y += profile.Buoyancy * 0.25f;
            }

            state.Velocity = MoveToward(state.Velocity, wish, profile.SwimAcceleration * dt);

            state = SweepMove(state, state.Velocity * dt, profile);
            state.IsGrounded = false;
            state.GroundEntity = 0;

            return state;
        }

        private MoveState StepGliding(MoveState state, MoveInput input, MovementProfile profile, float dt)
        {
            // Intentionally minimal: forward speed, a fixed sink rate, and
            // collision. A real glider wants lift from airspeed, stall,
            // thermals and a turn model - all of which belong in a
            // GlideProfile beside MovementProfile rather than hardcoded
            // here. This is a working placeholder with the right seams, not
            // a flight model.
            const float glideSink = -3f;
            const float glideSpeed = 12f;

            Vector3 wish = input.WishDirection * glideSpeed;
            wish.Y = glideSink;

            state.Velocity = MoveToward(state.Velocity, wish, 8f * dt);

            state = SweepMove(state, state.Velocity * dt, profile);
            state = ProbeGround(state, profile, wasGrounded: false);

            if (state.IsGrounded)
            {
                state.Mode = LocomotionMode.Grounded;
                state.LandedThisStep = true;
            }

            return state;
        }

        private MoveState StepScripted(MoveState state, MovementProfile profile, float dt)
        {
            // Server-authored displacement - knockback, leap, a rooted pull.
            // Input is ignored but collision is not, so a knockback cannot
            // put anyone through a wall.
            state.Velocity.Y += profile.Gravity * dt;
            state = SweepMove(state, state.Velocity * dt, profile);
            state = ProbeGround(state, profile, wasGrounded: false);

            if (state.IsGrounded)
            {
                state.Mode = LocomotionMode.Grounded;
                state.LandedThisStep = true;
            }

            return state;
        }

        // ---------------------------------------------------------------
        // Collision
        // ---------------------------------------------------------------

        /// <summary>
        /// Move by displacement, sliding along whatever is hit, substepped
        /// so no single sweep is long enough to tunnel.
        /// </summary>
        private MoveState SweepMove(MoveState state, Vector3 displacement, MovementProfile profile)
        {
            float radius = profile.Radius - profile.SkinWidth;
            float segment = profile.CapsuleSegment;
            float maxSweep = profile.Radius * MaxSweepFraction;

            float remaining = displacement.Length();
            if (remaining < MinDisplacement)
                return state;

            Vector3 direction = displacement / remaining;

            // Substep. This is the anti-tunnelling guarantee, and it is a
            // guarantee rather than a mitigation: no individual sweep is
            // ever longer than half the capsule radius, so there is no gap
            // a surface can hide in.
            int substeps = Math.Max(1, (int)Math.Ceiling(remaining / maxSweep));
            float perStep = remaining / substeps;

            for (int s = 0; s < substeps; s++)
            {
                float budget = perStep;

                for (int i = 0; i < MaxSlideIterations && budget > MinDisplacement; i++)
                {
                    if (!_world.CapsuleCast(state.Position, radius, segment, direction, budget + profile.SkinWidth, out var hit))
                    {
                        state.Position += direction * budget;
                        budget = 0f;
                        break;
                    }

                    hit.IsWalkable = IsWalkable(hit.Normal, profile);

                    // Stop just short, keeping the skin gap - never resting
                    // exactly on the surface. See MovementProfile.SkinWidth.
                    float advance = Math.Max(0f, hit.Distance - profile.SkinWidth);
                    state.Position += direction * advance;
                    budget -= advance;

                    // A low blocking wall may be a step. Trying this before
                    // sliding is what makes stairs walkable instead of
                    // something the character grinds against.
                    if (!hit.IsWalkable && state.IsGrounded &&
                        TryStepUp(ref state, direction, budget, profile))
                    {
                        continue;
                    }

                    // Project the remaining motion onto the contact plane
                    // and carry on in the new direction.
                    Vector3 remainingVec = direction * budget;
                    remainingVec = ProjectOnPlane(remainingVec, hit.Normal);

                    // Kill the component of VELOCITY into the surface too.
                    // Without this the character keeps accelerating into a
                    // wall and pops through the moment a gap appears.
                    state.Velocity = ProjectOnPlane(state.Velocity, hit.Normal);

                    budget = remainingVec.Length();
                    if (budget < MinDisplacement)
                        break;

                    direction = remainingVec / budget;

                    if (hit.IsWalkable && hit.EntityId != 0)
                        state.GroundEntity = hit.EntityId;
                }
            }

            return state;
        }

        /// <summary>
        /// Attempt to climb a low obstacle: lift, move forward, drop back
        /// down. Fails cleanly by restoring the original pose, so a failed
        /// step costs nothing.
        /// </summary>
        private bool TryStepUp(ref MoveState state, Vector3 direction, float budget, MovementProfile profile)
        {
            if (budget < MinDisplacement) return false;

            float radius = profile.Radius - profile.SkinWidth;
            float segment = profile.CapsuleSegment;
            Vector3 original = state.Position;

            Vector3 up = new Vector3(0f, profile.StepHeight, 0f);

            if (_world.CapsuleCast(state.Position, radius, segment, Vector3.UnitY, profile.StepHeight, out _))
                return false; // no headroom to lift into

            Vector3 lifted = state.Position + up;

            if (_world.CapsuleCast(lifted, radius, segment, direction, budget, out _))
                return false; // still blocked up there - a real wall, not a step

            Vector3 forward = lifted + direction * budget;

            // Drop back down and require landing on something walkable,
            // otherwise this "step" was the lip of a hole.
            if (!_world.CapsuleCast(forward, radius, segment, -Vector3.UnitY, profile.StepHeight * 2f, out var down) ||
                !IsWalkable(down.Normal, profile))
            {
                state.Position = original;
                return false;
            }

            state.Position = forward - new Vector3(0f, Math.Max(0f, down.Distance - profile.SkinWidth), 0f);
            state.IsGrounded = true;
            state.GroundNormal = down.Normal;
            state.GroundEntity = down.EntityId;
            return true;
        }

        /// <summary>
        /// Decide grounded-ness, and glue the character to descending
        /// slopes.
        ///
        /// The snap is why a character walking downhill stays on the ground
        /// instead of leaving it every step and arriving as a series of
        /// hops. It only applies when already grounded and not moving
        /// upward - snapping while ascending would cancel jumps.
        /// </summary>
        private MoveState ProbeGround(MoveState state, MovementProfile profile, bool wasGrounded)
        {
            float radius = profile.Radius - profile.SkinWidth;
            float segment = profile.CapsuleSegment;

            float probe = wasGrounded && state.Velocity.Y <= 0f
                ? profile.GroundSnapDistance
                : profile.SkinWidth * 2f;

            if (_world.CapsuleCast(state.Position, radius, segment, -Vector3.UnitY, probe, out var hit) &&
                IsWalkable(hit.Normal, profile))
            {
                state.Position -= new Vector3(0f, Math.Max(0f, hit.Distance - profile.SkinWidth), 0f);
                state.IsGrounded = true;
                state.GroundNormal = hit.Normal;
                state.GroundEntity = hit.EntityId;

                if (state.Velocity.Y < 0f)
                    state.Velocity.Y = 0f;
            }
            else
            {
                state.IsGrounded = false;
                state.GroundNormal = Vector3.UnitY;
                state.GroundEntity = 0;
            }

            return state;
        }

        private MoveState ResolveOverlap(MoveState state, MovementProfile profile)
        {
            float radius = profile.Radius - profile.SkinWidth;
            float segment = profile.CapsuleSegment;

            if (!_world.CheckCapsule(state.Position, radius, segment))
                return state;

            if (_world.ComputePenetration(state.Position, radius, segment, out var dir, out float dist) &&
                dist > 0f && IsFinite(dir))
            {
                state.Position += dir * (dist + profile.SkinWidth);
            }

            return state;
        }

        // ---------------------------------------------------------------
        // Helpers
        // ---------------------------------------------------------------

        private static bool IsWalkable(Vector3 normal, MovementProfile profile)
        {
            if (normal == Vector3.Zero) return false;
            float cosLimit = (float)Math.Cos(profile.SlopeLimit * Math.PI / 180.0);
            return normal.Y >= cosLimit;
        }

        private static Vector3 ProjectOnPlane(Vector3 v, Vector3 normal)
        {
            float lenSq = normal.LengthSquared();
            if (lenSq < 1e-8f) return v;
            return v - normal * (Vector3.Dot(v, normal) / lenSq);
        }

        private static Vector3 MoveToward(Vector3 current, Vector3 target, float maxDelta)
        {
            Vector3 delta = target - current;
            float distance = delta.Length();
            if (distance <= maxDelta || distance < 1e-6f) return target;
            return current + delta / distance * maxDelta;
        }

        private static bool IsFinite(Vector3 v) =>
            float.IsFinite(v.X) && float.IsFinite(v.Y) && float.IsFinite(v.Z);
    }
}
