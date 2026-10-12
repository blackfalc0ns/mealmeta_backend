using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace MealMate.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddFamilyUpgradePaymentReservation : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "PaymentCreationState",
                table: "SubscriptionTierUpgrades",
                type: "character varying(30)",
                maxLength: 30,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "PaymentUrl",
                table: "SubscriptionTierUpgrades",
                type: "character varying(2000)",
                maxLength: 2000,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "RequestFingerprint",
                table: "SubscriptionTierUpgrades",
                type: "character varying(128)",
                maxLength: 128,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "RequestIdempotencyKey",
                table: "SubscriptionTierUpgrades",
                type: "character varying(100)",
                maxLength: 100,
                nullable: true);

            migrationBuilder.CreateIndex(
                name: "IX_SubscriptionTierUpgrades_SubscriptionId_RequestIdempotencyK~",
                table: "SubscriptionTierUpgrades",
                columns: new[] { "SubscriptionId", "RequestIdempotencyKey" },
                unique: true,
                filter: "\"RequestIdempotencyKey\" IS NOT NULL");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "IX_SubscriptionTierUpgrades_SubscriptionId_RequestIdempotencyK~",
                table: "SubscriptionTierUpgrades");

            migrationBuilder.DropColumn(
                name: "PaymentCreationState",
                table: "SubscriptionTierUpgrades");

            migrationBuilder.DropColumn(
                name: "PaymentUrl",
                table: "SubscriptionTierUpgrades");

            migrationBuilder.DropColumn(
                name: "RequestFingerprint",
                table: "SubscriptionTierUpgrades");

            migrationBuilder.DropColumn(
                name: "RequestIdempotencyKey",
                table: "SubscriptionTierUpgrades");
        }
    }
}
