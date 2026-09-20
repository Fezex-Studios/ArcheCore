using System.Security.Cryptography;
using System.Text;
using ArcheCore.Server.Auth.Config;
using ArcheCore.Server.Auth.Contracts;
using ArcheCore.Server.Auth.Data;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;

namespace ArcheCore.Server.Auth.Endpoints;

/// <summary>
/// Port of ValidateSessionRoute.ts.
///
/// Called by the WorldServer only, never by a client. Tokens are one-shot:
/// a valid token is deleted the moment it's redeemed, so a token captured
/// in transit is only useful if the attacker beats the real player to it,
/// and replaying it later gets nothing.
/// </summary>
public static class ValidateSessionEndpoint
{
    public static void MapValidateSession(this IEndpointRouteBuilder app)
    {
        app.MapPost("/validate-session", async (
            HttpContext context,
            ValidateSessionRequest request,
            AuthDbContext db,
            IOptions<AuthServerConfig> configOptions,
            ILoggerFactory loggerFactory) =>
        {
            var log = loggerFactory.CreateLogger("ValidateSession");
            var secretBytes = Encoding.UTF8.GetBytes(configOptions.Value.InternalSecret);

            // ── Internal secret check ───────────────────────────────────
            var presented = context.Request.Headers["x-internal-secret"].ToString();

            // Fixed-time compare. The old version used `!==` on strings,
            // which bails on the first differing character — turning the
            // check into an oracle that leaks the secret one byte at a time
            // to anyone patient enough to measure.
            if (presented.Length == 0
                || !CryptographicOperations.FixedTimeEquals(
                       Encoding.UTF8.GetBytes(presented), secretBytes))
            {
                log.LogWarning(
                    "Rejected /validate-session from {Ip} — bad or missing x-internal-secret",
                    context.Connection.RemoteIpAddress);

                return Results.StatusCode(StatusCodes.Status403Forbidden);
            }

            var token = request?.Token;

            if (string.IsNullOrEmpty(token))
                return Results.Ok(new ValidateSessionResponse { Valid = false });

            var session = await db.Sessions.FirstOrDefaultAsync(s => s.Token == token);

            // Always delete on a hit: valid tokens are burned after one
            // use, expired ones get cleaned up as a side effect of being
            // looked at. Note the token itself is never logged — it's a
            // bearer credential, and a log line containing one is a session
            // handover to anyone who reads the log.
            if (session is not null)
            {
                db.Sessions.Remove(session);
                await db.SaveChangesAsync();
            }

            if (session is null || session.ExpiresAt < DateTime.UtcNow)
                return Results.Ok(new ValidateSessionResponse { Valid = false });

            return Results.Ok(new ValidateSessionResponse
            {
                Valid     = true,
                AccountId = session.AccountId
            });
        });

        // No .RequireRateLimiting — the WorldServer is the only caller and
        // it has its own per-peer AuthThrottle in front of this. The global
        // limiter also skips this path; see Program.cs.
    }
}
