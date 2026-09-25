// ArcheCore.Tests - integration tests for the audit fixes (C1-C3, H3, H5,
// M1, M3, M4, M7, M8), run against a REAL MySQL/MariaDB and the REAL
// persistence server process. Plain console app: no test framework, so no
// extra packages. Exit code 0 = all passed.
//
//   dotnet run --project ArcheCore.Tests
//
// Needs a MySQL/MariaDB you can throw away. Point it at one with
//   ARCHECORE_TEST_DB="Server=127.0.0.1;Port=3306;Database=archecore_test;User=root;Password=...;"
// The database is DROPPED and recreated on every run, so its name must
// contain "test" - the run refuses otherwise.
using System.Collections.Concurrent;
using System.Diagnostics;
using System.Numerics;
using ArcheCore.Network.Shared.Packets.PersistenceServer;
using ArcheCore.Network.Shared.Packets.PersistenceServer.P2W;
using ArcheCore.Network.Shared.Packets.PersistenceServer.W2P;
using ArcheCore.PersistenceServer.Api.Data;
using ArcheCore.Server.World.Managers;
using Microsoft.EntityFrameworkCore;
using MySqlConnector;
using Microsoft.EntityFrameworkCore.Infrastructure;
using ArcheCore.Server.World.Networking;
using Worldserver.ArcheCore.PersistenceServer.Scripts;

const string Secret = "integration-test-secret-0123456789-abcdef";
string Conn = Environment.GetEnvironmentVariable("ARCHECORE_TEST_DB")
              ?? "Server=127.0.0.1;Port=3306;Database=archecore_test;User=root;Password=root;";
const string Url = "http://127.0.0.1:7791";
string persistenceDll = Path.Combine(AppContext.BaseDirectory, "ArcheCore.Server.Persistence.dll");

if (!new MySqlConnectionStringBuilder(Conn).Database.Contains("test", StringComparison.OrdinalIgnoreCase))
{
    Console.Error.WriteLine("ARCHECORE_TEST_DB must name a database containing 'test' - it is dropped on every run.");
    return 2;
}

int passed = 0, failed = 0;
void Check(bool ok, string what)
{
    if (ok) { passed++; Console.WriteLine($"  PASS {what}"); }
    else { failed++; Console.WriteLine($"  FAIL {what}"); }
}

// ── Fresh database, real migrations (incl. the hand-written AddCharacterSaveSeq) ──
using (var db = new PersistenceDbContext(new DbContextOptionsBuilder<PersistenceDbContext>()
           .UseMySql(Conn, ServerVersion.AutoDetect(Conn)).Options))
{
    db.Database.EnsureDeleted();

    // Up to the save_seq migration, then plant duplicate names the
    // UniqueCharacterNames migration has to clean up.
    db.GetService<Microsoft.EntityFrameworkCore.Migrations.IMigrator>().Migrate("20260925120000_AddCharacterSaveSeq");
    db.Database.ExecuteSqlRaw("INSERT INTO characters (account_id, name, level, pos_x, pos_y, pos_z, gold) VALUES " +
                              "(900, 'Twin', 1, 0, 2, 0, 0), (901, 'twin', 1, 0, 2, 0, 0), (902, 'TWIN', 1, 0, 2, 0, 0)");
    db.Database.Migrate();
    Console.WriteLine("Migrations applied: " + string.Join(", ", db.Database.GetAppliedMigrations()));
    Console.WriteLine("Model snapshot matches the model (no pending changes): " + !db.Database.HasPendingModelChanges());
}

async Task<long> Scalar(string sql)
{
    await using var c = new MySqlConnection(Conn);
    await c.OpenAsync();
    await using var cmd = new MySqlCommand(sql, c);
    var r = await cmd.ExecuteScalarAsync();
    return r is null or DBNull ? -1 : Convert.ToInt64(r);
}
async Task Exec(string sql)
{
    await using var c = new MySqlConnection(Conn);
    await c.OpenAsync();
    await using var cmd = new MySqlCommand(sql, c);
    await cmd.ExecuteNonQueryAsync();
}

