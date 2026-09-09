namespace ArcheCore.Server.World.GameData.Items;

/// <summary>
/// Lookup table, not a C# enum, because designers should be able to add a
/// new category (e.g. "Consumable" -> "Consumable-Food" split) via
/// GameData/Content/item_categories.json without a code change or a new
/// migration. Contrast with ObjectiveType in the Quests domain, which IS
/// a fixed enum - that distinction (extensible-by-designers -> table,
/// fixed-by-code -> enum) is the rule of thumb to apply to future tables.
/// </summary>
public class ItemCategory
{
    public int Id { get; set; }
    public string Name { get; set; } = string.Empty;   // "Weapon", "Armor", "Consumable", "Material", "QuestItem"
}