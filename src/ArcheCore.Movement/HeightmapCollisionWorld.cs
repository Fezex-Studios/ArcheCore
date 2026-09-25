using System.Numerics;

namespace ArcheCore.Movement.Terrain
{
    /// <summary>
    /// ICollisionWorld over a height field. The server-side counterpart to
    /// the client's UnityCollisionWorld - see that class's remarks for why
    /// self-collision filtering matters there; nothing here needs the
    /// equivalent, because a heightmap has no character colliders to
    /// confuse itself with in the first place.
    ///
    /// ONE HEIGHTMAP OR A WHOLE WORLD. The height field can be a single
    /// HeightmapData (one Unity terrain) or a TiledHeightField (every
    /// terrain in a partitioned world, stitched). Sweeps that cross from
    /// one terrain onto its neighbour keep working, because every sample
    /// along the sweep asks the field, not one fixed heightmap - with a
    /// single heightmap the old behaviour ("swept off the edge, no hit") is
    /// unchanged.
    ///
    /// TERRAIN ONLY. This answers "where is the ground" and nothing about
    /// walls, buildings, or props - there is no lateral blocking geometry
    /// in a heightmap. Practically: CharacterMotor's slope/step/ground
    /// logic works correctly against this (that's all height-based), but a
    /// player who walks in a straight line through a building's footprint
    /// will not be stopped by this collision world. That gap is exactly
    /// the "mesh BVH later" half of the plan in ICollisionWorld's own doc
    /// comment. Until then, MovementValidator uses this for what it CAN
    /// catch outright: a reported position embedded below the ground.
    ///
    /// NO DATA = NOT VALIDATED, NOT REJECTED. A position with no height
    /// data under it (an interior instance, a dungeon, open ocean, a tile
    /// with no heightmap exported yet) has no ground truth to check
    /// against, so every query returns "nothing to report" rather than
    /// inventing an answer.
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

        private readonly IHeightField _field;

        public HeightmapCollisionWorld(HeightmapData heightmap)
            : this((IHeightField)heightmap)
        {
        }

        public HeightmapCollisionWorld(IHeightField field)
        {
            _field = field;
        }

        /// <summary>The ground data this world answers from.</summary>
        public IHeightField HeightField => _field;

        public bool CapsuleCast(
            Vector3 center, float radius, float segment,
            Vector3 direction, float distance, out CollisionHit hit)
        {
            hit = default;

            if (distance <= 0f || !TryBottomClearance(center, radius, segment, out _))
                return false;

            for (int i = 1; i <= SweepSamples; i++)
            {
                float t = distance * i / SweepSamples;
                Vector3 p = center + direction * t;

                if (!TryBottomClearance(p, radius, segment, out float depth))
                    return false; // swept off the edge of known terrain

                if (depth <= 0f)
                {
                    float tLo = distance * (i - 1) / SweepSamples;
                    float tHi = t;

                    for (int iter = 0; iter < RefineIterations; iter++)
                    {
                        float tMid = (tLo + tHi) * 0.5f;
                        Vector3 pm = center + direction * tMid;

                        // A midpoint with no data sits between two samples
                        // that both had some - treat it as "not yet in
                        // contact" and keep bisecting toward the known hit.
                        if (TryBottomClearance(pm, radius, segment, out float midDepth) && midDepth <= 0f)
                            tHi = tMid;
                        else
                            tLo = tMid;
                    }

                    Vector3 contact = center + direction * tHi;

                    if (!_field.TrySampleHeight(contact.X, contact.Z, out float groundY))
                        return false;

                    _field.TrySampleNormal(contact.X, contact.Z, out Vector3 normal);

                    hit.Point = new Vector3(contact.X, groundY, contact.Z);
                    hit.Normal = normal;
                    hit.Distance = tHi;
                    hit.EntityId = 0;
                    return true;
                }
            }

            return false;
        }

        public bool CheckCapsule(Vector3 center, float radius, float segment) =>
            TryBottomClearance(center, radius, segment, out float clearance) && clearance <= 0f;

        public bool ComputePenetration(
            Vector3 center, float radius, float segment,
            out Vector3 direction, out float distance)
        {
            direction = Vector3.UnitY;
            distance = 0f;

            if (!TryBottomClearance(center, radius, segment, out float clearance) || clearance >= 0f)
                return false;

            distance = -clearance;
            return true;
        }

        /// <summary>
        /// No water layer in this phase - a heightmap-only world has
        /// nowhere to author one yet. Returning float.MinValue is the
        /// documented "no water here" answer CharacterMotor already
        /// expects (see UnityCollisionWorld.SampleWaterLevel). Add a water
        /// plane list per tile alongside the heightmaps when swimming needs
        /// to be server-validated.
        /// </summary>
        public float SampleWaterLevel(Vector3 position) => float.MinValue;

        /// <summary>
        /// Positive while the capsule's bottom is above the ground at this
        /// horizontal position; negative once it has sunk below. False when
        /// there is no ground data here at all. The capsule caller already
        /// keeps a skin-width gap out of this on purpose (see
        /// MovementProfile.SkinWidth), so no extra margin is added.
        /// </summary>
        private bool TryBottomClearance(Vector3 center, float radius, float segment, out float clearance)
        {
            if (!_field.TrySampleHeight(center.X, center.Z, out float groundY))
            {
                clearance = 0f;
                return false;
            }

            float bottomY = center.Y - segment - radius;
            clearance = bottomY - groundY;
            return true;
        }
    }
}
