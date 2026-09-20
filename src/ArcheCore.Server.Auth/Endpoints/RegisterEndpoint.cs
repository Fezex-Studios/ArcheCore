using System.Text.RegularExpressions;
using ArcheCore.Server.Auth.Contracts;
using ArcheCore.Server.Auth.Data;
using ArcheCore.Server.Auth.Services;
using Microsoft.EntityFrameworkCore;

namespace ArcheCore.Server.Auth.Endpoints;

/// <summary>Port of RegisterRoute.ts. Same rules, same messages.</summary>
public static class RegisterEndpoint
{
    private const int UsernameMin = 3;
    private const int UsernameMax = 20;
    private const int PasswordMin = 8;

    /// <summary>
    /// bcrypt silently truncates past 72 BYTES. The old server checked
    /// .length, which is UTF-16 code units in JavaScript — so a password of
    /// 70 emoji passed the check and then got cut at 72 bytes without
    /// anyone knowing. Measuring bytes is the check that matches what
    /// bcrypt actually does.
    /// </summary>
    private const int PasswordMaxBytes = 72;

    private static readonly Regex UsernamePattern =
        new("^[a-zA-Z0-9_]+$", RegexOptions.Compiled);

    public static void MapRegister(this IEndpointRouteBuilder app)
    {
        app.MapPost("/register", async (
            RegisterRequest request,
            AuthDbContext db,
            PasswordService passwords,
            ILoggerFactory loggerFactory) =>
        {
            var log = loggerFactory.CreateLogger("Register");

            var username = request?.Username;
            var password = request?.Password;

            // ── Input validation ────────────────────────────────────────
            if (string.IsNullOrEmpty(username) || string.IsNullOrEmpty(password))
                return Fail("Username and password are required");

            if (username.Length < UsernameMin || username.Length > UsernameMax)
                return Fail($"Username must be between {UsernameMin} and {UsernameMax} characters");

            if (!UsernamePattern.IsMatch(username))
                return Fail("Username may only contain letters, numbers, and underscores");

            var passwordBytes = System.Text.Encoding.UTF8.GetByteCount(password);

            if (password.Length < PasswordMin || passwordBytes > PasswordMaxBytes)
                return Fail($"Password must be between {PasswordMin} and {PasswordMaxBytes} characters");

            // ── Existence check ─────────────────────────────────────────
            var exists = await db.Accounts
                .AsNoTracking()
                .AnyAsync(a => a.Username == username);

            if (exists)
            {
                // Deliberately the same generic message as the failure
                // below. Anything more specific confirms to a script which
                // usernames are taken, which is a free account-enumeration
                // oracle.
                return Fail("Registration failed");
            }

            try
            {
                var account = new Account
                {
                    Username     = username,
                    PasswordHash = passwords.Hash(password)
                };

                db.Accounts.Add(account);
                await db.SaveChangesAsync();

                log.LogInformation("Registered account '{Username}' (id {AccountId})",
                    username, account.AccountId);

                return Results.Ok(new RegisterResponse { Success = true });
            }
            catch (DbUpdateException ex)
            {
                // The unique index on username is the real guard against a
                // race between the check above and this insert. Landing
                // here means someone registered the same name in the gap.
                log.LogWarning(ex, "Registration insert failed for '{Username}'", username);
                return Fail("Registration failed");
            }
        })
        .RequireRateLimiting("auth-strict");   // registration is as abusable as login
    }

    private static IResult Fail(string message) =>
        Results.Ok(new RegisterResponse { Success = false, Message = message });
}
