using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace ArcheCore.Server.World.Migrations
{
    /// <inheritdoc />
    public partial class AddNpcAggression : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<float>(
                name: "AggroRadius",
                table: "NpcTemplates",
                type: "REAL",
                nullable: false,
                defaultValue: 0f);

            migrationBuilder.AddColumn<int>(
                name: "AttackCooldownMs",
                table: "NpcTemplates",
                type: "INTEGER",
                nullable: false,
                defaultValue: 0);

            migrationBuilder.AddColumn<int>(
                name: "AttackDamageMax",
                table: "NpcTemplates",
                type: "INTEGER",
                nullable: false,
                defaultValue: 0);

            migrationBuilder.AddColumn<int>(
                name: "AttackDamageMin",
                table: "NpcTemplates",
                type: "INTEGER",
                nullable: false,
                defaultValue: 0);

            migrationBuilder.AddColumn<float>(
                name: "AttackRange",
                table: "NpcTemplates",
                type: "REAL",
                nullable: false,
                defaultValue: 0f);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "AggroRadius",
                table: "NpcTemplates");

            migrationBuilder.DropColumn(
                name: "AttackCooldownMs",
                table: "NpcTemplates");

            migrationBuilder.DropColumn(
                name: "AttackDamageMax",
                table: "NpcTemplates");

            migrationBuilder.DropColumn(
                name: "AttackDamageMin",
                table: "NpcTemplates");

            migrationBuilder.DropColumn(
                name: "AttackRange",
                table: "NpcTemplates");
        }
    }
}
