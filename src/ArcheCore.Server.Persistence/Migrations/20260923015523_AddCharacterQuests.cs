using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace ArcheCore.Server.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddCharacterQuests : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "character_quests",
                columns: table => new
                {
                    character_id = table.Column<long>(type: "bigint", nullable: false),
                    quest_id = table.Column<int>(type: "int", nullable: false),
                    status = table.Column<byte>(type: "tinyint unsigned", nullable: false),
                    progress = table.Column<string>(type: "varchar(128)", maxLength: 128, nullable: false)
                        .Annotation("MySql:CharSet", "utf8mb4")
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_character_quests", x => new { x.character_id, x.quest_id });
                })
                .Annotation("MySql:CharSet", "utf8mb4");

            migrationBuilder.CreateIndex(
                name: "IX_character_quests_character_id",
                table: "character_quests",
                column: "character_id");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "character_quests");
        }
    }
}
