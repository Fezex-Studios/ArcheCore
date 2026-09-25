using ArcheCore.Network.Shared;
using ArcheCore.Network.Shared.Packets.PersistenceServer;
using ArcheCore.Network.Shared.Packets.PersistenceServer.P2W;
using ArcheCore.Network.Shared.Packets.PersistenceServer.W2P;
using ArcheCore.PersistenceServer.Api.Data;
using ArcheCore.PersistenceServer.Api.Extensions;
using ArcheCore.PersistenceServer.Api.Models;
using Microsoft.EntityFrameworkCore;

var builder = WebApplication.CreateBuilder(args);

var connectionString = builder.Configuration.GetConnectionString("Persistence")
    ?? throw new InvalidOperationException("Missing 'Persistence' connection string.");

// ── Shared secret with the WorldServer ───────────────────────────────
var internalSecret = builder.Configuration["InternalSecret"];

if (string.IsNullOrWhiteSpace(internalSecret)
    || internalSecret == "replace_this_with_a_real_secret"
    || internalSecret.Length < 32)
{
    throw new InvalidOperationException(
        "InternalSecret is missing, still the placeholder, or shorter than 32 chars. " +
        "Set it in appsettings.json (or the InternalSecret environment variable) to a " +
        "random value of at least 32 characters, and set the SAME value in the " +
        "WorldServer's World:InternalSecret. Generate one with: openssl rand -base64 48");
}

var secretBytes = System.Text.Encoding.UTF8.GetBytes(internalSecret);

builder.Services.AddDbContext<PersistenceDbContext>(options =>
    options.UseMySql(connectionString, ServerVersion.AutoDetect(connectionString)));

var app = builder.Build();

// ── Gate EVERY route on the shared secret ────────────────────────────
app.Use(async (context, next) =>
{
    var presented = context.Request.Headers["x-internal-secret"].ToString();

    if (presented.Length == 0
        || !System.Security.Cryptography.CryptographicOperations.FixedTimeEquals(
               System.Text.Encoding.UTF8.GetBytes(presented), secretBytes))
    {
        context.Response.StatusCode = StatusCodes.Status404NotFound;
        Console.Error.WriteLine(
            $"[Persistence] Rejected {context.Request.Method} {context.Request.Path} " +
            $"from {context.Connection.RemoteIpAddress} — bad or missing x-internal-secret.");
        return;
    }

    await next();
});

// ── /connect ─────────────────────────────────────────────────────────
app.MapPost("/connect", async (HttpContext context) =>
{
    var request = await context.Request.ReadMsgPackAsync<W2PConnectionRequest>();

    if (request is null)
    {
        context.Response.StatusCode = StatusCodes.Status400BadRequest;
        return;
    }

    Console.WriteLine($"[Persistence] {request.Message}");

    await context.Response.WriteMsgPackAsync(new P2WConnectResponse
    {
        Message = "Connected to Persistence Server"
    });
});

// ── /hello-world ─────────────────────────────────────────────────────
app.MapPost("/hello-world", async (HttpContext context) =>
{
    var request = await context.Request.ReadMsgPackAsync<W2PHelloWorldPacket>();

    if (request is null)
    {
        context.Response.StatusCode = StatusCodes.Status400BadRequest;
        return;
    }

    Console.WriteLine($"[FROM WORLDSERVER] {request.Message}");
});

// ── /characters/create ───────────────────────────────────────────────
app.MapPost("/characters/create", async (HttpContext context, PersistenceDbContext db) =>
{
    var request = await context.Request.ReadMsgPackAsync<W2PCreateCharacterRequest>();

    if (request is null)
    {
        context.Response.StatusCode = StatusCodes.Status400BadRequest;
        return;
    }

    // H5: the same rules the world server and the client check, enforced
    // here too - this is the last door before the database.
    string name = request.Name?.Trim() ?? "";

    P2WCreateCharacterResponse Refuse(string reason) => new()
    {
        Success = false, AccountId = request.AccountId, CharacterId = 0, Name = "", Reason = reason
    };

    if (!CharacterNameRules.IsValid(name, out var invalidReason))
    {
        await context.Response.WriteMsgPackAsync(Refuse(invalidReason));
        return;
    }

    // Case-insensitive: the name column uses a _ci collation, so this
    // finds "bebpu" when asked for "Bebpu". The unique index below is what
    // actually guarantees it; this just gives the common case a nice answer.
    if (await db.Characters.AsNoTracking().AnyAsync(c => c.Name == name))
    {
        await context.Response.WriteMsgPackAsync(Refuse("That name is taken."));
        return;
    }

    try
    {
        var character = new Character
        {
            AccountId = request.AccountId,
            Name      = name,
            Level     = 1,
            PosX      = 0,
            PosY      = 2,
            PosZ      = 0
            // Gold intentionally left unset - column default (0) is the
            // single source of truth for starting balance. Inventory
            // rows: none created here either - an empty inventory is the
            // absence of rows, not a table full of zeroed placeholders.
        };

        db.Characters.Add(character);
        await db.SaveChangesAsync();

        Console.WriteLine($"[Persistence] Created '{name}' for AccountId={request.AccountId}");

        await context.Response.WriteMsgPackAsync(new P2WCreateCharacterResponse
        {
            Success     = true,
            AccountId   = request.AccountId,
            CharacterId = character.CharacterId,
            Name        = name,
            Reason      = ""
        });
    }
    catch (DbUpdateException e) when (e.InnerException is MySqlConnector.MySqlException { ErrorCode: MySqlConnector.MySqlErrorCode.DuplicateKeyEntry })
    {
        // Two creates of the same name raced past the check above; the
        // unique index let exactly one win.
        await context.Response.WriteMsgPackAsync(Refuse("That name is taken."));
    }
    catch (Exception e)
    {
        Console.Error.WriteLine($"[Persistence] Create failed: {e}");
        await context.Response.WriteMsgPackAsync(Refuse("Character creation failed. Try again."));
    }
});

