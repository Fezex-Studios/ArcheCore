using ArcheCore.Library.Net.Worldserver;
using ArcheCore.Net.Client;
using ArcheCore.Net.Worldserver;
using ArcheCore.Worldserver.Core.Services;
using ArcheCore.Worldserver.Core.Services.Authservice;
using ArcheCore.WorldServer.Managers;
using ArcheCore.WorldServer.Networking.C2W;
using ArcheCore.Worldserver.Utils.Config;
using ArcheCore.Worldserver.Utils.Database.SQLite;
using LiteNetLib;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Options;
using Microsoft.Extensions.Logging;
using NLog;
using Shared.AuthService;
using Worldserver.ArcheCore.PersistenceServer.Scripts;

namespace ArcheCore.Worldserver;
public class WorldServer : IHostedService,INetEventListener
{
    // Utils
    private static readonly Logger Logger = LogManager.GetCurrentClassLogger();
    private readonly WorldServerConfig _world;
    private readonly NetworkConfig _network;
    private readonly IServiceScopeFactory _scopeFactory;
    private PacketDispatcher _packetDispatcher;
    
    // Managers
    private readonly QuestManager _questManager;
    private NetManager _server;
    private PlayerManager _playerManager;
    private SpawnManager _spawnManager;
    private ReplicationManager _replicationManager;
    private CancellationTokenSource _tickCts;
    
    
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
        AuthService authService
        )
    {
       
        _world = world.Value;
        _network = network.Value;
        _questManager = questManager;
        _scopeFactory = scopeFactory;
        _demoService = demoService;
        _authService = authService;
    }

    public async Task StartAsync(CancellationToken cancellationToken)
    {
        // 1. Connect to PersistenceServer first — other systems depend on it
        var persistenceClient = new PersistenceClient(_world);
        await persistenceClient.Start();

        // 2. Initialize managers
        _replicationManager = new ReplicationManager();
        _spawnManager = new SpawnManager(_replicationManager);
        _playerManager = new PlayerManager(_spawnManager, _replicationManager, _world);
        _playerManager.InitializeScripts();

        // 3. Load game data
        _questManager.LoadFromDatabase();

        // 4. Register packets
        _packetDispatcher = new PacketDispatcher();
        RegisterPackets();

        // 5. Start network — only now can clients connect
        _server = new NetManager(this);
        _server.Start(_network.Port);

        Logger.Info(
            "World started | {Host}:{Port} | TickRate={TickRate} | MaxPlayers={MaxPlayers}",
            _network.Host, _network.Port, _world.TickRate, _world.MaxPlayers);

        // 6. Spawn world objects — server is running, replication is ready
        _spawnManager.SpawnInitialCubes();

        // 7. Services
        await _demoService.RunService();

        // 8. Start tick loop last
        _tickCts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        _ = RunTickLoopAsync(_tickCts.Token);
    }

    private async Task RunTickLoopAsync(CancellationToken ct)
    {
        var interval = TimeSpan.FromMilliseconds(1000.0 / _world.TickRate);
        using var timer = new PeriodicTimer(interval);

        while (!ct.IsCancellationRequested && await timer.WaitForNextTickAsync(ct))
        {
            _playerManager.DrainActions();
            _server?.PollEvents();
        }
    }

    public Task StopAsync(CancellationToken cancellationToken)
    {
        Logger.Info("World stopping");
        _tickCts?.Cancel();
        _server?.Stop();
        return Task.CompletedTask;
    }
    private void RegisterPackets()
    {
        
        _packetDispatcher.Register(Opcodes.Authenticate, new C2WAuthenticateHandler(_playerManager, _authService));
        _packetDispatcher.Register(Opcodes.PlayerMove,   new C2WMovementHandler(_playerManager));
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