using MessagePack;

namespace ArcheCore.Network.Shared.Packets.PersistenceServer
{
    /// <summary>
    /// One thing waiting in a character's mailbox.
    ///
    /// There is ONE mailbox, and it lives on the persistence server. Anything
    /// that wants to give a player something posts a piece of mail here: the
    /// auction house (a sale's proceeds, a purchase, an unsold or cancelled
    /// listing), the cash shop, or an admin handing out a gift. The player
    /// claims all of it from the same window.
    /// </summary>
    [MessagePackObject(true)]
    public class MailDto
    {
        public long   Id;
        public long   CharacterId;

        /// <summary>"Auction House", "Cash Shop", "Game Master", or a player's name later on.</summary>
        public string Sender;
        public string Subject;

        public int    Gold;
        public int    ItemTemplateId;
        public int    ItemQuantity;
        public long   CreatedAtTicks;
    }

    /// <summary>
    /// One auction listing. The item in it is in NOBODY's inventory - it left
    /// the seller's bag when they listed it and exists only as this row.
    /// </summary>
    [MessagePackObject(true)]
    public class AuctionListingDto
    {
        public long   Id;
        public long   SellerCharacterId;
        public string SellerName;
        public int    ItemTemplateId;
        public int    Quantity;
        public int    Price;
        public long   ExpiresAtTicks;
    }

    /// <summary>
    /// One thing the cash shop sells. Priced in credits, which are bought with
    /// real money and have nothing to do with gold - that separation is the
    /// entire point of keeping this out of the auction house.
    /// </summary>
    [MessagePackObject(true)]
    public class CashShopItemDto
    {
        public int    Id;
        public string DisplayName;
        public string Category;
        public int    ItemTemplateId;
        public int    Quantity;
        public int    PriceCredits;
        public int    SortOrder;

        /// <summary>Whether this can be sent to another character. Set per item in the catalogue.</summary>
        public bool   IsGiftable;
    }
}
