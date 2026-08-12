using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace MealMate.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class PreserveResolvedDailyMenuSelection : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<Guid>(
                name: "ResolvedMenuCycleVersionId",
                table: "RestaurantDailyOrders",
                type: "uuid",
                nullable: true);

            migrationBuilder.AddColumn<Guid>(
                name: "ResolvedMenuDayId",
                table: "RestaurantDailyOrders",
                type: "uuid",
                nullable: true);

            migrationBuilder.AddColumn<Guid>(
                name: "ResolvedMenuCycleVersionId",
                table: "DailyMealSelections",
                type: "uuid",
                nullable: true);

            migrationBuilder.AddColumn<Guid>(
                name: "ResolvedMenuDayId",
                table: "DailyMealSelections",
                type: "uuid",
                nullable: true);

            migrationBuilder.CreateIndex(
                name: "IX_RestaurantDailyOrders_ResolvedMenuCycleVersionId",
                table: "RestaurantDailyOrders",
                column: "ResolvedMenuCycleVersionId");

            migrationBuilder.CreateIndex(
                name: "IX_RestaurantDailyOrders_ResolvedMenuDayId",
                table: "RestaurantDailyOrders",
                column: "ResolvedMenuDayId");

            migrationBuilder.CreateIndex(
                name: "IX_DailyMealSelections_ResolvedMenuCycleVersionId",
                table: "DailyMealSelections",
                column: "ResolvedMenuCycleVersionId");

            migrationBuilder.CreateIndex(
                name: "IX_DailyMealSelections_ResolvedMenuDayId",
                table: "DailyMealSelections",
                column: "ResolvedMenuDayId");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "IX_RestaurantDailyOrders_ResolvedMenuCycleVersionId",
                table: "RestaurantDailyOrders");

            migrationBuilder.DropIndex(
                name: "IX_RestaurantDailyOrders_ResolvedMenuDayId",
                table: "RestaurantDailyOrders");

            migrationBuilder.DropIndex(
                name: "IX_DailyMealSelections_ResolvedMenuCycleVersionId",
                table: "DailyMealSelections");

            migrationBuilder.DropIndex(
                name: "IX_DailyMealSelections_ResolvedMenuDayId",
                table: "DailyMealSelections");

            migrationBuilder.DropColumn(
                name: "ResolvedMenuCycleVersionId",
                table: "RestaurantDailyOrders");

            migrationBuilder.DropColumn(
                name: "ResolvedMenuDayId",
                table: "RestaurantDailyOrders");

            migrationBuilder.DropColumn(
                name: "ResolvedMenuCycleVersionId",
                table: "DailyMealSelections");

            migrationBuilder.DropColumn(
                name: "ResolvedMenuDayId",
                table: "DailyMealSelections");
        }
    }
}
