using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace MealMate.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddFamilyCheckoutPromoCodeAllocation : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropCheckConstraint(
                name: "CK_FamilyCheckoutBatchLines_NetPaid",
                table: "FamilyCheckoutBatchLines");

            migrationBuilder.AddColumn<decimal>(
                name: "PromoCodeDiscountAllocated",
                table: "FamilyCheckoutBatchLines",
                type: "numeric(18,3)",
                precision: 18,
                scale: 3,
                nullable: false,
                defaultValue: 0m);

            migrationBuilder.AddCheckConstraint(
                name: "CK_FamilyCheckoutBatchLines_NetPaid",
                table: "FamilyCheckoutBatchLines",
                sql: "\"NetPaidAmount\" >= 0 AND \"FamilyDiscountAllocated\" >= 0 AND \"PromoCodeDiscountAllocated\" >= 0 AND \"PriceBeforeFamilyDiscount\" >= 0 AND \"NetPaidAmount\" = \"PriceBeforeFamilyDiscount\" - \"FamilyDiscountAllocated\" - \"PromoCodeDiscountAllocated\"");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropCheckConstraint(
                name: "CK_FamilyCheckoutBatchLines_NetPaid",
                table: "FamilyCheckoutBatchLines");

            migrationBuilder.DropColumn(
                name: "PromoCodeDiscountAllocated",
                table: "FamilyCheckoutBatchLines");

            migrationBuilder.AddCheckConstraint(
                name: "CK_FamilyCheckoutBatchLines_NetPaid",
                table: "FamilyCheckoutBatchLines",
                sql: "\"NetPaidAmount\" >= 0 AND \"FamilyDiscountAllocated\" >= 0 AND \"PriceBeforeFamilyDiscount\" >= 0 AND \"NetPaidAmount\" <= \"PriceBeforeFamilyDiscount\"");
        }
    }
}
