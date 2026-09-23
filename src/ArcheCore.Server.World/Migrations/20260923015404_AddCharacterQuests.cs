using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace ArcheCore.Server.World.Migrations
{
    /// <inheritdoc />
    public partial class AddCharacterQuests : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "AcceptText",
                table: "Quests",
                type: "TEXT",
                nullable: false,
                defaultValue: "");

            migrationBuilder.AddColumn<string>(
                name: "CompleteText",
                table: "Quests",
                type: "TEXT",
                nullable: false,
                defaultValue: "");

            migrationBuilder.AddColumn<int>(
                name: "GiverNpcTemplateId",
                table: "Quests",
                type: "INTEGER",
                nullable: false,
                defaultValue: 0);

            migrationBuilder.AddColumn<int>(
                name: "MinLevel",
                table: "Quests",
                type: "INTEGER",
                nullable: false,
                defaultValue: 0);

            migrationBuilder.AddColumn<int>(
                name: "RequiredQuestId",
                table: "Quests",
                type: "INTEGER",
                nullable: false,
                defaultValue: 0);

            migrationBuilder.AddColumn<int>(
                name: "RewardGold",
                table: "Quests",
                type: "INTEGER",
                nullable: false,
                defaultValue: 0);

            migrationBuilder.AddColumn<int>(
                name: "RewardItemId",
                table: "Quests",
                type: "INTEGER",
                nullable: false,
                defaultValue: 0);

            migrationBuilder.AddColumn<int>(
                name: "RewardItemQuantity",
                table: "Quests",
                type: "INTEGER",
                nullable: false,
                defaultValue: 0);

            migrationBuilder.AddColumn<int>(
                name: "TurnInNpcTemplateId",
                table: "Quests",
                type: "INTEGER",
                nullable: false,
                defaultValue: 0);

            migrationBuilder.CreateTable(
                name: "QuestObjectives",
                columns: table => new
                {
                    Id = table.Column<int>(type: "INTEGER", nullable: false)
                        .Annotation("Sqlite:Autoincrement", true),
                    QuestId = table.Column<int>(type: "INTEGER", nullable: false),
                    SortOrder = table.Column<int>(type: "INTEGER", nullable: false),
                    ObjectiveType = table.Column<int>(type: "INTEGER", nullable: false),
                    TargetId = table.Column<int>(type: "INTEGER", nullable: false),
                    RequiredCount = table.Column<int>(type: "INTEGER", nullable: false),
                    Text = table.Column<string>(type: "TEXT", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_QuestObjectives", x => x.Id);
                });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "QuestObjectives");

            migrationBuilder.DropColumn(
                name: "AcceptText",
                table: "Quests");

            migrationBuilder.DropColumn(
                name: "CompleteText",
                table: "Quests");

            migrationBuilder.DropColumn(
                name: "GiverNpcTemplateId",
                table: "Quests");

            migrationBuilder.DropColumn(
                name: "MinLevel",
                table: "Quests");

            migrationBuilder.DropColumn(
                name: "RequiredQuestId",
                table: "Quests");

            migrationBuilder.DropColumn(
                name: "RewardGold",
                table: "Quests");

            migrationBuilder.DropColumn(
                name: "RewardItemId",
                table: "Quests");

            migrationBuilder.DropColumn(
                name: "RewardItemQuantity",
                table: "Quests");

            migrationBuilder.DropColumn(
                name: "TurnInNpcTemplateId",
                table: "Quests");
        }
    }
}
