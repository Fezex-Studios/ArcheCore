namespace ArcheCore.Server.World.Utils.Config;

public class WorldServerConfig
{
    public int    TickRate         { get; set; } = 20;
    public int    MaxPlayers       { get; set; } = 500;
    public String MOTD             { get; set; } = String.Empty;
    public String AuthServerUrl    { get; set; } = String.Empty;
    public String InternalSecret   { get; set; } = String.Empty;
    public String PersistenceBaseUrl { get; set; } = "http://127.0.0.1:7778"; // e.g. "http://127.0.0.1:7778"
}