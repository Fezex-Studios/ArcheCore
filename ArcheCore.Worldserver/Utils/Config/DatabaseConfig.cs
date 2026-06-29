namespace ArcheCore.Worldserver.Utils.Config;

public class DatabaseConfig
{
    public string Host { get; set; } = "localhost";
    public int Port { get; set; } = 3306;
    public string Name { get; set; } = string.Empty;
    public string User { get; set; } = string.Empty;
    public string Password { get; set; } = string.Empty;
    public string WorldDb { get; set; } = string.Empty;
}