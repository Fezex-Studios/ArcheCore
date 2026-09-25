using ArcheCore.Network.Shared.Packets.PersistenceServer;
using ArcheCore.Network.Shared.Packets.PersistenceServer.P2W;
using ArcheCore.Network.Shared.Packets.PersistenceServer.W2P;
using ArcheCore.Server.Auction.Data;
using ArcheCore.Server.Auction.Models;
using ArcheCore.Server.Auction.Services;
using MessagePack;
using Microsoft.EntityFrameworkCore;

// ArcheCore auction house.
//
// Its own ASP.NET service on its own MySQL database, holding the listings
// and an outbox of mail it owes players.
//
// WHY ITS OWN DATABASE
//
// A sale is "take the listing, pay the seller, deliver the item". The
// listing lives here, so removing it and QUEUEING the two pieces of mail
// happen in ONE transaction - there is no window where an item is off the
// board but nobody has been told they're owed anything.
//
// WHERE THE MAIL GOES
//
// Players have ONE general mailbox, and it lives on the persistence server
// (so admin gifts and the cash shop can use it too). This service can't
// write into another service's database in the same transaction, so it uses
// an OUTBOX: the sale writes mail_outbox rows alongside the listing delete,
// and MailDispatcher hands them to the persistence server afterwards,
// retrying until each is accepted. The persistence server delivers a given
// delivery key exactly once, so retrying can never double-deliver.
//
// It also means this service is disposable: take it down and the world keeps
// running, players just can't trade. Take the PERSISTENCE server down and
// trades still complete - the mail waits in the outbox and arrives when it
// comes back. Nothing here touches inventories, gold balances or characters;
// the world server owns those.

var builder = WebApplication.CreateBuilder(args);

string connectionString = builder.Configuration.GetConnectionString("AuctionDb")
    ?? throw new InvalidOperationException("ConnectionStrings:AuctionDb is not set in appsettings.json.");

int listingHours = builder.Configuration.GetValue("Auction:ListingHours", 24);
int feePercent   = builder.Configuration.GetValue("Auction:FeePercent", 5);
int maxResults   = builder.Configuration.GetValue("Auction:MaxResults", 100);

// Where the general mailbox lives, and the shared secret that proves this
// service is allowed to post to it (the persistence server's InternalSecret).
string persistenceUrl = builder.Configuration["Persistence:BaseUrl"] ?? "http://127.0.0.1:7778";
string? persistenceSecret = builder.Configuration["Persistence:InternalSecret"];

if (string.IsNullOrWhiteSpace(persistenceSecret)
    || persistenceSecret == "replace_this_with_a_real_secret"
    || persistenceSecret.Length < 32)
{
    throw new InvalidOperationException(
        "Persistence:InternalSecret is missing, still the placeholder, or shorter than 32 chars. " +
        "It must be the SAME value as InternalSecret in the persistence server's appsettings.json - " +
        "this service uses it to post mail to the general mailbox.");
}

builder.Services.AddDbContext<AuctionDbContext>(options =>
    options.UseMySql(connectionString, ServerVersion.AutoDetect(connectionString)));

builder.Services.AddHttpClient("persistence", client =>
{
    client.BaseAddress = new Uri(persistenceUrl);
    client.Timeout = TimeSpan.FromSeconds(10);
    client.DefaultRequestHeaders.Add("x-internal-secret", persistenceSecret);
});

builder.Services.AddSingleton<MailSignal>();
builder.Services.AddHostedService<MailDispatcher>();

var app = builder.Build();

