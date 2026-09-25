using MessagePack;

namespace ArcheCore.Network.Shared.Packets.C2W
{
    /// <summary>
    /// Take one piece of mail. MailId 0 = take everything that fits, and a
    /// negative id means "just show me my mailbox" without taking anything.
    /// </summary>
    [MessagePackObject(true)]
    public class C2WMailClaimPacket
    {
        public long MailId;
    }

    [MessagePackObject(true)]
    public class C2WAuctionBrowsePacket
    {
        public string Search;
        public bool   MineOnly;
    }

    [MessagePackObject(true)]
    public class C2WAuctionCreatePacket
    {
        public int Slot;
        public int Quantity;
        public int Price;
    }

    [MessagePackObject(true)]
    public class C2WAuctionBuyPacket
    {
        public long AuctionId;
    }

    [MessagePackObject(true)]
    public class C2WAuctionCancelPacket
    {
        public long AuctionId;
    }

    [MessagePackObject(true)]
    public class C2WCashShopBrowsePacket
    {
    }

    [MessagePackObject(true)]
    public class C2WCashShopBuyPacket
    {
        public int CashShopItemId;
    }

    /// <summary>
    /// Buy a cash shop item for ANOTHER character. It's charged to the
    /// sender's credits and arrives in the recipient's mailbox, from the
    /// sender's name. Only items flagged giftable can be sent.
    /// </summary>
    [MessagePackObject(true)]
    public class C2WCashShopGiftPacket
    {
        public int    CashShopItemId;
        public string RecipientName;
    }
}
