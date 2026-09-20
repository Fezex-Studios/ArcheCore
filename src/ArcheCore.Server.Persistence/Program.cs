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
// Every route below is privileged: they read and write character rows
// for arbitrary account ids, with no user-level identity anywhere in the
// protocol. The WorldServer is trusted to have already validated the
// player's session before it calls here, which means the ONLY thing
// standing between an attacker and "create/load/save any character on
// the shard" is proof that the caller really is the WorldServer.
//
// Refuse to start without it. A persistence server that boots with an
// empty or placeholder secret is worse than one that doesn't boot,
// because it looks like it's working.
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
// Placed before any MapPost so a route added later is protected by
// default rather than by remembering to protect it. Fixed-time compare
// so the failure can't be turned into a byte-at-a-time oracle.
app.Use(async (context, next) =>
{
    var presented = context.Request.Headers["x-internal-secret"].ToString();

    if (presented.Length == 0
        || !System.Security.Cryptography.CryptographicOperations.FixedTimeEquals(
               System.Text.Encoding.UTF8.GetBytes(presented), secretBytes))
    {
        // 404, not 401: an unauthenticated caller shouldn't be able to
        // confirm this service exists or enumerate which routes it has.
        context.Response.StatusCode = StatusCodes.Status404NotFound;
        Console.Error.WriteLine(
            $"[Persistence] Rejected {context.Request.Method} {context.Request.Path} " +
            $"from {context.Connection.RemoteIpAddress} — bad or missing x-internal-secret.");
        return;
    }

    await next();
});

// ── /connect ─────────────────────────────────────────────────────────
// Original just logged the message and sent back a fixed confirmation.
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
// Original never sent a response back — this keeps that: 200/empty on
// success, 400 only if the body itself was unreadable.
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
            // Gold intentionally left unset here — the column default (0)
            // is the single source of truth for starting balance. If you
            // ever want a non-zero starting balance, set it here
            // explicitly rather than changing the column default, so the
            // decision is visible in the code that creates characters,
            // not buried in a migration.
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
            // Gold is NOT on the character-select summary on purpose — the
            // list screen shows who you are, not what you're worth. Add it
            // here (and to CharacterSummary itself) only if the launcher's
            // character-select UI ends up wanting to display it.
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

    // W2PCharacterLoadRequest.AccountId is `long` in the shared protocol,
    // but the characters table (and every other W2P request) uses `int` —
    // an existing mismatch in the protocol, not something this port added.
    var accountId = (int)request.AccountId;

    var query = db.Characters.AsNoTracking().Where(c => c.AccountId == accountId);

    // Same branch as the original: a specific character if CharacterId was
    // given, otherwise whichever character exists for the account.
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
            Gold        = 0
        });
        return;
    }

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
        Gold        = row.Gold
    });
});

// ── /characters/save ─────────────────────────────────────────────────
// Original was `INSERT OR REPLACE` (a true upsert on an explicit id), not
// update-only — ON DUPLICATE KEY UPDATE is the MySQL equivalent, in one
// round trip, without going through EF's change tracker.
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
        // UPDATE, not upsert, and scoped to the owning account.
        //
        // The previous version was INSERT ... ON DUPLICATE KEY UPDATE with
        // `account_id = VALUES(account_id)` in the update list, which meant
        // a save could REASSIGN a character to a different account. Rows
        // are only ever created by /characters/create, so the insert half
        // was never needed — and `WHERE account_id` turns a mismatched
        // AccountId from a bug (silent ownership transfer) into a visible
        // zero-row result.
        //
        // `gold` added to the SET list. The column's own CHECK constraint
        // (CK_characters_gold_nonnegative) is the last line of defence if
        // a negative value ever reaches this far — PlayerManager.TryAddGold
        // is supposed to have refused it long before the packet was sent,
        // so hitting the constraint here means that guard was bypassed,
        // not that this endpoint needs its own copy of the same check.
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
            // Either the character doesn't exist or it belongs to someone
            // else. Both are bugs upstream, and both used to be papered
            // over — the first by silently inserting a row, the second by
            // silently stealing one.
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
        // Original only logged this — a save failure was invisible to
        // WorldServer. This still doesn't send a body (matching the old
        // fire-and-forget behavior), but the 500 means you *can* check
        // the status code now if you choose to.
        //
        // A MySQL CHECK-constraint violation (gold would have gone
        // negative) lands here too, as a generic exception — worth
        // grepping this log for "gold" specifically if TryAddGold's
        // in-memory guard and this endpoint's stored value ever disagree,
        // since that combination should be impossible and means one of
        // the two checks has a bug.
        Console.Error.WriteLine($"[Persistence] Save failed for CharacterId={request.CharacterId}: {e}");
        context.Response.StatusCode = StatusCodes.Status500InternalServerError;
    }
});

app.Run();