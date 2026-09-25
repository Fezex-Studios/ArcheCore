using MessagePack;

namespace ArcheCore.Network.Shared.Packets.PersistenceServer.W2P
{
    // ── Mail (the one general mailbox, on the persistence server) ────

    [MessagePackObject(true)]
    public class W2PMailListRequest
    {
        public int  AccountId;
        public long CharacterId;
    }

    /// <summary>
    /// Claim one piece of mail. The persistence server DELETES IT AND
    /// RETURNS IT in the same call, so two clicks can't claim it twice.
    /// </summary>
    [MessagePackObject(true)]
    public class W2PMailClaimRequest
    {
        public int  AccountId;
        public long CharacterId;
        public long MailId;
    }

    /// <summary>
    /// Post something to a character's mailbox. Used by the world server (a
    /// refund, a claim it couldn't deliver) and by the auction service's
    /// outbox.
    ///
    /// DeliveryKey makes a send safe to repeat. A caller that can't be sure
    /// its last attempt landed sends again with the SAME key, and the
    /// persistence server delivers it once. Leave it empty when there is no
    /// retry to protect against.
    /// </summary>
    [MessagePackObject(true)]
    public class W2PMailSendRequest
    {
        public string DeliveryKey;
        public long   CharacterId;
        public string Sender;
        public string Subject;
        public int    Gold;
        public int    ItemTemplateId;
        public int    ItemQuantity;
    }

    // ── Auction ──────────────────────────────────────────────────────

    [MessagePackObject(true)]
    public class W2PAuctionBrowseRequest
    {
        public string Search;
        public long   SellerCharacterId;   // 0 = anyone's
    }

    [MessagePackObject(true)]
    public class W2PAuctionCreateRequest
    {
        public long   SellerCharacterId;
        public string SellerName;
        public int    ItemTemplateId;
        public string ItemName;
        public int    Quantity;
        public int    Price;
        public long   ExpiresAtTicks;
    }

    /// <summary>Look at one listing without touching it - the world server needs the price before it charges anyone.</summary>
    [MessagePackObject(true)]
    public class W2PAuctionGetRequest
    {
        public long AuctionId;
    }

    /// <summary>
    /// Take a listing off the board: deletes and returns in one transaction,
    /// so exactly one caller can win any listing.
    ///
    ///   ExpectedSellerId != 0   a CANCEL: only if it's theirs; the item is mailed back to them.
    ///   BuyerCharacterId != 0   a PURCHASE: the seller is mailed their gold and the buyer is
    ///                           mailed the item, in that same transaction.
    ///
    /// PurchaseKey makes a BUY safe to repeat. The world server makes one up
    /// per purchase and, if the answer is lost or late, asks again with the
    /// SAME key: a purchase that already happened is answered "taken" instead
    /// of being attempted twice, and one that didn't happen is answered
    /// "not taken". That's how a timeout gets resolved instead of guessed at.
    /// </summary>
    [MessagePackObject(true)]
    public class W2PAuctionTakeRequest
    {
        public long   AuctionId;
        public long   ExpectedSellerId;
        public long   BuyerCharacterId;
        public string PurchaseKey;
    }

    [MessagePackObject(true)]
    public class W2PAuctionExpireRequest
    {
        public long NowTicks;
        public int  Limit;
    }

    // ── Cash shop ────────────────────────────────────────────────────

    [MessagePackObject(true)]
    public class W2PCashShopCatalogRequest
    {
        public int AccountId;
    }

    /// <summary>
    /// Buy with credits. The persistence server checks the balance, deducts it
    /// and writes the mail IN ONE TRANSACTION - all three in the same
    /// database, which is why the cash shop belongs there.
    /// </summary>
    [MessagePackObject(true)]
    public class W2PCashShopBuyRequest
    {
        public int  AccountId;
        public long CharacterId;
        public int  CashShopItemId;
    }

    /// <summary>
    /// Buy a cash shop item for someone else. The persistence server checks the
    /// item is giftable, finds the recipient, charges the SENDER's account and
    /// posts the item to the RECIPIENT's mailbox in one transaction.
    /// </summary>
    [MessagePackObject(true)]
    public class W2PCashShopGiftRequest
    {
        public int    AccountId;
        public long   CharacterId;
        public int    CashShopItemId;
        public string RecipientName;
    }
}
