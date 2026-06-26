using ArcheCore.Worldserver.Core.Services;
using ArcheCore.Worldserver.Utils.Config;
using ArcheCore.Worldserver.Utils.Database.SQLite;
using LiteNetLib;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Options;
using Microsoft.Extensions.Logging;


public class WorldServer : IHostedService,INetEventListener
{
    private readonly ILogger<WorldServer> _logger;
    private readonly WorldServerConfig _world;
    private readonly QuestManager _questManager;
    private readonly NetworkConfig _network;
    private readonly IServiceScopeFactory _scopeFactory;
    private readonly DemoService _demoService;

    public WorldServer(
        ILogger<WorldServer> logger,
        IOptions<WorldServerConfig> world,
        IServiceScopeFactory scopeFactory,
        IOptions<NetworkConfig> network,
        QuestManager questManager,
        DemoService demoService
        )
    {
        _logger = logger;
        _world = world.Value;
        _network = network.Value;
        _questManager = questManager;
        _scopeFactory = scopeFactory;
        _demoService = demoService;
    }

    public async Task StartAsync(CancellationToken cancellationToken)
    {
        _logger.LogInformation(
            "World starting | TickRate={TickRate} | MaxPlayers={MaxPlayers}",
            _world.TickRate,
            _world.MaxPlayers);
        
        _logger.LogInformation("Network Listening on {Host}:{Port}",
            _network.Host,
            _network.Port
            );
       _questManager.LoadFromDatabase();
       
        await _demoService.RunService();
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
        _logger.LogInformation("World stopping");
        _tickCts?.Cancel();
        _server?.Stop();
        return Task.CompletedTask;
    }
    private void RegisterPackets()
    {
        packetDispatcher.Register(
            Opcode.Authenticate,
            new C2WAuthenticateHandler(playerManager));

        packetDispatcher.Register(
            Opcode.PlayerMove,
            new C2WMovementHandler(playerManager));
    }
    public void OnPeerConnected(NetPeer peer)
    {
        _logger.LogInformation($"Client connected: {peer.Address}");
    }
    public void OnPeerDisconnected(NetPeer peer, DisconnectInfo info)
    {
        playerManager.HandlePlayerDisconnected(peer);
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
        Opcode packet = (Opcode)reader.GetUShort();
        packetDispatcher.Handle(packet, peer, reader);
        reader.Recycle();
    }
    public void OnNetworkError(
        System.Net.IPEndPoint endPoint,
        System.Net.Sockets.SocketError error)
    {
        WorldLogger.Warning($"Network Error: {error}");
    }
    public void OnNetworkLatencyUpdate(NetPeer peer, int latency) { }

    public void OnNetworkReceiveUnconnected(
        System.Net.IPEndPoint endPoint,
        NetPacketReader reader,
        UnconnectedMessageType messageType)
    {
        
    }
}