using (var scope = app.Services.CreateScope())
{
    var db = scope.ServiceProvider.GetRequiredService<AuctionDbContext>();

    // Its own database, so it owns its own schema outright. Swap this for
    // migrations when the shape starts changing under live data.
    await db.Database.EnsureCreatedAsync();

    // EnsureCreated only builds tables on a database that has NONE, so a table
    // added by a later version never appears on a database you already have.
    // These create whatever's missing and do nothing if it's there. Keep each
    // in step with its model (MailOutbox, AuctionPurchase).
    await db.Database.ExecuteSqlRawAsync(@"
        CREATE TABLE IF NOT EXISTS mail_outbox (
            id                BIGINT       NOT NULL AUTO_INCREMENT,
            delivery_key      VARCHAR(64)  NOT NULL,
            character_id      BIGINT       NOT NULL,
            sender            VARCHAR(64)  NOT NULL,
            subject           VARCHAR(128) NOT NULL,
            gold              INT          NOT NULL,
            item_template_id  INT          NOT NULL,
            item_quantity     INT          NOT NULL,
            created_at_ticks  BIGINT       NOT NULL,
            failed            TINYINT(1)   NOT NULL,
            failure_reason    VARCHAR(256) NOT NULL,
            PRIMARY KEY (id),
            UNIQUE KEY IX_mail_outbox_delivery_key (delivery_key),
            KEY IX_mail_outbox_failed (failed)
        )");

    await db.Database.ExecuteSqlRawAsync(@"
        CREATE TABLE IF NOT EXISTS auction_purchases (
            purchase_key        VARCHAR(64) NOT NULL,
            auction_id          BIGINT      NOT NULL,
            buyer_character_id  BIGINT      NOT NULL,
            seller_character_id BIGINT      NOT NULL,
            item_template_id    INT         NOT NULL,
            quantity            INT         NOT NULL,
            price               INT         NOT NULL,
            created_at_ticks    BIGINT      NOT NULL,
            PRIMARY KEY (purchase_key)
        )");

    // The very first version kept its own mailbox here, in auction_mail. Mail
    // still sitting in it is moved into the outbox so it reaches the general
    // mailbox instead of being stranded, and the old table is renamed out of
    // the way. Safe to interrupt: each row's key is legacy-<id>, and the
    // outbox and the persistence server both ignore a key they've seen.
    long legacyTable = await db.Database
        .SqlQuery<long>($"SELECT COUNT(*) AS Value FROM information_schema.tables WHERE table_schema = DATABASE() AND table_name = 'auction_mail'")
        .SingleAsync();

    if (legacyTable > 0)
    {
        int moved = await db.Database.ExecuteSqlRawAsync(@"
            INSERT IGNORE INTO mail_outbox
                (delivery_key, character_id, sender, subject, gold, item_template_id, item_quantity, created_at_ticks, failed, failure_reason)
            SELECT CONCAT('legacy-', id), character_id, sender, subject, gold, item_template_id, item_quantity, created_at_ticks, 0, ''
            FROM auction_mail");

        await db.Database.ExecuteSqlRawAsync("RENAME TABLE auction_mail TO auction_mail_migrated");

        Console.WriteLine($"[Auction] Moved {moved} piece(s) of old auction mail into the outbox " +
                          "(the old table is now auction_mail_migrated).");
    }

    int live = await db.AuctionListings.CountAsync();
    int queued = await db.MailOutbox.CountAsync(m => !m.Failed);
    int stuck = await db.MailOutbox.CountAsync(m => m.Failed);
    Console.WriteLine($"[Auction] Ready - {live} listing(s), {queued} piece(s) of mail queued, " +
                      $"{listingHours}h listings, {feePercent}% fee, mailbox at {persistenceUrl}.");

    if (stuck > 0)
        Console.Error.WriteLine($"[Auction] {stuck} piece(s) of mail were refused by the persistence server and are " +
                                "parked in mail_outbox with failed = 1. Look at failure_reason.");
}

// ── MessagePack over HTTP, same as the persistence server ────────────

static async Task<T?> ReadAsync<T>(HttpContext context)
{
    using var buffer = new MemoryStream();
    await context.Request.Body.CopyToAsync(buffer);
    return buffer.Length == 0 ? default : MessagePackSerializer.Deserialize<T>(buffer.ToArray());
}

static async Task WriteAsync<T>(HttpContext context, T payload)
{
    context.Response.ContentType = "application/x-msgpack";
    await context.Response.Body.WriteAsync(MessagePackSerializer.Serialize(payload));
}

static AuctionListingDto ToDto(AuctionListing row) => new()
{
    Id                = row.Id,
    SellerCharacterId = row.SellerCharacterId,
    SellerName        = row.SellerName,
    ItemTemplateId    = row.ItemTemplateId,
    Quantity          = row.Quantity,
    Price             = row.Price,
    ExpiresAtTicks    = row.ExpiresAtTicks
};

// The listing stores its name lowercased so searching needs no item tables;
// mail subjects read better in Title Case.
static string Pretty(string name) =>
    System.Globalization.CultureInfo.InvariantCulture.TextInfo.ToTitleCase(name);

// Queue a piece of mail. It is only a row here; MailDispatcher delivers it.
static MailOutbox NewMail(long characterId, string subject, int gold, int itemTemplateId, int quantity) => new()
{
    DeliveryKey    = Guid.NewGuid().ToString("N"),
    CharacterId    = characterId,
    Sender         = "Auction House",
    Subject        = subject.Length > 128 ? subject[..128] : subject,
    Gold           = gold,
    ItemTemplateId = itemTemplateId,
    ItemQuantity   = quantity,
    CreatedAtTicks = DateTime.UtcNow.Ticks
};

// The answer to a purchase that has already happened under this key.
static P2WAuctionTakeResponse PriorPurchaseAnswer(AuctionPurchase prior, W2PAuctionTakeRequest request, MailSignal signal)
{
    // Same key but a different listing or buyer isn't a retry, it's a mistake.
    if (prior.AuctionId != request.AuctionId || prior.BuyerCharacterId != request.BuyerCharacterId)
        return new P2WAuctionTakeResponse { Taken = false, Reason = "That purchase key was already used for something else." };

    // Make sure whatever it queued is being delivered; the answer doesn't wait for it.
    signal.Nudge();

    return new P2WAuctionTakeResponse
    {
        Taken = true,
        Listing = new AuctionListingDto
        {
            Id                = prior.AuctionId,
            SellerCharacterId = prior.SellerCharacterId,
            ItemTemplateId    = prior.ItemTemplateId,
            Quantity          = prior.Quantity,
            Price             = prior.Price
        }
    };
}

app.MapGet("/health", () => Results.Ok("auction-ok"));

// ── Browse ───────────────────────────────────────────────────────────

app.MapPost("/auctions/browse", async (HttpContext context, AuctionDbContext db) =>
{
    var request = await ReadAsync<W2PAuctionBrowseRequest>(context);
    if (request is null) { context.Response.StatusCode = StatusCodes.Status400BadRequest; return; }

    // Evaluate "now" here, in C#. EF can't translate DateTime.UtcNow.Ticks
    // inside a query, but it happily sends a plain long as a parameter.
    long nowTicks = DateTime.UtcNow.Ticks;

    var query = db.AuctionListings.AsNoTracking()
        .Where(a => a.ExpiresAtTicks > nowTicks);

    if (request.SellerCharacterId > 0)
        query = query.Where(a => a.SellerCharacterId == request.SellerCharacterId);

    if (!string.IsNullOrWhiteSpace(request.Search))
    {
        string search = request.Search.ToLowerInvariant();
        query = query.Where(a => a.ItemName.Contains(search));
    }

    var listings = await query.OrderBy(a => a.Price).ThenBy(a => a.Id).Take(maxResults).ToArrayAsync();
    await WriteAsync(context, new P2WAuctionBrowseResponse { Listings = listings.Select(ToDto).ToArray() });
});

// ── Create ───────────────────────────────────────────────────────────
//
// The world server has already taken the item out of the seller's bag by the
// time this is called. If this fails, that item exists nowhere else, which is
// why the world server mails it straight back on a false answer.

app.MapPost("/auctions/create", async (HttpContext context, AuctionDbContext db) =>
{
    var request = await ReadAsync<W2PAuctionCreateRequest>(context);
    if (request is null) { context.Response.StatusCode = StatusCodes.Status400BadRequest; return; }

    if (request.Quantity < 1 || request.Price < 1 || request.ItemTemplateId == 0)
    {
        await WriteAsync(context, new P2WAuctionCreateResponse { Created = false });
        return;
    }

    try
    {
        var row = new AuctionListing
        {
            SellerCharacterId = request.SellerCharacterId,
            SellerName        = request.SellerName ?? "",
            ItemTemplateId    = request.ItemTemplateId,
            Quantity          = request.Quantity,
            Price             = request.Price,
            ExpiresAtTicks    = request.ExpiresAtTicks > 0
                                ? request.ExpiresAtTicks
                                : DateTime.UtcNow.AddHours(listingHours).Ticks,
            ItemName          = (request.ItemName ?? "").ToLowerInvariant()
        };

        db.AuctionListings.Add(row);
        await db.SaveChangesAsync();

        await WriteAsync(context, new P2WAuctionCreateResponse { Created = true, AuctionId = row.Id });
    }
    catch (Exception ex)
    {
        Console.Error.WriteLine($"[Auction] Create failed for character {request.SellerCharacterId}: {ex.Message}");
        await WriteAsync(context, new P2WAuctionCreateResponse { Created = false });
    }
});

// ── Get: look, don't touch ───────────────────────────────────────────
//
// The world server has to know the price before it can charge a buyer, and
// it must charge BEFORE it takes the listing (see AuctionManager.BuyAsync).

app.MapPost("/auctions/get", async (HttpContext context, AuctionDbContext db) =>
{
    var request = await ReadAsync<W2PAuctionGetRequest>(context);
    if (request is null) { context.Response.StatusCode = StatusCodes.Status400BadRequest; return; }

    long nowTicks = DateTime.UtcNow.Ticks;

    var row = await db.AuctionListings.AsNoTracking()
        .FirstOrDefaultAsync(a => a.Id == request.AuctionId && a.ExpiresAtTicks > nowTicks);

    await WriteAsync(context, new P2WAuctionGetResponse { Found = row is not null, Listing = row is null ? null : ToDto(row) });
});

// ── Take: the endpoint everything depends on ─────────────────────────
//
// Deletes a listing and returns its contents in one transaction, so exactly
// one caller can ever win a given listing:
//
//   Taken = true    it's yours: the mail it owes has been QUEUED, nothing else to do
//   Taken = false   not taken - see Reason. Change nothing.
//
// Two callers, told apart by the request:
//
//   BUY      BuyerCharacterId + PurchaseKey set. The seller is queued their gold
//            (less the fee) and the buyer is queued the item, and the purchase
//            is recorded under its key - all in this transaction, so a sale
//            can't pay one side without the other. The world server has
//            ALREADY charged the buyer before calling this.
//   CANCEL   ExpectedSellerId set. Only if it's theirs; the item is queued home.
//   (expiry has its own endpoint below)
//
// BUYING IS SAFE TO REPEAT. If a purchase key is already recorded, the answer
// is "Taken = true" and nothing else happens. So when the world server never
// hears back, it just asks again with the same key and gets the truth.
//
// After the commit the mail dispatcher is nudged awake and given a moment
// (MailSignal), so the mail is normally in the mailbox before this answers.

app.MapPost("/auctions/take", async (HttpContext context, AuctionDbContext db, MailSignal signal) =>
{
    var request = await ReadAsync<W2PAuctionTakeRequest>(context);
    if (request is null) { context.Response.StatusCode = StatusCodes.Status400BadRequest; return; }

    bool isCancel = request.ExpectedSellerId != 0;
    string? key = string.IsNullOrWhiteSpace(request.PurchaseKey) ? null : request.PurchaseKey.Trim();

    if (!isCancel && (request.BuyerCharacterId <= 0 || key is null || key.Length > 64))
    {
        context.Response.StatusCode = StatusCodes.Status400BadRequest;
        return;
    }

    // Have we already done this exact purchase? Then say so, and do nothing.
    if (!isCancel)
    {
        var prior = await db.AuctionPurchases.AsNoTracking().FirstOrDefaultAsync(p => p.PurchaseKey == key);

        if (prior is not null)
        {
            await WriteAsync(context, PriorPurchaseAnswer(prior, request, signal));
            return;
        }
    }

    long nowTicks = DateTime.UtcNow.Ticks;

    await using var tx = await db.Database.BeginTransactionAsync();

    var row = await db.AuctionListings.FirstOrDefaultAsync(a => a.Id == request.AuctionId);

    string? refusal = null;

    if (row is null)
        refusal = "That's already gone.";
    else if (isCancel && row.SellerCharacterId != request.ExpectedSellerId)
        refusal = "That listing is no longer yours to cancel.";
    else if (!isCancel && row.SellerCharacterId == request.BuyerCharacterId)
        refusal = "That's your own listing - cancel it instead.";
    else if (!isCancel && row.ExpiresAtTicks <= nowTicks)
        refusal = "That listing has just expired.";

    if (refusal is not null)
    {
        await tx.RollbackAsync();
        await WriteAsync(context, new P2WAuctionTakeResponse { Taken = false, Reason = refusal });
        return;
    }

    string name = Pretty(row!.ItemName);
    db.AuctionListings.Remove(row);

    var queued = new List<MailOutbox>();

    if (isCancel)
    {
        // Cancelled: the item goes home by mail. Mailing it means a full bag
        // can't lose it, and it works whether or not the seller is online.
        queued.Add(NewMail(row.SellerCharacterId, $"Cancelled: {row.Quantity}x {name}",
                           0, row.ItemTemplateId, row.Quantity));
    }
    else
    {
        // Sold: the seller's gold and the buyer's item, queued together with
        // the delete. The buyer gets their purchase by mail, like everything else.
        int fee = row.Price * feePercent / 100;
        int payout = Math.Max(0, row.Price - fee);

        queued.Add(NewMail(row.SellerCharacterId, $"Sold: {row.Quantity}x {name}", payout, 0, 0));
        queued.Add(NewMail(request.BuyerCharacterId, $"Purchased: {row.Quantity}x {name}",
                           0, row.ItemTemplateId, row.Quantity));

        db.AuctionPurchases.Add(new AuctionPurchase
        {
            PurchaseKey       = key!,
            AuctionId         = row.Id,
            BuyerCharacterId  = request.BuyerCharacterId,
            SellerCharacterId = row.SellerCharacterId,
            ItemTemplateId    = row.ItemTemplateId,
            Quantity          = row.Quantity,
            Price             = row.Price,
            CreatedAtTicks    = nowTicks
        });
    }

    db.MailOutbox.AddRange(queued);

    try
    {
        await db.SaveChangesAsync();
    }
    catch (DbUpdateConcurrencyException)
    {
        // Two callers read the same row; the database let one delete it and
        // the other found nothing left to delete. That's the race working.
        await tx.RollbackAsync();
        await WriteAsync(context, new P2WAuctionTakeResponse { Taken = false, Reason = "That's already gone." });
        return;
    }
    catch (DbUpdateException) when (!isCancel)
    {
        // The purchase key was recorded by a duplicate request that got in a
        // moment ago. If so this purchase HAS happened - answer as such.
        await tx.RollbackAsync();
        db.ChangeTracker.Clear();

        var prior = await db.AuctionPurchases.AsNoTracking().FirstOrDefaultAsync(p => p.PurchaseKey == key);
        if (prior is null) throw;

        await WriteAsync(context, PriorPurchaseAnswer(prior, request, signal));
        return;
    }

    await tx.CommitAsync();

    // Get the mail to its owners now rather than on the worker's next lap.
    await signal.NudgeAsync(TimeSpan.FromSeconds(2));

    var ids = queued.Select(m => m.Id).ToList();
    bool stillQueued = await db.MailOutbox.AsNoTracking().AnyAsync(m => ids.Contains(m.Id));

    await WriteAsync(context, new P2WAuctionTakeResponse { Taken = true, Listing = ToDto(row), MailDelivered = !stillQueued });
});

// ── Expire ───────────────────────────────────────────────────────────
//
// Deleting the listing and queueing it home happen together, so an expiry
// can't half-happen either. An unsold listing comes back to its seller by mail.

app.MapPost("/auctions/expire", async (HttpContext context, AuctionDbContext db, MailSignal signal) =>
{
    var request = await ReadAsync<W2PAuctionExpireRequest>(context);
    if (request is null) { context.Response.StatusCode = StatusCodes.Status400BadRequest; return; }

    await using var tx = await db.Database.BeginTransactionAsync();

    var rows = await db.AuctionListings
        .Where(a => a.ExpiresAtTicks <= request.NowTicks)
        .OrderBy(a => a.ExpiresAtTicks)
        .Take(request.Limit > 0 ? request.Limit : 50)
        .ToListAsync();

    if (rows.Count > 0)
    {
        foreach (var row in rows)
            db.MailOutbox.Add(NewMail(row.SellerCharacterId, $"Unsold: {row.Quantity}x {Pretty(row.ItemName)}",
                                      0, row.ItemTemplateId, row.Quantity));

        db.AuctionListings.RemoveRange(rows);
        await db.SaveChangesAsync();
    }

    await tx.CommitAsync();

    if (rows.Count > 0)
        signal.Nudge();   // no one is waiting on an expiry, so no need to wait for it

    await WriteAsync(context, new P2WAuctionExpireResponse { Expired = rows.Select(ToDto).ToArray() });
});

app.Run();