using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace MealMate.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class EnforceFamilyPendingBatchUniqueness : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.RenameIndex(
                name: "IX_FamilyCheckoutBatches_FamilyGroupId",
                table: "FamilyCheckoutBatches",
                newName: "IX_FamilyCheckoutBatches_FamilyGroupId1");

            migrationBuilder.CreateIndex(
                name: "IX_FamilyCheckoutBatches_FamilyGroupId",
                table: "FamilyCheckoutBatches",
                column: "FamilyGroupId");

            migrationBuilder.CreateIndex(
                name: "IX_FamilyCheckoutBatches_FamilyGroupId_PaymentPending",
                table: "FamilyCheckoutBatches",
                column: "FamilyGroupId",
                unique: true,
                filter: "\"Status\" = 1");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "IX_FamilyCheckoutBatches_FamilyGroupId",
                table: "FamilyCheckoutBatches");

            migrationBuilder.DropIndex(
                name: "IX_FamilyCheckoutBatches_FamilyGroupId_PaymentPending",
                table: "FamilyCheckoutBatches");

            migrationBuilder.RenameIndex(
                name: "IX_FamilyCheckoutBatches_FamilyGroupId1",
                table: "FamilyCheckoutBatches",
                newName: "IX_FamilyCheckoutBatches_FamilyGroupId");
        }
    }
}