Process server = null;
async Task StartServer()
{
    var psi = new ProcessStartInfo("dotnet", $"\"{persistenceDll}\"")
    {
        RedirectStandardOutput = true, RedirectStandardError = true, UseShellExecute = false
    };
    psi.Environment["ConnectionStrings__Persistence"] = Conn;
    psi.Environment["InternalSecret"] = Secret;
    psi.Environment["ASPNETCORE_URLS"] = Url;
    psi.Environment["Kestrel__Endpoints__Http__Url"] = Url;   // beats the copied appsettings.json
    psi.Environment["Logging__LogLevel__Default"] = "Warning";
    server = Process.Start(psi);
    server.OutputDataReceived += (_, e) => { if (e.Data?.Contains("save-full") == true) Console.WriteLine("    [persistence] " + e.Data); };
    server.ErrorDataReceived += (_, e) => { if (!string.IsNullOrEmpty(e.Data)) Console.WriteLine("    [persistence!] " + e.Data); };
    server.BeginOutputReadLine();
    server.BeginErrorReadLine();

    using var http = new HttpClient();
    for (int i = 0; i < 100; i++)
    {
        try { await http.GetAsync(Url + "/"); return; } catch { await Task.Delay(200); }
    }
    throw new Exception("persistence server did not start");
}
void StopServer() { try { server?.Kill(true); server?.WaitForExit(); } catch { } server = null; }

await StartServer();

var client = new PersistenceClient(new ArcheCore.Server.World.Utils.Config.WorldServerConfig
{
    PersistenceBaseUrl = Url,
    InternalSecret = Secret
});

// A stand-in tick thread.
var tickQueue = new ConcurrentQueue<Action>();
var tickCts = new CancellationTokenSource();
var tickThread = new Thread(() =>
{
    while (!tickCts.IsCancellationRequested)
    {
        while (tickQueue.TryDequeue(out var a)) a();
        Thread.Sleep(5);
    }
}) { IsBackground = true };
tickThread.Start();

Task<T> OnTick<T>(Func<T> f)
{
    var tcs = new TaskCompletionSource<T>(TaskCreationOptions.RunContinuationsAsynchronously);
    tickQueue.Enqueue(() => { try { tcs.SetResult(f()); } catch (Exception e) { tcs.SetException(e); } });
    return tcs.Task;
}

int sends = 0;
Func<W2PCharacterSaveFullRequest, Task<P2WCharacterSaveFullResponse>> realSend = r =>
{
    Interlocked.Increment(ref sends);
    return client.W2PCharacterSaveFull.Send(r);
};

async Task<(long id, PlayerSession s)> NewCharacter(int accountId, string name)
{
    var created = await client.W2PCharacterCreate.Send(accountId, name);
    var load = await client.W2PCharacterLoad.Send(accountId, created.CharacterId);
    var s = new PlayerSession
    {
        AccountId = accountId, CharacterId = created.CharacterId, Name = name,
        Level = load.Level, Gold = load.Gold, Position = new Vector3(load.X, load.Y, load.Z), NetworkId = 1
    };
    s.MarkSaved();
    return (created.CharacterId, s);
}

async Task<P2WCharacterLoadResponse> Load(int accountId, long id) => await client.W2PCharacterLoad.Send(accountId, id);

// ═════════════════════════════════════════════════════════════════════
Console.WriteLine("\nC1 - the OLD save path is last-writer-wins");
{
    var (id, s) = await NewCharacter(1, "OldPath");
    // Two autosaves: gold 500 (newer) and gold 100 (older). The older one
    // arrives LAST - exactly what fire-and-forget HTTP allows.
    await client.W2PCharacterSave.Send(id, 1, "OldPath", 1, 0, 2, 0, 500);
    await client.W2PCharacterSave.Send(id, 1, "OldPath", 1, 0, 2, 0, 100);
    var l = await Load(1, id);
    Check(l.Gold == 100, $"old /characters/save: the late, OLDER save won (gold {l.Gold}) - the bug being fixed");
}