// ── /characters/list ─────────────────────────────────────────────────
app.MapPost("/characters/list", async (HttpContext context, PersistenceDbContext db) =>
{
    var request = await context.Request.ReadMsgPackAsync<W2PCharacterListRequest>();

    if (request is null)
    {
        context.Response.StatusCode = StatusCodes.Status400BadRequest;
        return;
    }

    var characters = await db.Characters
        .AsNoTracking()
        .Where(c => c.AccountId == request.AccountId)
        .Select(c => new CharacterSummary
        {
            CharacterId = c.CharacterId,
            Name        = c.Name,
            Level       = c.Level
        })
        .ToArrayAsync();

    await context.Response.WriteMsgPackAsync(new P2WCharacterListResponse
    {
        AccountId  = request.AccountId,
        Characters = characters
    });
});

// ── /characters/load ─────────────────────────────────────────────────
app.MapPost("/characters/load", async (HttpContext context, PersistenceDbContext db) =>
{
    var request = await context.Request.ReadMsgPackAsync<W2PCharacterLoadRequest>();

    if (request is null)
    {
        context.Response.StatusCode = StatusCodes.Status400BadRequest;
        return;
    }

    var accountId = (int)request.AccountId;

    var query = db.Characters.AsNoTracking().Where(c => c.AccountId == accountId);

    if (request.CharacterId > 0)
        query = query.Where(c => c.CharacterId == request.CharacterId);

    var row = await query.FirstOrDefaultAsync();

    if (row is null)
    {
        await context.Response.WriteMsgPackAsync(new P2WCharacterLoadResponse
        {
            Found       = false,
            AccountId   = accountId,
            CharacterId = 0,
            Name        = "",
            Level       = 0,
            X           = 0,
            Y           = 0,
            Z           = 0,
            Gold        = 0,
            Inventory   = Array.Empty<InventorySlotDto>(),
            Quests      = Array.Empty<QuestStateDto>()
        });
        return;
    }

    // Occupied slots only - a fresh or empty inventory is an empty array,
    // not 20 zeroed rows. PlayerSpawnManager reconstructs the dense
    // in-memory array on the WorldServer side.
    var inventory = await db.InventoryItems
        .AsNoTracking()
        .Where(i => i.CharacterId == row.CharacterId)
        .Select(i => new InventorySlotDto
        {
            Slot           = i.Slot,
            ItemTemplateId = i.ItemTemplateId,
            Quantity       = i.Quantity
        })
        .ToArrayAsync();

    var quests = await db.CharacterQuests
        .AsNoTracking()
        .Where(q => q.CharacterId == row.CharacterId)
        .Select(q => new QuestStateDto
        {
            QuestId  = q.QuestId,
            Status   = q.Status,
            Progress = q.Progress
        })
        .ToArrayAsync();

    await context.Response.WriteMsgPackAsync(new P2WCharacterLoadResponse
    {
        Found       = true,
        AccountId   = accountId,
        CharacterId = row.CharacterId,
        Name        = row.Name,
        Level       = row.Level,
        X           = row.PosX,
        Y           = row.PosY,
        Z           = row.PosZ,
        Gold        = row.Gold,
        SaveSeq     = row.SaveSeq,
        Inventory   = inventory,
        Quests      = quests
    });
});

