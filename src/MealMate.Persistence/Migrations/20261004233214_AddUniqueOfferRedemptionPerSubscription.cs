using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace MealMate.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddUniqueOfferRedemptionPerSubscription : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateIndex(
                name: "IX_OfferRedemptions_OfferId_SubscriptionId",
                table: "OfferRedemptions",
                columns: new[] { "OfferId", "SubscriptionId" },
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "IX_OfferRedemptions_OfferId_SubscriptionId",
                table: "OfferRedemptions");
        }
    }
}
