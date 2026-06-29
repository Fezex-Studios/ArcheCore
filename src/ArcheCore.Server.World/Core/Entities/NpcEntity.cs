using System.Numerics;

namespace ArcheCore.Server.World.Core.Entities;

public class NpcEntity
{
    public int        NetworkId  { get; set; }
    public int        TemplateId { get; set; }
    public string     Name       { get; set; } = string.Empty;
    public int        Level      { get; set; }
    public string     ModelType  { get; set; } = string.Empty;
    public Vector3    Position   { get; set; }
}