using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace ArcheCore.Server.Persistence.Migrations
{
    /// <summary>
    /// save_seq: the sequence number of the last character save written.
    /// /characters/save-full only writes a save whose sequence is higher,
    /// so saves can't land out of order or twice.
    /// </summary>
    public partial class AddCharacterSaveSeq : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<long>(
                name: "save_seq",
                table: "characters",
                type: "bigint",
                nullable: false,
                defaultValue: 0L);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "save_seq",
                table: "characters");
        }
    }
}
