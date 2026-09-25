using System;
using System.IO;
using System.Numerics;
using ArcheCore.Movement.World;
using ArcheCore.Server.World.Utils.Config;
using NLog;

namespace ArcheCore.Server.World.Core.World
{
    /// <summary>
    /// The shard's zones: which named region every point of the world is in,
    /// and what rules apply there. Loaded once at boot from the zone map file
    /// that Dev Tools > Zones exports (WorldServerConfig.ZoneMapPath).
    ///
    /// Used by:
    ///   - PlayerManager, which tracks each player's current zone and fires
    ///     PlayerEvent.OnEnterZone when it changes (quests, Lua, later music
    ///     and weather triggers).
    ///   - CombatManager, which refuses PvP inside Safe and Peaceful zones.
    ///   - EnterWorld, which sends Hash so a client with a different copy of
    ///     the map warns instead of silently showing wrong zone names.
    ///
    /// No file is a supported state: every lookup returns "no zone" (id 0),
    /// which changes no existing behaviour.
    /// </summary>
    public sealed class ZoneService
    {
        private static readonly Logger Logger = LogManager.GetCurrentClassLogger();

        private readonly ZoneMap _map;

        /// <summary>Hash of the loaded file, "" when there is none. Sent to clients.</summary>
        public string Hash { get; } = "";

        public bool HasZones => _map.ZoneCount > 0;

        public ZoneService(WorldServerConfig config)
        {
            string path = Resolve(config.ZoneMapPath);

            if (string.IsNullOrWhiteSpace(path) || !File.Exists(path))
            {
                _map = new ZoneMap();
                Logger.Warn("[Zones] No zone map at '{Path}' - the whole shard is 'no zone'. Paint zones in " +
                            "Dev Tools > Zones and save to this path.", path);
                return;
            }

            try
            {
                byte[] bytes = File.ReadAllBytes(path);
                _map = ZoneMap.Load(bytes);
                Hash = ZoneMap.HashBytes(bytes);
            }
            catch (Exception e)
            {
                _map = new ZoneMap();
                Logger.Error("[Zones] Could not read zone map '{Path}': {Error} - running with no zones.", path, e.Message);
                return;
            }

            Logger.Info("[Zones] Shard '{Shard}': {Zones} zone(s) over {Tiles} painted tile(s) from '{Path}' (hash {Hash}).",
                config.ShardName, _map.ZoneCount, System.Linq.Enumerable.Count(_map.PaintedTiles), path, Hash);
        }

        /// <summary>Zone id at a position; 0 where nothing is painted.</summary>
        public ushort ZoneIdAt(Vector3 position) => _map.ZoneIdAt(position.X, position.Z);

        /// <summary>Zone at a position, or null where nothing is painted.</summary>
        public ZoneDefinition ZoneAt(Vector3 position) => _map.ZoneAt(position.X, position.Z);

        public ZoneDefinition GetZone(ushort id) => _map.GetZone(id);

        /// <summary>
        /// False if this position is in a zone that forbids open PvP (Safe or
        /// Peaceful). Unzoned ground allows it - there, the server-wide
        /// AllowPlayerVersusPlayer switch and spawn-point safe radii decide,
        /// exactly as before zones existed.
        /// </summary>
        public bool AllowsPvpAt(Vector3 position, out ZoneDefinition zone)
        {
            zone = ZoneAt(position);
            return zone == null || zone.AllowsPvp;
        }

        private static string Resolve(string path)
        {
            if (string.IsNullOrWhiteSpace(path)) return string.Empty;
            return Path.IsPathRooted(path) ? path : Path.Combine(AppContext.BaseDirectory, path);
        }
    }
}