// ── /characters/save ─────────────────────────────────────────────────
app.MapPost("/characters/save", async (HttpContext context, PersistenceDbContext db) =>
{
    var request = await context.Request.ReadMsgPackAsync<W2PCharacterSaveRequest>();

    if (request is null)
    {
        context.Response.StatusCode = StatusCodes.Status400BadRequest;
        return;
    }

    try
    {
        var rows = await db.Database.ExecuteSqlInterpolatedAsync($"""
            UPDATE characters
               SET name  = {request.Name},
                   level = {request.Level},
                   pos_x = {request.X},
                   pos_y = {request.Y},
                   pos_z = {request.Z},
                   gold  = {request.Gold}
             WHERE character_id = {request.CharacterId}
               AND account_id   = {request.AccountId}
            """);

        if (rows == 0)
        {
            Console.Error.WriteLine(
                $"[Persistence] Save affected 0 rows: CharacterId={request.CharacterId} " +
                $"is missing or not owned by AccountId={request.AccountId}.");
            context.Response.StatusCode = StatusCodes.Status409Conflict;
            return;
        }

        Console.WriteLine($"Saved {request.Name}");
    }
    catch (Exception e)
    {
        Console.Error.WriteLine($"[Persistence] Save failed for CharacterId={request.CharacterId}: {e}");
        context.Response.StatusCode = StatusCodes.Status500InternalServerError;
    }
});

// ── /characters/save-full ────────────────────────────────────────────
//
// THE character save. Level, position, gold, the whole inventory and the
// whole quest log in ONE transaction, so a save either lands completely or
// not at all - the old three separate requests could land half-applied,
// and in any order.
//
// Ordering: the characters row is locked (SELECT ... FOR UPDATE) and the
// save is only written if its SaveSeq is higher than the stored save_seq.
// A save that arrives late, or twice (a retry after a lost answer), is
// answered Stale and changes nothing. That is what lets the world server
// retry a save whose answer it never got without ever writing old data
// over new.
//
// ClaimMailId deletes one mail row in the same transaction: the mail goes
// away exactly when the character holding its contents is saved.
//
// Answers:
//   200 + Saved / Stale / NotFound / MailGone  - definite, nothing to retry
//   200 + Invalid                               - definite, the request is wrong
//   500                                         - rolled back, safe to retry
app.MapPost("/characters/save-full", async (HttpContext context, PersistenceDbContext db) =>
{
    var request = await context.Request.ReadMsgPackAsync<W2PCharacterSaveFullRequest>();

    if (request is null)
    {
        context.Response.StatusCode = StatusCodes.Status400BadRequest;
        return;
    }

    // Validate before touching the database. A malformed save will never
    // succeed, so it must be answered definitively rather than with a 500
    // the world server would keep retrying.
    string? invalid = ValidateFullSave(request);
    if (invalid is not null)
    {
        Console.Error.WriteLine($"[Persistence] save-full REJECTED CharacterId={request.CharacterId}: {invalid}");
        await context.Response.WriteMsgPackAsync(new P2WCharacterSaveFullResponse
        {
            Result = CharacterSaveResult.Invalid
        });
        return;
    }

    P2WCharacterSaveFullResponse answer;

    try
    {
        await using var tx = await db.Database.BeginTransactionAsync();

        var seqs = await db.Database.SqlQuery<long>($"""
            SELECT save_seq AS Value
              FROM characters
             WHERE character_id = {request.CharacterId}
               AND account_id   = {request.AccountId}
             FOR UPDATE
            """).ToListAsync();

        if (seqs.Count == 0)
        {
            await tx.RollbackAsync();
            answer = new P2WCharacterSaveFullResponse { Result = CharacterSaveResult.NotFound };
        }
        else if (seqs[0] >= request.SaveSeq)
        {
            await tx.RollbackAsync();
            answer = new P2WCharacterSaveFullResponse { Result = CharacterSaveResult.Stale, CurrentSeq = seqs[0] };
        }
        else
        {
            bool mailGone = false;

            if (request.ClaimMailId > 0)
            {
                var deleted = await db.Database.ExecuteSqlInterpolatedAsync($"""
                    DELETE FROM mail
                     WHERE id           = {request.ClaimMailId}
                       AND character_id = {request.CharacterId}
                    """);

                mailGone = deleted == 0;
            }

            if (mailGone)
            {
                await tx.RollbackAsync();
                answer = new P2WCharacterSaveFullResponse { Result = CharacterSaveResult.MailGone, CurrentSeq = seqs[0] };
            }
            else
            {
                await db.Database.ExecuteSqlInterpolatedAsync($"""
                    UPDATE characters
                       SET level    = {request.Level},
                           pos_x    = {request.X},
                           pos_y    = {request.Y},
                           pos_z    = {request.Z},
                           gold     = {request.Gold},
                           save_seq = {request.SaveSeq}
                     WHERE character_id = {request.CharacterId}
                    """);

                // The whole inventory, replaced. 20 rows at most, so a
                // delete-and-insert is simpler than a diff and can't drift.
                await db.Database.ExecuteSqlInterpolatedAsync($"""
                    DELETE FROM character_inventory WHERE character_id = {request.CharacterId}
                    """);

                foreach (var slot in request.Inventory!)
                {
                    await db.Database.ExecuteSqlInterpolatedAsync($"""
                        INSERT INTO character_inventory (character_id, slot, item_template_id, quantity)
                        VALUES ({request.CharacterId}, {slot.Slot}, {slot.ItemTemplateId}, {slot.Quantity})
                        """);
                }

                if (request.Quests is not null)
                {
                    // Also replaced whole - which is what makes an abandoned
                    // quest (no longer in the log) actually go away.
                    await db.Database.ExecuteSqlInterpolatedAsync($"""
                        DELETE FROM character_quests WHERE character_id = {request.CharacterId}
                        """);

                    foreach (var quest in request.Quests)
                    {
                        if (quest.Status == 0)
                            continue;

                        await db.Database.ExecuteSqlInterpolatedAsync($"""
                            INSERT INTO character_quests (character_id, quest_id, status, progress)
                            VALUES ({request.CharacterId}, {quest.QuestId}, {quest.Status}, {quest.Progress ?? string.Empty})
                            """);
                    }
                }

                await tx.CommitAsync();
                answer = new P2WCharacterSaveFullResponse { Result = CharacterSaveResult.Saved, CurrentSeq = request.SaveSeq };
            }
        }
    }
    catch (Exception e)
    {
        // Nothing was committed (CommitAsync is the last thing in the try),
        // so the world server may safely send the same request again.
        Console.Error.WriteLine($"[Persistence] save-full failed for CharacterId={request.CharacterId}: {e.Message}");
        context.Response.StatusCode = StatusCodes.Status500InternalServerError;
        return;
    }

    await context.Response.WriteMsgPackAsync(answer);
});

