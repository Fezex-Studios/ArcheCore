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

    /// <summary>Where this NPC was originally spawned - wander is leashed around this, not around its current position, so it can't drift arbitrarily far over time.</summary>
    public Vector3    SpawnOrigin   { get; set; }

    /// <summary>Current wander destination. Null means "pick a new one".</summary>
    public Vector3?   WanderTarget  { get; set; }

    /// <summary>Which spawner produced this NPC - needed so despawn can tell the spawner one of its group died/left, and so the AI tick can skip NPCs that were placed by a GM command rather than a spawner.</summary>
    public int        SpawnerId     { get; set; }

    /// <summary>Copied from NpcTemplate.IsStationary. Stationary NPCs never enter the wander AI.</summary>
    public bool       IsStationary  { get; set; }

    public InteractableKind Kind => InteractableKind.Npc;
}