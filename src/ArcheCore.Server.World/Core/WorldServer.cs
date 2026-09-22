using ArcheCore.Library.Net.Worldserver;
using ArcheCore.Network.Client;
using ArcheCore.Network.Worldserver;
using ArcheCore.Server.World.Core.Services;
using ArcheCore.Server.World.Core.Services.Authservice;
using ArcheCore.Server.World.Utils.Config;
using ArcheCore.Server.World.Managers;
using ArcheCore.Server.World.Networking.C2W;
using ArcheCore.Server.World.Utils.Database.SQLite;
using LiteNetLib;
using MessagePack;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Options;
using Microsoft.Extensions.Logging;
using NLog;
using Shared.AuthService;
using Worldserver.ArcheCore.PersistenceServer.Scripts;

namespace ArcheCore.Server.World;
public class WorldServer : IHostedService, INetEventListener
{
    // Utils
    private static readonly Logger Logger = LogManager.GetCurrentClassLogger();
    private readonly WorldServerConfig _world;
    private readonly IHostEnvironment _hostEnvironment;
    private readonly NetworkConfig _network;
    private readonly IServiceScopeFactory _scopeFactory;
    private PacketDispatcher _packetDispatcher;
    private readonly GameDataPatchRunner _dataPatchRunner;
    private readonly IDbContextFactory<WorldDataDbContext> _dbFactory;

    // Managers
    private readonly QuestManager _questManager;
    private readonly ItemManager _itemManager;
    private NetManager _server;
    private PlayerManager _playerManager;
    private SpawnManager _spawnManager;
    private ReplicationManager _replicationManager;
    private InteractionRegistry _interactions;
    private InterestManager _interestManager;
    private NpcAiManager _npcAiManager;
    private HarvestManager _harvestManager;
    private ShopManager _shopManager;
    private CancellationTokenSource _tickCts;
    private Task _tickLoop;
    private PersistenceClient _persistenceClient;
    private DemoManager _demoManager;

    // Services
    private readonly DemoService _demoService;
    private readonly AuthService _authService;
    private readonly SpawnPointService _spawnPoints;

    private const string ConnectionKey = "MMO";

    // How long shutdown waits for the final character saves.
    private static readonly TimeSpan ShutdownSaveTimeout = TimeSpan.FromSeconds(10);

    public WorldServer(
        IOptions<WorldServerConfig> world,
        IHostEnvironment hostEnvironment,
        IServiceScopeFactory scopeFactory,
        IOptions<NetworkConfig> network,
        QuestManager questManager,
        ItemManager itemManager,
        DemoService demoService,
        AuthService authService,
        GameDataPatchRunner dataPatchRunner,
        IDbContextFactory<WorldDataDbContext> dbFactory,
        DemoManager demoManager,
        SpawnPointService spawnPoints)
    {
        _world = world.Value;
        _hostEnvironment = hostEnvironment;
        _network = network.Value;
        _questManager = questManager;
        _itemManager = itemManager;
        _scopeFactory = scopeFactory;
        _demoService = demoService;
        _authService = authService;
        _dataPatchRunner = dataPatchRunner;
        _dbFactory = dbFactory;
        _demoManager = demoManager;
        _spawnPoints = spawnPoints;
    }

