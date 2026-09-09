using ArcheCore.Server.World.GameData.Items;
using ArcheCore.Server.World.GameData.Npcs;
using ArcheCore.Server.World.GameData.Quests;
using ArcheCore.Server.World.GameData.TestTable;
using ArcheCore.Server.World.GameData.World.PlayerSpawn;
using Microsoft.EntityFrameworkCore;

namespace ArcheCore.Server.World.Utils.Database.SQLite;

public class WorldDataDbContext : DbContext
{
    public WorldDataDbContext(DbContextOptions<WorldDataDbContext> options)
        : base(options) { }

    public DbSet<QuestTable>      Quests       => Set<QuestTable>();
    public DbSet<NpcTemplate>     NpcTemplates => Set<NpcTemplate>();
    public DbSet<NpcSpawnerTable> NpcSpawners  => Set<NpcSpawnerTable>();
    public DbSet<TestTableOne>  TestTableOnes => Set<TestTableOne>();
    public DbSet<ItemStats>      ItemStats => Set<ItemStats>();
    public DbSet<ItemCategory>  ItemCategories => Set<ItemCategory>();
    public DbSet<ItemRarity>  ItemRarities => Set<ItemRarity>();
    public DbSet<ItemTable>       Items      => Set<ItemTable>();
    public DbSet<SpawnPointTable> SpawnPointTables => Set<SpawnPointTable>();
}