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

    try
    {
        var character = new Character
        {
            AccountId = request.AccountId,
            Name      = request.Name,
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

        Console.WriteLine($"[Persistence] Created '{request.Name}' for AccountId={request.AccountId}");

        await context.Response.WriteMsgPackAsync(new P2WCreateCharacterResponse
        {
            Success     = true,
            AccountId   = request.AccountId,
            CharacterId = character.CharacterId,
            Name        = request.Name
        });
    }
    catch (Exception e)
    {
        Console.Error.WriteLine($"[Persistence] Create failed: {e}");

        await context.Response.WriteMsgPackAsync(new P2WCreateCharacterResponse
        {
            Success     = false,
            AccountId   = request.AccountId,
            CharacterId = 0,
            Name        = ""
        });
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
            Inventory   = Array.Empty<InventorySlotDto>()
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
        Inventory   = inventory
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

app.Run();