// ── /characters/inventory/save ───────────────────────────────────────
// A per-slot diff, not a full snapshot - see W2PInventorySaveRequest.
// Each entry either upserts a row (item present) or deletes it (slot
// cleared: ItemTemplateId or Quantity <= 0). Wrapped in one transaction
// so a batch of several slot changes either all land or none do - a
// swap is two entries (both source and destination slots) and should
// never be observed half-applied.
app.MapPost("/characters/inventory/save", async (HttpContext context, PersistenceDbContext db) =>
{
    var request = await context.Request.ReadMsgPackAsync<W2PInventorySaveRequest>();

    if (request is null)
    {
        context.Response.StatusCode = StatusCodes.Status400BadRequest;
        return;
    }

    if (request.Changes is null || request.Changes.Length == 0)
    {
        // Nothing to do - still a success, not an error. WorldServer
        // only calls this route when it has a non-empty diff, but a
        // defensive no-op here is cheap and keeps this endpoint correct
        // even if that ever changes.
        return;
    }

    try
    {
        // Inventory rows carry no account_id of their own, so ownership
        // is checked up front here, once, the same way /characters/save
        // scopes its UPDATE to account_id - just as an explicit check
        // instead of a WHERE clause, since this is a mix of deletes and
        // upserts rather than one statement.
        var owns = await db.Characters
            .AsNoTracking()
            .AnyAsync(c => c.CharacterId == request.CharacterId && c.AccountId == request.AccountId);

        if (!owns)
        {
            Console.Error.WriteLine(
                $"[Persistence] Inventory save rejected: CharacterId={request.CharacterId} " +
                $"not owned by AccountId={request.AccountId}.");
            context.Response.StatusCode = StatusCodes.Status409Conflict;
            return;
        }

        await using var tx = await db.Database.BeginTransactionAsync();

        foreach (var change in request.Changes)
        {
            if (change.ItemTemplateId <= 0 || change.Quantity <= 0)
            {
                // Slot cleared - delete, don't store a zeroed row. A
                // delete for a slot that was already empty (never had a
                // row) affects 0 rows and is not an error - the end
                // state (no row) is exactly what was asked for.
                await db.Database.ExecuteSqlInterpolatedAsync($"""
                    DELETE FROM character_inventory
                     WHERE character_id = {request.CharacterId}
                       AND slot         = {change.Slot}
                    """);
            }
            else
            {
                await db.Database.ExecuteSqlInterpolatedAsync($"""
                    INSERT INTO character_inventory (character_id, slot, item_template_id, quantity)
                    VALUES ({request.CharacterId}, {change.Slot}, {change.ItemTemplateId}, {change.Quantity})
                    ON DUPLICATE KEY UPDATE
                        item_template_id = VALUES(item_template_id),
                        quantity         = VALUES(quantity)
                    """);
            }
        }

        await tx.CommitAsync();

        Console.WriteLine(
            $"[Persistence] CharacterId={request.CharacterId} inventory: " +
            $"{request.Changes.Length} slot(s) applied.");
    }
    catch (Exception e)
    {
        // A CK_character_inventory_item_positive violation lands here
        // too - it means a change with ItemTemplateId/Quantity > 0 was
        // sent for a slot that should have gone through the DELETE
        // branch instead, i.e. TryAddItem/TryMoveItem produced a bad
        // value. Worth grepping this log for "inventory" specifically if
        // that ever happens, since it means the WorldServer-side guard
        // has a bug, not this endpoint.
        Console.Error.WriteLine(
            $"[Persistence] Inventory save failed for CharacterId={request.CharacterId}: {e}");
        context.Response.StatusCode = StatusCodes.Status500InternalServerError;
    }
});

