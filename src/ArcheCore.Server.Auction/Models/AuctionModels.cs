namespace ArcheCore.Server.Auction.Models;

/// <summary>
/// One thing for sale.
///
/// THE ITEM IN HERE IS IN NOBODY'S INVENTORY. It left the seller's bag when
/// they listed it and exists only as this row - so every path that deletes a
/// row owes the contents to somebody. That's why every removal goes through
/// /auctions/take, which deletes and returns in a single transaction, and
/// why whoever wins that call must immediately hand the contents over or
/// post them to the seller's mailbox.
/// </summary>
public class AuctionListing
{
    public long   Id                { get; set; }
    public long   SellerCharacterId { get; set; }
    public string SellerName        { get; set; } = "";
    public int    ItemTemplateId    { get; set; }
    public int    Quantity          { get; set; }
    public int    Price             { get; set; }
    public long   ExpiresAtTicks    { get; set; }

    /// <summary>Lowercased item name, so searching needs no item tables here.</summary>
    public string ItemName          { get; set; } = "";
}

/// <summary>
/// A piece of mail this service owes a player, waiting to be handed to the
/// persistence server's general mailbox.
///
/// This is an OUTBOX. A sale must be atomic - take the listing, pay the
/// seller, deliver the item - but the mailbox lives in another service's
/// database, so no single transaction can span both. Instead the sale writes
/// these rows in the SAME transaction that removes the listing, and
/// MailDispatcher delivers them afterwards, retrying until the persistence
/// server accepts each one. Nothing can be lost between the two: either the
/// listing is still on the board, or these rows exist.
///
/// DeliveryKey is what makes retrying safe. The persistence server delivers
/// a given key once, no matter how many times it's sent.
/// </summary>
public class MailOutbox
{
    public long   Id             { get; set; }
    public string DeliveryKey    { get; set; } = "";
    public long   CharacterId    { get; set; }
    public string Sender         { get; set; } = "Auction House";
    public string Subject        { get; set; } = "";
    public int    Gold           { get; set; }
    public int    ItemTemplateId { get; set; }
    public int    ItemQuantity   { get; set; }
    public long   CreatedAtTicks { get; set; }

    /// <summary>
    /// The persistence server refused this for good (for example the
    /// character no longer exists). It stays here for an admin to look at
    /// and is never retried.
    /// </summary>
    public bool   Failed         { get; set; }
    public string FailureReason  { get; set; } = "";
}

/// <summary>
/// A completed purchase, keyed by the PurchaseKey the world server made up.
///
/// This is what lets a lost or late answer be RESOLVED rather than guessed
/// at. If the world server asks to buy again with a key that's already here,
/// the answer is "yes, that already happened" - not a second purchase and not
/// "already gone". If the key is NOT here, the purchase definitely didn't
/// happen. Written in the same transaction as the listing delete, so a
/// purchase and its record can't come apart.
///
/// Rows are only useful while a retry can still be in flight; anything older
/// than a few days can be deleted.
/// </summary>
public class AuctionPurchase
{
    public string PurchaseKey       { get; set; } = "";
    public long   AuctionId         { get; set; }
    public long   BuyerCharacterId  { get; set; }
    public long   SellerCharacterId { get; set; }
    public int    ItemTemplateId    { get; set; }
    public int    Quantity          { get; set; }
    public int    Price             { get; set; }
    public long   CreatedAtTicks    { get; set; }
}
