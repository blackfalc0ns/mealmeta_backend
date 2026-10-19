using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace MealMate.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddOfferCheckoutQuotes : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "OfferCheckoutQuotes",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    CustomerId = table.Column<Guid>(type: "uuid", nullable: false),
                    OfferId = table.Column<Guid>(type: "uuid", nullable: false),
                    ProgramId = table.Column<Guid>(type: "uuid", nullable: false),
                    BundleId = table.Column<Guid>(type: "uuid", nullable: false),
                    Tier = table.Column<int>(type: "integer", nullable: false),
                    DurationDays = table.Column<int>(type: "integer", nullable: false),
                    StartDate = table.Column<DateOnly>(type: "date", nullable: false),
                    EndDate = table.Column<DateOnly>(type: "date", nullable: false),
                    IncludeFriday = table.Column<bool>(type: "boolean", nullable: false),
                    FridayDays = table.Column<int>(type: "integer", nullable: false),
                    TotalServiceDays = table.Column<int>(type: "integer", nullable: false),
                    PromoCode = table.Column<string>(type: "character varying(50)", maxLength: 50, nullable: true),
                    BasePrice = table.Column<decimal>(type: "numeric(18,3)", precision: 18, scale: 3, nullable: false),
                    CampaignDiscount = table.Column<decimal>(type: "numeric(18,3)", precision: 18, scale: 3, nullable: false),
                    PromoDiscount = table.Column<decimal>(type: "numeric(18,3)", precision: 18, scale: 3, nullable: false),
                    WalletCredit = table.Column<decimal>(type: "numeric(18,3)", precision: 18, scale: 3, nullable: false),
                    GatewayFee = table.Column<decimal>(type: "numeric(18,3)", precision: 18, scale: 3, nullable: false),
                    FinalPrice = table.Column<decimal>(type: "numeric(18,3)", precision: 18, scale: 3, nullable: false),
                    CurrencyCode = table.Column<string>(type: "character varying(3)", maxLength: 3, nullable: false),
                    AddressId = table.Column<Guid>(type: "uuid", nullable: true),
                    PayloadSnapshotHash = table.Column<string>(type: "character varying(128)", maxLength: 128, nullable: true),
                    ExpiresAtUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    IsUsed = table.Column<bool>(type: "boolean", nullable: false),
                    UsedAtUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    CreatedAtUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_OfferCheckoutQuotes", x => x.Id);
                });

            migrationBuilder.CreateIndex(
                name: "IX_OfferCheckoutQuotes_CustomerId",
                table: "OfferCheckoutQuotes",
                column: "CustomerId");

            migrationBuilder.CreateIndex(
                name: "IX_OfferCheckoutQuotes_ExpiresAtUtc",
                table: "OfferCheckoutQuotes",
                column: "ExpiresAtUtc");

            migrationBuilder.CreateIndex(
                name: "IX_OfferCheckoutQuotes_OfferId",
                table: "OfferCheckoutQuotes",
                column: "OfferId");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "OfferCheckoutQuotes");
        }
    }
}
