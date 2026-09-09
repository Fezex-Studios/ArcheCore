using Microsoft.EntityFrameworkCore;
using ArcheCore.PersistenceServer.Api.Models;

namespace ArcheCore.PersistenceServer.Api.Data
{
    public class PersistenceDbContext : DbContext
    {
        public PersistenceDbContext(DbContextOptions<PersistenceDbContext> options)
            : base(options) { }

        public DbSet<Character> Characters => Set<Character>();

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

                entity.HasIndex(c => c.AccountId);
            });
        }
    }
}