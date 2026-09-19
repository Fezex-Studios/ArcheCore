using System.Numerics;

namespace ArcheCore.Movement
{
    /// <summary>
    /// How a character stands on something that is itself moving: a ship
    /// deck, a cart, a lift, a rotating platform.
    ///
    /// THE IDEA, AND WHY THE ALTERNATIVE FAILS
    ///
    /// The naive approach is to add the platform's motion to the character
    /// every tick. It does not survive rotation - a character near the rail
    /// of a turning ship needs a different delta from one at the mast, and
    /// there is no single velocity that satisfies both. It also fights
    /// prediction: the client must guess the platform's motion as well as
    /// its own, and the two errors compound.
    ///
    /// Instead the character's Position becomes LOCAL to the parent, and it
    /// is simulated entirely in that space. Standing still on a ship means
    /// local velocity zero, no matter what the ship is doing - the hull
    /// carries the character for free and rotation is handled by the
    /// transform rather than by arithmetic. Walking across the deck of a
    /// turning ship is then ordinary walking, in a space that happens to be
    /// moving.
    ///
    /// Collision inside the parent's space needs an ICollisionWorld scoped
    /// to that parent (the hull's own colliders). That is the piece to build
    /// when you get to ships: implement ICollisionWorld over a vehicle's
    /// local geometry and hand the motor that instead of the world one. The
    /// motor needs no changes at all - this is what the interface bought.
    ///
    /// Boarding and leaving are the only tricky moments, and both are just
    /// a change of space: convert the position and velocity, set or clear
    /// ParentEntity. Doing it in one place is what keeps that from becoming
    /// a source of dupes and desyncs.
    /// </summary>
    public static class ReferenceFrame
    {
        /// <summary>World-space pose of a parent entity for one tick.</summary>
        public struct Frame
        {
            public Vector3    Position;
            public Quaternion Rotation;

            /// <summary>Parent's own world velocity. Needed only at the
            /// boundary - when stepping on or off - so the character keeps
            /// the momentum it had.</summary>
            public Vector3    Velocity;

            /// <summary>Radians/second about the parent's up axis. Applied
            /// to a character's yaw as it rides, so facing turns with the
            /// deck rather than staying fixed relative to the world.</summary>
            public float      AngularVelocityY;

            public static Frame Identity => new Frame
            {
                Position = Vector3.Zero,
                Rotation = Quaternion.Identity,
                Velocity = Vector3.Zero,
                AngularVelocityY = 0f
            };
        }

        public static Vector3 ToLocal(in Frame frame, Vector3 worldPoint)
        {
            Quaternion inverse = Quaternion.Conjugate(frame.Rotation);
            return Vector3.Transform(worldPoint - frame.Position, inverse);
        }

        public static Vector3 ToWorld(in Frame frame, Vector3 localPoint) =>
            frame.Position + Vector3.Transform(localPoint, frame.Rotation);

        public static Vector3 DirectionToLocal(in Frame frame, Vector3 worldDirection) =>
            Vector3.Transform(worldDirection, Quaternion.Conjugate(frame.Rotation));

        public static Vector3 DirectionToWorld(in Frame frame, Vector3 localDirection) =>
            Vector3.Transform(localDirection, frame.Rotation);

        /// <summary>
        /// Step onto a parent. Position and velocity convert into its space;
        /// subtracting the parent's velocity is what makes a character that
        /// matched a moving ship's speed read as standing still on it.
        /// </summary>
        public static MoveState Attach(MoveState state, int parentEntity, in Frame frame)
        {
            state.Position = ToLocal(frame, state.Position);
            state.Velocity = DirectionToLocal(frame, state.Velocity - frame.Velocity);
            state.ParentEntity = parentEntity;
            return state;
        }

        /// <summary>
        /// Step off. The parent's velocity is added back, so walking off the
        /// bow of a moving ship launches the character forward with it
        /// rather than dropping them as if the ship had been still - which
        /// is both physically right and what players expect.
        /// </summary>
        public static MoveState Detach(MoveState state, in Frame frame)
        {
            state.Position = ToWorld(frame, state.Position);
            state.Velocity = DirectionToWorld(frame, state.Velocity) + frame.Velocity;
            state.ParentEntity = 0;
            return state;
        }

        /// <summary>
        /// Carry a rider's facing with a turning parent. Called once per
        /// tick while ParentEntity is set; without it a character on a
        /// rotating deck keeps a fixed world heading and appears to pivot
        /// against the ship.
        /// </summary>
        public static MoveState ApplyParentRotation(MoveState state, in Frame frame, float dt)
        {
            if (state.ParentEntity == 0) return state;
            state.Yaw += frame.AngularVelocityY * dt;
            return state;
        }
    }
}