    public async Task StartAsync(CancellationToken cancellationToken)
    {
        // 0a. Refuse to start on a config that looks fine but isn't.
        //     Runs before the migration and long before the socket opens,
        //     so a misconfigured server never reaches a state where a
        //     client could connect to it.
        _world.Validate(_hostEnvironment.IsDevelopment());

        // 0b. Apply EF Core migrations to worldserver.db
        await using var db = await _dbFactory.CreateDbContextAsync();
        await db.Database.MigrateAsync();

        // 0c. Apply SQL data patches (SQL/patches/*.sql, each once, in
        //     name order, tracked in schema_versions). After migrations, so
        //     a patch can always rely on the tables it fills existing.
        //     GameDataPatchRunner was constructed and injected before but
        //     never actually called - this is the call.
        await _dataPatchRunner.RunAsync();

        // 1. Connect to PersistenceServer
        _persistenceClient = new PersistenceClient(_world);
        await _persistenceClient.Start();

        // 2. Initialize managers
        // InterestManager is created here, not inside PlayerManager, because
        // SpawnManager needs the exact same instance - NPCs and players
        // share one grid (see SpawnManager.NpcIdBase).
        _replicationManager = new ReplicationManager();
        _interactions = new InteractionRegistry();
        _interestManager = new InterestManager();
        _spawnManager = new SpawnManager(_dbFactory, _interactions, _interestManager);
        _playerManager = new PlayerManager(_spawnManager, _replicationManager, _world, _persistenceClient, _demoManager, _interestManager, _itemManager);
        _playerManager.InitializeScripts();

        // Derive the snapshot LOD tiers from the interest radii. Must run
        // after both exist; nothing reads these values until the first
        // Flush, so the exact position within startup doesn't matter, but
        // keeping it here keeps the dependency visible instead of buried
        // further down.
        //
        // WHY THIS EXISTS: these two classes describe the same distances
        // from opposite directions, and holding independent numbers in two
        // files let them drift. MidRange was 80 while DespawnRadius was 85,
        // which put every entity in the 80-85 hysteresis band - still
        // visible, by definition, since that band is what stops spawn/
        // despawn flicker - into the slowest replication tier at 2Hz.
        _playerManager.Snapshots.ConfigureFromInterest(_interestManager);

        Logger.Info(
            "[LOD] near<{NearRange} mid<{MidRange} | interest spawn={SpawnRadius} despawn={DespawnRadius}",
            _playerManager.Snapshots.NearRange,
            _playerManager.Snapshots.MidRange,
            _interestManager.SpawnRadius,
            _interestManager.DespawnRadius);

        // NpcAiManager has no thread of its own - RunTickLoopAsync calls
        // Tick() once per tick, on the same thread as everything else that
        // touches InterestManager/SpatialGrid (neither is thread-safe).
        _npcAiManager = new NpcAiManager(_spawnManager, _interestManager, _replicationManager, _playerManager);

        // 3. Load game data
        _questManager.LoadFromDatabase();
        _itemManager.LoadFromDatabase();
        await _spawnPoints.LoadAsync();

        // Both check their item ids against ItemManager, so they load after it.
        _harvestManager = new HarvestManager(
            _dbFactory, _interactions, _interestManager, _spawnManager,
            _itemManager, _playerManager, _replicationManager);
        _shopManager = new ShopManager(_dbFactory, _itemManager, _playerManager, _spawnManager);
        _shopManager.LoadFromDatabase();

        // 4. Register packets
        _packetDispatcher = new PacketDispatcher();
        RegisterPackets();

        // 5. Start network
        _server = new NetManager(this);
        _server.Start(_network.Port);

        Logger.Info(
            "World started | {Host}:{Port} | TickRate={TickRate} | MaxPlayers={MaxPlayers}",
            _network.Host, _network.Port, _world.TickRate, _world.MaxPlayers);

        // 6. Load NPC spawner definitions (all dormant until a player is near).
        _spawnManager.LoadSpawnerDefinitions();

        // 6b. Place every harvest node in the interest grid. Before the tick
        //     loop starts, so the boot thread is the only one touching it.
        _harvestManager.LoadAndSpawnAll();

        // 7. Services
        await _demoService.RunService();

        // 8. Reset NPC AI timers, then start the tick loop (which drives NPC AI).
        _npcAiManager.Start();
        _tickCts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        _tickLoop = RunTickLoopAsync(_tickCts.Token);
    }

    private async Task RunTickLoopAsync(CancellationToken ct)
    {
        var interval = TimeSpan.FromMilliseconds(1000.0 / _world.TickRate);
        using var timer = new PeriodicTimer(interval);
        uint tick = 0;

        var health = new TickHealthMonitor(_world.TickRate, msg => Logger.Info(msg));
        var stopwatch = new System.Diagnostics.Stopwatch();

        try
        {
            while (!ct.IsCancellationRequested && await timer.WaitForNextTickAsync(ct))
            {
                stopwatch.Restart();
                try
                {
                    tick++;

                    // Before PollEvents: movement handlers timestamp with TickClock.
                    _playerManager.AdvanceTick(tick);

                    _playerManager.DrainActions();

                    // Every C2W handler runs inside here. OnNetworkReceive
                    // catches per-packet exceptions, so one bad packet can no
                    // longer abort the rest of this block for everyone.
                    _server?.PollEvents();

                    // NPC wander AI + spawner activation (internally rate-limited).
                    _npcAiManager.Tick();

                    // Harvest timers, move-to-cancel, node respawns.
                    _harvestManager.Tick();

                    // Spread-out periodic saves of dirty characters.
                    _playerManager.RunAutosave(tick);

                    // The only place a position packet leaves the server.
                    _playerManager.FlushSnapshots(tick);
                }
                catch (Exception ex)
                {
                    Logger.Error(ex, "[TickLoop] Unhandled exception in tick — continuing.");
                }
                finally
                {
                    health.Record(stopwatch.Elapsed.TotalMilliseconds);
                }
            }
        }
        catch (OperationCanceledException)
        {
            // Normal shutdown.
        }
    }