Console.WriteLine("\nC1 - save-full refuses out-of-order and repeated saves");
{
    var (id, _) = await NewCharacter(2, "SeqOrder");
    var inv = new[] { new InventorySlotDto { Slot = 0, ItemTemplateId = 7, Quantity = 3 } };
    var newer = new W2PCharacterSaveFullRequest { AccountId = 2, CharacterId = id, SaveSeq = 2, Level = 3, Gold = 500, Y = 2, Inventory = inv };
    var older = new W2PCharacterSaveFullRequest { AccountId = 2, CharacterId = id, SaveSeq = 1, Level = 2, Gold = 100, Y = 2, Inventory = Array.Empty<InventorySlotDto>() };

    var r1 = await client.W2PCharacterSaveFull.Send(newer);
    var r2 = await client.W2PCharacterSaveFull.Send(older);
    var r3 = await client.W2PCharacterSaveFull.Send(newer);
    var l = await Load(2, id);

    Check(r1.Result == CharacterSaveResult.Saved && r1.CurrentSeq == 2, "newer save written");
    Check(r2.Result == CharacterSaveResult.Stale && r2.CurrentSeq == 2, "late older save answered Stale");
    Check(r3.Result == CharacterSaveResult.Stale && r3.CurrentSeq == 2, "repeat of the same save answered Stale with CurrentSeq == its own seq");
    Check(l.Gold == 500 && l.Level == 3 && l.SaveSeq == 2, $"database holds the newer state (gold {l.Gold}, level {l.Level}, seq {l.SaveSeq})");
    Check(l.Inventory.Length == 1 && l.Inventory[0].ItemTemplateId == 7 && l.Inventory[0].Quantity == 3, "inventory from the newer save");
}

Console.WriteLine("\nC1 - the save chain: one in flight, coalesced, newest state lands");
{
    var (id, s) = await NewCharacter(3, "Chain");
    var persistence = new CharacterPersistence(realSend, tickQueue.Enqueue);
    int before = sends;

    await OnTick(() =>
    {
        for (int i = 1; i <= 50; i++)
        {
            s.Gold = i * 10;
            s.Inventory[i % 20] = new InventorySlot { ItemTemplateId = 100 + i, Quantity = i };
            s.InventoryDirty = true;
            persistence.SaveInBackground(s);
        }
        return true;
    });

    bool settled = await persistence.WhenSettledAsync(id, TimeSpan.FromSeconds(20));
    var l = await Load(3, id);
    int used = sends - before;

    Check(settled, "chain settled");
    Check(l.Gold == 500, $"final gold is the newest (500), got {l.Gold}");
    Check(used <= 3, $"50 saves coalesced into {used} request(s)");
    Check(l.Inventory.Length == 20 && l.Inventory.All(x => x.ItemTemplateId > 100), "full inventory snapshot, all 20 slots");
    Check(l.SaveSeq == used, $"save_seq ({l.SaveSeq}) == requests sent ({used})");
}

Console.WriteLine("\nC1 - a lost answer is resolved by asking again, not by guessing");
{
    var (id, s) = await NewCharacter(4, "LostAnswer");
    int calls = 0;
    Func<W2PCharacterSaveFullRequest, Task<P2WCharacterSaveFullResponse>> flaky = async r =>
    {
        var resp = await client.W2PCharacterSaveFull.Send(r);   // it DOES land...
        if (Interlocked.Increment(ref calls) == 1)
            throw new TaskCanceledException("simulated timeout");    // ...but the answer is lost
        return resp;
    };
    var persistence = new CharacterPersistence(flaky, tickQueue.Enqueue);

    var outcome = await await OnTick(() => { s.Gold = 777; return persistence.SaveNowAsync(s); });
    var l = await Load(4, id);

    Check(outcome == SaveOutcome.Saved, $"outcome Saved after retry ({calls} sends)");
    Check(l.Gold == 777 && l.SaveSeq == 1, $"written exactly once (gold {l.Gold}, seq {l.SaveSeq})");
}

