namespace ArcheCore.Server.World.GameData.Loot;

/// <summary>
/// What an NPC can drop. Attached to an NPC template via
/// NpcTemplate.LootTableId. Gold is a range; items are LootTableEntry rows,
/// each rolled independently.
/// </summary>
public class LootTable
{
    public int    Id      { get; set; }
    public string Name    { get; set; } = string.Empty;
    public int    MinGold { get; set; }
    public int    MaxGold { get; set; }
}

/// <summary>One possible drop. Chance is 0-1 (0.25 = 25%), rolled on every kill.</summary>
public class LootTableEntry
{
    public int    Id          { get; set; }
    public int    LootTableId { get; set; }
    public int    ItemId      { get; set; }
    public double Chance      { get; set; }
    public int    MinQuantity { get; set; } = 1;
    public int    MaxQuantity { get; set; } = 1;
}
