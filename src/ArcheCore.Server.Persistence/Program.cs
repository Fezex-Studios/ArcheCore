using ArcheCore.Network.Shared.Packets.PersistenceServer.P2W;
using ArcheCore.Network.Shared.Packets.PersistenceServer.W2P;
using ArcheCore.PersistenceServer.Api.Data;
using ArcheCore.PersistenceServer.Api.Extensions;
using ArcheCore.PersistenceServer.Api.Models;
using Microsoft.EntityFrameworkCore;

var builder = WebApplication.CreateBuilder(args);

var connectionString = builder.Configuration.GetConnectionString("Persistence")
    ?? throw new InvalidOperationException("Missing 'Persistence' connection string.");

builder.Services.AddDbContext<PersistenceDbContext>(options =>
    options.UseMySql(connectionString, ServerVersion.AutoDetect(connectionString)));

var app = builder.Build();

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
            Z           = 0
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
        Z           = row.PosZ
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
        await db.Database.ExecuteSqlInterpolatedAsync($"""
            INSERT INTO characters
                (character_id, account_id, name, level, pos_x, pos_y, pos_z)
            VALUES
                ({request.CharacterId}, {request.AccountId}, {request.Name},
                 {request.Level}, {request.X}, {request.Y}, {request.Z})
            ON DUPLICATE KEY UPDATE
                account_id = VALUES(account_id),
                name       = VALUES(name),
                level      = VALUES(level),
                pos_x      = VALUES(pos_x),
                pos_y      = VALUES(pos_y),
                pos_z      = VALUES(pos_z)
            """);

        Console.WriteLine($"Saved {request.Name}");
    }
    catch (Exception e)
    {
        // Original only logged this — a save failure was invisible to
        // WorldServer. This still doesn't send a body (matching the old
        // fire-and-forget behavior), but the 500 means you *can* check
        // the status code now if you choose to.
        Console.Error.WriteLine($"[Persistence] Save failed for CharacterId={request.CharacterId}: {e}");
        context.Response.StatusCode = StatusCodes.Status500InternalServerError;
    }
});

app.Run();