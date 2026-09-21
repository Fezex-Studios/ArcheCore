using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace ArcheCore.Server.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddCharacterInventory : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "character_inventory",
                columns: table => new
                {
                    character_id = table.Column<long>(type: "bigint", nullable: false),
                    slot = table.Column<int>(type: "int", nullable: false),
                    item_template_id = table.Column<int>(type: "int", nullable: false),
                    quantity = table.Column<int>(type: "int", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_character_inventory", x => new { x.character_id, x.slot });
                    table.CheckConstraint("CK_character_inventory_item_positive", "item_template_id > 0 AND quantity > 0");
                    table.ForeignKey(
                        name: "FK_character_inventory_characters_character_id",
                        column: x => x.character_id,
                        principalTable: "characters",
                        principalColumn: "character_id",
                        onDelete: ReferentialAction.Cascade);
                })
                .Annotation("MySql:CharSet", "utf8mb4");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "character_inventory");
        }
    }
}
