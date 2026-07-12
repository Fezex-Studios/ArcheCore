using System.Numerics;
using ArcheCore.Server.World.Core.Interaction;

namespace ArcheCore.Server.World.Core.Entities;

public class NpcEntity : IInteractable
{
    public int        NetworkId     { get; set; }
    public int        TemplateId    { get; set; }
    public string     Name          { get; set; } = string.Empty;
    public int        Level         { get; set; }
    public string     ModelType     { get; set; } = string.Empty;
    public Vector3    Position      { get; set; }
    public float      InteractRange { get; set; }

    public InteractableKind Kind => InteractableKind.Npc;
}