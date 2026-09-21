using ArcheCore.Server.World.GameData.Items;
using ArcheCore.Server.World.Utils.Database.SQLite;
using Microsoft.Extensions.DependencyInjection;
using NLog;

namespace ArcheCore.Server.World.Managers;

/// <summary>
/// In-memory item data, loaded once at boot from worldserver.db. Every
/// runtime lookup - "does this item exist", "is it usable", "what shares
/// its cooldown" - is a dictionary hit, never a DB query on the tick thread.
/// </summary>
public class ItemManager
{
    private readonly IServiceScopeFactory _scopeFactory;
    private static readonly Logger Logger = LogManager.GetCurrentClassLogger();

    private Dictionary<int, ItemTable> _items = new();
    private Dictionary<int, ItemUse> _uses = new();
    private Dictionary<int, int[]> _cooldownGroups = new();

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

        var uses = db.ItemUses.ToList();
        _uses = new Dictionary<int, ItemUse>();
        foreach (var use in uses)
        {
            if (!_items.ContainsKey(use.ItemId))
            {
                // A use row for an item that doesn't exist is a data bug,
                // not a crash - skip it loudly so it's found at boot.
                Logger.Warn("[ItemManager] ItemUse {Id} points at missing item {ItemId} - skipped", use.Id, use.ItemId);
                continue;
            }

            if (!_uses.TryAdd(use.ItemId, use))
                Logger.Warn("[ItemManager] Item {ItemId} has more than one ItemUse row - using the first", use.ItemId);
        }

        _cooldownGroups = _uses.Values
            .Where(u => u.CooldownGroup != 0)
            .GroupBy(u => u.CooldownGroup)
            .ToDictionary(g => g.Key, g => g.Select(u => u.ItemId).ToArray());

        Logger.Info("Loaded {Count} items ({Usable} usable) from worldserver.db", items.Count, _uses.Count);
    }

    public ItemTable GetById(int itemId)
    {
        return _items.TryGetValue(itemId, out var item) ? item : null;
    }

    /// <summary>True if itemId is a real row in Items. TryAddItem's gate.</summary>
    public bool Exists(int itemId) => _items.ContainsKey(itemId);

    /// <summary>Null means the item isn't usable.</summary>
    public ItemUse GetUse(int itemId)
    {
        return _uses.TryGetValue(itemId, out var use) ? use : null;
    }

    /// <summary>
    /// The key a cooldown is stored under. Group 0 means "own group", so
    /// the item id is used, negated so it can never collide with a real
    /// (positive) group id.
    /// </summary>
    public static int CooldownKey(ItemUse use) =>
        use.CooldownGroup != 0 ? use.CooldownGroup : -use.ItemId;

    /// <summary>Every item id that shares this use's cooldown, itself included.</summary>
    public int[] ItemsSharingCooldown(ItemUse use)
    {
        if (use.CooldownGroup != 0 && _cooldownGroups.TryGetValue(use.CooldownGroup, out var ids))
            return ids;

        return new[] { use.ItemId };
    }
}