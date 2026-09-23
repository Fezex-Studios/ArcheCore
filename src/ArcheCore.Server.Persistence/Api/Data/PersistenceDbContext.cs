using Microsoft.EntityFrameworkCore;
using ArcheCore.PersistenceServer.Api.Models;

namespace ArcheCore.PersistenceServer.Api.Data
{
    public class PersistenceDbContext : DbContext
    {
        public PersistenceDbContext(DbContextOptions<PersistenceDbContext> options)
            : base(options) { }

        public DbSet<Character> Characters => Set<Character>();
        public DbSet<CharacterInventoryItem> InventoryItems => Set<CharacterInventoryItem>();
        public DbSet<CharacterQuest> CharacterQuests => Set<CharacterQuest>();

        protected override void OnModelCreating(ModelBuilder modelBuilder)
        {
            modelBuilder.Entity<Character>(entity =>
            {
                entity.ToTable("characters");
                entity.HasKey(c => c.CharacterId);

                entity.Property(c => c.CharacterId).HasColumnName("character_id");
                entity.Property(c => c.AccountId).HasColumnName("account_id");
                entity.Property(c => c.Name).HasColumnName("name").HasMaxLength(64);
                entity.Property(c => c.Level).HasColumnName("level").HasDefaultValue(1);
                entity.Property(c => c.PosX).HasColumnName("pos_x").HasDefaultValue(0f);
                entity.Property(c => c.PosY).HasColumnName("pos_y").HasDefaultValue(2f);
                entity.Property(c => c.PosZ).HasColumnName("pos_z").HasDefaultValue(0f);

                entity.Property(c => c.Gold).HasColumnName("gold").HasDefaultValue(0);
                entity.HasCheckConstraint("CK_characters_gold_nonnegative", "gold >= 0");

                entity.HasIndex(c => c.AccountId);
            });

            // Per-slot inventory rows. No FK navigation property on
            // Character on purpose - WorldServer never wants EF to lazily
            // pull a character's whole inventory just because it touched
            // the Character row; every inventory read here is an explicit,
            // separate query (see /characters/load and
            // /characters/inventory/save in Program.cs).
            // Quest state, same shape and same reasoning as inventory rows:
            // no navigation property, explicit queries only, composite key so
            // saving is an upsert.
            modelBuilder.Entity<CharacterQuest>(entity =>
            {
                entity.ToTable("character_quests");
                entity.HasKey(q => new { q.CharacterId, q.QuestId });

                entity.Property(q => q.CharacterId).HasColumnName("character_id");
                entity.Property(q => q.QuestId).HasColumnName("quest_id");
                entity.Property(q => q.Status).HasColumnName("status");
                entity.Property(q => q.Progress).HasColumnName("progress").HasMaxLength(128).IsRequired();

                entity.HasIndex(q => q.CharacterId);
            });

            modelBuilder.Entity<CharacterInventoryItem>(entity =>
            {
                entity.ToTable("character_inventory");
                entity.HasKey(i => new { i.CharacterId, i.Slot });

                entity.Property(i => i.CharacterId).HasColumnName("character_id");
                entity.Property(i => i.Slot).HasColumnName("slot");
                entity.Property(i => i.ItemTemplateId).HasColumnName("item_template_id");
                entity.Property(i => i.Quantity).HasColumnName("quantity");

                // A row should never exist for an empty slot - if
                // ItemTemplateId or Quantity would be <= 0, the row is
                // deleted instead (see /characters/inventory/save). This
                // constraint is the same belt-and-braces role as gold's
                // nonnegative check: catches it if that rule is ever
                // bypassed, rather than enforcing it in the first place.
                entity.HasCheckConstraint(
                    "CK_character_inventory_item_positive",
                    "item_template_id > 0 AND quantity > 0");

                entity.HasOne<Character>()
                    .WithMany()
                    .HasForeignKey(i => i.CharacterId)
                    .HasPrincipalKey(c => c.CharacterId)
                    .OnDelete(DeleteBehavior.Cascade);
            });
        }
    }
}
