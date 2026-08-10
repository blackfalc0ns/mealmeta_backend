using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace MealMate.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddRestaurantServiceRegionDeliveryWindows : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<TimeOnly>(
                name: "DeliveryWindowEnd",
                table: "RestaurantServiceRegions",
                type: "time without time zone",
                nullable: false,
                defaultValue: new TimeOnly(0, 0, 0));

            migrationBuilder.AddColumn<TimeOnly>(
                name: "DeliveryWindowStart",
                table: "RestaurantServiceRegions",
                type: "time without time zone",
                nullable: false,
                defaultValue: new TimeOnly(0, 0, 0));

            migrationBuilder.AddColumn<int>(
                name: "PreparationLeadTimeMinutes",
                table: "RestaurantServiceRegions",
                type: "integer",
                nullable: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "DeliveryWindowEnd",
                table: "RestaurantServiceRegions");

            migrationBuilder.DropColumn(
                name: "DeliveryWindowStart",
                table: "RestaurantServiceRegions");

            migrationBuilder.DropColumn(
                name: "PreparationLeadTimeMinutes",
                table: "RestaurantServiceRegions");
        }
    }
}
