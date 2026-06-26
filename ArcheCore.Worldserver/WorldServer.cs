using ArcheCore.Worldserver.Core.Managers;
using ArcheCore.Worldserver.Core.Services;
using ArcheCore.Worldserver.Utils.Config;
using ArcheCore.Worldserver.Utils.Database.SQLite;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Options;
using Microsoft.Extensions.Logging;


public class WorldServer : IHostedService
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
        using var scope = _scopeFactory.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<WorldDataDbContext>();
        db.Database.EnsureCreated();
        var quests = db.Quests.ToList();
        _questManager.Load(quests);

        _logger.LogInformation("Loaded {Count} quests from SQLite", quests.Count);
        await _demoService.RunService();
    }

    public Task StopAsync(CancellationToken cancellationToken)
    {
        _logger.LogInformation("World stopping");
        return Task.CompletedTask;
    }
}