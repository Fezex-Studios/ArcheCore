using ArcheCore.Worldserver.GameData.Quests;
using ArcheCore.Worldserver.Utils.Database.SQLite;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

public class QuestManager
{
    private readonly IServiceScopeFactory _scopeFactory;
    private readonly ILogger<QuestManager> _logger;

    public QuestManager(IServiceScopeFactory scopeFactory, ILogger<QuestManager> logger)
    {
        _scopeFactory = scopeFactory;
        _logger = logger;
    }

    public void LoadFromDatabase()
    {
        using var scope = _scopeFactory.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<WorldDataDbContext>();

        db.Database.Migrate();

        var quests = db.Quests.ToList();

        Load(quests);

        _logger.LogInformation("Loaded {Count} quests from SQLite", quests.Count);
    }

    public void Load(List<QuestTable> quests)
    {
        // in-memory processing only
    }
}