using System.Numerics;
using Microsoft.EntityFrameworkCore;
using NLog;
using ArcheCore.Server.World.Utils.Database.SQLite;
using ArcheCore.Server.World.GameData.World.PlayerSpawn;   // SpawnPointTable, now named directly

namespace ArcheCore.Server.World.Core.Services;

/// <summary>
/// Registered in ServiceContainer alongside PlayerManager/PersistenceClient/etc
/// (see WorldServer.RegisterPackets) so any C2W handler can ask for it,
/// same pattern as every other manager. Load() is called once at boot,
/// same spot QuestManager.LoadFromDatabase() runs - see the WorldServer.cs
/// diff in this folder for exactly where.
///
/// This is intentionally simple - one cached "the default spawn point"
/// value, refreshed only at boot. If you later want spawn points that can
/// change without a restart (an admin tool moving the default town spawn
/// after a world event, say), swap the caching for a live query - nothing
/// else in this class's public API would need to change.
/// </summary>
public class SpawnPointService
{
    private static readonly Logger Logger = LogManager.GetCurrentClassLogger();

    private readonly IDbContextFactory<WorldDataDbContext> _dbFactory;

    // Hardcoded fallback if the SpawnPoints table is empty or missing a
    // default row - keeps the server bootable even before any spawn point
    // content has been added, same "never let missing content crash boot"
    // philosophy as the rest of the GameData layer.
    private static readonly Vector3 HardcodedFallback = new(0, 2, 0);

    private Vector3 _defaultSpawn = HardcodedFallback;

    // Every row, kept for respawn choice and safe zones. Read-only after
    // LoadAsync, so any thread can use it.
    private SpawnPointTable[] _all = System.Array.Empty<SpawnPointTable>();

    public SpawnPointService(IDbContextFactory<WorldDataDbContext> dbFactory)
    {
        _dbFactory = dbFactory;
    }

    public async Task LoadAsync()
    {
        await using var db = await _dbFactory.CreateDbContextAsync();

        _all = await db.SpawnPointTables.ToArrayAsync();

        int respawnPoints = _all.Count(s => s.IsRespawnPoint);
        int safeZones = _all.Count(s => s.SafeRadius > 0f);
        Logger.Info($"[SpawnPointService] {_all.Length} spawn point(s): " +
                    $"{respawnPoints} respawn point(s), {safeZones} safe zone(s).");

        var defaults = _all.Where(s => s.IsDefault).ToList();

        if (defaults.Count == 1)
        {
            _defaultSpawn = new Vector3(defaults[0].X, defaults[0].Y, defaults[0].Z);
            Logger.Info($"[SpawnPointService] Default spawn: '{defaults[0].Name}' " +
                        $"({defaults[0].X}, {defaults[0].Y}, {defaults[0].Z})");
            return;
        }

        if (defaults.Count > 1)
            Logger.Warn($"[SpawnPointService] {defaults.Count} SpawnPoints have IsDefault=true - " +
                        "should be exactly one. Using the first.");

        var fallback = defaults.FirstOrDefault() ?? _all.FirstOrDefault();

        if (fallback != null)
        {
            _defaultSpawn = new Vector3(fallback.X, fallback.Y, fallback.Z);
            Logger.Warn($"[SpawnPointService] No SpawnPoint marked IsDefault - " +
                        $"falling back to '{fallback.Name}'.");
        }
        else
        {
            Logger.Warn("[SpawnPointService] No SpawnPoints in the database at all - " +
                        $"using hardcoded fallback ({HardcodedFallback.X}, {HardcodedFallback.Y}, {HardcodedFallback.Z}). " +
                        "Add at least one row with IsDefault=true.");
        }
    }

    public Vector3 GetDefaultSpawn() => _defaultSpawn;

    /// <summary>
    /// Where a player who died HERE comes back: the nearest row marked
    /// IsRespawnPoint. With none marked, everyone returns to the default
    /// spawn - the behaviour before respawn points existed.
    /// </summary>
    public Vector3 GetRespawnNear(Vector3 diedAt)
    {
        SpawnPointTable best = null;
        float bestDistance = float.MaxValue;

        foreach (var point in _all)
        {
            if (!point.IsRespawnPoint) continue;

            float distance = Vector3.Distance(diedAt, new Vector3(point.X, point.Y, point.Z));
            if (distance < bestDistance)
            {
                bestDistance = distance;
                best = point;
            }
        }

        return best != null ? new Vector3(best.X, best.Y, best.Z) : _defaultSpawn;
    }

    /// <summary>
    /// Is this position inside a no-PvP zone? Checked for BOTH players in a
    /// fight, so standing outside a town and shooting in doesn't work.
    /// </summary>
    public bool IsInSafeZone(Vector3 position, out string zoneName)
    {
        foreach (var point in _all)
        {
            if (point.SafeRadius <= 0f) continue;

            if (Vector3.Distance(position, new Vector3(point.X, point.Y, point.Z)) <= point.SafeRadius)
            {
                zoneName = point.Name;
                return true;
            }
        }

        zoneName = null;
        return false;
    }
}