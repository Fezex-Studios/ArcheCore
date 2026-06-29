namespace ArcheCore.Server.World.GameData.Npcs;

public class NpcTemplate
{
    public int    Id        { get; set; }
    public string Name      { get; set; } = string.Empty;
    public int    Level     { get; set; }
    public string ModelType { get; set; } = string.Empty;
}