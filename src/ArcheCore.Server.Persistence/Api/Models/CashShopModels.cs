namespace ArcheCore.PersistenceServer.Api.Models
{
    /// <summary>
    /// Something the cash shop sells. A row here is the whole definition:
    /// what it's called, what item it hands over, how many, and what it
    /// costs in CREDITS.
    ///
    /// Credits are bought with real money and are deliberately not gold. Keeping
    /// the two apart is why the cash shop is a separate system from the
    /// auction house rather than a tab on it.
    /// </summary>
    public class CashShopItem
    {
        public int    Id             { get; set; }
        public string DisplayName    { get; set; } = "";
        public string Category       { get; set; } = "";

        /// <summary>Items.item_id on the world server - what the buyer actually receives.</summary>
        public int    ItemTemplateId { get; set; }
        public int    Quantity       { get; set; } = 1;

        public int    PriceCredits   { get; set; }
        public int    SortOrder      { get; set; }

        /// <summary>Off the shelf without deleting the row (and its history).</summary>
        public bool   IsEnabled      { get; set; } = true;

        /// <summary>
        /// Can a player buy this FOR someone else? Decided per item. It
        /// defaults to false on purpose: forgetting to set it should mean the
        /// Gift button doesn't appear, never that something you meant to keep
        /// personal can be sent to anyone.
        /// </summary>
        public bool   IsGiftable     { get; set; } = false;
    }

    /// <summary>
    /// An account's credit balance. Per ACCOUNT, not per character: credits are
    /// bought by a person, and every character they own spends the same
    /// purse.
    /// </summary>
    public class AccountCredits
    {
        public int  AccountId { get; set; }
        public int  Balance   { get; set; }
        public long UpdatedAtTicks { get; set; }
    }
}
