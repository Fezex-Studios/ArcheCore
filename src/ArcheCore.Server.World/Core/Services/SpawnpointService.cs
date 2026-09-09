using System.Numerics;
using Microsoft.EntityFrameworkCore;
using NLog;
using ArcheCore.Server.World.Utils.Database.SQLite;

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

    public SpawnPointService(IDbContextFactory<WorldDataDbContext> dbFactory)
    {
        _dbFactory = dbFactory;
    }

    public async Task LoadAsync()
    {
        await using var db = await _dbFactory.CreateDbContextAsync();

        var defaults = await db.SpawnPointTables.Where(s => s.IsDefault).ToListAsync();

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

        var fallback = defaults.FirstOrDefault() ?? await db.SpawnPointTables.FirstOrDefaultAsync();

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
}