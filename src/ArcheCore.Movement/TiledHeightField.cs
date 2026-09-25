using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Numerics;
using ArcheCore.Movement.World;

namespace ArcheCore.Movement.Terrain
{
    /// <summary>
    /// Every terrain heightmap in a partitioned world, answering height
    /// queries as if it were one seamless field. The server's half of world
    /// partitioning: the client streams tile SCENES around the player, the
    /// server streams tile HEIGHTS around wherever players are.
    ///
    /// INDEXED AT BOOT, LOADED ON DEMAND
    ///
    /// Only each file's 32-byte header is read at startup. The header says
    /// where the terrain sits, which is enough to index it into every
    /// WorldGrid tile it overlaps. The samples - 1MB for a 513x513 terrain,
    /// 4MB at 1025x1025 - load the first time a query lands on that
    /// terrain, and unload again after sitting unused (EvictIdle). An
    /// ArcheAge-sized continent is thousands of terrains; a shard with
    /// players in a few dozen places only ever holds those few dozen in
    /// memory. Set preload in the server config to load everything at boot
    /// instead, when memory is plentiful and you'd rather pay the cost up
    /// front than on a player's first step into a tile.
    ///
    /// NOT ALIGNED TO THE GRID ON PURPOSE. Terrains don't have to sit on
    /// tile boundaries or match TileSize - the index maps each tile to
    /// whichever terrains overlap it, and a query checks their real bounds.
    /// That keeps existing terrain (yours starts at -529.54, 1898.03)
    /// working without re-authoring it.
    ///
    /// OVERLAPS. Where two terrains overlap - normally just their shared
    /// edge - the first one indexed wins. Unity keeps neighbouring
    /// terrains' edge heights identical when they're connected, so which
    /// one answers on the seam doesn't matter.
    ///
    /// NOT THREAD SAFE. One instance belongs to the world server's tick
    /// thread, like SpatialGrid and InterestManager.
    /// </summary>
    public sealed class TiledHeightField : IHeightField
    {
        private sealed class Source
        {
            public string          Path;
            public HeightmapHeader Header;
            public HeightmapData   Data;        // null until first needed
            public long            LastUsed;    // Stopwatch timestamp
            public bool            LoadFailed;  // don't retry a broken file every query
        }

        private readonly List<Source> _sources = new List<Source>();
        private readonly Dictionary<TileCoord, List<Source>> _byTile = new Dictionary<TileCoord, List<Source>>();
        private readonly Func<string, HeightmapData> _loader;
        private readonly List<TileCoord> _tileScratch = new List<TileCoord>(16);

        /// <summary>
        /// Called with a message when something worth logging happens (a
        /// lazy load, a failed file). The movement library has no logger of
        /// its own; the world server points this at NLog.
        /// </summary>
        public Action<string> Log { get; set; }

        /// <param name="loader">
        /// Loads a file's samples. HeightmapData.Load by default; tests
        /// pass an in-memory loader.
        /// </param>
        public TiledHeightField(Func<string, HeightmapData> loader = null)
        {
            _loader = loader ?? HeightmapData.Load;
        }

        public int SourceCount => _sources.Count;
        public int IndexedTileCount => _byTile.Count;

        public int LoadedCount
        {
            get
            {
                int n = 0;
                foreach (var s in _sources) if (s.Data != null) n++;
                return n;
            }
        }

        public long LoadedBytes
        {
            get
            {
                long n = 0;
                foreach (var s in _sources) if (s.Data != null) n += s.Data.SizeInBytes;
                return n;
            }
        }

        /// <summary>What every indexed terrain would cost if all were loaded at once.</summary>
        public long TotalBytesIfFullyLoaded
        {
            get
            {
                long n = 0;
                foreach (var s in _sources) n += s.Header.SampleBytes;
                return n;
            }
        }

        /// <summary>
        /// Indexes one heightmap by its header. Cheap - the samples are not
        /// read. Returns false (and logs) for an unreadable file rather than
        /// throwing, so one bad export can't stop a shard from booting.
        /// </summary>
        public bool AddFile(string path)
        {
            HeightmapHeader header;

            try
            {
                header = HeightmapData.ReadHeader(path);
            }
            catch (Exception e)
            {
                Log?.Invoke($"[Terrain] Skipped '{path}': {e.Message}");
                return false;
            }

            AddSource(new Source { Path = path, Header = header });
            return true;
        }

        /// <summary>
        /// Adds an already-loaded heightmap (tests, or a terrain generated in
        /// code). It never unloads, since there is no file to reload it from.
        /// </summary>
        public void AddLoaded(HeightmapData data, string name = "(in memory)")
        {
            var header = new HeightmapHeader
            {
                OriginX = data.OriginX, OriginZ = data.OriginZ,
                SizeX = data.SizeX, SizeZ = data.SizeZ,
                ResolutionX = data.ResolutionX, ResolutionZ = data.ResolutionZ
            };

            AddSource(new Source { Path = null, Header = header, Data = data, LastUsed = Stopwatch.GetTimestamp() });
            Log?.Invoke($"[Terrain] Added {name}");
        }

