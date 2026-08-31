using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace MealMate.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddMenuCyclePendingSequence : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "PendingSequenceJson",
                table: "MenuPlanVersions",
                type: "text",
                nullable: true);

            migrationBuilder.AddColumn<bool>(
                name: "IsActiveInCycle",
                table: "MenuDays",
                type: "boolean",
                nullable: false,
                defaultValue: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "PendingSequenceJson",
                table: "MenuPlanVersions");

            migrationBuilder.DropColumn(
                name: "IsActiveInCycle",
                table: "MenuDays");
        }
    }
}
