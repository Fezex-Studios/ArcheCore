using ArcheCore.Server.World.GameData.Items;
using ArcheCore.Server.World.Utils.Database.SQLite;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using NLog;

namespace ArcheCore.Server.World.Managers;

public class ItemManager
{
    private readonly IServiceScopeFactory _scopeFactory;
    private static readonly Logger Logger = LogManager.GetCurrentClassLogger();

    private Dictionary<int, ItemTable> _items = new();

    public ItemManager(IServiceScopeFactory scopeFactory)
    {
        _scopeFactory = scopeFactory;
    }

    public void LoadFromDatabase()
    {
        using var scope = _scopeFactory.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<WorldDataDbContext>();

        var items = db.Items.ToList();
        _items = items.ToDictionary(i => i.item_id);

        Logger.Info("Loaded {Count} items from worldserver.db", items.Count);
    }

    public ItemTable GetById(int itemId)
    {
        return _items.TryGetValue(itemId, out var item) ? item : null;
    }
}
