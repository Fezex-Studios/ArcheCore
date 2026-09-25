using Microsoft.EntityFrameworkCore.Metadata;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace ArcheCore.Server.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class GeneralMailbox : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "cash_shop_mail");

            migrationBuilder.AddColumn<bool>(
                name: "is_giftable",
                table: "cash_shop_items",
                type: "tinyint(1)",
                nullable: false,
                defaultValue: false);

            migrationBuilder.CreateTable(
                name: "mail",
                columns: table => new
                {
                    id = table.Column<long>(type: "bigint", nullable: false)
                        .Annotation("MySql:ValueGenerationStrategy", MySqlValueGenerationStrategy.IdentityColumn),
                    character_id = table.Column<long>(type: "bigint", nullable: false),
                    sender = table.Column<string>(type: "varchar(64)", maxLength: 64, nullable: false)
                        .Annotation("MySql:CharSet", "utf8mb4"),
                    subject = table.Column<string>(type: "varchar(128)", maxLength: 128, nullable: false)
                        .Annotation("MySql:CharSet", "utf8mb4"),
                    gold = table.Column<int>(type: "int", nullable: false),
                    item_template_id = table.Column<int>(type: "int", nullable: false),
                    item_quantity = table.Column<int>(type: "int", nullable: false),
                    created_at_ticks = table.Column<long>(type: "bigint", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_mail", x => x.id);
                })
                .Annotation("MySql:CharSet", "utf8mb4");

            migrationBuilder.CreateTable(
                name: "mail_receipts",
                columns: table => new
                {
                    delivery_key = table.Column<string>(type: "varchar(64)", maxLength: 64, nullable: false)
                        .Annotation("MySql:CharSet", "utf8mb4"),
                    created_at_ticks = table.Column<long>(type: "bigint", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_mail_receipts", x => x.delivery_key);
                })
                .Annotation("MySql:CharSet", "utf8mb4");

            migrationBuilder.CreateIndex(
                name: "IX_mail_character_id",
                table: "mail",
                column: "character_id");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "mail");

            migrationBuilder.DropTable(
                name: "mail_receipts");

            migrationBuilder.DropColumn(
                name: "is_giftable",
                table: "cash_shop_items");

            migrationBuilder.CreateTable(
                name: "cash_shop_mail",
                columns: table => new
                {
                    id = table.Column<long>(type: "bigint", nullable: false)
                        .Annotation("MySql:ValueGenerationStrategy", MySqlValueGenerationStrategy.IdentityColumn),
                    character_id = table.Column<long>(type: "bigint", nullable: false),
                    created_at_ticks = table.Column<long>(type: "bigint", nullable: false),
                    gold = table.Column<int>(type: "int", nullable: false),
                    item_quantity = table.Column<int>(type: "int", nullable: false),
                    item_template_id = table.Column<int>(type: "int", nullable: false),
                    sender = table.Column<string>(type: "varchar(64)", maxLength: 64, nullable: false)
                        .Annotation("MySql:CharSet", "utf8mb4"),
                    subject = table.Column<string>(type: "varchar(128)", maxLength: 128, nullable: false)
                        .Annotation("MySql:CharSet", "utf8mb4")
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_cash_shop_mail", x => x.id);
                })
                .Annotation("MySql:CharSet", "utf8mb4");

            migrationBuilder.CreateIndex(
                name: "IX_cash_shop_mail_character_id",
                table: "cash_shop_mail",
                column: "character_id");
        }
    }
}
