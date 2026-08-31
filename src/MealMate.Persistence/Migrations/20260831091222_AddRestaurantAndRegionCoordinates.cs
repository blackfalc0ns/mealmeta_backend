using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace MealMate.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddRestaurantAndRegionCoordinates : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<decimal>(
                name: "Latitude",
                table: "RestaurantRegistrations",
                type: "numeric(9,6)",
                precision: 9,
                scale: 6,
                nullable: true);

            migrationBuilder.AddColumn<decimal>(
                name: "Longitude",
                table: "RestaurantRegistrations",
                type: "numeric(9,6)",
                precision: 9,
                scale: 6,
                nullable: true);

            migrationBuilder.AddColumn<decimal>(
                name: "Latitude",
                table: "RestaurantProfiles",
                type: "numeric(9,6)",
                precision: 9,
                scale: 6,
                nullable: true);

            migrationBuilder.AddColumn<decimal>(
                name: "Longitude",
                table: "RestaurantProfiles",
                type: "numeric(9,6)",
                precision: 9,
                scale: 6,
                nullable: true);

            migrationBuilder.AddColumn<decimal>(
                name: "CentroidLatitude",
                table: "Regions",
                type: "numeric(9,6)",
                precision: 9,
                scale: 6,
                nullable: true);

            migrationBuilder.AddColumn<decimal>(
                name: "CentroidLongitude",
                table: "Regions",
                type: "numeric(9,6)",
                precision: 9,
                scale: 6,
                nullable: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "Latitude",
                table: "RestaurantRegistrations");

            migrationBuilder.DropColumn(
                name: "Longitude",
                table: "RestaurantRegistrations");

            migrationBuilder.DropColumn(
                name: "Latitude",
                table: "RestaurantProfiles");

            migrationBuilder.DropColumn(
                name: "Longitude",
                table: "RestaurantProfiles");

            migrationBuilder.DropColumn(
                name: "CentroidLatitude",
                table: "Regions");

            migrationBuilder.DropColumn(
                name: "CentroidLongitude",
                table: "Regions");
        }
    }
}
