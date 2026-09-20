using System.Numerics;

namespace ArcheCore.Movement.Terrain
{
    /// <summary>
    /// ICollisionWorld over a HeightmapData. The server-side counterpart to
    /// the client's UnityCollisionWorld - see that class's remarks for why
    /// self-collision filtering matters there; nothing here needs the
    /// equivalent, because a heightmap has no character colliders to
    /// confuse itself with in the first place.
    ///
    /// TERRAIN ONLY. This answers "where is the ground" and nothing about
    /// walls, buildings, or props - there is no lateral blocking geometry
    /// in a heightmap. Practically: CharacterMotor's slope/step/ground
    /// logic works correctly against this (that's all height-based), but a
    /// player who walks in a straight line through a building's footprint
    /// will not be stopped by this collision world. That gap is exactly
    /// the "mesh BVH later" half of the plan in ICollisionWorld's own doc
    /// comment - closing it needs importing real collision meshes for
    /// buildings/props, which is a separate, larger piece of work than a
    /// heightmap. Until then, MovementValidator uses this for what it CAN
    /// catch outright: a reported position embedded below the ground,
    /// which is the "falls through the floor" class of bug, and standing
    /// on terrain steeper than the profile allows.
    ///
    /// OUT OF BOUNDS = NOT VALIDATED, NOT REJECTED. A position outside the
    /// loaded heightmap's extent (an interior instance, a dungeon, a zone
    /// with no heightmap authored yet) has no ground truth to check
    /// against, so every query returns "nothing to report" rather than
    /// inventing an answer from clamped edge data. Instances need their
    /// own collision data before this can validate them - see the class
    /// remarks on HeightmapData.
    /// </summary>
    public sealed class HeightmapCollisionWorld : ICollisionWorld
    {
        /// <summary>How many points along a sweep to sample before giving
        /// up on finding a contact. CharacterMotor already bounds each
        /// individual sweep to at most half the capsule radius (see
        /// MaxSweepFraction), so this only ever has to resolve a short
        /// segment - 8 samples plus refinement is generous for that.</summary>
        private const int SweepSamples = 8;

        /// <summary>Bisection refinement passes after the coarse sweep
        /// finds which sample bracket the contact falls in.</summary>
        private const int RefineIterations = 6;

        private readonly HeightmapData _heightmap;

        public HeightmapCollisionWorld(HeightmapData heightmap)
        {
            _heightmap = heightmap;
        }

        public bool CapsuleCast(
            Vector3 center, float radius, float segment,
            Vector3 direction, float distance, out CollisionHit hit)
        {
            hit = default;

            if (distance <= 0f || !_heightmap.IsInBounds(center.X, center.Z))
                return false;

            float previousDepth = BottomClearance(center, radius, segment);

            for (int i = 1; i <= SweepSamples; i++)
            {
                float t = distance * i / SweepSamples;
                Vector3 p = center + direction * t;

                if (!_heightmap.IsInBounds(p.X, p.Z))
                    return false; // swept off the edge of known terrain

                float depth = BottomClearance(p, radius, segment);

                if (depth <= 0f)
                {
                    float tLo = distance * (i - 1) / SweepSamples;
                    float tHi = t;

                    for (int iter = 0; iter < RefineIterations; iter++)
                    {
                        float tMid = (tLo + tHi) * 0.5f;
                        Vector3 pm = center + direction * tMid;
                        if (BottomClearance(pm, radius, segment) <= 0f) tHi = tMid; else tLo = tMid;
                    }

                    Vector3 contact = center + direction * tHi;
                    float groundY = _heightmap.SampleHeight(contact.X, contact.Z);

                    hit.Point = new Vector3(contact.X, groundY, contact.Z);
                    hit.Normal = _heightmap.SampleNormal(contact.X, contact.Z);
                    hit.Distance = tHi;
                    hit.EntityId = 0;
                    return true;
                }

                previousDepth = depth;
            }

            return false;
        }

        public bool CheckCapsule(Vector3 center, float radius, float segment)
        {
            if (!_heightmap.IsInBounds(center.X, center.Z))
                return false;

            return BottomClearance(center, radius, segment) <= 0f;
        }

        public bool ComputePenetration(
            Vector3 center, float radius, float segment,
            out Vector3 direction, out float distance)
        {
            direction = Vector3.UnitY;
            distance = 0f;

            if (!_heightmap.IsInBounds(center.X, center.Z))
                return false;

            float clearance = BottomClearance(center, radius, segment);
            if (clearance >= 0f)
                return false;

            distance = -clearance;
            return true;
        }

        /// <summary>
        /// No water layer in this phase - a heightmap-only world has
        /// nowhere to author one yet. Returning float.MinValue is the
        /// documented "no water here" answer CharacterMotor already
        /// expects (see UnityCollisionWorld.SampleWaterLevel). Add a water
        /// plane list per zone alongside the heightmap when swimming needs
        /// to be server-validated.
        /// </summary>
        public float SampleWaterLevel(Vector3 position) => float.MinValue;

        /// <summary>
        /// Positive while the capsule's bottom is above the ground at this
        /// horizontal position; negative once it has sunk below. Zero is
        /// exact contact - the capsule caller already keeps a skin-width
        /// gap out of this on purpose (see MovementProfile.SkinWidth), so
        /// this class doesn't need its own margin on top of that.
        /// </summary>
        private float BottomClearance(Vector3 center, float radius, float segment)
        {
            float bottomY = center.Y - segment - radius;
            float groundY = _heightmap.SampleHeight(center.X, center.Z);
            return bottomY - groundY;
        }
    }
}
