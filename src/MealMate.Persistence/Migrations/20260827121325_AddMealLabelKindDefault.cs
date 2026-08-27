using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace MealMate.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddMealLabelKindDefault : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AlterColumn<int>(
                name: "Kind",
                table: "MealLabels",
                type: "integer",
                nullable: false,
                defaultValue: 1,
                oldClrType: typeof(int),
                oldType: "integer");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AlterColumn<int>(
                name: "Kind",
                table: "MealLabels",
                type: "integer",
                nullable: false,
                oldClrType: typeof(int),
                oldType: "integer",
                oldDefaultValue: 1);
        }
    }
}
