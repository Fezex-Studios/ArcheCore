using MessagePack;

namespace ArcheCore.Network.Shared.Packets.PersistenceServer.P2W
{
    [MessagePackObject(true)]
    public class P2WMailListResponse
    {
        public MailDto[] Mail;
    }

    /// <summary>Claimed = false means someone got there first - give the player nothing.</summary>
    [MessagePackObject(true)]
    public class P2WMailClaimResponse
    {
        public bool    Claimed;
        public MailDto Mail;
    }

    [MessagePackObject(true)]
    public class P2WAuctionBrowseResponse
    {
        public AuctionListingDto[] Listings;
    }

    [MessagePackObject(true)]
    public class P2WAuctionCreateResponse
    {
        public bool Created;
        public long AuctionId;
    }

    /// <summary>
    /// Sent = false means it was NOT posted (no such character, or nonsense
    /// contents) and never will be - retrying won't help. Sent = true also
    /// covers "this DeliveryKey was already delivered".
    /// </summary>
    [MessagePackObject(true)]
    public class P2WMailSendResponse
    {
        public bool   Sent;
        public string Reason;
    }

    [MessagePackObject(true)]
    public class P2WAuctionGetResponse
    {
        public bool              Found;
        public AuctionListingDto Listing;
    }

    /// <summary>
    /// Taken = false means the listing was not taken (already gone, expired,
    /// or your own). Change nothing. Reason says why, in words a player can read.
    /// </summary>
    [MessagePackObject(true)]
    public class P2WAuctionTakeResponse
    {
        public bool              Taken;
        public string            Reason;
        public AuctionListingDto Listing;

        /// <summary>
        /// True when the mail this take queued has already reached the
        /// persistence server, so it's in the mailbox right now. False means
        /// it's queued and will follow in a moment.
        /// </summary>
        public bool              MailDelivered;
    }

    [MessagePackObject(true)]
    public class P2WAuctionExpireResponse
    {
        public AuctionListingDto[] Expired;
    }

    [MessagePackObject(true)]
    public class P2WCashShopCatalogResponse
    {
        public CashShopItemDto[] Items;

        /// <summary>The account's credit balance, so the window can show it.</summary>
        public int Balance;
    }

    /// <summary>
    /// Bought = false with a reason: not enough credits, or the item is gone.
    /// Nothing was charged and nothing was posted.
    /// </summary>
    [MessagePackObject(true)]
    public class P2WCashShopBuyResponse
    {
        public bool   Bought;
        public string Reason;
        public int    Balance;
        public string DeliveredName;
        public int    DeliveredQuantity;

        /// <summary>For a gift: the recipient's name as it's actually spelled.</summary>
        public string RecipientName;
    }
}
