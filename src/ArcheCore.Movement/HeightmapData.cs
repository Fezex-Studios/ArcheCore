using System;
using System.IO;
using System.Numerics;

namespace ArcheCore.Movement.Terrain
{
    /// <summary>
    /// A regular-grid heightmap plus the world-space transform that places
    /// it, and nothing else - no engine types, no Unity Terrain reference.
    /// This is the file that makes the world server's copy of the ground
    /// the SAME data the client's Unity Terrain renders, rather than a
    /// hand-tuned approximation of it - see ICollisionWorld's remark that
    /// consistency matters more than fidelity: a server heightmap that
    /// disagrees with the client's visual mesh produces constant small
    /// corrections on every slope.
    ///
    /// ONE HEIGHTMAP PER ZONE/SCENE. A multi-terrain or instanced world
    /// needs one of these per terrain tile, keyed by zone id - that
    /// composition lives above this class, not in it.
    ///
    /// WHAT THIS DOES NOT COVER, ON PURPOSE
    ///
    /// This is ground height only - open, rolling terrain. It says nothing
    /// about caves, overhangs, buildings, bridges, or anything else that
    /// isn't a single height per (x, z). Those need the "mesh BVH later"
    /// half of the ICollisionWorld plan - a real collision mesh import, not
    /// an extension of this class. Treat instances and dungeons as
    /// excluded from terrain validation (see HeightmapCollisionWorld) until
    /// that phase exists, not as something this format should stretch to
    /// fit.
    /// </summary>
    public sealed class HeightmapData
    {
        private const uint Magic = 0x41434854; // "ACHT"
        private const uint FormatVersion = 1;

        /// <summary>World-space X of grid cell (0, 0).</summary>
        public float OriginX { get; }

        /// <summary>World-space Z of grid cell (0, 0).</summary>
        public float OriginZ { get; }

        /// <summary>World-space size the grid spans along X.</summary>
        public float SizeX { get; }

        /// <summary>World-space size the grid spans along Z.</summary>
        public float SizeZ { get; }

        /// <summary>Grid resolution along X (number of samples, not cells).</summary>
        public int ResolutionX { get; }

        /// <summary>Grid resolution along Z.</summary>
        public int ResolutionZ { get; }

        /// <summary>
        /// Row-major heights, ResolutionX * ResolutionZ entries, indexed
        /// [z * ResolutionX + x]. World-space Y, not a 0-1 normalized
        /// value - Unity's TerrainData.GetHeights returns normalized
        /// samples that the exporter must multiply by terrainData.size.y
        /// before this constructor ever sees them, precisely so nothing
        /// downstream has to remember the denormalization step.
        /// </summary>
        private readonly float[] _heights;

        public HeightmapData(
            float originX, float originZ,
            float sizeX, float sizeZ,
            int resolutionX, int resolutionZ,
            float[] heights)
        {
            if (resolutionX < 2 || resolutionZ < 2)
                throw new ArgumentException("Heightmap resolution must be at least 2x2.");
            if (heights == null || heights.Length != resolutionX * resolutionZ)
                throw new ArgumentException("Height array does not match resolutionX * resolutionZ.");
            if (sizeX <= 0f || sizeZ <= 0f)
                throw new ArgumentException("Heightmap world size must be positive.");

            OriginX = originX;
            OriginZ = originZ;
            SizeX = sizeX;
            SizeZ = sizeZ;
            ResolutionX = resolutionX;
            ResolutionZ = resolutionZ;
            _heights = heights;
        }

