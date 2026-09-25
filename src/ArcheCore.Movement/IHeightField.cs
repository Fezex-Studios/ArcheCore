using System.Numerics;

namespace ArcheCore.Movement.Terrain
{
    /// <summary>
    /// "Where is the ground at (x, z)?" - the one question
    /// HeightmapCollisionWorld needs answered, split out so it can be
    /// answered by ONE heightmap (HeightmapData) or by a whole partitioned
    /// world of them stitched together (TiledHeightField) with no change to
    /// the collision code.
    ///
    /// The Try- shape is the point. "No ground data here" is a normal,
    /// expected answer - open ocean, an unexported tile, an interior - and
    /// every caller has to handle it as "can't validate", never as
    /// "height zero". A plain float return would invite exactly that bug.
    /// </summary>
    public interface IHeightField
    {
        /// <summary>World-space ground height, or false where there is no data.</summary>
        bool TrySampleHeight(float worldX, float worldZ, out float height);

        /// <summary>Surface normal, or false where there is no data.</summary>
        bool TrySampleNormal(float worldX, float worldZ, out Vector3 normal);
    }
}