        /// <summary>Indexes every *.achtmap in a folder (not recursive). Returns how many were added.</summary>
        public int AddDirectory(string directory)
        {
            if (string.IsNullOrEmpty(directory) || !Directory.Exists(directory))
                return 0;

            var files = Directory.GetFiles(directory, "*.achtmap");
            Array.Sort(files, StringComparer.Ordinal); // deterministic "first wins" on overlaps

            int added = 0;
            foreach (var file in files)
                if (AddFile(file)) added++;

            return added;
        }

        private void AddSource(Source source)
        {
            _sources.Add(source);

            _tileScratch.Clear();
            WorldGrid.GetTilesOverlapping(
                source.Header.OriginX, source.Header.OriginZ,
                source.Header.MaxX, source.Header.MaxZ,
                _tileScratch);

            foreach (var tile in _tileScratch)
            {
                if (!_byTile.TryGetValue(tile, out var list))
                    _byTile[tile] = list = new List<Source>(2);
                list.Add(source);
            }
        }

        /// <summary>True if any indexed terrain covers this point (loads nothing).</summary>
        public bool HasCoverage(float worldX, float worldZ) => FindSource(worldX, worldZ) != null;

        public bool TrySampleHeight(float worldX, float worldZ, out float height)
        {
            var data = Resolve(worldX, worldZ);
            if (data == null)
            {
                height = 0f;
                return false;
            }

            height = data.SampleHeight(worldX, worldZ);
            return true;
        }

        public bool TrySampleNormal(float worldX, float worldZ, out Vector3 normal)
        {
            var data = Resolve(worldX, worldZ);
            if (data == null)
            {
                normal = Vector3.UnitY;
                return false;
            }

            // HeightmapData.SampleNormal takes its central differences from
            // its own grid, which clamps at its edge. On a seam that flattens
            // the normal slightly - acceptable, since the neighbouring
            // terrain's edge row is identical when Unity has them connected.
            normal = data.SampleNormal(worldX, worldZ);
            return true;
        }

        /// <summary>
        /// Loads every terrain overlapping a circle, now. Call when you know
        /// a player is about to be somewhere (spawn, teleport) so the first
        /// movement packet there doesn't pay the disk read.
        /// </summary>
        public void Prefetch(float worldX, float worldZ, float radius)
        {
            _tileScratch.Clear();
            WorldGrid.GetTilesOverlapping(worldX - radius, worldZ - radius, worldX + radius, worldZ + radius, _tileScratch);

            long now = Stopwatch.GetTimestamp();

            foreach (var tile in _tileScratch)
            {
                if (!_byTile.TryGetValue(tile, out var list)) continue;
                foreach (var source in list) EnsureLoaded(source, now);
            }
        }

        /// <summary>Loads every indexed terrain. For small worlds or boxes with memory to spare.</summary>
        public void LoadAll()
        {
            long now = Stopwatch.GetTimestamp();
            foreach (var source in _sources) EnsureLoaded(source, now);
        }

        /// <summary>
        /// Drops the samples of every file-backed terrain nobody has queried
        /// for <paramref name="idle"/>. They reload transparently on the next
        /// query. Returns how many were unloaded.
        /// </summary>
        public int EvictIdle(TimeSpan idle)
        {
            long now = Stopwatch.GetTimestamp();
            long idleTicks = (long)(idle.TotalSeconds * Stopwatch.Frequency);
            int evicted = 0;

            foreach (var source in _sources)
            {
                if (source.Data == null || source.Path == null) continue;
                if (now - source.LastUsed < idleTicks) continue;

                source.Data = null;
                evicted++;
            }

            return evicted;
        }

        private Source FindSource(float worldX, float worldZ)
        {
            if (!_byTile.TryGetValue(WorldGrid.TileOf(worldX, worldZ), out var list))
                return null;

            for (int i = 0; i < list.Count; i++)
            {
                if (list[i].Header.Contains(worldX, worldZ))
                    return list[i];
            }

            return null;
        }

        private HeightmapData Resolve(float worldX, float worldZ)
        {
            var source = FindSource(worldX, worldZ);
            if (source == null) return null;

            long now = Stopwatch.GetTimestamp();
            return EnsureLoaded(source, now) ? source.Data : null;
        }

        private bool EnsureLoaded(Source source, long now)
        {
            source.LastUsed = now;

            if (source.Data != null) return true;
            if (source.LoadFailed || source.Path == null) return false;

            var sw = Stopwatch.StartNew();

            try
            {
                source.Data = _loader(source.Path);
            }
            catch (Exception e)
            {
                source.LoadFailed = true;
                Log?.Invoke($"[Terrain] Failed to load '{source.Path}': {e.Message} - this terrain will not be validated.");
                return false;
            }

            Log?.Invoke($"[Terrain] Loaded '{System.IO.Path.GetFileName(source.Path)}' " +
                        $"({source.Data.SizeInBytes / (1024 * 1024.0):F1} MB) in {sw.Elapsed.TotalMilliseconds:F1} ms");
            return true;
        }
    }
}
