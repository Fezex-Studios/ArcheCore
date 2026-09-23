namespace ArcheCore.Server.World.GameData.Npcs;

public class NpcTemplate
{
    public int    Id        { get; set; }
    public string Name      { get; set; } = string.Empty;
    public int    Level     { get; set; }
    public string ModelType { get; set; } = string.Empty;
    public float  InteractRange { get; set; } = 4f; // default, per-row override in the DB

    /// <summary>
    /// True = never wanders. Merchants, quest givers, guards on a post.
    ///
    /// Named this way round on purpose: the migration adds the column to
    /// existing rows with the default (false), so every NPC that wandered
    /// before keeps wandering. A "Wanders" column would have defaulted to
    /// false and frozen every orc in the world.
    /// </summary>
    public bool   IsStationary { get; set; }

    /// <summary>
    /// 0 = can't be attacked at all (merchants, quest givers). The migration
    /// gives every existing NPC 0, so nothing becomes attackable by
    /// accident - an NPC has to be given health on purpose.
    /// </summary>
    public int    MaxHealth { get; set; }

    /// <summary>LootTables.Id dropped on death. 0 = drops nothing.</summary>
    public int    LootTableId { get; set; }

    /// <summary>Seconds after death before the spawner replaces it. 0 = the default (30s).</summary>
    public int    RespawnSeconds { get; set; }

    /// <summary>Shown above the name and in the hover tooltip, e.g. "Merchant". Empty = none.</summary>
    public string Title { get; set; } = string.Empty;

    /// <summary>What the NPC says when you use its Talk action. Empty = nothing (Lua can still react).</summary>
    public string Greeting { get; set; } = string.Empty;

    // ── Fighting back (roadmap J) ──
    // All default to 0, so an NPC only becomes dangerous when its row says so.

    /// <summary>How close a player must come before it attacks. 0 = never aggressive.</summary>
    public float  AggroRadius      { get; set; }

    /// <summary>How close it must be to hit. Also how close it walks before stopping.</summary>
    public float  AttackRange      { get; set; } = 2.5f;
    public int    AttackCooldownMs { get; set; } = 2000;
    public int    AttackDamageMin  { get; set; }
    public int    AttackDamageMax  { get; set; }
}
