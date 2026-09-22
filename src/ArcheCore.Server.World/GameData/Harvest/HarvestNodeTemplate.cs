namespace ArcheCore.Server.World.GameData.Harvest;

/// <summary>
/// What a kind of node IS - an iron vein, a herb bush. One row per kind;
/// HarvestNodeSpawn places copies of it in the world. Same split as
/// NpcTemplate / NpcSpawnerTable.
///
/// A new gatherable is an INSERT here plus an Items row, never a code change.
/// </summary>
public class HarvestNodeTemplate
{
    public int    Id        { get; set; }
    public string Name      { get; set; } = string.Empty;

    /// <summary>WorldObjectPrefabRegistry key on the client, e.g. "IronVein".</summary>
    public string ModelType { get; set; } = string.Empty;

    /// <summary>Items.item_id granted on a successful harvest. Checked at boot -
    /// a template pointing at a missing item is skipped, never spawned.</summary>
    public int    ItemId      { get; set; }
    public int    MinQuantity { get; set; } = 1;
    public int    MaxQuantity { get; set; } = 1;

    /// <summary>How long the player must stand still to finish.</summary>
    public int    HarvestTimeMs  { get; set; } = 3000;

    /// <summary>How long the node stays depleted after a successful harvest.</summary>
    public int    RespawnSeconds { get; set; } = 60;

    public float  InteractRange  { get; set; } = 4f;
}