Console.WriteLine("\nC2 - relog waits for the logout save (the old code loaded first)");
{
    var (id, s) = await NewCharacter(5, "Relog");
    // The character has an item, saved.
    var setup = new CharacterPersistence(realSend, tickQueue.Enqueue);
    await await OnTick(() => { s.Inventory[0] = new InventorySlot { ItemTemplateId = 42, Quantity = 1 }; s.InventoryDirty = true; return setup.SaveNowAsync(s); });

    // Logout save that removes the item, on a slow network (1.5s).
    Func<W2PCharacterSaveFullRequest, Task<P2WCharacterSaveFullResponse>> slow = async r =>
    {
        await Task.Delay(1500);
        return await client.W2PCharacterSaveFull.Send(r);
    };
    var persistence = new CharacterPersistence(slow, tickQueue.Enqueue);
    persistence.OnLoaded(id, 1);
    await OnTick(() => { s.Inventory[0] = default; s.InventoryDirty = true; persistence.SaveInBackground(s); return true; });

    // What the OLD login did: load immediately.
    var early = await Load(5, id);
    Check(early.Inventory.Any(x => x.ItemTemplateId == 42), "loading immediately sees the item that was already gone (old behaviour = dupe)");
    Check(!persistence.IsLoadCurrent(id, early.SaveSeq, out var why), $"spawn guard refuses that load: {why}");

    // What login does now.
    bool settled = await persistence.WhenSettledAsync(id, TimeSpan.FromSeconds(10));
    var late = await Load(5, id);
    Check(settled && late.Inventory.All(x => x.ItemTemplateId != 42), "after waiting, the load no longer has the item");
    Check(persistence.IsLoadCurrent(id, late.SaveSeq, out _), "spawn guard accepts the settled load");
}

Console.WriteLine("\nC2 - login times out (refused) while persistence is down, then recovers");
{
    var (id, s) = await NewCharacter(6, "Outage");
    var persistence = new CharacterPersistence(realSend, tickQueue.Enqueue);
    StopServer();

    await OnTick(() => { s.Gold = 4242; persistence.SaveInBackground(s); return true; });
    bool settledDuringOutage = await persistence.WhenSettledAsync(id, TimeSpan.FromSeconds(2));
    Check(!settledDuringOutage, "not settled while the persistence server is down (login would be refused)");

    await StartServer();
    bool settled = await persistence.WhenSettledAsync(id, TimeSpan.FromSeconds(40));
    var l = await Load(6, id);
    Check(settled && l.Gold == 4242, $"retried until it landed once the server was back (gold {l.Gold})");
}

Console.WriteLine("\nC3 - mail claim: character save and mail delete are one transaction");
{
    var (id, s) = await NewCharacter(7, "MailTest");
    var persistence = new CharacterPersistence(realSend, tickQueue.Enqueue);
    persistence.OnLoaded(id, 0);

    await client.W2PMail.Send(id, "Auction House", "Sold", 250, 9, 5);
    var box = await client.W2PMail.List(id, 7);
    var mail = box.Mail.Single();

    var outcome = await await OnTick(() =>
    {
        s.Gold += mail.Gold;
        s.Inventory[3] = new InventorySlot { ItemTemplateId = mail.ItemTemplateId, Quantity = mail.ItemQuantity };
        s.InventoryDirty = true;
        return persistence.SaveClaimingMailAsync(s, mail.Id);
    });

    var l = await Load(7, id);
    var after = await client.W2PMail.List(id, 7);
    Check(outcome == SaveOutcome.Saved, "claim saved");
    Check(after.Mail.Length == 0, "mail deleted");
    Check(l.Gold == 250 && l.Inventory.Any(x => x.Slot == 3 && x.ItemTemplateId == 9 && x.Quantity == 5), "character holds the contents");

    // The same mail claimed again (double click that got past the in-memory guard).
    var again = await await OnTick(() => { s.Gold += mail.Gold; return persistence.SaveClaimingMailAsync(s, mail.Id); });
    var l2 = await Load(7, id);
    Check(again == SaveOutcome.MailGone, "second claim answered MailGone");
    Check(l2.Gold == 250, $"and wrote nothing (gold still {l2.Gold})");
}

