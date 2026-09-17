using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace MealMate.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddOffersAndPromotionsModule : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "Offers",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    NameAr = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false),
                    NameEn = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false),
                    DescriptionAr = table.Column<string>(type: "character varying(2000)", maxLength: 2000, nullable: true),
                    DescriptionEn = table.Column<string>(type: "character varying(2000)", maxLength: 2000, nullable: true),
                    BannerImageUrl = table.Column<string>(type: "character varying(1000)", maxLength: 1000, nullable: true),
                    Status = table.Column<int>(type: "integer", nullable: false),
                    Type = table.Column<int>(type: "integer", nullable: false),
                    DiscountType = table.Column<int>(type: "integer", nullable: false),
                    FundingMode = table.Column<int>(type: "integer", nullable: false),
                    DiscountRate = table.Column<decimal>(type: "numeric(9,6)", precision: 9, scale: 6, nullable: true),
                    DiscountAmount_Amount = table.Column<decimal>(type: "numeric(18,3)", precision: 18, scale: 3, nullable: true),
                    DiscountAmount_CurrencyCode = table.Column<string>(type: "character varying(3)", maxLength: 3, nullable: true),
                    PlatformShareRate = table.Column<decimal>(type: "numeric(9,6)", precision: 9, scale: 6, nullable: true),
                    RestaurantShareRate = table.Column<decimal>(type: "numeric(9,6)", precision: 9, scale: 6, nullable: true),
                    StartsAtUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    EndsAtUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    MinOrderAmount_Amount = table.Column<decimal>(type: "numeric(18,3)", precision: 18, scale: 3, nullable: true),
                    MinOrderAmount_CurrencyCode = table.Column<string>(type: "character varying(3)", maxLength: 3, nullable: true),
                    MinSubscriptionDays = table.Column<int>(type: "integer", nullable: true),
                    MaxSubscriptionDays = table.Column<int>(type: "integer", nullable: true),
                    TotalUsageLimit = table.Column<int>(type: "integer", nullable: true),
                    CurrentUsageCount = table.Column<int>(type: "integer", nullable: false),
                    PerCustomerLimit = table.Column<int>(type: "integer", nullable: false),
                    CustomerSegment = table.Column<int>(type: "integer", nullable: false),
                    AllowedGovernoratesJson = table.Column<string>(type: "text", nullable: true),
                    CampaignId = table.Column<Guid>(type: "uuid", nullable: true),
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
                    table.PrimaryKey("PK_Offers", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "OfferAuditLogs",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    OfferId = table.Column<Guid>(type: "uuid", nullable: false),
                    ActorId = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false),
                    Action = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false),
                    BeforeStateJson = table.Column<string>(type: "text", nullable: true),
                    AfterStateJson = table.Column<string>(type: "text", nullable: true),
                    DetailsJson = table.Column<string>(type: "text", nullable: true),
                    JustificationReason = table.Column<string>(type: "character varying(1000)", maxLength: 1000, nullable: true),
                    CreatedAtUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_OfferAuditLogs", x => x.Id);
                    table.ForeignKey(
                        name: "FK_OfferAuditLogs_Offers_OfferId",
                        column: x => x.OfferId,
                        principalTable: "Offers",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "OfferBundles",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    OfferId = table.Column<Guid>(type: "uuid", nullable: false),
                    BundleId = table.Column<Guid>(type: "uuid", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_OfferBundles", x => x.Id);
                    table.ForeignKey(
                        name: "FK_OfferBundles_Offers_OfferId",
                        column: x => x.OfferId,
                        principalTable: "Offers",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "OfferPrograms",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    OfferId = table.Column<Guid>(type: "uuid", nullable: false),
                    ProgramId = table.Column<Guid>(type: "uuid", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_OfferPrograms", x => x.Id);
                    table.ForeignKey(
                        name: "FK_OfferPrograms_Offers_OfferId",
                        column: x => x.OfferId,
                        principalTable: "Offers",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "OfferRedemptions",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    OfferId = table.Column<Guid>(type: "uuid", nullable: false),
                    CustomerId = table.Column<Guid>(type: "uuid", nullable: false),
                    SubscriptionId = table.Column<Guid>(type: "uuid", nullable: false),
                    PromoCodeId = table.Column<Guid>(type: "uuid", nullable: true),
                    DiscountAppliedAmount_Amount = table.Column<decimal>(type: "numeric(18,3)", precision: 18, scale: 3, nullable: false),
                    DiscountAppliedAmount_CurrencyCode = table.Column<string>(type: "character varying(3)", maxLength: 3, nullable: false),
                    PlatformFundedAmount_Amount = table.Column<decimal>(type: "numeric(18,3)", precision: 18, scale: 3, nullable: false),
                    PlatformFundedAmount_CurrencyCode = table.Column<string>(type: "character varying(3)", maxLength: 3, nullable: false),
                    RestaurantFundedAmount_Amount = table.Column<decimal>(type: "numeric(18,3)", precision: 18, scale: 3, nullable: false),
                    RestaurantFundedAmount_CurrencyCode = table.Column<string>(type: "character varying(3)", maxLength: 3, nullable: false),
                    RedeemedAtUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_OfferRedemptions", x => x.Id);
                    table.ForeignKey(
                        name: "FK_OfferRedemptions_Offers_OfferId",
                        column: x => x.OfferId,
                        principalTable: "Offers",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "OfferRestaurants",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    OfferId = table.Column<Guid>(type: "uuid", nullable: false),
                    RestaurantId = table.Column<Guid>(type: "uuid", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_OfferRestaurants", x => x.Id);
                    table.ForeignKey(
                        name: "FK_OfferRestaurants_Offers_OfferId",
                        column: x => x.OfferId,
                        principalTable: "Offers",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateIndex(
                name: "IX_OfferAuditLogs_CreatedAtUtc",
                table: "OfferAuditLogs",
                column: "CreatedAtUtc");

            migrationBuilder.CreateIndex(
                name: "IX_OfferAuditLogs_OfferId",
                table: "OfferAuditLogs",
                column: "OfferId");

            migrationBuilder.CreateIndex(
                name: "IX_OfferBundles_OfferId_BundleId",
                table: "OfferBundles",
                columns: new[] { "OfferId", "BundleId" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_OfferPrograms_OfferId_ProgramId",
                table: "OfferPrograms",
                columns: new[] { "OfferId", "ProgramId" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_OfferRedemptions_CustomerId",
                table: "OfferRedemptions",
                column: "CustomerId");

            migrationBuilder.CreateIndex(
                name: "IX_OfferRedemptions_OfferId",
                table: "OfferRedemptions",
                column: "OfferId");

            migrationBuilder.CreateIndex(
                name: "IX_OfferRedemptions_RedeemedAtUtc",
                table: "OfferRedemptions",
                column: "RedeemedAtUtc");

            migrationBuilder.CreateIndex(
                name: "IX_OfferRedemptions_SubscriptionId",
                table: "OfferRedemptions",
                column: "SubscriptionId");

            migrationBuilder.CreateIndex(
                name: "IX_OfferRestaurants_OfferId_RestaurantId",
                table: "OfferRestaurants",
                columns: new[] { "OfferId", "RestaurantId" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_Offers_CampaignId",
                table: "Offers",
                column: "CampaignId");

            migrationBuilder.CreateIndex(
                name: "IX_Offers_StartsAtUtc_EndsAtUtc",
                table: "Offers",
                columns: new[] { "StartsAtUtc", "EndsAtUtc" });

            migrationBuilder.CreateIndex(
                name: "IX_Offers_Status",
                table: "Offers",
                column: "Status");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "OfferAuditLogs");

            migrationBuilder.DropTable(
                name: "OfferBundles");

            migrationBuilder.DropTable(
                name: "OfferPrograms");

            migrationBuilder.DropTable(
                name: "OfferRedemptions");

            migrationBuilder.DropTable(
                name: "OfferRestaurants");

            migrationBuilder.DropTable(
                name: "Offers");
        }
    }
}
