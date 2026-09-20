using Microsoft.EntityFrameworkCore;

namespace ArcheCore.Server.Auth.Data;

/// <summary>One row of `accounts`.</summary>
public sealed class Account
{
    public int      AccountId    { get; set; }
    public string   Username     { get; set; } = string.Empty;
    public string   PasswordHash { get; set; } = string.Empty;
    public DateTime CreatedAt    { get; set; }
}

/// <summary>
/// One row of `sessions`. At most one per account, enforced by a unique
/// index, same as the old schema.
/// </summary>
public sealed class Session
{
    public string   Token     { get; set; } = string.Empty;
    public int      AccountId { get; set; }
    public DateTime ExpiresAt { get; set; }
}

/// <summary>
/// One row of `failed_logins`. LockedUntil is null when not locked.
/// </summary>
public sealed class FailedLogin
{
    public string    Username    { get; set; } = string.Empty;
    public int       Attempts    { get; set; }
    public DateTime? LockedUntil { get; set; }
}

public sealed class AuthDbContext : DbContext
{
    public AuthDbContext(DbContextOptions<AuthDbContext> options) : base(options) { }

    public DbSet<Account>     Accounts     => Set<Account>();
    public DbSet<Session>     Sessions     => Set<Session>();
    public DbSet<FailedLogin> FailedLogins => Set<FailedLogin>();

    protected override void OnModelCreating(ModelBuilder b)
    {
        // Explicit lengths everywhere a column is indexed. MySQL cannot put
        // a unique index on an unbounded TEXT column without a prefix
        // length, so leaving these to the default (longtext) would make the
        // migration fail at create time rather than at use time.
        //
        // Timestamps are real DATETIME columns now, not the ISO strings the
        // SQLite version stored. That was only ever a compatibility
        // concession to the rows Database.ts had already written; since the
        // move to MySQL imports the data anyway, there is no reason to keep
        // comparing timestamps as text. Everything is UTC by convention —
        // MySQL does not store an offset, so the rule is that nothing ever
        // writes a local time here.

        b.Entity<Account>(e =>
        {
            e.ToTable("accounts");
            e.HasKey(a => a.AccountId);

            e.Property(a => a.AccountId)
                .HasColumnName("account_id")
                .ValueGeneratedOnAdd();

            e.Property(a => a.Username)
                .HasColumnName("username")
                .HasMaxLength(20)
                .IsRequired();

            // bcrypt output is always 60 chars; 100 leaves room to change
            // algorithm later without another migration.
            e.Property(a => a.PasswordHash)
                .HasColumnName("password_hash")
                .HasMaxLength(100)
                .IsRequired();

            e.Property(a => a.CreatedAt)
                .HasColumnName("created_at")
                .HasDefaultValueSql("CURRENT_TIMESTAMP(6)");

            // Note this index is case-INSENSITIVE under MySQL's default
            // collation, where SQLite's was case-sensitive. That is a
            // deliberate improvement: it stops someone registering "Aidan"
            // against an existing "aidan" and impersonating them in chat.
            // It also means login matches regardless of case, which is
            // what players expect. See AUTH-PORT-README before importing,
            // since accounts differing only in case will collide.
            e.HasIndex(a => a.Username)
                .IsUnique()
                .HasDatabaseName("ux_accounts_username");
        });

        b.Entity<Session>(e =>
        {
            e.ToTable("sessions");
            e.HasKey(s => s.Token);

            // base64url of 32 bytes is 43 chars. 64 is headroom.
            e.Property(s => s.Token)
                .HasColumnName("token")
                .HasMaxLength(64);

            e.Property(s => s.AccountId)
                .HasColumnName("account_id")
                .IsRequired();

            e.Property(s => s.ExpiresAt)
                .HasColumnName("expires_at")
                .HasColumnType("datetime(6)")
                .IsRequired();

            // One active session per account, enforced by the database
            // rather than by application logic.
            e.HasIndex(s => s.AccountId)
                .IsUnique()
                .HasDatabaseName("ux_sessions_account_id");

            // The purge job scans on this. Cheap now, and it stops the
            // hourly sweep turning into a full table scan later.
            e.HasIndex(s => s.ExpiresAt)
                .HasDatabaseName("ix_sessions_expires_at");
        });

        b.Entity<FailedLogin>(e =>
        {
            e.ToTable("failed_logins");
            e.HasKey(f => f.Username);

            e.Property(f => f.Username)
                .HasColumnName("username")
                .HasMaxLength(20);

            e.Property(f => f.Attempts)
                .HasColumnName("attempts")
                .HasDefaultValue(0);

            e.Property(f => f.LockedUntil)
                .HasColumnName("locked_until")
                .HasColumnType("datetime(6)");
        });
    }
}