// ── /characters/quests/save ──────────────────────────────────────────
//
// Upsert the quests that changed for one character. Mirrors
// /characters/inventory/save: ownership checked once up front, then one
// transaction, and only what WorldServer says changed.
//
// Unlike inventory there are no deletes - a quest never stops having
// happened. Abandoning drops it back to "not started", which is a status
// of 0 and means the row goes away.
app.MapPost("/characters/quests/save", async (HttpContext context, PersistenceDbContext db) =>
{
    var request = await context.Request.ReadMsgPackAsync<W2PQuestSaveRequest>();

    if (request is null)
    {
        context.Response.StatusCode = StatusCodes.Status400BadRequest;
        return;
    }

    if (request.Quests is null || request.Quests.Length == 0)
        return; // nothing changed - a success, not an error

    try
    {
        var owns = await db.Characters
            .AsNoTracking()
            .AnyAsync(c => c.CharacterId == request.CharacterId && c.AccountId == request.AccountId);

        if (!owns)
        {
            Console.Error.WriteLine(
                $"[Persistence] Quest save rejected: CharacterId={request.CharacterId} " +
                $"not owned by AccountId={request.AccountId}.");
            context.Response.StatusCode = StatusCodes.Status409Conflict;
            return;
        }

        await using var tx = await db.Database.BeginTransactionAsync();

        foreach (var change in request.Quests)
        {
            var existing = await db.CharacterQuests
                .FirstOrDefaultAsync(q => q.CharacterId == request.CharacterId && q.QuestId == change.QuestId);

            if (change.Status == 0)
            {
                // Abandoned: forget it entirely, so it can be taken again.
                if (existing != null)
                    db.CharacterQuests.Remove(existing);

                continue;
            }

            if (existing is null)
            {
                db.CharacterQuests.Add(new CharacterQuest
                {
                    CharacterId = request.CharacterId,
                    QuestId     = change.QuestId,
                    Status      = change.Status,
                    Progress    = change.Progress ?? string.Empty
                });
            }
            else
            {
                existing.Status   = change.Status;
                existing.Progress = change.Progress ?? string.Empty;
            }
        }

        await db.SaveChangesAsync();
        await tx.CommitAsync();
    }
    catch (Exception ex)
    {
        Console.Error.WriteLine($"[Persistence] Quest save failed for CharacterId={request.CharacterId}: {ex.Message}");
        context.Response.StatusCode = StatusCodes.Status500InternalServerError;
    }
});

// ── Cash shop ────────────────────────────────────────────────────────
//
// The catalogue and the credit balances live in THIS database, next to the
// mailbox the goods are delivered to, which is what lets a purchase be one
// transaction: check the balance, deduct it, post the mail. There is no
// moment where an account has been charged and nothing was sent.
//
// Credits are per ACCOUNT (one purse for all your characters) and have
// nothing to do with gold. Delivery is to the CHARACTER who bought it.

app.MapPost("/cashshop/catalog", async (HttpContext context, PersistenceDbContext db) =>
{
    var request = await context.Request.ReadMsgPackAsync<W2PCashShopCatalogRequest>();
    if (request is null) { context.Response.StatusCode = StatusCodes.Status400BadRequest; return; }

    var items = await db.CashShopItems.AsNoTracking()
        .Where(i => i.IsEnabled)
        .OrderBy(i => i.SortOrder).ThenBy(i => i.Id)
        .Select(i => new CashShopItemDto
        {
            Id = i.Id, DisplayName = i.DisplayName, Category = i.Category,
            ItemTemplateId = i.ItemTemplateId, Quantity = i.Quantity,
            PriceCredits = i.PriceCredits, SortOrder = i.SortOrder,
            IsGiftable = i.IsGiftable
        })
        .ToArrayAsync();

    var purse = await db.AccountCredits.AsNoTracking().FirstOrDefaultAsync(c => c.AccountId == request.AccountId);

    await context.Response.WriteMsgPackAsync(new P2WCashShopCatalogResponse
    {
        Items = items,
        Balance = purse?.Balance ?? 0
    });
});

