namespace ArcheCore.Server.World.GameData.Harvest;

/// <summary>
/// One placed node in the world. Unlike NPC spawners there's no Count or
/// Radius - a node is a single fixed object, so it's placed exactly.
/// </summary>
public class HarvestNodeSpawn
{
    public int   Id         { get; set; }
    public int   TemplateId { get; set; }
    public float X          { get; set; }
    public float Y          { get; set; }
    public float Z          { get; set; }

    /// <summary>Facing in degrees, so rocks and bushes don't all point the same way.</summary>
    public float Yaw        { get; set; }
}