Console.WriteLine("\nC3 - a failure half way through a claim rolls ALL of it back");
{
    var (id, _) = await NewCharacter(8, "Rollback");
    await client.W2PMail.Send(id, "Auction House", "Sold", 999, 0, 0);
    var mail = (await client.W2PMail.List(id, 8)).Mail.Single();

    // Make the inventory insert blow up AFTER the mail delete and the gold update ran.
    await Exec("CREATE TRIGGER boom BEFORE INSERT ON character_inventory FOR EACH ROW " +
               "BEGIN IF NEW.item_template_id = 666 THEN SIGNAL SQLSTATE '45000' SET MESSAGE_TEXT = 'simulated crash'; END IF; END");

    var req = new W2PCharacterSaveFullRequest
    {
        AccountId = 8, CharacterId = id, SaveSeq = 1, Level = 1, Gold = 999, Y = 2,
        Inventory = new[] { new InventorySlotDto { Slot = 0, ItemTemplateId = 666, Quantity = 1 } },
        ClaimMailId = mail.Id
    };

    bool threw500 = false;
    try { await client.W2PCharacterSaveFull.Send(req); }
    catch (HttpRequestException e) when ((int?)e.StatusCode == 500) { threw500 = true; }

    await Exec("DROP TRIGGER boom");
    var l = await Load(8, id);
    var box = await client.W2PMail.List(id, 8);

    Check(threw500, "persistence answered 500");
    Check(box.Mail.Length == 1, "mail is still there");
    Check(l.Gold == 0 && l.SaveSeq == 0, $"character untouched (gold {l.Gold}, seq {l.SaveSeq})");
}

Console.WriteLine("\nValidation - a malformed save is refused definitively (not retried for ever)");
{
    var (id, s) = await NewCharacter(9, "Invalid");
    int calls = 0;
    var persistence = new CharacterPersistence(r => { Interlocked.Increment(ref calls); return client.W2PCharacterSaveFull.Send(r); }, tickQueue.Enqueue);
    var outcome = await await OnTick(() => { s.Gold = -5; return persistence.SaveNowAsync(s); });
    Check(outcome == SaveOutcome.Failed && calls == 1, $"negative gold -> Failed after {calls} request");
    await Task.Delay(100);
    Check(await OnTick(() => s.IsDirty), "session marked dirty again");
}

Console.WriteLine("\nQuests - the log is replaced whole, so an abandoned quest stays abandoned");
{
    var (id, _) = await NewCharacter(10, "Quests");
    QuestStateDto Q(int qid, byte st) => new() { QuestId = qid, Status = st, Progress = "1,0" };
    var a = await client.W2PCharacterSaveFull.Send(new W2PCharacterSaveFullRequest
    { AccountId = 10, CharacterId = id, SaveSeq = 1, Level = 1, Y = 2, Inventory = Array.Empty<InventorySlotDto>(), Quests = new[] { Q(1, 1), Q(2, 1) } });
    var b = await client.W2PCharacterSaveFull.Send(new W2PCharacterSaveFullRequest
    { AccountId = 10, CharacterId = id, SaveSeq = 2, Level = 1, Y = 2, Inventory = Array.Empty<InventorySlotDto>(), Quests = new[] { Q(2, 2) } });
    var c = await client.W2PCharacterSaveFull.Send(new W2PCharacterSaveFullRequest
    { AccountId = 10, CharacterId = id, SaveSeq = 3, Level = 1, Y = 2, Inventory = Array.Empty<InventorySlotDto>(), Quests = null });
    var l = await Load(10, id);
    Check(l.Quests.Length == 1 && l.Quests[0].QuestId == 2 && l.Quests[0].Status == 2, "quest 1 (abandoned) gone, quest 2 updated");
    Check(c.Result == CharacterSaveResult.Saved, "Quests = null leaves the log alone");
}

Console.WriteLine("\nOwnership - another account can't save this character");
{
    var (id, _) = await NewCharacter(11, "Owner");
    var r = await client.W2PCharacterSaveFull.Send(new W2PCharacterSaveFullRequest
    { AccountId = 999, CharacterId = id, SaveSeq = 1, Level = 50, Y = 2, Gold = 1_000_000, Inventory = Array.Empty<InventorySlotDto>() });
    var l = await Load(11, id);
    Check(r.Result == CharacterSaveResult.NotFound && l.Gold == 0 && l.Level == 1, "NotFound, nothing written");
}


