using ArcheCore.Server.Auth.Config;
using ArcheCore.Server.Auth.Contracts;
using ArcheCore.Server.Auth.Data;
using ArcheCore.Server.Auth.Services;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;

namespace ArcheCore.Server.Auth.Endpoints;

/// <summary>
/// Launcher sessions without the password (launcher L2/L3).
///
///   POST /session/launch-token  { RefreshToken }
///       -> { Success, Token, RefreshToken, Username }
///       Trades a refresh token for a one-shot LAUNCH token (valid for
///       Auth:LaunchTokenSeconds) plus a replacement refresh token. The
///       launcher calls this on every Play and when it starts up with a
///       remembered login. It never sends the password again.
///
///   POST /logout  { RefreshToken }
///       Revokes it. Logging out of the launcher makes the remembered login
///       useless even if someone copied it.
/// </summary>
public static class SessionEndpoints
{
    public static void MapSessionEndpoints(this IEndpointRouteBuilder app)
    {
        app.MapPost("/session/launch-token", async (
            RefreshRequest request,
            AuthDbContext db,
            IOptions<AuthServerConfig> configOptions) =>
        {
            var config = configOptions.Value;
            var now = DateTime.UtcNow;

            var accountId = await RefreshTokens.RedeemAsync(db, request?.RefreshToken, now);
            if (accountId is null)
                return Results.Ok(new LoginResponse { Success = false, Message = "Your saved login has expired. Please log in again." });

            var account = await db.Accounts.AsNoTracking().FirstOrDefaultAsync(a => a.AccountId == accountId);
            if (account is null)
                return Results.Ok(new LoginResponse { Success = false, Message = "That account no longer exists." });

            // An account-wide lock (too many failed passwords) also stops
            // remembered logins - otherwise the lock would only stop the
            // person who DOESN'T have the account.
            var locked = await db.FailedLogins.AsNoTracking()
                .AnyAsync(f => f.Username == account.Username && f.LockedUntil != null && f.LockedUntil > now);
            if (locked)
                return Results.Ok(new LoginResponse { Success = false, Message = "Account locked. Try again later." });

            var token = TokenService.CreateToken();

            await db.Database.ExecuteSqlInterpolatedAsync($"""
                REPLACE INTO sessions (token, account_id, expires_at)
                VALUES ({token}, {account.AccountId}, {now.AddSeconds(config.LaunchTokenSeconds)})
                """);

            var replacement = await RefreshTokens.IssueAsync(db, account.AccountId, config, now);

            return Results.Ok(new LoginResponse
            {
                Success      = true,
                Token        = token,
                RefreshToken = replacement,
                Username     = account.Username
            });
        });

        app.MapPost("/logout", async (RefreshRequest request, AuthDbContext db) =>
        {
            await RefreshTokens.RevokeAsync(db, request?.RefreshToken);
            return Results.Ok(new LogoutResponse { Success = true });
        });
    }
}