app.MapPost("/cashshop/buy", async (HttpContext context, PersistenceDbContext db) =>
{
    var request = await context.Request.ReadMsgPackAsync<W2PCashShopBuyRequest>();
    if (request is null) { context.Response.StatusCode = StatusCodes.Status400BadRequest; return; }

    // The character must belong to the account paying for it.
    var owns = await db.Characters.AsNoTracking()
        .AnyAsync(c => c.CharacterId == request.CharacterId && c.AccountId == request.AccountId);

    if (!owns)
    {
        Console.Error.WriteLine(
            $"[Persistence] Cash shop purchase rejected: CharacterId={request.CharacterId} " +
            $"not owned by AccountId={request.AccountId}.");
        await context.Response.WriteMsgPackAsync(new P2WCashShopBuyResponse { Bought = false, Reason = "That character isn't yours." });
        return;
    }

    await using var tx = await db.Database.BeginTransactionAsync();

    var item = await db.CashShopItems.AsNoTracking()
        .FirstOrDefaultAsync(i => i.Id == request.CashShopItemId && i.IsEnabled);

    if (item is null)
    {
        await tx.RollbackAsync();
        await context.Response.WriteMsgPackAsync(new P2WCashShopBuyResponse { Bought = false, Reason = "That isn't for sale." });
        return;
    }

    var purse = await db.AccountCredits.FirstOrDefaultAsync(c => c.AccountId == request.AccountId);
    int balance = purse?.Balance ?? 0;

    if (balance < item.PriceCredits)
    {
        await tx.RollbackAsync();
        await context.Response.WriteMsgPackAsync(new P2WCashShopBuyResponse
        {
            Bought = false,
            Reason = $"That costs {item.PriceCredits} credits and you have {balance}.",
            Balance = balance
        });
        return;
    }

    if (purse is null)
    {
        purse = new AccountCredits { AccountId = request.AccountId, Balance = 0 };
        db.AccountCredits.Add(purse);
    }

    // Charge and deliver together. Either both happen or neither does.
    purse.Balance = balance - item.PriceCredits;
    purse.UpdatedAtTicks = DateTime.UtcNow.Ticks;

    db.Mail.Add(new Mail
    {
        CharacterId    = request.CharacterId,
        Sender         = "Cash Shop",
        Subject        = Clip($"Purchase: {item.DisplayName}", 128),
        Gold           = 0,
        ItemTemplateId = item.ItemTemplateId,
        ItemQuantity   = item.Quantity,
        CreatedAtTicks = DateTime.UtcNow.Ticks
    });

    await db.SaveChangesAsync();
    await tx.CommitAsync();

    Console.WriteLine($"[Persistence] Account {request.AccountId} bought '{item.DisplayName}' " +
                      $"for {item.PriceCredits} credits; {purse.Balance} left.");

    await context.Response.WriteMsgPackAsync(new P2WCashShopBuyResponse
    {
        Bought = true,
        Balance = purse.Balance,
        DeliveredName = item.DisplayName,
        DeliveredQuantity = item.Quantity
    });
});

// Mail subjects are capped at 128 characters; a long item name mustn't turn a paid purchase into an error.
static string Clip(string text, int max) => text.Length <= max ? text : text[..max];

// ── Gifting ──────────────────────────────────────────────────────────
//
// Buy a cash shop item FOR ANOTHER CHARACTER. Same shape as a purchase - check
// the balance, deduct it, post the mail, all in one transaction - except that
// the account charged is the SENDER's and the mail goes to the RECIPIENT's
// mailbox, from the sender's name.
//
// Only items whose is_giftable flag is set can be sent. That's checked here,
// not just by hiding the button: the button is a courtesy, this is the rule.

app.MapPost("/cashshop/gift", async (HttpContext context, PersistenceDbContext db) =>
{
    var request = await context.Request.ReadMsgPackAsync<W2PCashShopGiftRequest>();
    if (request is null) { context.Response.StatusCode = StatusCodes.Status400BadRequest; return; }

    static P2WCashShopBuyResponse No(string reason, int balance = 0) => new() { Bought = false, Reason = reason, Balance = balance };

    string recipientName = (request.RecipientName ?? "").Trim();

    if (recipientName.Length == 0 || recipientName.Length > 64)
    {
        await context.Response.WriteMsgPackAsync(No("Type the name of the character to send it to."));
        return;
    }

    // The sender must be a character on the account that's paying.
    var sender = await db.Characters.AsNoTracking()
        .FirstOrDefaultAsync(c => c.CharacterId == request.CharacterId && c.AccountId == request.AccountId);

    if (sender is null)
    {
        Console.Error.WriteLine(
            $"[Persistence] Cash shop gift rejected: CharacterId={request.CharacterId} " +
            $"not owned by AccountId={request.AccountId}.");
        await context.Response.WriteMsgPackAsync(No("That character isn't yours."));
        return;
    }

    await using var tx = await db.Database.BeginTransactionAsync();

    var item = await db.CashShopItems.AsNoTracking()
        .FirstOrDefaultAsync(i => i.Id == request.CashShopItemId && i.IsEnabled);

    if (item is null)
    {
        await tx.RollbackAsync();
        await context.Response.WriteMsgPackAsync(No("That isn't for sale."));
        return;
    }

    if (!item.IsGiftable)
    {
        await tx.RollbackAsync();
        await context.Response.WriteMsgPackAsync(No("That can't be given as a gift."));
        return;
    }

    var recipient = await db.Characters.AsNoTracking().FirstOrDefaultAsync(c => c.Name == recipientName);

    if (recipient is null)
    {
        await tx.RollbackAsync();
        await context.Response.WriteMsgPackAsync(No($"There's no character named {recipientName}."));
        return;
    }

    if (recipient.CharacterId == sender.CharacterId)
    {
        await tx.RollbackAsync();
        await context.Response.WriteMsgPackAsync(No("That's you - just buy it for yourself."));
        return;
    }

    var purse = await db.AccountCredits.FirstOrDefaultAsync(c => c.AccountId == request.AccountId);
    int balance = purse?.Balance ?? 0;

    if (balance < item.PriceCredits)
    {
        await tx.RollbackAsync();
        await context.Response.WriteMsgPackAsync(No($"That costs {item.PriceCredits} credits and you have {balance}.", balance));
        return;
    }

    if (purse is null)
    {
        purse = new AccountCredits { AccountId = request.AccountId, Balance = 0 };
        db.AccountCredits.Add(purse);
    }

    // Charge the sender and deliver to the recipient together. Either both happen or neither does.
    purse.Balance = balance - item.PriceCredits;
    purse.UpdatedAtTicks = DateTime.UtcNow.Ticks;

    db.Mail.Add(new Mail
    {
        CharacterId    = recipient.CharacterId,
        Sender         = Clip(string.IsNullOrWhiteSpace(sender.Name) ? "Someone" : sender.Name, 64),
        Subject        = Clip($"Gift: {item.DisplayName}", 128),
        Gold           = 0,
        ItemTemplateId = item.ItemTemplateId,
        ItemQuantity   = item.Quantity,
        CreatedAtTicks = DateTime.UtcNow.Ticks
    });

    await db.SaveChangesAsync();
    await tx.CommitAsync();

    Console.WriteLine($"[Persistence] Account {request.AccountId} (character {sender.CharacterId}) gifted " +
                      $"'{item.DisplayName}' to character {recipient.CharacterId} ({recipient.Name}) " +
                      $"for {item.PriceCredits} credits; {purse.Balance} left.");

    await context.Response.WriteMsgPackAsync(new P2WCashShopBuyResponse
    {
        Bought = true,
        Balance = purse.Balance,
        DeliveredName = item.DisplayName,
        DeliveredQuantity = item.Quantity,
        RecipientName = recipient.Name
    });
});

