namespace ArcheCore.PersistenceServer.Api.Models
{
    /// <summary>
    /// The general mailbox: one piece of mail waiting for a character.
    ///
    /// EVERYTHING that gives a player something goes through here - the
    /// auction house (sale proceeds, purchases, unsold and cancelled
    /// listings), the cash shop, refunds, and admins handing out gifts. The
    /// player claims all of it from one window.
    ///
    /// Mail can carry gold, one item stack, or both, plus a subject. Claiming
    /// deletes the row and returns it in one transaction.
    /// </summary>
    public class Mail
    {
        public long   Id             { get; set; }
        public long   CharacterId    { get; set; }
        public string Sender         { get; set; } = "";
        public string Subject        { get; set; } = "";
        public int    Gold           { get; set; }
        public int    ItemTemplateId { get; set; }
        public int    ItemQuantity   { get; set; }
        public long   CreatedAtTicks { get; set; }
    }

    /// <summary>
    /// A note that a delivery key has already been posted.
    ///
    /// Other services (the auction house) send mail through an outbox that
    /// retries until it's told "yes". If their last attempt actually landed
    /// but the reply was lost - or they crashed before forgetting it - the
    /// retry carries the same key, and this table is how we recognise it and
    /// say "already done" instead of delivering twice.
    ///
    /// It has to be its own table rather than a column on Mail, because a
    /// player may have claimed (and so deleted) the mail before a late retry
    /// arrives. Receipts are tiny and only matter for as long as a retry can
    /// still be in flight; anything older than a few days can be deleted.
    /// </summary>
    public class MailReceipt
    {
        public string DeliveryKey    { get; set; } = "";
        public long   CreatedAtTicks { get; set; }
    }
}
