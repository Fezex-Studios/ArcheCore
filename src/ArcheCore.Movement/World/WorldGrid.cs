using System;
using System.Collections.Generic;
using System.Globalization;

namespace ArcheCore.Movement.World
{
    /// <summary>
    /// One tile of the world partition. Integer tile indices, not world
    /// units: tile (0, 0) covers [0, TileSize) on X and Z, tile (-1, 0)
    /// covers [-TileSize, 0), and so on in every direction. There is no
    /// maximum - the grid is unbounded and sparse, so an ocean with nothing
    /// in it costs nothing.
    /// </summary>
    public readonly struct TileCoord : IEquatable<TileCoord>
    {
        public readonly int X;
        public readonly int Z;

        public TileCoord(int x, int z)
        {
            X = x;
            Z = z;
        }

        public bool Equals(TileCoord other) => X == other.X && Z == other.Z;
        public override bool Equals(object obj) => obj is TileCoord other && Equals(other);
        public override int GetHashCode() => unchecked((X * 73856093) ^ (Z * 19349663));
        public static bool operator ==(TileCoord a, TileCoord b) => a.Equals(b);
        public static bool operator !=(TileCoord a, TileCoord b) => !a.Equals(b);

        public override string ToString() => "(" + X + ", " + Z + ")";

        /// <summary>The additive scene holding this tile's static world, e.g. "tile_-2_3".</summary>
        public string SceneName => WorldGrid.SceneNameFor(this);
    }

    /// <summary>
    /// THE world partition, shared verbatim by the world server and the
    /// Unity client so the two can never disagree about which tile a
    /// position is in.
    ///
    /// WHAT A TILE IS
    ///
    /// A square of TileSize world units holding the STATIC world: terrain,
    /// rocks, trees, buildings, water. On the client each tile is one
    /// additive scene named tile_{x}_{z}, streamed in and out around the
    /// player (WorldStreamer). On the server the same area is covered by
    /// whatever heightmaps overlap it (TiledHeightField).
    ///
    /// Tiles are NOT the interest grid. SpatialGrid/InterestManager decide
    /// which DYNAMIC entities (players, NPCs, nodes) a client hears about,
    /// at a much finer 50-unit cell size tuned for replication. The two
    /// grids answer different questions and are deliberately independent -
    /// retuning view distance must never force re-cutting the world into
    /// new scenes.
    ///
    /// WHY 512
    ///
    /// A power of two, so tile boundaries - and the floating-origin shifts
    /// the client snaps to them - are exactly representable in float at
    /// any distance from the origin. Large enough that a running player
    /// crosses a boundary roughly every 90 seconds, so the streamer isn't
    /// churning; small enough that a 3x3 ring (1.5km across) is a sensible
    /// resident set on a mid-range GPU. Matches a Unity terrain of 513x513
    /// heightmap resolution at 1m spacing, the standard terrain size.
    ///
    /// CHANGING IT LATER means re-cutting every tile scene and re-exporting
    /// every heightmap. Treat it as fixed once content exists.
    ///
    /// THE ONE AUTHORING RULE
    ///
    /// No single Terrain may be larger than TileSize on either axis. That
    /// is what lets streaming work without terrains being aligned to the
    /// grid: a terrain covering point P then always has its corner (its
    /// transform position, which decides which tile scene it lives in)
    /// within one tile of P, so loading the 3x3 ring around the player
    /// always includes the ground under their feet. The World Tiles tab in
    /// Dev Tools validates this.
    /// </summary>
    public static class WorldGrid
    {
        /// <summary>World units per tile edge. See class remarks before changing.</summary>
        public const float TileSize = 512f;

        /// <summary>Prefix of every tile scene's name.</summary>
        public const string TileScenePrefix = "tile_";

        public static TileCoord TileOf(float worldX, float worldZ) => new TileCoord(
            (int)MathF.Floor(worldX / TileSize),
            (int)MathF.Floor(worldZ / TileSize));

        /// <summary>World-space X of this tile's west edge.</summary>
        public static float MinX(TileCoord tile) => tile.X * TileSize;

        /// <summary>World-space Z of this tile's south edge.</summary>
        public static float MinZ(TileCoord tile) => tile.Z * TileSize;

        /// <summary>World-space centre of the tile on the ground plane (Y is 0).</summary>
        public static void Center(TileCoord tile, out float x, out float z)
        {
            x = (tile.X + 0.5f) * TileSize;
            z = (tile.Z + 0.5f) * TileSize;
        }

