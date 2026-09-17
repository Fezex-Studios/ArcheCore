namespace ArcheCore.Server.World.Utils.Config;

public class WorldServerConfig
{
    public int    TickRate         { get; set; } = 20;
    public int    MaxPlayers       { get; set; } = 500;
    public String MOTD             { get; set; } = String.Empty;
    public String AuthServerUrl    { get; set; } = String.Empty;
    public String InternalSecret   { get; set; } = String.Empty;
    public String PersistenceBaseUrl { get; set; } = "http://127.0.0.1:7778"; // e.g. "http://127.0.0.1:7778"

    /// <summary>
    /// DEV/LOAD-TEST ONLY. When true, AuthService accepts tokens of the
    /// form "loadtest:{n}" and returns account id (900000 + n) WITHOUT
    /// calling the real AuthServer. Never true outside a local/staging
    /// load-test run — this is a full auth bypass with no code path that
    /// flips it on automatically; it only does anything if you set it in
    /// appsettings yourself.
    /// </summary>
    public bool AllowLoadTestBypass { get; set; } = false;
}