// ── The mailbox ──────────────────────────────────────────────────────
//
// ONE general mailbox for every character, shared by everything that gives a
// player something: the auction house, the cash shop, refunds, and admin
// gifts (see SQL/admin_mail_examples.sql).

static MailDto ToMailDto(Mail m) => new()
{
    Id = m.Id, CharacterId = m.CharacterId, Sender = m.Sender, Subject = m.Subject,
    Gold = m.Gold, ItemTemplateId = m.ItemTemplateId, ItemQuantity = m.ItemQuantity,
    CreatedAtTicks = m.CreatedAtTicks
};

app.MapPost("/mail/list", async (HttpContext context, PersistenceDbContext db) =>
{
    var request = await context.Request.ReadMsgPackAsync<W2PMailListRequest>();
    if (request is null) { context.Response.StatusCode = StatusCodes.Status400BadRequest; return; }

    var owns = await db.Characters.AsNoTracking()
        .AnyAsync(c => c.CharacterId == request.CharacterId && c.AccountId == request.AccountId);

    if (!owns)
    {
        context.Response.StatusCode = StatusCodes.Status409Conflict;
        return;
    }

    var rows = await db.Mail.AsNoTracking()
        .Where(m => m.CharacterId == request.CharacterId)
        .OrderBy(m => m.CreatedAtTicks)
        .ThenBy(m => m.Id)
        .ToArrayAsync();

    await context.Response.WriteMsgPackAsync(new P2WMailListResponse { Mail = rows.Select(ToMailDto).ToArray() });
});

// Deletes AND returns in one transaction - two clicks can't claim it twice.
app.MapPost("/mail/claim", async (HttpContext context, PersistenceDbContext db) =>
{
    var request = await context.Request.ReadMsgPackAsync<W2PMailClaimRequest>();
    if (request is null) { context.Response.StatusCode = StatusCodes.Status400BadRequest; return; }

    var owns = await db.Characters.AsNoTracking()
        .AnyAsync(c => c.CharacterId == request.CharacterId && c.AccountId == request.AccountId);

    if (!owns)
    {
        context.Response.StatusCode = StatusCodes.Status409Conflict;
        return;
    }

    await using var tx = await db.Database.BeginTransactionAsync();

    var row = await db.Mail.FirstOrDefaultAsync(m => m.Id == request.MailId && m.CharacterId == request.CharacterId);

    if (row is null)
    {
        await tx.RollbackAsync();
        await context.Response.WriteMsgPackAsync(new P2WMailClaimResponse { Claimed = false });
        return;
    }

    db.Mail.Remove(row);

    try
    {
        await db.SaveChangesAsync();
    }
    catch (DbUpdateConcurrencyException)
    {
        // A second click got here at the same moment and deleted it first.
        await tx.RollbackAsync();
        await context.Response.WriteMsgPackAsync(new P2WMailClaimResponse { Claimed = false });
        return;
    }

    await tx.CommitAsync();

    await context.Response.WriteMsgPackAsync(new P2WMailClaimResponse { Claimed = true, Mail = ToMailDto(row) });
});

