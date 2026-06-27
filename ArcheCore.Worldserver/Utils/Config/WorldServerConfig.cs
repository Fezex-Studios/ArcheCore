namespace ArcheCore.Worldserver.Utils.Config;

public class WorldServerConfig
{
    public int TickRate { get; set; } = 20;
    public int MaxPlayers { get; set; } = 500;
    public String MOTD { get; set; } = String.Empty;
    public String AuthServerUrl { get; set; } = String.Empty;
    public String InternalSecret { get; set; } = String.Empty;
    public int PersistencePort  { get; set; } 
}