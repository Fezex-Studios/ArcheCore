namespace ArcheCore.Server.World.GameData.Shops;

/// <summary>
/// One row of a shop's list.
///
///   BuyPrice  - gold the PLAYER pays to buy one.       0 = not for sale.
///   SellPrice - gold the MERCHANT pays for one.        0 = won't buy it.
///
/// A row with only SellPrice set is how a merchant buys something without
/// selling it - an ore trader that takes your ore but has none in stock.
/// Stock is unlimited in this pass.
/// </summary>
public class ShopItem
{
    public int Id        { get; set; }
    public int ShopId    { get; set; }
    public int ItemId    { get; set; }
    public int BuyPrice  { get; set; }
    public int SellPrice { get; set; }

    /// <summary>Display order in the shop window, lowest first.</summary>
    public int SortOrder { get; set; }
}
