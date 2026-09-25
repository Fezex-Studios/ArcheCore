using System;
using System.Collections.Generic;
using System.IO;
using System.Text;

namespace ArcheCore.Movement.World
{
    /// <summary>
    /// How PvP works inside a zone. Ordered from most to least protected, so
    /// "is PvP allowed here" is a comparison rather than a list.
    /// </summary>
    public enum ZonePvpMode : byte
    {
        /// <summary>Sanctuary. Nobody can attack anybody - towns, starting areas.</summary>
        Safe = 0,

        /// <summary>Peaceful territory. No open PvP (flagging/duels come later).</summary>
        Peaceful = 1,

        /// <summary>Open PvP - ArcheAge's "conflict" state.</summary>
        Contested = 2,

        /// <summary>Open PvP with no crime consequences - war zones, the ocean.</summary>
        War = 3,
    }

    /// <summary>
    /// One named region of the world - Solzreed, Gweonid Forest. Purely data:
    /// what players see (name, subtitle), what rules apply (PvP mode, level
    /// range), and the colour it's painted in the editor.
    /// </summary>
    public sealed class ZoneDefinition
    {
        /// <summary>1..65535. 0 is reserved for "no zone" (unpainted ground).</summary>
        public ushort Id;

        /// <summary>
        /// Stable identifier for code, Lua and folder names - "solzreed",
        /// "gweonid_forest_3". Lowercase, no spaces. Never shown to players.
        /// </summary>
        public string Key = "";

        /// <summary>What the zone banner says - "Solzreed Peninsula".</summary>
        public string DisplayName = "";

        /// <summary>Optional smaller line under the name - "Nuia", "Haranya Frontier".</summary>
        public string Subtitle = "";

        public ZonePvpMode PvpMode = ZonePvpMode.Peaceful;
        public ushort MinLevel;
        public ushort MaxLevel;

        /// <summary>Editor paint colour, 0xRRGGBBAA.</summary>
        public uint ColorRgba = 0x4DA6FFFF;

        public bool AllowsPvp => PvpMode >= ZonePvpMode.Contested;

        public ZoneDefinition Clone() => (ZoneDefinition)MemberwiseClone();
    }

    /// <summary>
    /// Which zone every patch of ground belongs to, shared verbatim by the
    /// world server (PvP rules, "entered zone" events) and the client (the
    /// zone banner, later music and weather).
    ///
    /// ZONES ARE NOT TILES
    ///
    /// Tiles (WorldGrid) are fixed 512m squares that exist so the client can
    /// stream the world. Zones are design regions of any shape - Solzreed
    /// covers many tiles, and a tile on a border holds parts of two zones.
    /// Redrawing a zone border never touches a tile scene.
    ///
    /// THE GRID
    ///
    /// Zones are painted onto 32m cells - 16x16 per tile. Fine enough that
    /// a border can follow a river or a ridge (ArcheAge's jagged borders are
    /// the same idea), coarse enough that a whole continent is a few MB.
    /// Only tiles with at least one painted cell are stored; the rest of the
    /// world is zone 0, "no zone".
    ///
    /// ONE FILE, DEFINITIONS AND BORDERS TOGETHER
    ///
    /// The zone list lives in the same file as the painted cells rather
    /// than in a database table. The two always change together, and one
    /// file means the map can never reference a zone id the definitions
    /// don't have. The client and server each load a copy; the server sends
    /// its Hash on entering the world so a client with a stale copy says so.
    ///
    /// Written by Dev Tools > Zones; read with Load. Not thread safe.
    /// </summary>
    public sealed class ZoneMap
    {
        private const uint Magic = 0x4D5A4341; // "ACZM"
        private const uint FormatVersion = 1;

        /// <summary>World units per zone cell edge.</summary>
        public const float CellSize = 32f;

        /// <summary>Cells along one edge of a WorldGrid tile (512 / 32 = 16).</summary>
        public const int CellsPerTile = (int)(WorldGrid.TileSize / CellSize);

        private const int CellsPerTileSquared = CellsPerTile * CellsPerTile;

        private readonly Dictionary<ushort, ZoneDefinition> _zones = new Dictionary<ushort, ZoneDefinition>();
        private readonly Dictionary<TileCoord, ushort[]> _cells = new Dictionary<TileCoord, ushort[]>();

        /// <summary>Every defined zone, in no particular order.</summary>
        public IEnumerable<ZoneDefinition> Zones => _zones.Values;

