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
}