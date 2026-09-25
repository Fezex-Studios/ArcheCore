using System;
using System.Diagnostics;
using System.IO;
using System.Numerics;
using ArcheCore.Movement;
using ArcheCore.Movement.Terrain;
using ArcheCore.Server.World.Utils.Config;
using NLog;

namespace ArcheCore.Server.World.Core.World
{
    /// <summary>
    /// The shard's knowledge of the ground: every exported terrain
    /// heightmap, stitched into one seamless field. The server half of world
    /// partitioning - the client streams tile scenes around its player, this
    /// streams tile heights around wherever players and NPCs actually are.
    ///
    /// ONE PER SHARD. A shard is one seamless world run by one world server
    /// process, so there is one of these, built at boot from
    /// WorldServerConfig.TerrainDirectory, owned by the tick thread.
    ///
    /// Consumers:
    ///   - MovementValidator, through Collision: rejects positions below the
    ///     ground (was configured but never loaded before this existed).
    ///   - NpcAiManager, through TryGetGroundHeight: NPCs walk ON the
    ///     terrain rather than across a flat plane at their spawn height.
    ///   - Teleports and respawns, through Prefetch: load the destination's
    ///     heights before the player's first movement packet arrives there.
    ///
    /// No heightmaps at all is a supported state - Collision is null and
    /// every query reports "no data", which every caller treats as "can't
    /// validate", never as "height zero".
    /// </summary>
    public sealed class WorldTerrainService
    {
        private static readonly Logger Logger = LogManager.GetCurrentClassLogger();

        private static readonly TimeSpan EvictCheckInterval = TimeSpan.FromSeconds(60);

        private readonly TiledHeightField _field;
        private readonly TimeSpan _idleUnload;
        private readonly bool _preloaded;
        private readonly Stopwatch _sinceEvictCheck = Stopwatch.StartNew();

        /// <summary>For MovementValidator. Null when the shard has no terrain data.</summary>
        public ICollisionWorld Collision { get; }

        public bool HasTerrain => _field.SourceCount > 0;

        public WorldTerrainService(WorldServerConfig config)
        {
            _field = new TiledHeightField { Log = message => Logger.Info(message) };
            _idleUnload = TimeSpan.FromMinutes(Math.Max(1, config.TerrainIdleUnloadMinutes));

            string directory = Resolve(config.TerrainDirectory);
            if (!string.IsNullOrWhiteSpace(directory))
            {
                if (Directory.Exists(directory))
                    _field.AddDirectory(directory);
                else
                    Logger.Warn("[Terrain] TerrainDirectory '{Dir}' does not exist - no terrain from it.", directory);
            }

            // Legacy single-file setting. Skipped if it's inside the folder
            // we just indexed, so the same terrain isn't added twice.
            string legacy = Resolve(config.HeightmapTerrainPath);
            if (!string.IsNullOrWhiteSpace(legacy))
            {
                bool alreadyIndexed =
                    !string.IsNullOrWhiteSpace(directory) &&
                    string.Equals(
                        Path.GetFullPath(Path.GetDirectoryName(legacy) ?? ""),
                        Path.GetFullPath(directory),
                        StringComparison.OrdinalIgnoreCase);

                if (!alreadyIndexed)
                {
                    if (File.Exists(legacy)) _field.AddFile(legacy);
                    else Logger.Warn("[Terrain] HeightmapTerrainPath '{Path}' not found.", legacy);
                }
            }

            if (!HasTerrain)
            {
                Logger.Warn("[Terrain] No heightmaps loaded for shard '{Shard}'. Movement is speed-checked only - " +
                            "nothing server-side knows where the ground is. Export terrain with " +
                            "Dev Tools > World Tiles > Export All Terrain into '{Dir}'.",
                            config.ShardName, directory);
                Collision = null;
                return;
            }

            Collision = new HeightmapCollisionWorld(_field);

            if (config.PreloadAllTerrain)
            {
                var sw = Stopwatch.StartNew();
                _field.LoadAll();
                _preloaded = true;
                Logger.Info("[Terrain] Preloaded {Count} heightmap(s), {MB:F1} MB, in {Ms:F0} ms.",
                    _field.LoadedCount, _field.LoadedBytes / (1024 * 1024.0), sw.Elapsed.TotalMilliseconds);
            }

            Logger.Info(
                "[Terrain] Shard '{Shard}': {Count} heightmap(s) from '{Dir}' covering {Tiles} world tile(s); " +
                "{Total:F1} MB if fully loaded, {Mode}.",
                config.ShardName, _field.SourceCount, directory, _field.IndexedTileCount,
                _field.TotalBytesIfFullyLoaded / (1024 * 1024.0),
                config.PreloadAllTerrain ? "preloaded" : $"loaded on demand, unloaded after {_idleUnload.TotalMinutes:F0} min idle");
        }

        /// <summary>Ground height here, or false where there's no terrain data.</summary>
        public bool TryGetGroundHeight(float x, float z, out float height) =>
            _field.TrySampleHeight(x, z, out height);

        /// <summary>
        /// Load the heights around a destination now. Call before a
        /// server-driven move (spawn, respawn, teleport) so the first
        /// movement packet there doesn't pay the disk read on the tick.
        /// </summary>
        public void Prefetch(Vector3 position, float radius = 256f)
        {
            if (HasTerrain)
                _field.Prefetch(position.X, position.Z, radius);
        }

        /// <summary>Tick thread. Unloads idle heightmaps about once a minute.</summary>
        public void Tick()
        {
            if (!HasTerrain || _preloaded || _sinceEvictCheck.Elapsed < EvictCheckInterval)
                return;

            _sinceEvictCheck.Restart();

            int evicted = _field.EvictIdle(_idleUnload);
            if (evicted > 0)
                Logger.Info("[Terrain] Unloaded {Count} idle heightmap(s); {Loaded} resident ({MB:F1} MB).",
                    evicted, _field.LoadedCount, _field.LoadedBytes / (1024 * 1024.0));
        }

        private static string Resolve(string path)
        {
            if (string.IsNullOrWhiteSpace(path)) return string.Empty;
            return Path.IsPathRooted(path) ? path : Path.Combine(AppContext.BaseDirectory, path);
        }
    }
}