        public int ZoneCount => _zones.Count;

        /// <summary>Tiles that have at least one painted cell.</summary>
        public IEnumerable<TileCoord> PaintedTiles => _cells.Keys;

        // ── Definitions ──────────────────────────────────────────────────

        public bool TryGetZone(ushort id, out ZoneDefinition zone) => _zones.TryGetValue(id, out zone);

        public ZoneDefinition GetZone(ushort id) => _zones.TryGetValue(id, out var z) ? z : null;

        public ZoneDefinition FindByKey(string key)
        {
            foreach (var z in _zones.Values)
                if (string.Equals(z.Key, key, StringComparison.OrdinalIgnoreCase))
                    return z;
            return null;
        }

        /// <summary>Adds or replaces a definition. Id 0 is rejected.</summary>
        public void SetZone(ZoneDefinition zone)
        {
            if (zone == null) throw new ArgumentNullException(nameof(zone));
            if (zone.Id == 0) throw new ArgumentException("Zone id 0 is reserved for 'no zone'.");
            _zones[zone.Id] = zone;
        }

        /// <summary>
        /// Removes a definition AND erases every cell painted with it, so the
        /// map can never point at a zone that no longer exists.
        /// </summary>
        public void RemoveZone(ushort id)
        {
            if (!_zones.Remove(id)) return;

            foreach (var cells in _cells.Values)
                for (int i = 0; i < cells.Length; i++)
                    if (cells[i] == id) cells[i] = 0;

            PruneEmptyTiles();
        }

        /// <summary>Lowest unused id, for a new zone.</summary>
        public ushort NextFreeId()
        {
            for (int id = 1; id <= ushort.MaxValue; id++)
                if (!_zones.ContainsKey((ushort)id)) return (ushort)id;
            throw new InvalidOperationException("All 65535 zone ids are in use.");
        }

        // ── Lookup ───────────────────────────────────────────────────────

        /// <summary>Zone id at a world position, 0 where nothing is painted. O(1).</summary>
        public ushort ZoneIdAt(float worldX, float worldZ)
        {
            var tile = WorldGrid.TileOf(worldX, worldZ);
            if (!_cells.TryGetValue(tile, out var cells)) return 0;

            CellIndex(tile, worldX, worldZ, out int cx, out int cz);
            return cells[cz * CellsPerTile + cx];
        }

        /// <summary>Zone at a world position, or null where nothing is painted.</summary>
        public ZoneDefinition ZoneAt(float worldX, float worldZ) => GetZone(ZoneIdAt(worldX, worldZ));

        // ── Painting ─────────────────────────────────────────────────────

        /// <summary>Paints the cell containing this world position. id 0 erases.</summary>
        public void Paint(float worldX, float worldZ, ushort id)
        {
            var tile = WorldGrid.TileOf(worldX, worldZ);
            CellIndex(tile, worldX, worldZ, out int cx, out int cz);
            SetCell(tile, cx, cz, id);
        }

        /// <summary>
        /// Paints every cell whose centre is within <paramref name="radius"/>
        /// of a world position - the editor's round brush. Always paints at
        /// least the cell under the centre. Returns cells changed.
        /// </summary>
        public int PaintCircle(float worldX, float worldZ, float radius, ushort id)
        {
            int changed = 0;
            float r = Math.Max(radius, 0f);
            float r2 = r * r;

            int minCx = (int)Math.Floor((worldX - r) / CellSize);
            int maxCx = (int)Math.Floor((worldX + r) / CellSize);
            int minCz = (int)Math.Floor((worldZ - r) / CellSize);
            int maxCz = (int)Math.Floor((worldZ + r) / CellSize);

            for (int gx = minCx; gx <= maxCx; gx++)
            for (int gz = minCz; gz <= maxCz; gz++)
            {
                float centreX = (gx + 0.5f) * CellSize;
                float centreZ = (gz + 0.5f) * CellSize;
                float dx = centreX - worldX, dz = centreZ - worldZ;

                bool underBrushCentre = (int)Math.Floor(worldX / CellSize) == gx && (int)Math.Floor(worldZ / CellSize) == gz;
                if (!underBrushCentre && dx * dx + dz * dz > r2) continue;

                if (PaintGlobalCell(gx, gz, id)) changed++;
            }

            return changed;
        }

