using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace ArcheCore.Server.World.Migrations
{
    /// <inheritdoc />
    public partial class ItemTableUpdate : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.RenameColumn(
                name: "Name",
                table: "Items",
                newName: "name");

            migrationBuilder.RenameColumn(
                name: "MaxStack",
                table: "Items",
                newName: "max_stack");

            migrationBuilder.RenameColumn(
                name: "Id",
                table: "Items",
                newName: "item_id");

            migrationBuilder.AddColumn<string>(
                name: "description",
                table: "Items",
                type: "TEXT",
                nullable: false,
                defaultValue: "");

            migrationBuilder.AddColumn<string>(
                name: "icon_name",
                table: "Items",
                type: "TEXT",
                nullable: false,
                defaultValue: "");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "description",
                table: "Items");

            migrationBuilder.DropColumn(
                name: "icon_name",
                table: "Items");

            migrationBuilder.RenameColumn(
                name: "name",
                table: "Items",
                newName: "Name");

            migrationBuilder.RenameColumn(
                name: "max_stack",
                table: "Items",
                newName: "MaxStack");

            migrationBuilder.RenameColumn(
                name: "item_id",
                table: "Items",
                newName: "Id");
        }
    }
}
