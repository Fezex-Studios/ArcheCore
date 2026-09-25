using ArcheCore.Server.Auth.Config;
using ArcheCore.Server.Auth.Contracts;
using ArcheCore.Server.Auth.Data;
using ArcheCore.Server.Auth.Services;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;

namespace ArcheCore.Server.Auth.Endpoints;

/// <summary>
/// Port of LoginRoute.ts, same one-session-per-account rule.
///
/// Lockout is two-level (audit gap 3):
///   - per (username, address): MaxLoginAttempts failures lock that name
///     for that address only (LoginThrottle, in memory). Typing someone
///     else's name five times no longer locks THEM out.
///   - per account: AccountLockAttempts failures from anywhere lock the
///     account everywhere (failed_logins table) - the backstop against a
///     distributed guess.
/// </summary>
public static class LoginEndpoint
{
    public static void MapLogin(this IEndpointRouteBuilder app)
    {
        app.MapPost("/login", async (
            LoginRequest request,
            AuthDbContext db,
            PasswordService passwords,
            IOptions<AuthServerConfig> configOptions,
            ILoggerFactory loggerFactory,
            LoginThrottle throttle,
            HttpContext http) =>
        {
            var config = configOptions.Value;
            var log    = loggerFactory.CreateLogger("Login");

            var username = request?.Username;
            var password = request?.Password;

            if (string.IsNullOrEmpty(username) || string.IsNullOrEmpty(password))
                return Fail("Invalid username or password");

            var now = DateTime.UtcNow;
            var ip  = ClientAddress.Of(http, config.TrustForwardedFor);
            var lockout = TimeSpan.FromMinutes(config.LockoutMinutes);

            // ── Per-address lockout ─────────────────────────────────────
            if (throttle.IsLocked(username, ip, now, out var ipRemaining))
                return Fail($"Too many failed attempts. Try again in {ipRemaining} minute(s).");

            // ── Account-wide lockout ────────────────────────────────────
            var failRow = await db.FailedLogins
                .FirstOrDefaultAsync(f => f.Username == username);

            if (failRow?.LockedUntil is not null)
            {
                if (failRow.LockedUntil > now)
                {
                    var remaining = (int)Math.Ceiling(
                        (failRow.LockedUntil.Value - now).TotalMinutes);

                    return Fail($"Account locked. Try again in {remaining} minute(s).");
                }

                // Lockout expired — clear it and start fresh.
                db.FailedLogins.Remove(failRow);
                await db.SaveChangesAsync();
                failRow = null;
            }

            // ── Credential check ────────────────────────────────────────
            var account = await db.Accounts
                .AsNoTracking()
                .FirstOrDefaultAsync(a => a.Username == username);

            bool valid;

            if (account is null)
            {
                // Hash anyway. Without this, a login for a username that
                // doesn't exist returns in microseconds while a real one
                // takes the ~250ms bcrypt costs at work factor 12, and that
                // difference is measurable over the network — it tells an
                // attacker which usernames are real before they guess a
                // single password.
                passwords.BurnTime(password);
                valid = false;
            }
            else
            {
                valid = passwords.Verify(password, account.PasswordHash);
            }

            if (account is null || !valid)
            {
                // Only count failures against accounts that exist. Counting
                // them for non-existent usernames would let anyone lock out
                // a name they merely suspect is taken, and would fill the
                // table with junk rows from a dictionary run.
                if (account is not null)
                {
                    bool ipLocked = throttle.RecordFailure(username, ip, now, config.MaxLoginAttempts, lockout);

                    var attempts = (failRow?.Attempts ?? 0) + 1;

                    if (attempts >= config.AccountLockAttempts)
                    {
                        var lockedUntil = now.AddMinutes(config.LockoutMinutes);

                        await UpsertFailure(db, failRow, username, attempts, lockedUntil);

                        log.LogWarning(
                            "Account '{Username}' locked everywhere after {Attempts} failed attempts",
                            username, attempts);

                        return Fail(
                            $"Too many failed attempts. Account locked for {config.LockoutMinutes} minutes.");
                    }

                    await UpsertFailure(db, failRow, username, attempts, lockedUntil: null);

                    if (ipLocked)
                    {
                        log.LogWarning("Login for '{Username}' locked for {Ip} after {Max} failed attempts",
                            username, ip, config.MaxLoginAttempts);

                        return Fail($"Too many failed attempts. Try again in {config.LockoutMinutes} minutes.");
                    }
                }

                // Identical message whether or not the account exists.
                return Fail("Invalid username or password");
            }

            // ── Success ─────────────────────────────────────────────────
            throttle.Clear(username, ip);

            if (failRow is not null)
            {
                db.FailedLogins.Remove(failRow);
                await db.SaveChangesAsync();
            }

            var token     = TokenService.CreateToken();
            var expiresAt = now.AddHours(config.SessionLifetimeHours);

            // MySQL's REPLACE INTO is the equivalent of the SQLite
            // INSERT OR REPLACE the original used, and it's here for the
            // same reason: it kills any existing session for this account
            // and creates the new one in a single atomic statement.
            //
            // It matters that BOTH the primary key (token) and the unique
            // index (account_id) trigger the replace, because the conflict
            // we actually care about is on account_id — the same player
            // logging in again. Doing this as an EF delete-then-add is two
            // statements with a window in between where the account has no
            // session, and it trips the unique index if the delete hasn't
            // flushed yet.
            await db.Database.ExecuteSqlInterpolatedAsync($"""
                REPLACE INTO sessions (token, account_id, expires_at)
                VALUES ({token}, {account.AccountId}, {expiresAt})
                """);

            log.LogInformation("Login OK for '{Username}' (id {AccountId})",
                username, account.AccountId);

            return Results.Ok(new LoginResponse { Success = true, Token = token });
        })
        .RequireRateLimiting("auth-strict");
    }

    private static async Task UpsertFailure(
        AuthDbContext db,
        FailedLogin? existing,
        string username,
        int attempts,
        DateTime? lockedUntil)
    {
        if (existing is null)
        {
            db.FailedLogins.Add(new FailedLogin
            {
                Username    = username,
                Attempts    = attempts,
                LockedUntil = lockedUntil
            });
        }
        else
        {
            existing.Attempts    = attempts;
            existing.LockedUntil = lockedUntil;
        }

        await db.SaveChangesAsync();
    }

    private static IResult Fail(string message) =>
        Results.Ok(new LoginResponse { Success = false, Message = message });
}
