using MessagePack;

namespace ArcheCore.Network.Shared.Packets.W2C
{
    /// <summary>
    /// One row of a merchant's list.
    ///   BuyPrice  - what the PLAYER pays the merchant. 0 = not for sale.
    ///   SellPrice - what the merchant pays the PLAYER. 0 = won't buy it.
    /// A row can be either or both: a merchant can buy ore without selling it.
    /// </summary>
    [MessagePackObject(true)]
    public class ShopItemData
    {
        public int    ItemTemplateId;
        public string ItemName;
        public int    BuyPrice;
        public int    SellPrice;
    }

    /// <summary>
    /// The merchant you interacted with, and everything they trade. Sent in
    /// response to C2WInteract on an NPC whose template has a shop.
    ///
    /// Prices here are for DISPLAY. Every buy/sell is re-priced on the
    /// server from its own copy of the shop table, so a modified client
    /// can't change what it pays.
    /// </summary>
    [MessagePackObject(true)]
    public class W2CShopOpenPacket
    {
        public int            NpcNetworkId;
        public int            ShopId;
        public string         ShopName;
        public ShopItemData[] Items;
    }
}
