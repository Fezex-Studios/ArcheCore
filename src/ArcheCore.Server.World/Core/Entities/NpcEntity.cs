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

    // ── Combat (roadmap G) ──
    // On the entity rather than NpcAiState: stationary NPCs never get an AI
    // state, but still need to know whether they can be attacked.

    /// <summary>0 = can't be attacked.</summary>
    public int        MaxHealth      { get; set; }
    public int        Health         { get; set; }
    public bool       IsAttackable   => MaxHealth > 0;
    public bool       IsDead         => IsAttackable && Health <= 0;

    public int        LootTableId    { get; set; }
    public int        RespawnSeconds { get; set; }

    public string     Title          { get; set; } = string.Empty;
    public string     Greeting       { get; set; } = string.Empty;

    // ── Fighting back (roadmap J) ──
    public float      AggroRadius      { get; set; }
    public float      AttackRange      { get; set; } = 2.5f;
    public int        AttackCooldownMs { get; set; } = 2000;
    public int        AttackDamageMin  { get; set; }
    public int        AttackDamageMax  { get; set; }

    /// <summary>Attacks players (and so can be pulled, chased and leashed).</summary>
    public bool       IsAggressive   => AggroRadius > 0f && AttackDamageMax > 0;

    public InteractableKind Kind => InteractableKind.Npc;
}