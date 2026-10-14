using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace MealMate.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddCampaignApprovalAndPricingPolicy : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<DateTime>(
                name: "ApprovedAtUtc",
                table: "Campaigns",
                type: "timestamp with time zone",
                nullable: true);

            migrationBuilder.AddColumn<Guid>(
                name: "ApprovedByUserId",
                table: "Campaigns",
                type: "uuid",
                nullable: true);

            migrationBuilder.AddColumn<uint>(
                name: "xmin",
                table: "Campaigns",
                type: "xid",
                rowVersion: true,
                nullable: false,
                defaultValue: 0u);

            migrationBuilder.AddColumn<int>(
                name: "AcceptedSnapshotVersion",
                table: "CampaignRestaurantEnrollments",
                type: "integer",
                nullable: true);

            migrationBuilder.AddColumn<DateTime>(
                name: "RespondedAtUtc",
                table: "CampaignRestaurantEnrollments",
                type: "timestamp with time zone",
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "CampaignSnapshotVersion",
                table: "CampaignCheckoutSessions",
                type: "integer",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "CurrencyCode",
                table: "CampaignCheckoutSessions",
                type: "text",
                nullable: true);

            migrationBuilder.AddColumn<decimal>(
                name: "FinalAmount",
                table: "CampaignCheckoutSessions",
                type: "numeric",
                nullable: true);

            migrationBuilder.AddColumn<Guid>(
                name: "PricingPolicyId",
                table: "CampaignCheckoutSessions",
                type: "uuid",
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "PricingPolicyVersion",
                table: "CampaignCheckoutSessions",
                type: "integer",
                nullable: true);

            migrationBuilder.CreateTable(
                name: "CampaignPricingPolicies",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    Version = table.Column<int>(type: "integer", nullable: false),
                    Model = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    CurrencyCode = table.Column<string>(type: "character varying(3)", maxLength: 3, nullable: false),
                    RoundingRule = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: false),
                    ContributionRulesJson = table.Column<string>(type: "text", nullable: false),
                    StackingRulesJson = table.Column<string>(type: "text", nullable: false),
                    RefundRulesJson = table.Column<string>(type: "text", nullable: false),
                    ApprovedByUserId = table.Column<Guid>(type: "uuid", nullable: true),
                    ApprovedAtUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    CreatedAtUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    CreatedByUserId = table.Column<Guid>(type: "uuid", nullable: true),
                    ModifiedAtUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    ModifiedByUserId = table.Column<Guid>(type: "uuid", nullable: true),
                    IsDeleted = table.Column<bool>(type: "boolean", nullable: false),
                    DeletedAtUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    DeletedByUserId = table.Column<Guid>(type: "uuid", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_CampaignPricingPolicies", x => x.Id);
                });

            migrationBuilder.CreateIndex(
                name: "IX_CampaignPricingPolicies_CurrencyCode_ApprovedAtUtc",
                table: "CampaignPricingPolicies",
                columns: new[] { "CurrencyCode", "ApprovedAtUtc" });

            migrationBuilder.CreateIndex(
                name: "IX_CampaignPricingPolicies_Version",
                table: "CampaignPricingPolicies",
                column: "Version",
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "CampaignPricingPolicies");

            migrationBuilder.DropColumn(
                name: "ApprovedAtUtc",
                table: "Campaigns");

            migrationBuilder.DropColumn(
                name: "ApprovedByUserId",
                table: "Campaigns");

            migrationBuilder.DropColumn(
                name: "xmin",
                table: "Campaigns");

            migrationBuilder.DropColumn(
                name: "AcceptedSnapshotVersion",
                table: "CampaignRestaurantEnrollments");

            migrationBuilder.DropColumn(
                name: "RespondedAtUtc",
                table: "CampaignRestaurantEnrollments");

            migrationBuilder.DropColumn(
                name: "CampaignSnapshotVersion",
                table: "CampaignCheckoutSessions");

            migrationBuilder.DropColumn(
                name: "CurrencyCode",
                table: "CampaignCheckoutSessions");

            migrationBuilder.DropColumn(
                name: "FinalAmount",
                table: "CampaignCheckoutSessions");

            migrationBuilder.DropColumn(
                name: "PricingPolicyId",
                table: "CampaignCheckoutSessions");

            migrationBuilder.DropColumn(
                name: "PricingPolicyVersion",
                table: "CampaignCheckoutSessions");
        }
    }
}
