using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace MealMate.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddOffersPromotionsCustomerGaps : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<decimal>(
                name: "RewardPercentSnapshot",
                table: "ReferralAttributions",
                type: "numeric(5,2)",
                nullable: true);

            migrationBuilder.AddColumn<Guid>(
                name: "RewardSubscriptionId",
                table: "ReferralAttributions",
                type: "uuid",
                nullable: true);

            migrationBuilder.AddColumn<DateTime>(
                name: "RewardedAtUtc",
                table: "ReferralAttributions",
                type: "timestamp with time zone",
                nullable: true);

            migrationBuilder.CreateTable(
                name: "CustomerOfferCalendarEntries",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    CustomerUserId = table.Column<Guid>(type: "uuid", nullable: false),
                    OfferId = table.Column<Guid>(type: "uuid", nullable: false),
                    ScheduledDate = table.Column<DateOnly>(type: "date", nullable: false),
                    Status = table.Column<int>(type: "integer", nullable: false),
                    Source = table.Column<string>(type: "text", nullable: true),
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
                    table.PrimaryKey("PK_CustomerOfferCalendarEntries", x => x.Id);
                    table.ForeignKey(
                        name: "FK_CustomerOfferCalendarEntries_Offers_OfferId",
                        column: x => x.OfferId,
                        principalTable: "Offers",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "CustomerSavedOffers",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    CustomerUserId = table.Column<Guid>(type: "uuid", nullable: false),
                    OfferId = table.Column<Guid>(type: "uuid", nullable: false),
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
                    table.PrimaryKey("PK_CustomerSavedOffers", x => x.Id);
                    table.ForeignKey(
                        name: "FK_CustomerSavedOffers_Offers_OfferId",
                        column: x => x.OfferId,
                        principalTable: "Offers",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "IX_CustomerOfferCalendarEntries_CustomerUserId_OfferId_Schedul~",
                table: "CustomerOfferCalendarEntries",
                columns: new[] { "CustomerUserId", "OfferId", "ScheduledDate" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_CustomerOfferCalendarEntries_OfferId",
                table: "CustomerOfferCalendarEntries",
                column: "OfferId");

            migrationBuilder.CreateIndex(
                name: "IX_CustomerSavedOffers_CustomerUserId_OfferId",
                table: "CustomerSavedOffers",
                columns: new[] { "CustomerUserId", "OfferId" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_CustomerSavedOffers_OfferId",
                table: "CustomerSavedOffers",
                column: "OfferId");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "CustomerOfferCalendarEntries");

            migrationBuilder.DropTable(
                name: "CustomerSavedOffers");

            migrationBuilder.DropColumn(
                name: "RewardPercentSnapshot",
                table: "ReferralAttributions");

            migrationBuilder.DropColumn(
                name: "RewardSubscriptionId",
                table: "ReferralAttributions");

            migrationBuilder.DropColumn(
                name: "RewardedAtUtc",
                table: "ReferralAttributions");
        }
    }
}
