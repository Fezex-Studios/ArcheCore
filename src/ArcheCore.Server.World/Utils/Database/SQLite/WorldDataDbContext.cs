using ArcheCore.Server.World.GameData.Items;
using ArcheCore.Server.World.GameData.Npcs;
using ArcheCore.Server.World.GameData.Quests;
using Microsoft.EntityFrameworkCore;

namespace ArcheCore.Server.World.Utils.Database.SQLite;

public class WorldDataDbContext : DbContext
{
    public WorldDataDbContext(DbContextOptions<WorldDataDbContext> options)
        : base(options) { }

    public DbSet<QuestTable>      Quests       => Set<QuestTable>();
    public DbSet<ItemTable>       Items        => Set<ItemTable>();
    public DbSet<NpcTemplate>     NpcTemplates => Set<NpcTemplate>();
    public DbSet<NpcSpawnerTable> NpcSpawners  => Set<NpcSpawnerTable>();
}