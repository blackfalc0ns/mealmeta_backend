using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace MealMate.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class PersistRestaurantProgramSelections : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "SelectedBundleIdsJson",
                table: "RestaurantRegistrations",
                type: "character varying(4000)",
                maxLength: 4000,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "SelectedProgramIdsJson",
                table: "RestaurantRegistrations",
                type: "character varying(4000)",
                maxLength: 4000,
                nullable: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "SelectedBundleIdsJson",
                table: "RestaurantRegistrations");

            migrationBuilder.DropColumn(
                name: "SelectedProgramIdsJson",
                table: "RestaurantRegistrations");
        }
    }
}