Console.WriteLine("\nH5 - duplicate names were renamed by the migration; names are unique, case-insensitively");
{
    var names = new List<string>();
    await using (var c = new MySqlConnection(Conn))
    {
        await c.OpenAsync();
        await using var cmd = new MySqlCommand("SELECT name FROM characters WHERE account_id BETWEEN 900 AND 902 ORDER BY character_id", c);
        await using var r = await cmd.ExecuteReaderAsync();
        while (await r.ReadAsync()) names.Add(r.GetString(0));
    }
    Check(names.Count == 3 && names[0] == "Twin" && names[1].StartsWith("twin_") && names[2].StartsWith("TWIN_"),
          "oldest keeps 'Twin', later ones renamed: " + string.Join(", ", names));

    var a = await client.W2PCharacterCreate.Send(20, "Bebpu");
    var b = await client.W2PCharacterCreate.Send(21, "bebpu");
    var bad = await client.W2PCharacterCreate.Send(21, "<size=99>X");
    var reserved = await client.W2PCharacterCreate.Send(21, "Admin");
    var shortName = await client.W2PCharacterCreate.Send(21, "Al");
    Check(a.Success, "Bebpu created");
    Check(!b.Success && b.Reason == "That name is taken.", $"bebpu refused: '{b.Reason}'");
    Check(!bad.Success && bad.Reason.Length > 0, $"rich-text name refused: '{bad.Reason}'");
    Check(!reserved.Success && reserved.Reason.Contains("reserved"), $"reserved name refused: '{reserved.Reason}'");
    Check(!shortName.Success, $"2-letter name refused: '{shortName.Reason}'");

    // Race past the pre-check: the unique index still holds.
    var race = await Task.WhenAll(Enumerable.Range(0, 8).Select(i => client.W2PCharacterCreate.Send(30 + i, "Racer")));
    Check(race.Count(x => x.Success) == 1, $"8 simultaneous creates of one name -> {race.Count(x => x.Success)} succeeded");
}

Console.WriteLine("\nH3 - token bucket per (peer, opcode)");
{
    long t = 0;
    long sec = System.Diagnostics.Stopwatch.Frequency;
    var rl = new PacketRateLimiter(() => t) { FloodDropLimit = 50, FloodWindowSeconds = 10 };
    var op = ArcheCore.Library.Net.Worldserver.Opcodes.C2WAuctionBrowse; // 1/s, burst 4
    int allowed = 0;
    for (int i = 0; i < 10; i++) if (rl.Check(1, op) == PacketRateLimiter.Verdict.Allow) allowed++;
    Check(allowed == 4, $"burst of 10 browse packets -> {allowed} allowed (burst 4)");
    t += 2 * sec;
    allowed = 0;
    for (int i = 0; i < 10; i++) if (rl.Check(1, op) == PacketRateLimiter.Verdict.Allow) allowed++;
    Check(allowed == 2, $"2s later -> {allowed} allowed (1/s refill)");
    Check(rl.Check(2, op) == PacketRateLimiter.Verdict.Allow, "another peer has its own bucket");
    Check(rl.Check(1, ArcheCore.Library.Net.Worldserver.Opcodes.PlayerMove) == PacketRateLimiter.Verdict.Allow, "another opcode has its own bucket");

    // 20Hz movement for 10s is never dropped.
    var mv = new PacketRateLimiter(() => t);
    int moveDrops = 0;
    for (int i = 0; i < 200; i++) { t += sec / 20; if (mv.Check(5, ArcheCore.Library.Net.Worldserver.Opcodes.PlayerMove) != PacketRateLimiter.Verdict.Allow) moveDrops++; }
    Check(moveDrops == 0, "a real client's 20Hz movement is never dropped");

    PacketRateLimiter.Verdict last = PacketRateLimiter.Verdict.Allow;
    for (int i = 0; i < 200 && last != PacketRateLimiter.Verdict.Disconnect; i++) last = rl.Check(1, op);
    Check(last == PacketRateLimiter.Verdict.Disconnect, "a sustained flood gets the peer disconnected");
}


