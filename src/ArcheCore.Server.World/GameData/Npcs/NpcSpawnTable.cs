namespace ArcheCore.Server.World.GameData.Npcs;

public class NpcSpawnerTable
{
    public int   Id         { get; set; }
    public int   TemplateId { get; set; }
    public float X          { get; set; }
    public float Y          { get; set; }
    public float Z          { get; set; }
    public int   Count      { get; set; }
    public float Radius     { get; set; }  // add this
}