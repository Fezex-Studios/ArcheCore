
using System.ComponentModel.DataAnnotations;

namespace ArcheCore.Server.World.GameData.Items;

public class ItemTable
{
    [Key]
    public int item_id { get; set; }
    public string name { get; set; }
    public string description { get; set; }
    public string icon_name { get; set; }

    /// <summary>ItemCategories.Id ("Weapon", "Material"...). 0 = uncategorised.
    /// Shown in the client tooltip under the item name.</summary>
    public int category_id { get; set; }

    /// <summary>ItemRarities.Id. 0 = no rarity (name shown in plain text colour).
    /// The rarity's ColorHex colours the item name in tooltips.</summary>
    public int rarity_id { get; set; }

    /// <summary>Minimum character level, shown in the tooltip. 0 = no requirement.
    /// Display only for now - nothing enforces it yet.</summary>
    public int required_level { get; set; }
}