        /// <summary>
        /// Bilinearly-interpolated world-space ground height at (x, z).
        /// Clamped to the grid edge outside the covered area rather than
        /// extrapolating - a query past the terrain's bounds almost always
        /// means the position is invalid for a different reason (world
        /// bound, wrong zone) and returning the edge height avoids
        /// inventing a slope that isn't there.
        /// </summary>
        public float SampleHeight(float worldX, float worldZ)
        {
            float u = (worldX - OriginX) / SizeX * (ResolutionX - 1);
            float v = (worldZ - OriginZ) / SizeZ * (ResolutionZ - 1);

            u = Clamp(u, 0f, ResolutionX - 1);
            v = Clamp(v, 0f, ResolutionZ - 1);

            int x0 = (int)MathF.Floor(u);
            int z0 = (int)MathF.Floor(v);
            int x1 = Math.Min(x0 + 1, ResolutionX - 1);
            int z1 = Math.Min(z0 + 1, ResolutionZ - 1);

            float tx = u - x0;
            float tz = v - z0;

            float h00 = At(x0, z0);
            float h10 = At(x1, z0);
            float h01 = At(x0, z1);
            float h11 = At(x1, z1);

            float hx0 = h00 + (h10 - h00) * tx;
            float hx1 = h01 + (h11 - h01) * tx;

            return hx0 + (hx1 - hx0) * tz;
        }

        /// <summary>
        /// Surface normal at (x, z), from the same central-difference
        /// slope a Unity Terrain uses. Needed for IsWalkable checks - a
        /// height-only query can't tell a gentle rise from a cliff.
        /// </summary>
        public Vector3 SampleNormal(float worldX, float worldZ)
        {
            float dx = SizeX / (ResolutionX - 1);
            float dz = SizeZ / (ResolutionZ - 1);

            float hL = SampleHeight(worldX - dx, worldZ);
            float hR = SampleHeight(worldX + dx, worldZ);
            float hD = SampleHeight(worldX, worldZ - dz);
            float hU = SampleHeight(worldX, worldZ + dz);

            var normal = new Vector3(hL - hR, 2f * dx, hD - hU);
            return normal.LengthSquared() > 1e-12f ? Vector3.Normalize(normal) : Vector3.UnitY;
        }

        public bool IsInBounds(float worldX, float worldZ) =>
            worldX >= OriginX && worldX <= OriginX + SizeX &&
            worldZ >= OriginZ && worldZ <= OriginZ + SizeZ;

        private float At(int x, int z) => _heights[z * ResolutionX + x];

        private static float Clamp(float v, float min, float max) =>
            v < min ? min : v > max ? max : v;

        // ---------------------------------------------------------------
        // Binary I/O. Deliberately a hand-rolled format rather than JSON -
        // a 1025x1025 heightmap is 4MB of floats and this is loaded once
        // at zone startup, not hand-edited, so there is nothing readability
        // buys here that's worth the size and parse cost.
        // ---------------------------------------------------------------

        public void Save(string path)
        {
            using var stream = File.Create(path);
            using var writer = new BinaryWriter(stream);

            writer.Write(Magic);
            writer.Write(FormatVersion);
            writer.Write(OriginX);
            writer.Write(OriginZ);
            writer.Write(SizeX);
            writer.Write(SizeZ);
            writer.Write(ResolutionX);
            writer.Write(ResolutionZ);

            foreach (var h in _heights)
                writer.Write(h);
        }

        public static HeightmapData Load(string path)
        {
            using var stream = File.OpenRead(path);
            using var reader = new BinaryReader(stream);

            uint magic = reader.ReadUInt32();
            if (magic != Magic)
                throw new InvalidDataException($"'{path}' is not an ArcheCore heightmap (bad magic).");

            uint version = reader.ReadUInt32();
            if (version != FormatVersion)
                throw new InvalidDataException($"'{path}' is heightmap format v{version}, expected v{FormatVersion}.");

            float originX = reader.ReadSingle();
            float originZ = reader.ReadSingle();
            float sizeX = reader.ReadSingle();
            float sizeZ = reader.ReadSingle();
            int resolutionX = reader.ReadInt32();
            int resolutionZ = reader.ReadInt32();

            var heights = new float[resolutionX * resolutionZ];
            for (int i = 0; i < heights.Length; i++)
                heights[i] = reader.ReadSingle();

            return new HeightmapData(originX, originZ, sizeX, sizeZ, resolutionX, resolutionZ, heights);
        }
    }
}
