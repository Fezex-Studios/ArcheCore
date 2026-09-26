using System.Security.Cryptography;
using System.Text;
using ArcheCore.Server.Auth.Config;
using ArcheCore.Server.Auth.Data;
using Microsoft.EntityFrameworkCore;

namespace ArcheCore.Server.Auth.Services;

/// <summary>
/// Refresh tokens (launcher L2): issue, redeem-and-rotate, revoke.
///
/// The raw token only ever exists in the response and on the player's
/// machine (OS keychain, or launcher memory). The table holds its SHA-256.
/// Every redemption deletes the token and issues a new one, so a copied
/// token stops working the next time the real launcher uses its copy -
/// and vice versa.
/// </summary>
public static class RefreshTokens
{
    public static string Hash(string raw) =>
        Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(raw))).ToLowerInvariant();

    public static async Task<string> IssueAsync(AuthDbContext db, int accountId, AuthServerConfig config, DateTime now)
    {
        var raw = TokenService.CreateToken();

        db.RefreshTokens.Add(new RefreshToken
        {
            TokenHash = Hash(raw),
            AccountId = accountId,
            CreatedAt = now,
            ExpiresAt = now.AddDays(config.RefreshTokenDays)
        });

        await db.SaveChangesAsync();
        return raw;
    }

    /// <summary>
    /// Burn this refresh token. Returns the account it belonged to, or null
    /// if it's unknown or expired. The caller issues the replacement.
    /// </summary>
    public static async Task<int?> RedeemAsync(AuthDbContext db, string? raw, DateTime now)
    {
        if (string.IsNullOrWhiteSpace(raw) || raw.Length > 128)
            return null;

        var hash = Hash(raw);

        // Delete-and-return in one statement pair: whoever deletes the row
        // wins; a racing second redemption of the same token gets nothing.
        var row = await db.RefreshTokens.AsNoTracking().FirstOrDefaultAsync(r => r.TokenHash == hash);
        if (row is null)
            return null;

        int deleted = await db.RefreshTokens.Where(r => r.TokenHash == hash).ExecuteDeleteAsync();
        if (deleted == 0 || row.ExpiresAt < now)
            return null;

        return row.AccountId;
    }

    public static Task<int> RevokeAsync(AuthDbContext db, string? raw) =>
        string.IsNullOrWhiteSpace(raw)
            ? Task.FromResult(0)
            : db.RefreshTokens.Where(r => r.TokenHash == Hash(raw)).ExecuteDeleteAsync();
}
