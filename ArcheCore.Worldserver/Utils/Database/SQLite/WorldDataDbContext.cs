using ArcheCore.Worldserver.GameData.Quests;
using Microsoft.EntityFrameworkCore;

namespace ArcheCore.Worldserver.Utils.Database.SQLite;

public class WorldDataDbContext: DbContext
{
    public WorldDataDbContext(DbContextOptions<WorldDataDbContext> options)
        : base(options)
    {
    }

    public DbSet<QuestTable> Quests => Set<QuestTable>();
}