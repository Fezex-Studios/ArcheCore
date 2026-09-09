namespace ArcheCore.Server.World.GameData.Items;

/// <summary>Extensible lookup table, same reasoning as ItemCategory.</summary>
public class ItemRarity
{
    public int Id { get; set; }
    public string Name { get; set; } = string.Empty;    // "Common", "Uncommon", "Rare", "Epic", "Legendary"
    public string ColorHex { get; set; } = "#FFFFFF";   // for client-side tooltip/name coloring
}