using System.Security.Cryptography;

namespace ArcheCore.Server.Auth.Services;

/// <summary>
/// Port of SessionService.ts.
///
/// The old version returned a v4 UUID, which carries 122 bits of
/// randomness inside a fixed, recognisable shape. This returns 256 bits of
/// CSPRNG output, base64url-encoded.
///
/// Changing the format costs nothing: tokens are one-shot (burned by
/// /validate-session on first use) and expire in a day, so there is no
/// stored token anywhere that needs to keep parsing. Nothing downstream
/// inspects the token's structure — the WorldServer treats it as an opaque
/// string and hands it straight back here.
/// </summary>
public static class TokenService
{
    public static string CreateToken()
    {
        Span<byte> bytes = stackalloc byte[32];
        RandomNumberGenerator.Fill(bytes);

        // base64url: URL-safe alphabet, no padding. Keeps the token safe to
        // put in a header, a query string, or a launcher argv without
        // anything needing to escape it.
        return Convert.ToBase64String(bytes)
            .Replace('+', '-')
            .Replace('/', '_')
            .TrimEnd('=');
    }
}
