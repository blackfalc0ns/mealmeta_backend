using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace MealMate.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class PromoCodeCheckoutLifecycle : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<int>(
                name: "PerCustomerLimit",
                table: "PromoCodes",
                type: "integer",
                nullable: true);

            migrationBuilder.CreateTable(
                name: "PromoCodeRedemptions",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    PromoCodeId = table.Column<Guid>(type: "uuid", nullable: false),
                    CustomerId = table.Column<Guid>(type: "uuid", nullable: false),
                    SubscriptionId = table.Column<Guid>(type: "uuid", nullable: false),
                    Status = table.Column<string>(type: "character varying(24)", maxLength: 24, nullable: false),
                    DiscountAppliedAmount_Amount = table.Column<decimal>(type: "numeric(18,3)", precision: 18, scale: 3, nullable: false),
                    DiscountAppliedAmount_CurrencyCode = table.Column<string>(type: "character varying(3)", maxLength: 3, nullable: false),
                    PlatformFundedAmount_Amount = table.Column<decimal>(type: "numeric(18,3)", precision: 18, scale: 3, nullable: false),
                    PlatformFundedAmount_CurrencyCode = table.Column<string>(type: "character varying(3)", maxLength: 3, nullable: false),
                    RestaurantFundedAmount_Amount = table.Column<decimal>(type: "numeric(18,3)", precision: 18, scale: 3, nullable: false),
                    RestaurantFundedAmount_CurrencyCode = table.Column<string>(type: "character varying(3)", maxLength: 3, nullable: false),
                    DiscountPercentSnapshot = table.Column<decimal>(type: "numeric", nullable: true),
                    DiscountAmountSnapshot = table.Column<decimal>(type: "numeric", nullable: true),
                    TargetingSnapshotJson = table.Column<string>(type: "jsonb", nullable: false),
                    HeldAtUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    CommittedAtUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    ReleasedAtUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    ReleaseReason = table.Column<string>(type: "character varying(500)", maxLength: 500, nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_PromoCodeRedemptions", x => x.Id);
                    table.ForeignKey(
                        name: "FK_PromoCodeRedemptions_PromoCodes_PromoCodeId",
                        column: x => x.PromoCodeId,
                        principalTable: "PromoCodes",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.AddCheckConstraint(
                name: "CK_PromoCodes_PerCustomerLimit_Positive",
                table: "PromoCodes",
                sql: "\"PerCustomerLimit\" IS NULL OR \"PerCustomerLimit\" > 0");

            migrationBuilder.CreateIndex(
                name: "IX_PromoCodeRedemptions_PromoCodeId_CustomerId_Status",
                table: "PromoCodeRedemptions",
                columns: new[] { "PromoCodeId", "CustomerId", "Status" });

            migrationBuilder.CreateIndex(
                name: "IX_PromoCodeRedemptions_PromoCodeId_SubscriptionId",
                table: "PromoCodeRedemptions",
                columns: new[] { "PromoCodeId", "SubscriptionId" },
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "PromoCodeRedemptions");

            migrationBuilder.DropCheckConstraint(
                name: "CK_PromoCodes_PerCustomerLimit_Positive",
                table: "PromoCodes");

            migrationBuilder.DropColumn(
                name: "PerCustomerLimit",
                table: "PromoCodes");
        }
    }
}