        /// <summary>Zone id of one cell of one tile. 0 if the tile has nothing painted.</summary>
        public ushort GetCell(TileCoord tile, int cx, int cz) =>
            _cells.TryGetValue(tile, out var cells) ? cells[cz * CellsPerTile + cx] : (ushort)0;

        /// <summary>Sets one cell. Returns true if it changed. Painting 0 on an empty tile allocates nothing.</summary>
        public bool SetCell(TileCoord tile, int cx, int cz, ushort id)
        {
            if (cx < 0 || cx >= CellsPerTile || cz < 0 || cz >= CellsPerTile)
                throw new ArgumentOutOfRangeException(nameof(cx), "Cell index outside the tile.");

            if (!_cells.TryGetValue(tile, out var cells))
            {
                if (id == 0) return false;
                _cells[tile] = cells = new ushort[CellsPerTileSquared];
            }

            int i = cz * CellsPerTile + cx;
            if (cells[i] == id) return false;

            cells[i] = id;
            return true;
        }

        /// <summary>Fills every cell of a tile with one zone (0 clears the tile).</summary>
        public void FillTile(TileCoord tile, ushort id)
        {
            if (id == 0)
            {
                _cells.Remove(tile);
                return;
            }

            if (!_cells.TryGetValue(tile, out var cells))
                _cells[tile] = cells = new ushort[CellsPerTileSquared];

            for (int i = 0; i < cells.Length; i++) cells[i] = id;
        }

        private bool PaintGlobalCell(int globalCx, int globalCz, ushort id)
        {
            int tx = FloorDiv(globalCx, CellsPerTile);
            int tz = FloorDiv(globalCz, CellsPerTile);
            int cx = globalCx - tx * CellsPerTile;
            int cz = globalCz - tz * CellsPerTile;
            return SetCell(new TileCoord(tx, tz), cx, cz, id);
        }

        // ── Queries used by tools ────────────────────────────────────────

        /// <summary>Tiles containing at least one cell of this zone.</summary>
        public List<TileCoord> TilesContaining(ushort id)
        {
            var result = new List<TileCoord>();
            foreach (var kv in _cells)
                if (Array.IndexOf(kv.Value, id) >= 0) result.Add(kv.Key);
            return result;
        }

        /// <summary>
        /// The zone covering the most cells of a tile (0 if the tile is
        /// mostly unpainted). What "which zone's folder does this tile belong
        /// in" means.
        /// </summary>
        public ushort DominantZone(TileCoord tile)
        {
            if (!_cells.TryGetValue(tile, out var cells)) return 0;

            var counts = new Dictionary<ushort, int>();
            foreach (var id in cells)
                counts[id] = counts.TryGetValue(id, out int n) ? n + 1 : 1;

            ushort best = 0;
            int bestCount = -1;
            foreach (var kv in counts)
                if (kv.Value > bestCount || (kv.Value == bestCount && kv.Key < best))
                {
                    best = kv.Key;
                    bestCount = kv.Value;
                }

            return best;
        }

        /// <summary>Painted cells per zone id (0 excluded).</summary>
        public Dictionary<ushort, int> CountCells()
        {
            var counts = new Dictionary<ushort, int>();
            foreach (var cells in _cells.Values)
                foreach (var id in cells)
                    if (id != 0) counts[id] = counts.TryGetValue(id, out int n) ? n + 1 : 1;
            return counts;
        }

        /// <summary>Drops tiles whose cells are all 0, so the file only stores painted ground.</summary>
        public void PruneEmptyTiles()
        {
            var empty = new List<TileCoord>();
            foreach (var kv in _cells)
            {
                bool any = false;
                foreach (var id in kv.Value) if (id != 0) { any = true; break; }
                if (!any) empty.Add(kv.Key);
            }
            foreach (var t in empty) _cells.Remove(t);
        }

        // ── Serialisation ────────────────────────────────────────────────