    public async Task StopAsync(CancellationToken cancellationToken)
    {
        Logger.Info("World stopping");
        _npcAiManager?.Stop();

        // 1. Stop the tick loop and WAIT for it, so nothing else touches
        //    sessions while we save them below.
        _tickCts?.Cancel();
        if (_tickLoop != null)
        {
            try { await _tickLoop; }
            catch (Exception ex) { Logger.Error(ex, "[Shutdown] Tick loop ended with an error."); }
        }

        // 2. Apply anything still queued (e.g. spawns that just finished loading).
        _playerManager?.DrainActions();

        // 3. Final save for everyone still online.
        if (_playerManager != null)
        {
            var saveAll = _playerManager.SaveAllAsync();
            var finished = await Task.WhenAny(saveAll, Task.Delay(ShutdownSaveTimeout, CancellationToken.None));
            if (finished != saveAll)
                Logger.Error($"[Shutdown] Final saves did not finish within {ShutdownSaveTimeout.TotalSeconds:F0}s.");
        }

        // 4. Only now drop the connections.
        _server?.Stop();
        _persistenceClient?.Dispose();
    }

    /// <summary>
    /// Wires every [PacketOpcode]-tagged handler in this assembly into
    /// _packetDispatcher automatically. ServiceContainer is the single
    /// source of truth for every dependency a handler constructor can ask for.
    /// </summary>
    private void RegisterPackets()
    {
        var services = new ServiceContainer();
        services.Register(_playerManager);
        services.Register(_persistenceClient);
        services.Register(_interestManager);   // the one real InterestManager - shared with SpawnManager
        services.Register(_replicationManager);
        services.Register(_authService);
        services.Register(_interactions);
        services.Register(_spawnPoints);
        services.Register(_spawnManager);
        services.Register(_itemManager);
        services.Register(_world);
        services.Register(_playerManager.Jumps);
        services.Register(_harvestManager);
        services.Register(_shopManager);

        _packetDispatcher.AutoRegister(services.Resolve, typeof(WorldServer).Assembly);
    }

    public void OnPeerConnected(NetPeer peer)
    {
        Logger.Info($"Client connected: {peer.Address}");
    }

    public void OnPeerDisconnected(NetPeer peer, DisconnectInfo info)
    {
        _playerManager.HandlePlayerDisconnected(peer);
    }

    public void OnConnectionRequest(ConnectionRequest request)
    {
        request.AcceptIfKey(ConnectionKey);
    }

    /// <summary>
    /// Runs inside PollEvents on the tick thread. Every packet is isolated:
    ///  - too short to hold an opcode         -> kick (not a real client)
    ///  - payload that fails to deserialize    -> kick (malformed / tampered)
    ///  - any other exception (a handler bug) -> log, keep the player
    /// Either way the exception stops here, so the rest of the tick (other
    /// players' packets, NPC AI, autosave, snapshots) still runs.
    /// </summary>
    public void OnNetworkReceive(
        NetPeer peer,
        NetPacketReader reader,
        byte channel,
        DeliveryMethod delivery)
    {
        Opcodes packet = 0;

        try
        {
            if (reader.AvailableBytes < sizeof(ushort))
            {
                Logger.Warn($"[Net] Packet too short ({reader.AvailableBytes} bytes) from {peer.Address} — disconnecting");
                peer.Disconnect();
                return;
            }

            packet = (Opcodes)reader.GetUShort();
            _packetDispatcher.Handle(packet, peer, reader);
        }
        catch (MessagePackSerializationException ex)
        {
            Logger.Warn($"[Net] Malformed {packet} packet from {peer.Address} — disconnecting. {ex.Message}");
            peer.Disconnect();
        }
        catch (Exception ex)
        {
            Logger.Error(ex, $"[Net] Handler for {packet} threw (peer {peer.Address}) — packet dropped.");
        }
        finally
        {
            reader.Recycle();
        }
    }

    public void OnNetworkError(
        System.Net.IPEndPoint endPoint,
        System.Net.Sockets.SocketError error)
    {
        Logger.Warn($"Network Error: {error}");
    }

    public void OnNetworkLatencyUpdate(NetPeer peer, int latency) { }

    public void OnNetworkReceiveUnconnected(
        System.Net.IPEndPoint endPoint,
        NetPacketReader reader,
        UnconnectedMessageType messageType)
    {
    }
}