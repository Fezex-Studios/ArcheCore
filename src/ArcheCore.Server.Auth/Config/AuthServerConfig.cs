namespace ArcheCore.Server.Auth.Config;

/// <summary>
/// Replaces ServerConfig.ts and the .env file it read.
///
/// The old server pulled AUTH_DATABASE_PATH, GAME_DATABASE_PATH and
/// INTERNAL_SECRET out of process.env via dotenv, which meant the auth
/// server's configuration lived in a different place, a different format,
/// and a different editor from the other two servers. It's all
/// appsettings.json now, same as World and Persistence.
///
/// Environment variables still work and still win, which is what you want
/// for deployment: ASP.NET Core's default configuration chain reads
/// appsettings.json, then appsettings.{Environment}.json, then environment
/// variables. So `Auth__InternalSecret=...` in the environment overrides
/// the file without editing it. (Double underscore is the separator for
/// nested keys.)
///
/// The database connection is NOT here — it lives under ConnectionStrings
/// with the name "Auth", matching how the persistence server does it.
/// </summary>
public sealed class AuthServerConfig
{
    /// <summary>
    /// Path to the encrypted client game data blob served by /gamedata/db.
    /// Was GAME_DATABASE_PATH. The route never inspects the file's format,
    /// so this can point at gamedata.bin or gamedata.db equally.
    /// </summary>
    public string GameDataPath { get; set; } = "gamedata.bin";

    /// <summary>
    /// Shared with the WorldServer ONLY. Must match World:InternalSecret in
    /// the world server's appsettings.json and InternalSecret in the
    /// persistence server's. Never sent to a client.
    /// </summary>
    public string InternalSecret { get; set; } = string.Empty;

    /// <summary>
    /// Failed logins allowed from ONE address before that username is
    /// locked for that address (LoginThrottle). The owner logging in from
    /// elsewhere is unaffected.
    /// </summary>
    public int MaxLoginAttempts { get; set; } = 5;

    /// <summary>How long a lockout lasts.</summary>
    public int LockoutMinutes { get; set; } = 15;

    /// <summary>
    /// Failed logins from ALL addresses together before the account itself
    /// is locked everywhere. The backstop against one account being guessed
    /// at from many machines; high enough that nobody can lock someone
    /// else out casually.
    /// </summary>
    public int AccountLockAttempts { get; set; } = 50;

    /// <summary>
    /// Believe the X-Forwarded-For header for the client's address. ONLY
    /// turn this on when Auth sits behind a reverse proxy (nginx, Caddy)
    /// that sets it - otherwise any client can type a fake address into
    /// the header and walk around every per-IP limit.
    /// </summary>
    public bool TrustForwardedFor { get; set; } = false;

    /// <summary>How long an issued session token stays valid, if unused.</summary>
    public int SessionLifetimeHours { get; set; } = 24;

    /// <summary>
    /// How long a one-shot LAUNCH token lives (launcher L3). It is passed to
    /// the game on the command line, where other local processes can read it,
    /// so it should be dead within a couple of minutes whether or not the
    /// world server burned it. Replaces SessionLifetimeHours for launch
    /// tokens; that setting is now unused.
    /// </summary>
    public int LaunchTokenSeconds { get; set; } = 120;

    /// <summary>
    /// How long a refresh token (what the launcher keeps instead of the
    /// password) lives without being used. Every use replaces it with a new
    /// one, so an active player never hits this.
    /// </summary>
    public int RefreshTokenDays { get; set; } = 30;

    /// <summary>
    /// Called once at boot, before the socket opens. Same rule as the other
    /// two servers: a config mistake that would cost you the shard should
    /// cost you a failed startup instead, loudly, while you're watching.
    /// </summary>
    public void Validate()
    {
        const string Placeholder = "replace_this_with_a_real_secret";

        if (string.IsNullOrWhiteSpace(InternalSecret)
            || InternalSecret == Placeholder
            || InternalSecret.StartsWith("CHANGE_ME", StringComparison.Ordinal)
            || InternalSecret.Length < 32)
        {
            throw new InvalidOperationException(
                "Auth:InternalSecret is missing, still the placeholder, or shorter than 32 " +
                "characters. It must be the SAME value as World:InternalSecret in the world " +
                "server and InternalSecret in the persistence server. " +
                "Generate one with: openssl rand -base64 48");
        }

        if (MaxLoginAttempts < 1)
            throw new InvalidOperationException("Auth:MaxLoginAttempts must be at least 1.");

        if (LockoutMinutes < 1)
            throw new InvalidOperationException("Auth:LockoutMinutes must be at least 1.");

        if (AccountLockAttempts < MaxLoginAttempts)
            throw new InvalidOperationException("Auth:AccountLockAttempts must be at least Auth:MaxLoginAttempts.");

        if (SessionLifetimeHours < 1)
            throw new InvalidOperationException("Auth:SessionLifetimeHours must be at least 1.");

        if (LaunchTokenSeconds < 15 || LaunchTokenSeconds > 3600)
            throw new InvalidOperationException("Auth:LaunchTokenSeconds must be between 15 and 3600.");

        if (RefreshTokenDays < 1)
            throw new InvalidOperationException("Auth:RefreshTokenDays must be at least 1.");
    }
}