        public void Save(Stream stream)
        {
            PruneEmptyTiles();

            using var w = new BinaryWriter(stream, Encoding.UTF8, leaveOpen: true);

            w.Write(Magic);
            w.Write(FormatVersion);
            w.Write(CellSize);

            // Sorted, so saving the same map twice produces identical bytes -
            // which is what makes the hash meaningful and diffs quiet.
            var zones = new List<ZoneDefinition>(_zones.Values);
            zones.Sort((a, b) => a.Id.CompareTo(b.Id));

            w.Write(zones.Count);
            foreach (var z in zones)
            {
                w.Write(z.Id);
                w.Write(z.Key ?? "");
                w.Write(z.DisplayName ?? "");
                w.Write(z.Subtitle ?? "");
                w.Write((byte)z.PvpMode);
                w.Write(z.MinLevel);
                w.Write(z.MaxLevel);
                w.Write(z.ColorRgba);
            }

            var tiles = new List<TileCoord>(_cells.Keys);
            tiles.Sort((a, b) => a.X != b.X ? a.X.CompareTo(b.X) : a.Z.CompareTo(b.Z));

            w.Write(tiles.Count);
            foreach (var t in tiles)
            {
                w.Write(t.X);
                w.Write(t.Z);
                foreach (var id in _cells[t]) w.Write(id);
            }
        }

        public byte[] ToBytes()
        {
            using var ms = new MemoryStream();
            Save(ms);
            return ms.ToArray();
        }

        public void Save(string path)
        {
            string dir = Path.GetDirectoryName(path);
            if (!string.IsNullOrEmpty(dir)) Directory.CreateDirectory(dir);
            File.WriteAllBytes(path, ToBytes());
        }

        public static ZoneMap Load(Stream stream)
        {
            using var r = new BinaryReader(stream, Encoding.UTF8, leaveOpen: true);

            if (r.ReadUInt32() != Magic)
                throw new InvalidDataException("Not an ArcheCore zone map (bad magic).");

            uint version = r.ReadUInt32();
            if (version != FormatVersion)
                throw new InvalidDataException($"Zone map format v{version}, expected v{FormatVersion}.");

            float cellSize = r.ReadSingle();
            if (Math.Abs(cellSize - CellSize) > 0.001f)
                throw new InvalidDataException($"Zone map uses {cellSize}m cells, this build expects {CellSize}m.");

            var map = new ZoneMap();

            int zoneCount = r.ReadInt32();
            for (int i = 0; i < zoneCount; i++)
            {
                var z = new ZoneDefinition
                {
                    Id          = r.ReadUInt16(),
                    Key         = r.ReadString(),
                    DisplayName = r.ReadString(),
                    Subtitle    = r.ReadString(),
                    PvpMode     = (ZonePvpMode)r.ReadByte(),
                    MinLevel    = r.ReadUInt16(),
                    MaxLevel    = r.ReadUInt16(),
                    ColorRgba   = r.ReadUInt32()
                };

                if (z.Id != 0) map._zones[z.Id] = z;
            }

            int tileCount = r.ReadInt32();
            for (int i = 0; i < tileCount; i++)
            {
                var tile = new TileCoord(r.ReadInt32(), r.ReadInt32());
                var cells = new ushort[CellsPerTileSquared];
                for (int c = 0; c < cells.Length; c++) cells[c] = r.ReadUInt16();
                map._cells[tile] = cells;
            }

            return map;
        }

        public static ZoneMap Load(byte[] bytes)
        {
            using var ms = new MemoryStream(bytes, writable: false);
            return Load(ms);
        }

        public static ZoneMap Load(string path)
        {
            using var fs = File.OpenRead(path);
            return Load(fs);
        }

        /// <summary>
        /// FNV-1a 64 of the saved bytes, as 16 hex digits. Identical maps
        /// give identical hashes (Save is deterministic), so the server
        /// sending its hash on enter-world is enough for a client to know its
        /// copy is stale.
        /// </summary>
        public string ComputeHash() => HashBytes(ToBytes());

        public static string HashBytes(byte[] bytes)
        {
            ulong hash = 14695981039346656037UL;
            foreach (byte b in bytes)
            {
                hash ^= b;
                hash *= 1099511628211UL;
            }
            return hash.ToString("x16");
        }

        // ── Helpers ──────────────────────────────────────────────────────

        private static void CellIndex(TileCoord tile, float worldX, float worldZ, out int cx, out int cz)
        {
            cx = (int)Math.Floor((worldX - WorldGrid.MinX(tile)) / CellSize);
            cz = (int)Math.Floor((worldZ - WorldGrid.MinZ(tile)) / CellSize);

            // Float rounding right on the tile's far edge can land one past it.
            if (cx >= CellsPerTile) cx = CellsPerTile - 1; else if (cx < 0) cx = 0;
            if (cz >= CellsPerTile) cz = CellsPerTile - 1; else if (cz < 0) cz = 0;
        }

        private static int FloorDiv(int a, int b) => a >= 0 ? a / b : -((-a + b - 1) / b);
    }
}
