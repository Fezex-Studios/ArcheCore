namespace ArcheCore.LoadTester
{
    /// <summary>
    /// Usage:
    ///   dotnet run -- --host 127.0.0.1 --port 7777 --auth http://127.0.0.1:5000 --bots 500 --ramp 50
    ///
    /// Ramps up in batches (default 50 bots/sec) instead of connecting all
    /// N at once — mirrors how real load actually arrives, and avoids a
    /// thundering herd of connect attempts hitting the server in one
    /// instant.
    ///
    /// CHANGED: bots are pre-allocated into one fixed array, and a SINGLE
    /// shared driver loop below calls Poll()/CheckTimeout()/MaybeMove()
    /// on every bot once per shared tick, instead of each bot owning its
    /// own long-lived polling and movement Tasks. The old per-bot-Task
    /// design created tens of thousands of concurrent async loops at a
    /// few thousand bots, which starved the thread pool on a single dev
    /// box before the server itself was ever meaningfully stressed — a
    /// test that plateaus under that design is measuring the tester, not
    /// the server. This version does O(bots) work on ONE thread per
    /// shared tick instead.
    ///
    /// Per-bot sockets are still unavoidable (LiteNetLib dedupes peers by
    /// remote endpoint, so each simulated client needs its own local
    /// port/NetManager to connect to the same server address) — what
    /// changed is the SCHEDULING overhead, which is what a busy box runs
    /// out of first. Still best run from a second machine once you're
    /// past a couple thousand bots, but this pushes that ceiling much
    /// further on a single box.
    /// </summary>
    public static class Program
    {
        public static async Task Main(string[] args)
        {
            var opts = ParseArgs(args);

            Directory.CreateDirectory("logs");
            var logPath = Path.Combine("logs", $"loadtest-{DateTime.Now:yyyy-MM-dd_HH-mm-ss}.txt");
            Console.SetOut(new TeeTextWriter(Console.Out, logPath));
            Console.WriteLine($"Logging this run to {Path.GetFullPath(logPath)}");

            var metrics = new Metrics();

            ITokenSource tokenSource = opts.PregeneratedTokenFile is not null
                ? new PregeneratedTokenSource(opts.PregeneratedTokenFile)
                : opts.UseBypass
                    ? new LoadTestBypassTokenSource()
                    : new HttpLoginTokenSource(opts.AuthServerUrl);

            // Fixed-size array, allocated up front. A bot not yet started
            // just sits in State.NotStarted — Poll/CheckTimeout/MaybeMove
            // are all cheap no-ops for it — so the driver loop can safely
            // iterate the WHOLE array from tick one with no concurrent-
            // modification hazard against the ramp loop below.
            var bots = new BotClient[opts.BotCount];
            for (int i = 0; i < opts.BotCount; i++)
                bots[i] = new BotClient(i, opts.Host, opts.Port, metrics, opts.MoveIntervalSeconds);

            var cts = new CancellationTokenSource();

            Console.CancelKeyPress += (_, e) =>
            {
                e.Cancel = true;
                cts.Cancel();
            };

            var printTask = PrintLoopAsync(metrics, opts.BotCount, cts.Token);

            // Ramp loop: only responsible for STARTING bots on schedule
            // (which briefly awaits a token fetch — a short-lived Task,
            // not a long-lived loop). All ongoing per-tick work happens
            // in driverTask below, shared across every bot.
            var ramp = Task.Run(async () =>
            {
                for (int i = 0; i < opts.BotCount && !cts.IsCancellationRequested; i++)
                {
                    _ = bots[i].StartAsync(tokenSource, cts.Token);

                    if ((i + 1) % opts.RampPerSecond == 0)
                        await Task.Delay(1000, cts.Token);
                }
            }, cts.Token);

            // The one shared loop that replaces every bot's individual
            // polling/movement Tasks. 15ms cadence matches the old
            // per-bot poll interval; O(bots) per iteration is cheap even
            // at several thousand — this is exactly the kind of tight,
            // allocation-free loop a single thread handles fine, which is
            // the whole point of consolidating onto one.
            var driverTask = Task.Run(async () =>
            {
                while (!cts.IsCancellationRequested)
                {
                    var now = DateTime.UtcNow;
                    for (int i = 0; i < bots.Length; i++)
                    {
                        bots[i].Poll();
                        bots[i].CheckTimeout(now);
                        bots[i].MaybeMove(now);
                    }
                    await Task.Delay(15, cts.Token);
                }
            }, cts.Token);

            Console.WriteLine($"Ramping {opts.BotCount} bots at {opts.RampPerSecond}/sec against {opts.Host}:{opts.Port} ...");
            Console.WriteLine("Ctrl+C to stop.\n");

            try
            {
                await Task.WhenAll(ramp, driverTask, printTask);
            }
            catch (OperationCanceledException) { }
        }

        private static async Task PrintLoopAsync(Metrics metrics, int targetBots, CancellationToken ct)
        {
            Console.WriteLine("time      connected  spawned  disc  authFail  connTO  |  rx KB/s   tx KB/s  avg rx B/s per bot  pkts/s");

            while (!ct.IsCancellationRequested)
            {
                await Task.Delay(1000, ct);
                if (ct.IsCancellationRequested) break;

                var snap = metrics.Read();
                var (sent, received, packets) = metrics.DrainInterval();

                var rxKBs = received / 1024.0;
                var txKBs = sent / 1024.0;
                var perBot = snap.Spawned > 0 ? received / (double)snap.Spawned : 0;

                Console.WriteLine(
                    $"{DateTime.Now:HH:mm:ss}  {snap.Connected,9}  {snap.Spawned,7}  {snap.Disconnected,4}  {snap.AuthFailures,8}  {snap.ConnectTimeouts,6}  |  " +
                    $"{rxKBs,8:F1}  {txKBs,8:F1}  {perBot,10:F0}  {packets,7}");

                if (snap.Spawned >= Math.Min(50, targetBots) && perBot > 15_000)
                {
                    Console.WriteLine($"  [!] avg bytes/sec/bot ({perBot:F0}) is above the ~15,000 B/s target from INTEGRATION.md");
                }
            }
        }

        private sealed class Options
        {
            public string Host = "127.0.0.1";
            public int Port = 7777;
            public string AuthServerUrl = "http://127.0.0.1:5000";
            public int BotCount = 500;
            public int RampPerSecond = 50;
            public float MoveIntervalSeconds = 0.5f;
            public string? PregeneratedTokenFile;
            public bool UseBypass;
        }

        private static Options ParseArgs(string[] args)
        {
            var o = new Options();
            for (int i = 0; i < args.Length; i++)
            {
                if (args[i] == "--bypass") { o.UseBypass = true; continue; }
                if (i >= args.Length - 1) continue;

                switch (args[i])
                {
                    case "--host": o.Host = args[++i]; break;
                    case "--port": o.Port = int.Parse(args[++i]); break;
                    case "--auth": o.AuthServerUrl = args[++i]; break;
                    case "--bots": o.BotCount = int.Parse(args[++i]); break;
                    case "--ramp": o.RampPerSecond = int.Parse(args[++i]); break;
                    case "--move-interval": o.MoveIntervalSeconds = float.Parse(args[++i]); break;
                    case "--token-file": o.PregeneratedTokenFile = args[++i]; break;
                }
            }
            return o;
        }
    }
}