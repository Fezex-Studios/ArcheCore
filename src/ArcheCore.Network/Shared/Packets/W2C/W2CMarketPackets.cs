using MessagePack;

namespace ArcheCore.Network.Shared.Packets.W2C
{
    [MessagePackObject(true)]
    public class MailEntryData
    {
        public long   Id;
        public string Sender;
        public string Subject;
        public int    Gold;
        public int    ItemTemplateId;
        public int    ItemQuantity;
        public string ItemName;
    }

    /// <summary>Everything waiting in this character's mailbox.</summary>
    [MessagePackObject(true)]
    public class W2CMailListPacket
    {
        public MailEntryData[] Mail;
    }

    [MessagePackObject(true)]
    public class AuctionEntryData
    {
        public long   Id;
        public string SellerName;
        public int    ItemTemplateId;
        public string ItemName;
        public int    Quantity;
        public int    Price;
        public int    MinutesLeft;
        public bool   Mine;
    }

    [MessagePackObject(true)]
    public class W2CAuctionListPacket
    {
        public AuctionEntryData[] Listings;
        public string Search;
        public bool   MineOnly;

        /// <summary>Cut taken when something sells, for the sell panel.</summary>
        public int    FeePercent;
    }

    [MessagePackObject(true)]
    public class CashShopEntryData
    {
        public int    Id;
        public string DisplayName;
        public string Category;
        public int    ItemTemplateId;
        public int    Quantity;
        public int    PriceCredits;

        /// <summary>Show a Gift button for this item.</summary>
        public bool   IsGiftable;
    }

    /// <summary>The cash shop, and what this account can spend.</summary>
    [MessagePackObject(true)]
    public class W2CCashShopListPacket
    {
        public CashShopEntryData[] Items;
        public int Balance;
    }

    /// <summary>
    /// Said out loud to the player. Shared by the auction house, the cash
    /// shop and the mailbox - they all just need to tell you what
    /// happened.
    /// </summary>
    [MessagePackObject(true)]
    public class W2CMarketResultPacket
    {
        public bool   Success;
        public string Message;

        /// <summary>Which window should refresh itself: 0 none, 1 auction, 2 cash shop, 3 mail.</summary>
        public int    Refresh;
    }
}
