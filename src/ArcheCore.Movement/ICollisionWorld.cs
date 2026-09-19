using System.Numerics;

namespace ArcheCore.Movement
{
    /// <summary>
    /// The one seam between the movement math and the engine.
    ///
    /// This interface existing is what lets CharacterMotor run inside a
    /// headless world server that has no PhysX, no GameObjects and no frame
    /// loop. Unity implements it with Physics.CapsuleCast; the server
    /// implements it against a heightmap first and a mesh BVH later. Neither
    /// implementation knows anything about movement, and the motor knows
    /// nothing about either.
    ///
    /// Keep it this small. Every method added here is one more thing the
    /// server has to be able to answer, and the temptation to add
    /// "just one" engine-specific query is how a portable core stops being
    /// portable.
    ///
    /// CONSISTENCY MATTERS MORE THAN FIDELITY. The two implementations do
    /// not have to agree perfectly, but where they disagree, the client will
    /// predict one thing and the server another, and the player gets
    /// corrected. A server heightmap that is 10cm off from the client's
    /// visual mesh produces constant small corrections on slopes. Prefer
    /// deriving both from the same source data over tuning them to match.
    /// </summary>
    public interface ICollisionWorld
    {
        /// <summary>
        /// Sweep a capsule and report the first blocking contact.
        /// </summary>
        /// <param name="center">Capsule centre at the start of the sweep.</param>
        /// <param name="radius">Already shrunk by skin width by the caller.</param>
        /// <param name="segment">Centre-to-cap distance; 0 for a sphere.</param>
        /// <param name="direction">Normalized.</param>
        /// <param name="distance">Maximum sweep length.</param>
        /// <returns>False if nothing was hit within distance.</returns>
        bool CapsuleCast(
            Vector3 center,
            float radius,
            float segment,
            Vector3 direction,
            float distance,
            out CollisionHit hit);

        /// <summary>
        /// True if a capsule at this pose overlaps anything. Used to detect
        /// a character that has ended up inside geometry - after a teleport,
        /// a spawn into bad data, or a moving platform closing on it - so
        /// the motor can push out rather than sweep from an invalid pose,
        /// which produces garbage results in every collision system.
        /// </summary>
        bool CheckCapsule(Vector3 center, float radius, float segment);

        /// <summary>
        /// Resolve an existing overlap. Returns the direction and distance
        /// to move to separate. Implementations that cannot compute this
        /// (a pure heightmap) may return straight up by the penetration
        /// depth, which is correct for terrain and adequate elsewhere.
        /// </summary>
        bool ComputePenetration(
            Vector3 center,
            float radius,
            float segment,
            out Vector3 direction,
            out float distance);

        /// <summary>
        /// Water surface height at a horizontal position, or float.MinValue
        /// where there is no water. Separate from geometry casts because
        /// water is usually a volume or a height field rather than something
        /// you sweep against.
        /// </summary>
        float SampleWaterLevel(Vector3 position);
    }

    public struct CollisionHit
    {
        public Vector3 Point;
        public Vector3 Normal;

        /// <summary>Distance travelled along the sweep before contact.</summary>
        public float Distance;

        /// <summary>
        /// Entity id of what was hit, or 0 for static world. Non-zero is
        /// what promotes standing on something into riding its reference
        /// frame - see MoveState.ParentEntity.
        /// </summary>
        public int EntityId;

        /// <summary>
        /// True if the surface is flat enough to stand on. Computed by the
        /// motor from the normal and the profile's slope limit rather than
        /// by the implementation, so that the decision lives in one place
        /// and both worlds cannot disagree about it.
        /// </summary>
        public bool IsWalkable;
    }
}