        public static bool Contains(TileCoord tile, float worldX, float worldZ) =>
            TileOf(worldX, worldZ) == tile;

        /// <summary>
        /// Tiles apart along the longer axis - "rings" around a tile. The
        /// 3x3 block around A is every tile at distance &lt;= 1.
        /// </summary>
        public static int Distance(TileCoord a, TileCoord b) =>
            Math.Max(Math.Abs(a.X - b.X), Math.Abs(a.Z - b.Z));

        /// <summary>
        /// Every tile within <paramref name="radius"/> rings of centre,
        /// NEAREST FIRST (centre, then ring 1, then ring 2...), so a caller
        /// that loads in list order loads the ground under the player
        /// before the horizon. Appends to <paramref name="results"/>.
        /// </summary>
        public static void GetTilesInRadius(TileCoord centre, int radius, List<TileCoord> results)
        {
            if (radius < 0) return;

            results.Add(centre);

            for (int ring = 1; ring <= radius; ring++)
            {
                for (int dx = -ring; dx <= ring; dx++)
                {
                    results.Add(new TileCoord(centre.X + dx, centre.Z - ring));
                    results.Add(new TileCoord(centre.X + dx, centre.Z + ring));
                }

                for (int dz = -ring + 1; dz <= ring - 1; dz++)
                {
                    results.Add(new TileCoord(centre.X - ring, centre.Z + dz));
                    results.Add(new TileCoord(centre.X + ring, centre.Z + dz));
                }
            }
        }

        /// <summary>
        /// Every tile an axis-aligned rectangle overlaps. Used to index a
        /// heightmap (or any bounded thing) into the tiles it touches.
        /// A rectangle ending exactly ON a boundary does not spill into the
        /// next tile.
        /// </summary>
        public static void GetTilesOverlapping(
            float minX, float minZ, float maxX, float maxZ, List<TileCoord> results)
        {
            var lo = TileOf(minX, minZ);

            // Nudge the max edge inward so a terrain spanning exactly
            // [0, 512] indexes only into tile 0, not tile 0 AND tile 1.
            var hi = TileOf(
                maxX > minX ? maxX - 0.001f : maxX,
                maxZ > minZ ? maxZ - 0.001f : maxZ);

            for (int x = lo.X; x <= hi.X; x++)
            for (int z = lo.Z; z <= hi.Z; z++)
                results.Add(new TileCoord(x, z));
        }

        public static string SceneNameFor(TileCoord tile) =>
            TileScenePrefix +
            tile.X.ToString(CultureInfo.InvariantCulture) + "_" +
            tile.Z.ToString(CultureInfo.InvariantCulture);

        /// <summary>Parses "tile_-2_3" (or a path ending in "tile_-2_3.unity").</summary>
        public static bool TryParseSceneName(string sceneNameOrPath, out TileCoord tile)
        {
            tile = default;
            if (string.IsNullOrEmpty(sceneNameOrPath)) return false;

            string name = sceneNameOrPath;

            int slash = Math.Max(name.LastIndexOf('/'), name.LastIndexOf('\\'));
            if (slash >= 0) name = name.Substring(slash + 1);

            if (name.EndsWith(".unity", StringComparison.OrdinalIgnoreCase))
                name = name.Substring(0, name.Length - ".unity".Length);

            if (!name.StartsWith(TileScenePrefix, StringComparison.Ordinal))
                return false;

            string rest = name.Substring(TileScenePrefix.Length);

            // The separator is the first '_' AFTER the first character, so
            // a negative X ("-2_3") still splits in the right place.
            int sep = rest.IndexOf('_', 1);
            if (sep <= 0 || sep == rest.Length - 1) return false;

            if (!int.TryParse(rest.Substring(0, sep), NumberStyles.AllowLeadingSign, CultureInfo.InvariantCulture, out int x) ||
                !int.TryParse(rest.Substring(sep + 1), NumberStyles.AllowLeadingSign, CultureInfo.InvariantCulture, out int z))
                return false;

            tile = new TileCoord(x, z);
            return true;
        }

        /// <summary>
        /// Rounds a world coordinate to the nearest tile boundary. The
        /// client's floating origin only ever moves by whole tiles, which
        /// keeps every shift an exact float and means a tile scene's
        /// contents land on the same sub-millimetre positions no matter how
        /// many shifts happened before it loaded.
        /// </summary>
        public static float SnapToTileBoundary(float worldCoordinate) =>
            MathF.Round(worldCoordinate / TileSize) * TileSize;
    }
}
