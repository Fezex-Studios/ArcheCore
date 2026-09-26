namespace ArcheCore.Server.Auth.Contracts;

// The launcher (src/api/login.api.ts) and the WorldServer's AuthService
// both read these as PascalCase. ASP.NET Core serializes camelCase by
// default, which would silently turn every `Success` into `success` and
// break both callers with a 200 OK and an empty-looking body. Program.cs
// sets PropertyNamingPolicy = null to keep these names exactly as written.
//
// Incoming JSON is matched case-insensitively by default, so clients
// sending either casing on the request side keep working.

public sealed class LoginRequest
{
    public string? Username { get; set; }
    public string? Password { get; set; }
}

public sealed class LoginResponse
{
    public bool    Success { get; set; }

    /// <summary>One-shot launch token, valid for Auth:LaunchTokenSeconds.</summary>
    public string? Token   { get; set; }

    /// <summary>
    /// Long-lived credential the launcher keeps instead of the password and
    /// trades for launch tokens at /session/launch-token. Single use: each
    /// trade returns a replacement.
    /// </summary>
    public string? RefreshToken { get; set; }

    public string? Username { get; set; }
    public string? Message { get; set; }
}

public sealed class RefreshRequest
{
    public string? RefreshToken { get; set; }
}

public sealed class LogoutResponse
{
    public bool Success { get; set; }
}

public sealed class RegisterRequest
{
    public string? Username { get; set; }
    public string? Password { get; set; }
}

public sealed class RegisterResponse
{
    public bool    Success { get; set; }
    public string? Message { get; set; }
}

public sealed class ValidateSessionRequest
{
    public string? Token { get; set; }
}

public sealed class ValidateSessionResponse
{
    public bool Valid     { get; set; }
    public int  AccountId { get; set; }
}

public sealed class GameDataVersionResponse
{
    public string Hash { get; set; } = string.Empty;
}

public sealed class ErrorResponse
{
    public string Error { get; set; } = string.Empty;
}
