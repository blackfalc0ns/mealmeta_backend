using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace MealMate.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddTripStopActiveSubscriptionDayPartialIndex : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "IX_TripStops_SubscriptionDayId",
                table: "TripStops");

            migrationBuilder.CreateIndex(
                name: "IX_TripStops_Active_SubscriptionDayId",
                table: "TripStops",
                column: "SubscriptionDayId",
                unique: true,
                filter: "\"SubscriptionDayId\" IS NOT NULL AND \"Status\" IN (1, 2)");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "IX_TripStops_Active_SubscriptionDayId",
                table: "TripStops");

            migrationBuilder.CreateIndex(
                name: "IX_TripStops_SubscriptionDayId",
                table: "TripStops",
                column: "SubscriptionDayId");
        }
    }
}
