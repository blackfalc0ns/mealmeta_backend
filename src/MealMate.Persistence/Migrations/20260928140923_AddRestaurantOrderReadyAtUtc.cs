using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace MealMate.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddRestaurantOrderReadyAtUtc : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<DateTime>(
                name: "ReadyForPickupAtUtc",
                table: "RestaurantDailyOrders",
                type: "timestamp with time zone",
                nullable: true);

            migrationBuilder.CreateIndex(
                name: "IX_RestaurantDailyOrders_ReadyForPickupAtUtc",
                table: "RestaurantDailyOrders",
                column: "ReadyForPickupAtUtc");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "IX_RestaurantDailyOrders_ReadyForPickupAtUtc",
                table: "RestaurantDailyOrders");

            migrationBuilder.DropColumn(
                name: "ReadyForPickupAtUtc",
                table: "RestaurantDailyOrders");
        }
    }
}