Console.WriteLine("\nM7 - Lua sandbox and instruction budget");
{
    var dir = Path.Combine(Path.GetTempPath(), "luatest_" + Guid.NewGuid().ToString("N"));
    Directory.CreateDirectory(dir);
    File.WriteAllText(Path.Combine(dir, "a_good.lua"), "count = 0\nServer:RegisterPlayerEvent(4, function(msg) count = count + 1 end)");
    File.WriteAllText(Path.Combine(dir, "b_loop.lua"), "Server:RegisterPlayerEvent(4, function(msg) while true do end end)");
    File.WriteAllText(Path.Combine(dir, "c_io.lua"), "io_ok = (io ~= nil)\nos_exec = (os ~= nil and os.execute ~= nil)\nload_ok = (load ~= nil)");
    File.WriteAllText(Path.Combine(dir, "d_toplevel_loop.lua"), "while true do end");

    var engine = new ArcheCore.Server.World.Lua.Scripting.LuaEngine();
    var sw = System.Diagnostics.Stopwatch.StartNew();
    engine.LoadAllScripts(dir);
    Check(sw.ElapsedMilliseconds < 5000, $"a script looping forever at load was stopped ({sw.ElapsedMilliseconds}ms)");

    var scriptField = typeof(ArcheCore.Server.World.Lua.Scripting.LuaEngine)
        .GetField("script", System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance);
    var lua = (MoonSharp.Interpreter.Script)scriptField.GetValue(engine);
    Check(lua.Globals.Get("io_ok").Boolean == false, "io is not available");
    Check(lua.Globals.Get("os_exec").Boolean == false, "os.execute is not available");
    Check(lua.Globals.Get("load_ok").Boolean == false, "load is not available");

    var times = new List<long>();
    for (int i = 0; i < 5; i++)
    {
        sw.Restart();
        engine.FireEvent(ArcheCore.Server.World.Lua.Scripting.PlayerEvent.OnChat, "hi");
        times.Add(sw.ElapsedMilliseconds);
    }
    Check(times.Max() < 200, "each event with a runaway hook returns quickly: " + string.Join("/", times) + " ms");
    Check(times.Skip(3).Max() < 5, $"after 3 overruns the runaway hook is switched off ({times[4]} ms)");
    Check(lua.Globals.Get("count").Number == 5, $"the good hook still ran every time (count {lua.Globals.Get("count").Number})");
}


Console.WriteLine("\nM1 - only players observe");
{
    var im = new InterestManager();
    int npcBase = SpawnManager.NpcIdBase;
    // A camp: 30 mobs and 50 nodes packed together, no players.
    for (int i = 0; i < 80; i++) im.UpdatePosition(npcBase + i, new Vector3(i % 10, 0, i / 10));
    int npcNpcLinks = 0;
    for (int i = 0; i < 80; i++) npcNpcLinks += im.GetKnownBy(npcBase + i).Count;
    Check(npcNpcLinks == 0, $"80 NPCs/nodes side by side track nobody ({npcNpcLinks} links; was 80x79 before)");

    var (entered, _) = im.UpdatePosition(1, new Vector3(5, 0, 5));
    Check(entered.Count == 80, $"a player walking in sees all 80 ({entered.Count})");
    Check(im.GetKnownBy(npcBase + 3).Count == 1 && im.GetKnownBy(npcBase + 3)[0] == 1, "each NPC is known by exactly that player");

    var (e2, _) = im.UpdatePosition(npcBase + 3, new Vector3(20, 0, 20));   // NPC wanders
    Check(e2.Count == 0 && im.GetKnownBy(npcBase + 3).Count == 1, "a wandering NPC picks up no other NPCs, keeps its player");

    var (_, left) = im.UpdatePosition(1, new Vector3(500, 0, 500));
    Check(left.Count == 80, $"player walks away: all 80 leave ({left.Count})");
    var (e3, _) = im.UpdatePosition(2, new Vector3(501, 0, 500));
    Check(e3.Count == 1 && e3[0] == 1, "two players still see each other");
}

