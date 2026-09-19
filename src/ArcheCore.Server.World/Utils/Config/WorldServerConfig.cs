namespace ArcheCore.Server.World.Utils.Config;

public class WorldServerConfig
{
    public int    TickRate         { get; set; } = 20;
    public int    MaxPlayers       { get; set; } = 500;
    public String MOTD             { get; set; } = String.Empty;
    public String AuthServerUrl    { get; set; } = String.Empty;
    public String InternalSecret   { get; set; } = String.Empty;
    public String PersistenceBaseUrl { get; set; } = "http://127.0.0.1:7778";

    /// <summary>
    /// How often every in-world character is saved, in seconds. Saves are
    /// spread evenly across the interval (each player lands on its own tick),
    /// so there is never a burst of N saves at once. Only characters whose
    /// position or level changed since their last save are sent.
    /// 0 or less disables autosave (disconnect / level-up / shutdown saves
    /// still happen).
    /// </summary>
    public int AutosaveIntervalSeconds { get; set; } = 120;

    /// <summary>
    /// DEV/LOAD-TEST ONLY. When true, AuthService accepts tokens of the
    /// form "loadtest:{n}" and returns account id (900000 + n) WITHOUT
    /// calling the real AuthServer. Never true outside a local/staging
    /// load-test run — this is a full auth bypass.
    /// </summary>
    public bool AllowLoadTestBypass { get; set; } = false;

    /// <summary>
    /// DEV ONLY. Enables client-driven debug opcodes that grant state the
    /// server has no way to verify - currently just C2W LevelUp. Never
    /// true on anything players can reach.
    /// </summary>
    public bool AllowDebugCommands { get; set; } = false;

    /// <summary>
    /// Called once at boot, before the socket opens. Every check here is
    /// something that silently produces a working-looking server with no
    /// security: a default secret that an attacker already knows, or a
    /// bypass flag left on after a load test. A config mistake that costs
    /// you the shard should cost you a failed startup instead, loudly,
    /// while you are watching.
    /// </summary>
    public void Validate(bool isDevelopment)
    {
        const string Placeholder = "replace_this_with_a_real_secret";

        if (string.IsNullOrWhiteSpace(InternalSecret)
            || InternalSecret == Placeholder
            || InternalSecret.Length < 32)
        {
            throw new InvalidOperationException(
                "World:InternalSecret is missing, still the placeholder, or shorter than " +
                "32 characters. It authenticates this server to BOTH the AuthServer and " +
                "the Persistence server, so all three must carry the same value. " +
                "Generate one with: openssl rand -base64 48");
        }

        if (AllowLoadTestBypass && !isDevelopment)
        {
            throw new InvalidOperationException(
                "World:AllowLoadTestBypass is true outside the Development environment. " +
                "This is a complete authentication bypass - any client can present " +
                "\"loadtest:N\" and be account 900000+N. Set it to false.");
        }

        if (AllowDebugCommands && !isDevelopment)
        {
            throw new InvalidOperationException(
                "World:AllowDebugCommands is true outside the Development environment. " +
                "Set it to false.");
        }
    }
}