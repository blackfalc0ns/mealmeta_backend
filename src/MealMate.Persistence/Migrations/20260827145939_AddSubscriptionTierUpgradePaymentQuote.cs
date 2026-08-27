using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace MealMate.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddSubscriptionTierUpgradePaymentQuote : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "PaymentId",
                table: "SubscriptionTierUpgrades",
                type: "character varying(100)",
                maxLength: 100,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "QuoteJson",
                table: "SubscriptionTierUpgrades",
                type: "jsonb",
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "QuotedRemainingDays",
                table: "SubscriptionTierUpgrades",
                type: "integer",
                nullable: false,
                defaultValue: 0);

            migrationBuilder.AddColumn<decimal>(
                name: "QuotedTotalDue_Amount",
                table: "SubscriptionTierUpgrades",
                type: "numeric(18,3)",
                precision: 18,
                scale: 3,
                nullable: false,
                defaultValue: 0m);

            migrationBuilder.AddColumn<string>(
                name: "QuotedTotalDue_CurrencyCode",
                table: "SubscriptionTierUpgrades",
                type: "character varying(3)",
                maxLength: 3,
                nullable: false,
                defaultValue: "KWD");

            migrationBuilder.Sql(
                """
                UPDATE "SubscriptionTierUpgrades"
                SET "QuotedTotalDue_Amount" = "PriceDifference_Amount",
                    "QuotedTotalDue_CurrencyCode" = "PriceDifference_CurrencyCode";
                """);

            migrationBuilder.CreateIndex(
                name: "IX_SubscriptionTierUpgrades_PaymentId",
                table: "SubscriptionTierUpgrades",
                column: "PaymentId");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "IX_SubscriptionTierUpgrades_PaymentId",
                table: "SubscriptionTierUpgrades");

            migrationBuilder.DropColumn(
                name: "PaymentId",
                table: "SubscriptionTierUpgrades");

            migrationBuilder.DropColumn(
                name: "QuoteJson",
                table: "SubscriptionTierUpgrades");

            migrationBuilder.DropColumn(
                name: "QuotedRemainingDays",
                table: "SubscriptionTierUpgrades");

            migrationBuilder.DropColumn(
                name: "QuotedTotalDue_Amount",
                table: "SubscriptionTierUpgrades");

            migrationBuilder.DropColumn(
                name: "QuotedTotalDue_CurrencyCode",
                table: "SubscriptionTierUpgrades");
        }
    }
}
