using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace ArcheCore.Server.Persistence.Migrations
{
    /// <summary>
    /// Character names are unique, case-insensitively (audit H5).
    ///
    /// Existing duplicates are renamed first - the OLDEST character keeps
    /// the name, later ones become "Name_12345" (their character id) - so
    /// the unique index can be created on a database that already has them.
    /// </summary>
    public partial class UniqueCharacterNames : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql(
                "UPDATE characters c " +
                "JOIN characters d ON LOWER(c.name) = LOWER(d.name) AND c.character_id > d.character_id " +
                "SET c.name = CONCAT(LEFT(c.name, 40), '_', c.character_id);");

            migrationBuilder.AlterColumn<string>(
                name: "name",
                table: "characters",
                type: "varchar(64)",
                maxLength: 64,
                nullable: false,
                collation: "utf8mb4_unicode_ci",
                oldClrType: typeof(string),
                oldType: "varchar(64)",
                oldMaxLength: 64)
                .Annotation("MySql:CharSet", "utf8mb4");

            migrationBuilder.CreateIndex(
                name: "IX_characters_name",
                table: "characters",
                column: "name",
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "IX_characters_name",
                table: "characters");

            migrationBuilder.AlterColumn<string>(
                name: "name",
                table: "characters",
                type: "varchar(64)",
                maxLength: 64,
                nullable: false,
                oldClrType: typeof(string),
                oldType: "varchar(64)",
                oldMaxLength: 64,
                oldCollation: "utf8mb4_unicode_ci")
                .Annotation("MySql:CharSet", "utf8mb4");
        }
    }
}
