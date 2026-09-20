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

    /// <summary>Failed logins allowed before the account is locked.</summary>
    public int MaxLoginAttempts { get; set; } = 5;

    /// <summary>How long a lockout lasts.</summary>
    public int LockoutMinutes { get; set; } = 15;

    /// <summary>How long an issued session token stays valid, if unused.</summary>
    public int SessionLifetimeHours { get; set; } = 24;

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

        if (SessionLifetimeHours < 1)
            throw new InvalidOperationException("Auth:SessionLifetimeHours must be at least 1.");
    }
}
