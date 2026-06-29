using Microsoft.Extensions.Options;
using Microsoft.Data.Sqlite;
using NLog;
using ArcheCore.Server.World.Utils.Config;

namespace ArcheCore.Server.World.Utils.Database.SQLite;

public class GameDataPatchRunner
{
    private static readonly Logger Logger = LogManager.GetCurrentClassLogger();
    private readonly string _connectionString;
    private readonly string _patchDir;

    public GameDataPatchRunner(IOptions<DatabaseConfig> dbConfig)
    {
        var worldDb = dbConfig.Value.WorldDb;
        _connectionString = $"Data Source={worldDb}";

        // SQL/patches/ sits next to the .exe in the output dir
        _patchDir = Path.Combine(AppContext.BaseDirectory, "SQL", "patches");
    }

    public async Task RunAsync()
    {
        if (!Directory.Exists(_patchDir))
        {
            Logger.Warn($"[Patcher] Patch directory not found, skipping: {_patchDir}");
            return;
        }

        await using var conn = new SqliteConnection(_connectionString);
        await conn.OpenAsync();

        // Bootstrap the version tracking table if it doesn't exist yet
        await using (var bootstrap = new SqliteCommand("""
            CREATE TABLE IF NOT EXISTS schema_versions (
                patch TEXT PRIMARY KEY NOT NULL,
                applied_at TEXT NOT NULL DEFAULT (datetime('now'))
            );
            """, conn))
        {
            await bootstrap.ExecuteNonQueryAsync();
        }

        var patches = Directory.GetFiles(_patchDir, "*.sql")
            .OrderBy(f => Path.GetFileName(f)) // lexicographic — 001_, 002_ etc.
            .ToList();

        if (patches.Count == 0)
        {
            Logger.Info("[Patcher] No patches found.");
            return;
        }

        foreach (var patchPath in patches)
        {
            var patchName = Path.GetFileName(patchPath);

            // Check if already applied
            await using var checkCmd = new SqliteCommand(
                "SELECT COUNT(*) FROM schema_versions WHERE patch = @patch", conn);
            checkCmd.Parameters.AddWithValue("@patch", patchName);
            var count = Convert.ToInt32(await checkCmd.ExecuteScalarAsync());

            if (count > 0)
            {
                Logger.Info($"[Patcher] Skipping {patchName} (already applied)");
                continue;
            }

            Logger.Info($"[Patcher] Applying {patchName}...");

            var sql = await File.ReadAllTextAsync(patchPath);

            // Wrap each patch in a transaction so a bad patch doesn't half-apply
            await using var tx = await conn.BeginTransactionAsync();
            try
            {
                await using var cmd = new SqliteCommand(sql, conn, (SqliteTransaction)tx);
                await cmd.ExecuteNonQueryAsync();

                await using var recordCmd = new SqliteCommand(
                    "INSERT INTO schema_versions (patch) VALUES (@patch)", conn, (SqliteTransaction)tx);
                recordCmd.Parameters.AddWithValue("@patch", patchName);
                await recordCmd.ExecuteNonQueryAsync();

                await tx.CommitAsync();
                Logger.Info($"[Patcher] Applied {patchName}");
            }
            catch (Exception ex)
            {
                await tx.RollbackAsync();
                Logger.Error(ex, $"[Patcher] Failed applying {patchName} — rolled back. Fix the script and restart.");
                throw; // Stops server boot — you want to know about this
            }
        }
    }
}