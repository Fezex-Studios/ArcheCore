namespace ArcheCore.Worldserver.Utils.Config;

public class NetworkConfig
{
    public string Host { get; set; } = "0.0.0.0";
    public int Port { get; set; } = 1234;
    public int Backlog { get; set; } = 100;
}