// Post something to a character. Called by the world server (refunds, a claim
// it couldn't deliver) and by the auction service's outbox.
//
// A DeliveryKey makes it safe to repeat: the first send posts the mail and
// records a receipt IN THE SAME TRANSACTION; any later send with that key is
// answered "sent" without posting again.
app.MapPost("/mail/send", async (HttpContext context, PersistenceDbContext db) =>
{
    var request = await context.Request.ReadMsgPackAsync<W2PMailSendRequest>();
    if (request is null) { context.Response.StatusCode = StatusCodes.Status400BadRequest; return; }

    if (request.Gold < 0 || request.ItemQuantity < 0 || (request.ItemTemplateId != 0 && request.ItemQuantity < 1))
    {
        await context.Response.WriteMsgPackAsync(new P2WMailSendResponse { Sent = false, Reason = "Invalid mail contents." });
        return;
    }

    var exists = await db.Characters.AsNoTracking().AnyAsync(c => c.CharacterId == request.CharacterId);

    if (!exists)
    {
        await context.Response.WriteMsgPackAsync(new P2WMailSendResponse { Sent = false, Reason = "No such character." });
        return;
    }

    string? key = string.IsNullOrWhiteSpace(request.DeliveryKey) ? null : request.DeliveryKey.Trim();

    if (key is not null && key.Length > 64)
    {
        await context.Response.WriteMsgPackAsync(new P2WMailSendResponse { Sent = false, Reason = "Delivery key too long." });
        return;
    }

    if (key is not null && await db.MailReceipts.AsNoTracking().AnyAsync(r => r.DeliveryKey == key))
    {
        await context.Response.WriteMsgPackAsync(new P2WMailSendResponse { Sent = true });
        return;
    }

    long now = DateTime.UtcNow.Ticks;

    db.Mail.Add(new Mail
    {
        CharacterId    = request.CharacterId,
        Sender         = string.IsNullOrWhiteSpace(request.Sender) ? "System" : request.Sender,
        Subject        = request.Subject ?? "",
        Gold           = request.Gold,
        ItemTemplateId = request.ItemTemplateId,
        ItemQuantity   = request.ItemQuantity,
        CreatedAtTicks = now
    });

    if (key is not null)
        db.MailReceipts.Add(new MailReceipt { DeliveryKey = key, CreatedAtTicks = now });

    try
    {
        // One SaveChanges is one transaction: the mail and its receipt land together or not at all.
        await db.SaveChangesAsync();
    }
    catch (DbUpdateException) when (key is not null)
    {
        // Two sends of the same key raced and the primary key let one win.
        // If the receipt exists now, that's a success, not an error.
        db.ChangeTracker.Clear();

        if (await db.MailReceipts.AsNoTracking().AnyAsync(r => r.DeliveryKey == key))
        {
            await context.Response.WriteMsgPackAsync(new P2WMailSendResponse { Sent = true });
            return;
        }

        throw;
    }

    await context.Response.WriteMsgPackAsync(new P2WMailSendResponse { Sent = true });
});

app.Run();

// ── helpers ──────────────────────────────────────────────────────────

static string? ValidateFullSave(W2PCharacterSaveFullRequest r)
{
    if (r.CharacterId <= 0) return "no CharacterId";
    if (r.SaveSeq <= 0) return "SaveSeq must be positive";
    if (r.Gold < 0) return $"negative gold {r.Gold}";
    if (r.Level < 1) return $"level {r.Level}";
    if (float.IsNaN(r.X) || float.IsNaN(r.Y) || float.IsNaN(r.Z) ||
        float.IsInfinity(r.X) || float.IsInfinity(r.Y) || float.IsInfinity(r.Z))
        return "position is not a number";
    if (r.Inventory is null) return "Inventory is null (send an empty array for an empty bag)";
    if (r.Inventory.Length > 256) return $"{r.Inventory.Length} inventory rows";

    var slots = new HashSet<int>();
    foreach (var s in r.Inventory)
    {
        if (s is null) return "null inventory row";
        if (s.Slot < 0 || s.Slot > 255) return $"slot {s.Slot}";
        if (s.ItemTemplateId <= 0 || s.Quantity <= 0) return $"slot {s.Slot} holds item {s.ItemTemplateId} x{s.Quantity}";
        if (!slots.Add(s.Slot)) return $"slot {s.Slot} listed twice";
    }

    if (r.Quests is not null)
    {
        var ids = new HashSet<int>();
        foreach (var q in r.Quests)
        {
            if (q is null) return "null quest row";
            if (!ids.Add(q.QuestId)) return $"quest {q.QuestId} listed twice";
            if ((q.Progress ?? "").Length > 128) return $"quest {q.QuestId} progress longer than 128";
        }
    }

    return null;
}

