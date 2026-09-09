namespace ArcheCore.Server.World.GameData.Items;

/// <summary>
/// One row per equippable item - not every ItemTable row will have a
/// matching ItemStats row (a crafting material has no stats), so this is
/// a 1:0-or-1 relationship, not embedded directly on ItemTable. Keeps
/// ItemTable itself lean for the ~80% of items (materials, quest items,
/// currency) that never need stat columns at all.
/// </summary>
public class ItemStats
{
    public int Id { get; set; }
    public int ItemId { get; set; }          // FK-by-convention -> ItemTable.Id

    public int Strength { get; set; }
    public int Agility { get; set; }
    public int Intelligence { get; set; }
    public int Vitality { get; set; }
    public int ArmorRating { get; set; }
}