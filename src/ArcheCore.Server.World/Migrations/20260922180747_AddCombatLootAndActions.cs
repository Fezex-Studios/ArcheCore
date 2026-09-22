using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace ArcheCore.Server.World.Migrations
{
    /// <inheritdoc />
    public partial class AddCombatLootAndActions : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "Greeting",
                table: "NpcTemplates",
                type: "TEXT",
                nullable: false,
                defaultValue: "");

            migrationBuilder.AddColumn<string>(
                name: "Title",
                table: "NpcTemplates",
                type: "TEXT",
                nullable: false,
                defaultValue: "");

            migrationBuilder.CreateTable(
                name: "InteractableActions",
                columns: table => new
                {
                    Id = table.Column<int>(type: "INTEGER", nullable: false)
                        .Annotation("Sqlite:Autoincrement", true),
                    TargetKind = table.Column<int>(type: "INTEGER", nullable: false),
                    TemplateId = table.Column<int>(type: "INTEGER", nullable: false),
                    Slot = table.Column<int>(type: "INTEGER", nullable: false),
                    ActionType = table.Column<int>(type: "INTEGER", nullable: false),
                    Label = table.Column<string>(type: "TEXT", nullable: false),
                    IconName = table.Column<string>(type: "TEXT", nullable: false),
                    CursorName = table.Column<string>(type: "TEXT", nullable: false),
                    IsEnabled = table.Column<bool>(type: "INTEGER", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_InteractableActions", x => x.Id);
                });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "InteractableActions");

            migrationBuilder.DropColumn(
                name: "Greeting",
                table: "NpcTemplates");

            migrationBuilder.DropColumn(
                name: "Title",
                table: "NpcTemplates");
        }
    }
}
