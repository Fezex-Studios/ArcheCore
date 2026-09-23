using ArcheCore.Server.World.GameData.Combat;
using ArcheCore.Server.World.GameData.Harvest;
using ArcheCore.Server.World.GameData.Interaction;
using ArcheCore.Server.World.GameData.Items;
using ArcheCore.Server.World.GameData.Loot;
using ArcheCore.Server.World.GameData.Npcs;
using ArcheCore.Server.World.GameData.Quests;
using ArcheCore.Server.World.GameData.Shops;
using ArcheCore.Server.World.GameData.TestTable;
using ArcheCore.Server.World.GameData.World.PlayerSpawn;
using Microsoft.EntityFrameworkCore;

namespace ArcheCore.Server.World.Utils.Database.SQLite;

public class WorldDataDbContext : DbContext
{
    public WorldDataDbContext(DbContextOptions<WorldDataDbContext> options)
        : base(options) { }

    public DbSet<QuestTable>          Quests          => Set<QuestTable>();
    public DbSet<QuestObjectiveTable> QuestObjectives => Set<QuestObjectiveTable>();
    public DbSet<NpcTemplate>     NpcTemplates => Set<NpcTemplate>();
    public DbSet<NpcSpawnerTable> NpcSpawners  => Set<NpcSpawnerTable>();
    public DbSet<TestTableOne>  TestTableOnes => Set<TestTableOne>();
    public DbSet<ItemStats>      ItemStats => Set<ItemStats>();
    public DbSet<ItemUse>        ItemUses  => Set<ItemUse>();
    public DbSet<ItemCategory>  ItemCategories => Set<ItemCategory>();
    public DbSet<ItemRarity>  ItemRarities => Set<ItemRarity>();
    public DbSet<ItemTable>       Items      => Set<ItemTable>();
    public DbSet<SpawnPointTable> SpawnPointTables => Set<SpawnPointTable>();

    // Harvesting (roadmap E)
    public DbSet<HarvestNodeTemplate> HarvestNodeTemplates => Set<HarvestNodeTemplate>();
    public DbSet<HarvestNodeSpawn>    HarvestNodeSpawns    => Set<HarvestNodeSpawn>();

    // NPC shops (roadmap F)
    public DbSet<ShopTemplate> Shops     => Set<ShopTemplate>();
    public DbSet<ShopItem>     ShopItems => Set<ShopItem>();

    // Combat and loot (roadmap H/I)
    public DbSet<SkillTemplate>  Skills           => Set<SkillTemplate>();
    public DbSet<LootTable>      LootTables       => Set<LootTable>();
    public DbSet<LootTableEntry> LootTableEntries => Set<LootTableEntry>();

    // Interaction actions (F/G) for every interactable
    public DbSet<InteractableAction> InteractableActions => Set<InteractableAction>();
}
