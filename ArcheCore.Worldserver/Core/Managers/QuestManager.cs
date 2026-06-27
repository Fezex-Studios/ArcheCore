using ArcheCore.Worldserver.GameData.Quests;
using ArcheCore.Worldserver.Utils.Database.SQLite;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using NLog;

public class QuestManager
{
    private readonly IServiceScopeFactory _scopeFactory;
    private static readonly Logger  Logger = LogManager.GetCurrentClassLogger();

    public QuestManager(IServiceScopeFactory scopeFactory, ILogger<QuestManager> logger)
    {
        _scopeFactory = scopeFactory;
        
    }

    public void LoadFromDatabase()
    {
        using var scope = _scopeFactory.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<WorldDataDbContext>();

        db.Database.Migrate();

        var quests = db.Quests.ToList();

        Load(quests);

        Logger.Info("Loaded {Count} quests from SQLite", quests.Count);
    }

    public void Load(List<QuestTable> quests)
    {
        // in-memory processing only
    }
}