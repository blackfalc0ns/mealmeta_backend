using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace MealMate.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddCustomerCampaignQuotes : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "CustomerCampaignQuotes",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    CustomerId = table.Column<Guid>(type: "uuid", nullable: false),
                    CampaignId = table.Column<Guid>(type: "uuid", nullable: false),
                    ProgramId = table.Column<Guid>(type: "uuid", nullable: false),
                    CampaignBundleId = table.Column<Guid>(type: "uuid", nullable: false),
                    CatalogBundleId = table.Column<Guid>(type: "uuid", nullable: false),
                    DurationDays = table.Column<int>(type: "integer", nullable: false),
                    StartDate = table.Column<DateOnly>(type: "date", nullable: false),
                    AddressId = table.Column<Guid>(type: "uuid", nullable: false),
                    PromoCode = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: true),
                    ReferencePrice = table.Column<decimal>(type: "numeric(18,3)", precision: 18, scale: 3, nullable: false),
                    DiscountAmount = table.Column<decimal>(type: "numeric(18,3)", precision: 18, scale: 3, nullable: false),
                    CouponDiscountAmount = table.Column<decimal>(type: "numeric(18,3)", precision: 18, scale: 3, nullable: false),
                    FinalPrice = table.Column<decimal>(type: "numeric(18,3)", precision: 18, scale: 3, nullable: false),
                    CurrencyCode = table.Column<string>(type: "character varying(10)", maxLength: 10, nullable: false),
                    CustomerDiscountPercent = table.Column<decimal>(type: "numeric(9,3)", precision: 9, scale: 3, nullable: true),
                    CanShowReferencePrice = table.Column<bool>(type: "boolean", nullable: false),
                    PricingPolicyId = table.Column<Guid>(type: "uuid", nullable: true),
                    PricingPolicyVersion = table.Column<int>(type: "integer", nullable: false),
                    CampaignSnapshotVersion = table.Column<int>(type: "integer", nullable: false),
                    ServiceDates = table.Column<DateOnly[]>(type: "date[]", nullable: false),
                    ExpiresAtUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
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
                    table.PrimaryKey("PK_CustomerCampaignQuotes", x => x.Id);
                });

            migrationBuilder.CreateIndex(
                name: "IX_CustomerCampaignQuotes_CustomerId",
                table: "CustomerCampaignQuotes",
                column: "CustomerId");

            migrationBuilder.CreateIndex(
                name: "IX_CustomerCampaignQuotes_CustomerId_CampaignId_CampaignBundle~",
                table: "CustomerCampaignQuotes",
                columns: new[] { "CustomerId", "CampaignId", "CampaignBundleId", "DurationDays", "StartDate" });

            migrationBuilder.CreateIndex(
                name: "IX_CustomerCampaignQuotes_ExpiresAtUtc",
                table: "CustomerCampaignQuotes",
                column: "ExpiresAtUtc");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "CustomerCampaignQuotes");
        }
    }
}
