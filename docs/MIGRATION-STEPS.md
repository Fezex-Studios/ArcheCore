# Migration Steps: Retiring `GameDataPatchRunner`

Concrete, copy-pasteable changes for the decision made in
`09-KNOWN-GAPS-AND-NEXT-STEPS.md`: **schema comes from EF Core migrations
only.** This version has no opinion on how you seed row data — that's your
own tooling now, not something these docs prescribe. If you want a
JSON-file-based seeder later, that's a separate, optional add-on, not a
required part of the schema cutover below.

## 0. One-time step on each existing dev machine

Before deploying this change, decide how each existing `Data/worldserver.db`
gets an `__EFMigrationsHistory` table that matches reality:

- **If the database has no real player data yet** (sounds like this is
  still true for ArcheCore) — simplest path: delete
  `Data/worldserver.db` on every dev machine. The code changes below will
  create it fresh from the three existing migrations
  (`InitialCreate`, `AddItemTable`, `AddNpcInteractRange`) the first time
  the server boots. **The tables will be empty** — repopulate them with
  whatever seeding method you're using now.
- **If there's real data you need to keep** — instead, manually create the
  `__EFMigrationsHistory` table and insert one row per migration already
  reflected in the live schema:
  ```sql
  CREATE TABLE "__EFMigrationsHistory" (
      "MigrationId" TEXT NOT NULL CONSTRAINT "PK___EFMigrationsHistory" PRIMARY KEY,
      "ProductVersion" TEXT NOT NULL
  );
  INSERT INTO "__EFMigrationsHistory" (MigrationId, ProductVersion) VALUES
      ('20260627034058_InitialCreate', '8.0.0'),
      ('20260627034639_AddItemTable', '8.0.0'),
      ('20260705082758_AddNpcInteractRange', '8.0.0');
  ```
  (match `ProductVersion` to whatever EF Core version is in your `.csproj`
  if it's not 8.0.0). This tells `MigrateAsync()` those three are already
  applied, so it won't try to re-run `CREATE TABLE` against tables that
  already exist. The existing NPC template/spawner rows in the file stay
  exactly as they are — nothing here touches row data, only the
  bookkeeping table EF uses to track schema state.

## 1. `ServerBootstrap.cs` — remove the patch runner registration

```diff
- builder.Services.AddSingleton<GameDataPatchRunner>();
  builder.Services.AddHostedService<WorldServer>();
```

(`GameDataPatchRunner.cs` and `SQL/patches/*.sql` can be deleted entirely
once step 0 above is done and you've confirmed migrations apply cleanly —
see step 4 below.)

## 2. `WorldServer.cs` — constructor

```diff
  public WorldServer(
      IOptions<WorldServerConfig> world,
      IServiceScopeFactory scopeFactory,
      IOptions<NetworkConfig> network,
      QuestManager questManager,
      DemoService demoService,
      AuthService authService,
-     GameDataPatchRunner dataPatchRunner,
      IDbContextFactory<WorldDataDbContext> dbFactory,
      DemoManager demoManager
      )
  {
      _world = world.Value;
      _network = network.Value;
      _questManager = questManager;
      _scopeFactory = scopeFactory;
      _demoService = demoService;
      _authService = authService;
-     _dataPatchRunner = dataPatchRunner;
      _dbFactory = dbFactory;
      _demoManager = demoManager;
  }
```

Also remove the now-unused field:

```diff
- private readonly GameDataPatchRunner _dataPatchRunner;
```

## 3. `WorldServer.StartAsync` — step 0 of boot

```diff
  public async Task StartAsync(CancellationToken cancellationToken)
  {
-     // 0. Run DB patches first
-     await _dataPatchRunner.RunAsync();
+     // 0. Apply any pending EF migrations
+     await using (var db = await _dbFactory.CreateDbContextAsync())
+     {
+         await db.Database.MigrateAsync(cancellationToken);
+     }

      // 1. Connect to PersistenceServer
      _persistenceClient = new PersistenceClient(_world);
      await _persistenceClient.Start();
```

That's the entire runtime change. Schema now updates itself on boot;
getting actual rows into those tables is entirely up to whatever tool or
process you're already using for that.

## 4. Delete once the above is verified working

- `Utils/Database/SQLite/GameDataPatchRunner.cs`
- `SQL/patches/005_npc_templates.sql`
- `SQL/patches/006_npc_spawners_table.sql`
- `SQL/patches/007_npc_template_data.sql`
- `SQL/patches/010_add_enemyspwn.sql`
- `SQL/patches/023_world_enemy_update.sql`
- `SQL/patches/readme.md`, `SQL/readme.md` (both empty already)

Before deleting `007`/`010`/`023` specifically — make sure whatever your
"other, better way" of seeding data is can already reproduce the rows
those files used to insert (one `Orc Grunt` template, four spawner
placements). If it can't yet, keep those three `.sql` files around as a
reference for what needs to end up in the tables, even after you stop
running them through the patch runner.

## 5. Verify

1. Delete (or reset per step 0) a dev `Data/worldserver.db`.
2. Boot the WorldServer. Confirm in the logs that EF applies
   `InitialCreate`, `AddItemTable`, `AddNpcInteractRange`
   (`Microsoft.EntityFrameworkCore.Database.Command` / `Migrations`
   category logging — note `ServerBootstrap` currently silences EF
   logging entirely via
   `options.UseLoggerFactory(LoggerFactory.Create(_ => { }))`, so you
   won't see this by default; temporarily comment that line out for this
   one verification run, or just inspect the resulting `.db` file with
   `sqlite3 worldserver.db ".tables"` afterward).
3. Run your own seeding process to repopulate NPC templates/spawners.
4. Connect a client, confirm the Orc Grunt spawners and their
   `InteractRange`-based interact behavior still work exactly as before.
5. Boot the server a second time without deleting the DB — confirm
   `MigrateAsync()` is a no-op (no migrations applied, since all three are
   already recorded in `__EFMigrationsHistory`) and your existing rows are
   untouched.
