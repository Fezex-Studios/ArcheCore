namespace ArcheCore.Server.World.GameData.Shops;

/// <summary>
/// A merchant's shop, attached to an NPC TEMPLATE. Every NPC spawned from
/// that template trades from this list - so "the blacksmith" is one shop
/// no matter how many blacksmiths are placed.
/// </summary>
public class ShopTemplate
{
    public int    Id            { get; set; }
    public int    NpcTemplateId { get; set; }
    public string Name          { get; set; } = string.Empty;
}
