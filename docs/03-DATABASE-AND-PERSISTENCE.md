# Database & Persistence

ArcheCore has **four** separate SQLite databases, owned by three different
processes, managed by three different mechanisms. Knowing which one owns
what — and one real collision between two of them — will save you a
confusing debugging session.

| Database file | Owning process | Managed by | Holds |
|---|---|---|---|
| `Data/worldserver.db` | WorldServer (C#) | **EF Core migrations** (the SQL patch runner has been retired — see below) | Quests, Items, NpcTemplates, NpcSpawners, and a growing set of Items/Shop/Quests tables (static game-design data) |
| `persistence.db` | PersistenceServer (Bun/TS) | Hand-written `db.exec(...)` in `Database.ts` | `characters` table — account_id, name, level, position |
| `gamedata.db` | Unity Client (read-only copy) | Built/served by the AuthServer, downloaded by `GameDataBootstrap` | Client-local mirror of reference data (items, etc.) for offline lookups, shipped encrypted |
| (AuthServer's own DB) | AuthServer | Not in this snapshot | Accounts, credentials, session tokens |

## `worldserver.db` — EF Core migrations only (formerly two competing systems)

**Current state: EF Core migrations are the single schema-management
system for this database.** `WorldServer.StartAsync` calls
`WorldDataDbContext.Database.MigrateAsync()` at boot, which applies
whatever migrations haven't run yet. Seeding actual rows (NPC templates,
spawners, and whatever else) is deliberately **not** part of this — that's
handled by separate tooling, not baked into the boot sequence. Adding a
new table is: write the POCO, add a `DbSet<T>`, run
`dotnet ef migrations add <Name>`, done — see `code/GameData/README.md` in
this doc set for a worked 15-table example (Items, Shop, and Quests
domains) and `code/MIGRATION-STEPS.md` for the exact cutover steps.

The rest of this section is kept as a record of **why** — this is what
the database looked like before the fix, and the reasoning that led to
picking EF over the alternative.

This is the one to be careful with. `WorldServerConfig`/`DatabaseConfig`
point **both** of the following at the exact same file
(`Data/worldserver.db`):

**1. EF Core migrations** (`Migrations/`, driven by `WorldDataDbContext`):

```csharp
public class WorldDataDbContext : DbContext
{
    public DbSet<QuestTable>      Quests       => Set<QuestTable>();
    public DbSet<ItemTable>       Items        => Set<ItemTable>();
    public DbSet<NpcTemplate>     NpcTemplates => Set<NpcTemplate>();
    public DbSet<NpcSpawnerTable> NpcSpawners  => Set<NpcSpawnerTable>();
}
```

Three migrations exist: `InitialCreate` (Quests), `AddItemTable` (Items),
and `AddNpcInteractRange`, which — despite the name suggesting a single
column add — actually creates **both** `NpcSpawners` and `NpcTemplates`
from scratch, `InteractRange` column included:

```csharp
// 20260705082758_AddNpcInteractRange.cs
migrationBuilder.CreateTable(name: "NpcTemplates", columns: table => new {
    Id = table.Column<int>(...).Annotation("Sqlite:Autoincrement", true),
    Name = table.Column<string>(...),
    Level = table.Column<int>(...),
    ModelType = table.Column<string>(...),
    InteractRange = table.Column<float>(...)   // <- included from the start here
}, constraints: table => table.PrimaryKey("PK_NpcTemplates", x => x.Id));
```

**2. A raw SQL patch runner** (`GameDataPatchRunner`, `SQL/patches/*.sql`),
run unconditionally on every boot, first thing, before the persistence
connection is even opened:

```csharp
// WorldServer.StartAsync
await _dataPatchRunner.RunAsync();       // <- step 0, every boot
await _persistenceClient.Start();
```

The patch runner tracks what it's applied in its own `schema_versions`
table and skips anything already recorded — but it has **no idea EF
migrations exist**, and vice versa. Patch `005_npc_templates.sql` creates
`NpcTemplates` *without* `InteractRange`:

```sql
-- 005_npc_templates.sql
CREATE TABLE IF NOT EXISTS "NpcTemplates" (
    "Id"        INTEGER NOT NULL,
    "Name"      TEXT    NOT NULL,
    "Level"     INTEGER NOT NULL DEFAULT 1,
    "ModelType" TEXT    NOT NULL,
    CONSTRAINT "PK_NpcTemplates" PRIMARY KEY("Id" AUTOINCREMENT)
);
```

...while migration `AddNpcInteractRange` creates the *same table name*
*with* `InteractRange`. **Whichever one runs first on a fresh database
wins**, and — this is the important part — **nothing in this codebase ever
calls `dbContext.Database.Migrate()`.** Grepping the whole `Server.World`
project for `Database.Migrate` turns up nothing. The EF migrations exist as
C# files and describe a schema, but nothing here applies them
automatically at startup. In practice, on a fresh checkout, the SQL patch
runner is the only one of the two systems that actually mutates
`worldserver.db` on boot — the migrations only take effect if someone runs
`dotnet ef database update` by hand, and if they do, they need patch
`005`+`006`+`007` to either already be marked applied in `schema_versions`
or to be deleted, or the patch runner will try to `CREATE TABLE` something
that already exists with a different shape.

**What this means practically:** if `NpcTemplate.InteractRange` is coming
back as `0` or the column doesn't exist, check which of the two systems
actually created the table on that particular `worldserver.db` file, not
just whether the migration exists in source.

**Decision: EF Core migrations are the schema system going forward, the
SQL patch runner is being retired.** See
[09-KNOWN-GAPS-AND-NEXT-STEPS.md](09-KNOWN-GAPS-AND-NEXT-STEPS.md) and
`code/MIGRATION-STEPS.md` for the exact steps. In short:
`WorldServer.StartAsync` calls `WorldDataDbContext.Database.MigrateAsync()`
instead of `GameDataPatchRunner.RunAsync()`. The three seed-only patches
(`007`, `010`, `023` — all auto-generated by the Unity "NPC Spawner
Exporter" tool) insert *data*, not schema, so they aren't something EF
migrations replace at all — that content gets seeded through whatever
tooling the project already has for it, separate from schema management
entirely.

### Patch runner mechanics

`GameDataPatchRunner.RunAsync()` (`Utils/Database/SQLite/GameDataPatchRunner.cs`):

```csharp
var patches = Directory.GetFiles(_patchDir, "*.sql")
    .OrderBy(f => Path.GetFileName(f))   // lexicographic: 001_, 002_, ...
    .ToList();

foreach (var patchPath in patches)
{
    var patchName = Path.GetFileName(patchPath);
    // skip if already in schema_versions...

    await using var tx = await conn.BeginTransactionAsync();
    try
    {
        await using var cmd = new SqliteCommand(sql, conn, (SqliteTransaction)tx);
        await cmd.ExecuteNonQueryAsync();
        // record in schema_versions...
        await tx.CommitAsync();
    }
    catch (Exception ex)
    {
        await tx.RollbackAsync();
        Logger.Error(ex, $"[Patcher] Failed applying {patchName} — rolled back. Fix the script and restart.");
        throw;   // stops server boot entirely
    }
}
```

Ordering is purely lexicographic on filename — hence the `005_`, `006_`,
`007_`, `010_`, `023_` numbering. A failed patch **aborts server startup**
(the `throw` propagates out of `StartAsync`), which is the right call for a
half-applied schema, but means a typo in a new patch file takes the whole
WorldServer down at boot, not just at the point something queries the
broken table.

To add a new patch: drop a new `NNN_description.sql` file in
`SQL/patches/`, numbered higher than anything existing. It'll run exactly
once, transactionally, next boot.

## `persistence.db` — the Bun/TypeScript character store

Owned entirely by `ArcheCore.Server.Persistence`. Schema is created inline,
not migrated, in `Database.ts`:

```ts
export const db = new Database("persistence.db");

db.exec(`
    CREATE TABLE IF NOT EXISTS characters
    (
        character_id INTEGER PRIMARY KEY AUTOINCREMENT,
        account_id   INTEGER NOT NULL,
        name         TEXT    NOT NULL,
        level        INTEGER NOT NULL DEFAULT 1,
        pos_x        REAL    NOT NULL DEFAULT 0,
        pos_y        REAL    NOT NULL DEFAULT 2,
        pos_z        REAL    NOT NULL DEFAULT 0
    )
`);
```

If you need a second table here (inventory, for a shop system) this is the
pattern to follow today — there's no migration tool on this side at all,
just `CREATE TABLE IF NOT EXISTS` guarding idempotency. A real migration
story (even a minimal numbered-file runner like `GameDataPatchRunner`, just
in TypeScript) is worth adding before this table count grows past two or
three.

Every handler that touches this DB uses `better-sqlite3`-style synchronous
prepared statements (via `bun:sqlite`, which has a compatible API):

```ts
// W2PCharacterSaveHandler.ts
db.prepare(`
    INSERT OR REPLACE INTO characters
    (character_id, account_id, name, level, pos_x, pos_y, pos_z)
    VALUES (?, ?, ?, ?, ?, ?, ?)
`).run(save.CharacterId, save.AccountId, save.Name, save.Level, save.X, save.Y, save.Z);
```

```ts
// W2PCharacterListHandler.ts
const rows = db.prepare(`SELECT * FROM characters WHERE account_id = ?`)
    .all(request.AccountId) as CharacterRow[];

SendP2WCharacterListResponse(socket, {
    AccountId: request.AccountId,
    Characters: rows.map(r => ({
        CharacterId: r.character_id,
        Name: r.name,
        Level: r.level
    }))
});
```

Note the `INSERT OR REPLACE` on save keys off `character_id` — a save for a
`character_id` that doesn't exist yet will silently create it with
whatever `AUTOINCREMENT` would have assigned, which is fine for the
current single-writer-per-character flow but worth remembering if you ever
allow concurrent saves for the same character from two sources.

## `gamedata.db` — client-local, read-only, encrypted

This one belongs to the Unity client but is *produced* server-side (by the
AuthServer, not in this snapshot) and shipped two ways: bundled in
`StreamingAssets/GameData/gamedata.db` for day-0, and downloadable/
version-checked at runtime by `GameDataBootstrap`:

```csharp
// GameDataBootstrap.cs
private string GameDataDir => Path.Combine(Application.persistentDataPath, "GameData");
private string DbPath   => Path.Combine(GameDataDir, "gamedata.db");
private string HashPath => Path.Combine(GameDataDir, "gamedata.hash");
private string BundledDbPath => Path.Combine(Application.streamingAssetsPath, "GameData", "gamedata.db");
```

It checks a hash against the AuthServer, downloads a fresh copy if it's
stale, then `GameDataDatabase.Initialize(encryptedDbPath)` decrypts it to a
throwaway temp file before opening a **read-only** `sqlite-net` connection:

```csharp
// GameDataDatabase.cs
decryptedTempPath = Path.Combine(Application.temporaryCachePath, "gamedata.decrypted.db");
// ...GameDataCrypto.DecryptToFile(encryptedDbPath, decryptedTempPath)...

SQLiteConnectionString options = new SQLiteConnectionString(
    decryptedTempPath,
    SQLiteOpenFlags.ReadOnly | SQLiteOpenFlags.FullMutex,
    storeDateTimeAsTicks: false);

Connection = new SQLiteConnection(options);
```

The plaintext temp file is deliberately written to `temporaryCachePath`
(not `persistentDataPath`) and deleted again in `Close()` — the encrypted
copy is what persists between sessions, never the decrypted one. This
exists so item names/stats/etc. can be looked up client-side (tooltips,
UI) without a round trip to the WorldServer for pure reference data, while
still making the raw values mildly inconvenient to rip out of the client
install. `ItemRepository`/`ItemRecord` (`Assets/ArcheCore.Client/GameData/`)
are the read-side API over this connection.

**This is a fifth conceptual data source you'll want to keep in sync
manually**: whatever authoritative item/NPC data lives in
`worldserver.db`'s `Items`/`NpcTemplates` tables needs a corresponding
export step into whatever builds `gamedata.db` on the AuthServer side.
Nothing in this snapshot automates that — worth designing before a shop
system doubles the amount of reference data that needs to stay in sync
across both databases.

## Request/response pattern across the WorldServer ↔ PersistenceServer link

See [02-NETWORKING-AND-PACKETS.md](02-NETWORKING-AND-PACKETS.md) for the
wire format; the short version for anything touching character data from a
C2W handler is: call the matching method on `PersistenceClient` (e.g.
`persistence.W2PCharacter.LoadList(accountId)`), `await` it — it resolves
via the `TaskCompletionSource` dictionaries once the P2W reply arrives —
and re-enter `playerManager.EnqueueAction(...)` before touching any
tick-thread state afterward.
