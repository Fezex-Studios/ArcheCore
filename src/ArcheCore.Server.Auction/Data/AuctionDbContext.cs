using ArcheCore.Server.Auction.Models;
using Microsoft.EntityFrameworkCore;

namespace ArcheCore.Server.Auction.Data;

/// <summary>
/// The auction house's OWN MySQL database: the listings, and an outbox of
/// mail waiting to reach the persistence server's general mailbox.
///
/// Its own database rather than a shared one is what makes a sale safe to
/// write: taking a listing and queueing the seller's gold and the buyer's
/// item happen in ONE transaction here. It also means this service can be
/// backed up, migrated or taken down on its own - the world keeps running,
/// players just can't trade.
///
/// It holds no player inventories and no gold balances. It knows an item as
/// an id, a name and a quantity; the world server owns everything else.
/// </summary>
public class AuctionDbContext : DbContext
{
    public AuctionDbContext(DbContextOptions<AuctionDbContext> options) : base(options) { }

    public DbSet<AuctionListing> AuctionListings => Set<AuctionListing>();
    public DbSet<MailOutbox> MailOutbox => Set<MailOutbox>();
    public DbSet<AuctionPurchase> AuctionPurchases => Set<AuctionPurchase>();

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        modelBuilder.Entity<AuctionListing>(entity =>
        {
            entity.ToTable("auction_listings");
            entity.HasKey(a => a.Id);

            entity.Property(a => a.Id).HasColumnName("id").ValueGeneratedOnAdd();
            entity.Property(a => a.SellerCharacterId).HasColumnName("seller_character_id");
            entity.Property(a => a.SellerName).HasColumnName("seller_name").HasMaxLength(64).IsRequired();
            entity.Property(a => a.ItemTemplateId).HasColumnName("item_template_id");
            entity.Property(a => a.Quantity).HasColumnName("quantity");
            entity.Property(a => a.Price).HasColumnName("price");
            entity.Property(a => a.ExpiresAtTicks).HasColumnName("expires_at_ticks");
            entity.Property(a => a.ItemName).HasColumnName("item_name").HasMaxLength(128).IsRequired();

            entity.HasIndex(a => a.SellerCharacterId);
            entity.HasIndex(a => a.ExpiresAtTicks);
            entity.HasIndex(a => a.ItemName);
        });

        modelBuilder.Entity<MailOutbox>(entity =>
        {
            entity.ToTable("mail_outbox");
            entity.HasKey(m => m.Id);

            entity.Property(m => m.Id).HasColumnName("id").ValueGeneratedOnAdd();
            entity.Property(m => m.DeliveryKey).HasColumnName("delivery_key").HasMaxLength(64).IsRequired();
            entity.Property(m => m.CharacterId).HasColumnName("character_id");
            entity.Property(m => m.Sender).HasColumnName("sender").HasMaxLength(64).IsRequired();
            entity.Property(m => m.Subject).HasColumnName("subject").HasMaxLength(128).IsRequired();
            entity.Property(m => m.Gold).HasColumnName("gold");
            entity.Property(m => m.ItemTemplateId).HasColumnName("item_template_id");
            entity.Property(m => m.ItemQuantity).HasColumnName("item_quantity");
            entity.Property(m => m.CreatedAtTicks).HasColumnName("created_at_ticks");
            entity.Property(m => m.Failed).HasColumnName("failed");
            entity.Property(m => m.FailureReason).HasColumnName("failure_reason").HasMaxLength(256).IsRequired();

            entity.HasIndex(m => m.DeliveryKey).IsUnique();
            entity.HasIndex(m => m.Failed);
        });

        modelBuilder.Entity<AuctionPurchase>(entity =>
        {
            entity.ToTable("auction_purchases");
            entity.HasKey(p => p.PurchaseKey);

            entity.Property(p => p.PurchaseKey).HasColumnName("purchase_key").HasMaxLength(64).ValueGeneratedNever();
            entity.Property(p => p.AuctionId).HasColumnName("auction_id");
            entity.Property(p => p.BuyerCharacterId).HasColumnName("buyer_character_id");
            entity.Property(p => p.SellerCharacterId).HasColumnName("seller_character_id");
            entity.Property(p => p.ItemTemplateId).HasColumnName("item_template_id");
            entity.Property(p => p.Quantity).HasColumnName("quantity");
            entity.Property(p => p.Price).HasColumnName("price");
            entity.Property(p => p.CreatedAtTicks).HasColumnName("created_at_ticks");
        });
    }
}