Console.WriteLine("\nM3 - serialise once: bytes identical to the old per-peer writer");
{
    var payload = new ArcheCore.Network.Shared.Packets.W2C.W2CMOTDPacket { Message = "hello" };
    byte[] encoded = ArcheCore.Network.Worldserver.WorldserverPacketSender.Encode(ArcheCore.Library.Net.Worldserver.Opcodes.MOTD, payload);
    var w = new LiteNetLib.Utils.NetDataWriter();
    w.Put((ushort)ArcheCore.Library.Net.Worldserver.Opcodes.MOTD);
    w.Put(MessagePack.MessagePackSerializer.Serialize(payload));
    Check(encoded.SequenceEqual(w.CopyData()), "Encode() == old NetDataWriter bytes (client needs no change to read it)");
    Check(ArcheCore.Network.Shared.PacketChannels.For(ArcheCore.Library.Net.Worldserver.Opcodes.W2CAuctionList) == 1 &&
          ArcheCore.Network.Shared.PacketChannels.For(ArcheCore.Library.Net.Worldserver.Opcodes.ChatMessage) == 2 &&
          ArcheCore.Network.Shared.PacketChannels.For(ArcheCore.Library.Net.Worldserver.Opcodes.W2CCombatEvent) == 0,
          "M4 channels: auction list -> bulk, chat -> chat, combat -> world");
}

Console.WriteLine("\nM8 - velocity codec");
{
    float Rt(float v) => ArcheCore.Network.Shared.VelocityCodec.Decode(ArcheCore.Network.Shared.VelocityCodec.Encode(v));
    Check(Math.Abs(Rt(80f) - 80f) < 1.6f, $"80 u/s fall survives ({Rt(80f):F2}); old cap was 31.75");
    Check(Math.Abs(Rt(-80f) + 80f) < 1.6f, $"negative too ({Rt(-80f):F2})");
    Check(Math.Abs(Rt(5f) - 5f) < 0.2f, $"run speed precise ({Rt(5f):F3})");
    Check(Rt(0f) == 0f && Math.Abs(Rt(0.02f) - 0.02f) < 0.02f, "zero stays zero, tiny speeds stay tiny");
    Check(Rt(500f) == 100f, "clamps at 100 u/s");
    float worst = 0; for (float v = 0; v <= 10; v += 0.01f) worst = Math.Max(worst, Math.Abs(Rt(v) - v));
    Check(worst < 0.3f, $"worst error 0-10 u/s: {worst:F3} u/s");
}


Console.WriteLine("\nProtocol version - an out-of-date client is refused with a message");
{
    var serverListener = new LiteNetLib.EventBasedNetListener();
    var netServer = new LiteNetLib.NetManager(serverListener);
    serverListener.ConnectionRequestEvent += request =>
    {
        // Same logic as WorldServer.OnConnectionRequest.
        string key = null;
        try { key = request.Data.GetString(); } catch { }
        if (key == ArcheCore.Network.Shared.ProtocolVersion.ConnectionKey) { request.Accept(); return; }
        var w = new LiteNetLib.Utils.NetDataWriter();
        w.Put($"Your client is out of date (protocol {ArcheCore.Network.Shared.ProtocolVersion.Parse(key)}, server {ArcheCore.Network.Shared.ProtocolVersion.Current}). Update the game.");
        request.Reject(w);
    };
    netServer.Start(7792);

    async Task<(string result, string message)> TryConnect(string key)
    {
        var listener = new LiteNetLib.EventBasedNetListener();
        var client = new LiteNetLib.NetManager(listener);
        string result = null, message = null;
        listener.PeerConnectedEvent += _ => result = "connected";
        listener.PeerDisconnectedEvent += (_, info) =>
        {
            result = info.Reason.ToString();
            if (info.AdditionalData != null && info.AdditionalData.AvailableBytes > 0) message = info.AdditionalData.GetString();
        };
        client.Start();
        client.Connect("127.0.0.1", 7792, key);
        for (int i = 0; i < 200 && result == null; i++) { netServer.PollEvents(); client.PollEvents(); await Task.Delay(10); }
        client.Stop();
        return (result, message);
    }

    var ok = await TryConnect(ArcheCore.Network.Shared.ProtocolVersion.ConnectionKey);
    var old = await TryConnect("MMO");
    var stale = await TryConnect("ArcheCore/1");
    netServer.Stop();

    Check(ok.result == "connected", "current client connects");
    Check(old.result == "ConnectionRejected", $"pre-versioning client ('MMO') refused: {old.result}");
    Check(stale.result == "ConnectionRejected" && stale.message != null && stale.message.Contains("protocol 1"),
          $"older protocol refused with a message: \"{stale.message}\"");
}

tickCts.Cancel();
StopServer();
Console.WriteLine($"\n{passed} passed, {failed} failed");
return failed == 0 ? 0 : 1;
