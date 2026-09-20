using ArcheCore.Server.Auth.Data;
using Microsoft.EntityFrameworkCore;

namespace ArcheCore.Server.Auth.Services;

/// <summary>
/// Port of the purgeExpiredSessions() + setInterval() pair at the bottom of
/// Database.ts: clear out expired rows once at startup and hourly after.
///
/// This is housekeeping, not security. /validate-session already refuses an
/// expired token and deletes it on the way out, so an expired row sitting
/// in the table can't be used. This just stops the table growing forever
/// with rows nobody will ever look up again.
///
/// Runs as a BackgroundService rather than a timer callback so it gets a
/// proper DI scope for the DbContext and participates in graceful shutdown.
/// </summary>
public sealed class SessionPurgeService : BackgroundService
{
    private static readonly TimeSpan Interval = TimeSpan.FromHours(1);

    private readonly IServiceScopeFactory _scopeFactory;
    private readonly ILogger<SessionPurgeService> _log;

    public SessionPurgeService(
        IServiceScopeFactory scopeFactory,
        ILogger<SessionPurgeService> log)
    {
        _scopeFactory = scopeFactory;
        _log = log;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        // First pass immediately, matching the old startup call.
        await PurgeAsync(stoppingToken);

        using var timer = new PeriodicTimer(Interval);

        try
        {
            while (await timer.WaitForNextTickAsync(stoppingToken))
                await PurgeAsync(stoppingToken);
        }
        catch (OperationCanceledException)
        {
            // Normal shutdown.
        }
    }

    private async Task PurgeAsync(CancellationToken ct)
    {
        try
        {
            using var scope = _scopeFactory.CreateScope();
            var db = scope.ServiceProvider.GetRequiredService<AuthDbContext>();

            var now = DateTime.UtcNow;

            // Single DELETE statement, no change tracking, no loading rows
            // into memory first. Uses ix_sessions_expires_at.
            var removed = await db.Sessions
                .Where(s => s.ExpiresAt < now)
                .ExecuteDeleteAsync(ct);

            if (removed > 0)
                _log.LogInformation("[DB] Purged {Count} expired session(s)", removed);
        }
        catch (Exception ex)
        {
            // A failed purge must never take the server down — the sessions
            // it would have removed are already unusable.
            _log.LogError(ex, "[DB] Session purge failed");
        }
    }
}
