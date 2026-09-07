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
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Options;
using Microsoft.Extensions.Logging;
using NLog;
using Shared.AuthService;
using Worldserver.ArcheCore.PersistenceServer.Scripts;

namespace ArcheCore.Server.World;
public class WorldServer : IHostedService,INetEventListener
{
    // Utils
    private static readonly Logger Logger = LogManager.GetCurrentClassLogger();
    private readonly WorldServerConfig _world;
    private readonly NetworkConfig _network;
    private readonly IServiceScopeFactory _scopeFactory;
    private PacketDispatcher _packetDispatcher;
    private readonly GameDataPatchRunner _dataPatchRunner;
    private readonly IDbContextFactory<WorldDataDbContext> _dbFactory;  // add

    
    // Managers
    private readonly QuestManager _questManager;
    private NetManager _server;
    private PlayerManager _playerManager;
    private SpawnManager _spawnManager;
    private ReplicationManager _replicationManager;
    private InteractionRegistry _interactions;
    private CancellationTokenSource _tickCts;
    private PersistenceClient _persistenceClient;
    private DemoManager _demoManager;
    
    
    // Services
    private readonly DemoService _demoService;
    private readonly AuthService _authService;
    
    
    private const string ConnectionKey = "MMO";
    

    public WorldServer(
        
        IOptions<WorldServerConfig> world,
        IServiceScopeFactory scopeFactory,
        IOptions<NetworkConfig> network,
        QuestManager questManager,
        DemoService demoService,
        AuthService authService,
        GameDataPatchRunner dataPatchRunner,
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
        _dataPatchRunner = dataPatchRunner;
        _dbFactory = dbFactory;
        _demoManager = demoManager;
    }

    public async Task StartAsync(CancellationToken cancellationToken)
    {
        // 0. Run DB patches first
        await _dataPatchRunner.RunAsync();

        // 1. Connect to PersistenceServer
        _persistenceClient = new PersistenceClient(_world);
        await _persistenceClient.Start();

        // 2. Initialize managers
        _replicationManager = new ReplicationManager();
        _interactions = new InteractionRegistry();
        _spawnManager = new SpawnManager(_replicationManager, _dbFactory, _interactions);  // pass registry
        _playerManager = new PlayerManager(_spawnManager, _replicationManager, _world,_persistenceClient,_demoManager);
        _playerManager.InitializeScripts();

        // 3. Load game data
        _questManager.LoadFromDatabase();

        // 4. Register packets
        _packetDispatcher = new PacketDispatcher();
        RegisterPackets();

        // 5. Start network
        _server = new NetManager(this);
        _server.Start(_network.Port);

        Logger.Info(
            "World started | {Host}:{Port} | TickRate={TickRate} | MaxPlayers={MaxPlayers}",
            _network.Host, _network.Port, _world.TickRate, _world.MaxPlayers);

        // 6. Spawn world objects from DB
        _spawnManager.SpawnInitialObjects();  // renamed from SpawnInitialCubes

        // 7. Services
        await _demoService.RunService();

        // 8. Start tick loop
        _tickCts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        _ = RunTickLoopAsync(_tickCts.Token);
    }

    private async Task RunTickLoopAsync(CancellationToken ct)
    {
        var interval = TimeSpan.FromMilliseconds(1000.0 / _world.TickRate);
        using var timer = new PeriodicTimer(interval);

        while (!ct.IsCancellationRequested && await timer.WaitForNextTickAsync(ct))
        {
            try
            {
                _playerManager.DrainActions();
                _server?.PollEvents();
            }
            catch (Exception ex)
            {
                Logger.Error(ex, "[TickLoop] Unhandled exception in tick — continuing.");
            }
        }
    }

    public Task StopAsync(CancellationToken cancellationToken)
    {
        Logger.Info("World stopping");
        _tickCts?.Cancel();
        _server?.Stop();
        return Task.CompletedTask;
    }
    /// <summary>
    /// Wires every [PacketOpcode]-tagged handler in this assembly into
    /// _packetDispatcher automatically. Adding a new packet handler no
    /// longer requires an edit here at all — see
    /// ArcheCore.Server.World/ADDING_PACKETS.md.
    ///
    /// ServiceContainer is the single source of truth for every dependency
    /// a handler constructor can ask for. If a handler needs something new,
    /// register it here once; AutoRegister resolves the rest by reflection.
    /// </summary>
    private void RegisterPackets()
    {
        var services = new ServiceContainer();
        services.Register(_playerManager);
        services.Register(_persistenceClient);
        services.Register(_playerManager.Interest);   // the one real InterestManager
        services.Register(_replicationManager);
        services.Register(_authService);
        services.Register(_interactions);

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
    public void OnNetworkReceive(
        NetPeer peer,
        NetPacketReader reader,
        byte channel,
        DeliveryMethod delivery)
    {
        Opcodes packet = (Opcodes)reader.GetUShort();
        _packetDispatcher.Handle(packet, peer, reader);
        reader.Recycle();
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