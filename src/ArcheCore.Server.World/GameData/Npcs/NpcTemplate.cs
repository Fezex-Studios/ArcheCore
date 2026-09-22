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
}
