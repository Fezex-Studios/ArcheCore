using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace ArcheCore.Server.World.Migrations
{
    /// <inheritdoc />
    public partial class AddCombatandLoot : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<int>(
                name: "LootTableId",
                table: "NpcTemplates",
                type: "INTEGER",
                nullable: false,
                defaultValue: 0);

            migrationBuilder.AddColumn<int>(
                name: "MaxHealth",
                table: "NpcTemplates",
                type: "INTEGER",
                nullable: false,
                defaultValue: 0);

            migrationBuilder.AddColumn<int>(
                name: "RespawnSeconds",
                table: "NpcTemplates",
                type: "INTEGER",
                nullable: false,
                defaultValue: 0);

            migrationBuilder.CreateTable(
                name: "LootTableEntries",
                columns: table => new
                {
                    Id = table.Column<int>(type: "INTEGER", nullable: false)
                        .Annotation("Sqlite:Autoincrement", true),
                    LootTableId = table.Column<int>(type: "INTEGER", nullable: false),
                    ItemId = table.Column<int>(type: "INTEGER", nullable: false),
                    Chance = table.Column<double>(type: "REAL", nullable: false),
                    MinQuantity = table.Column<int>(type: "INTEGER", nullable: false),
                    MaxQuantity = table.Column<int>(type: "INTEGER", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_LootTableEntries", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "LootTables",
                columns: table => new
                {
                    Id = table.Column<int>(type: "INTEGER", nullable: false)
                        .Annotation("Sqlite:Autoincrement", true),
                    Name = table.Column<string>(type: "TEXT", nullable: false),
                    MinGold = table.Column<int>(type: "INTEGER", nullable: false),
                    MaxGold = table.Column<int>(type: "INTEGER", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_LootTables", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "Skills",
                columns: table => new
                {
                    Id = table.Column<int>(type: "INTEGER", nullable: false)
                        .Annotation("Sqlite:Autoincrement", true),
                    Name = table.Column<string>(type: "TEXT", nullable: false),
                    Range = table.Column<float>(type: "REAL", nullable: false),
                    CooldownMs = table.Column<int>(type: "INTEGER", nullable: false),
                    MinDamage = table.Column<int>(type: "INTEGER", nullable: false),
                    MaxDamage = table.Column<int>(type: "INTEGER", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Skills", x => x.Id);
                });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "LootTableEntries");

            migrationBuilder.DropTable(
                name: "LootTables");

            migrationBuilder.DropTable(
                name: "Skills");

            migrationBuilder.DropColumn(
                name: "LootTableId",
                table: "NpcTemplates");

            migrationBuilder.DropColumn(
                name: "MaxHealth",
                table: "NpcTemplates");

            migrationBuilder.DropColumn(
                name: "RespawnSeconds",
                table: "NpcTemplates");
        }
    }
}
