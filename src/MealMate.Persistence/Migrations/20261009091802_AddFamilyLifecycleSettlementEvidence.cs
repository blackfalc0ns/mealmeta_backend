using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace MealMate.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddFamilyLifecycleSettlementEvidence : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<DateOnly>(
                name: "EffectiveServiceDate",
                table: "SubscriptionTierUpgrades",
                type: "date",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "EffectiveServiceDatesJson",
                table: "SubscriptionTierUpgrades",
                type: "jsonb",
                nullable: true);

            migrationBuilder.AddColumn<Guid>(
                name: "PayerCustomerUserId",
                table: "SubscriptionTierUpgrades",
                type: "uuid",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "VerifiedCurrencyCode",
                table: "SubscriptionTierUpgrades",
                type: "text",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "VerifiedInvoiceId",
                table: "SubscriptionTierUpgrades",
                type: "text",
                nullable: true);

            migrationBuilder.AddColumn<decimal>(
                name: "VerifiedPaidAmount",
                table: "SubscriptionTierUpgrades",
                type: "numeric(18,3)",
                precision: 18,
                scale: 3,
                nullable: true);

            migrationBuilder.AddColumn<DateTime>(
                name: "VerifiedPaidAtUtc",
                table: "SubscriptionTierUpgrades",
                type: "timestamp with time zone",
                nullable: true);

            migrationBuilder.AddColumn<Guid>(
                name: "ActorUserId",
                table: "SubscriptionCancellations",
                type: "uuid",
                nullable: true);

            migrationBuilder.AddColumn<Guid>(
                name: "BatchId",
                table: "SubscriptionCancellations",
                type: "uuid",
                nullable: true);

            migrationBuilder.AddColumn<Guid>(
                name: "BatchLineId",
                table: "SubscriptionCancellations",
                type: "uuid",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "ContributionRefundsJson",
                table: "SubscriptionCancellations",
                type: "jsonb",
                nullable: true);

            migrationBuilder.AddColumn<Guid>(
                name: "FamilyGroupId",
                table: "SubscriptionCancellations",
                type: "uuid",
                nullable: true);

            migrationBuilder.AddColumn<Guid>(
                name: "PayerCustomerUserId",
                table: "SubscriptionCancellations",
                type: "uuid",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "ResultMetadataJson",
                table: "SubscriptionCancellations",
                type: "jsonb",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "SettlementKind",
                table: "SubscriptionCancellations",
                type: "character varying(50)",
                maxLength: 50,
                nullable: true);

            migrationBuilder.AddColumn<Guid>(
                name: "BlockRegionId",
                table: "FamilyCheckoutBatchLines",
                type: "uuid",
                nullable: true);

            migrationBuilder.CreateTable(
                name: "FamilySubscriptionDeliveryAmendments",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    SubscriptionId = table.Column<Guid>(type: "uuid", nullable: false),
                    Version = table.Column<long>(type: "bigint", nullable: false),
                    ActorUserId = table.Column<Guid>(type: "uuid", nullable: false),
                    EffectiveFromDate = table.Column<DateOnly>(type: "date", nullable: false),
                    AddressId = table.Column<Guid>(type: "uuid", nullable: false),
                    AddressLabel = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false),
                    AddressLine = table.Column<string>(type: "character varying(2000)", maxLength: 2000, nullable: false),
                    RegionId = table.Column<Guid>(type: "uuid", nullable: false),
                    BlockRegionId = table.Column<Guid>(type: "uuid", nullable: true),
                    Governorate = table.Column<string>(type: "text", nullable: true),
                    Area = table.Column<string>(type: "text", nullable: true),
                    Block = table.Column<string>(type: "text", nullable: true),
                    AffectedDayIdsJson = table.Column<string>(type: "jsonb", nullable: false),
                    CreatedAtUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_FamilySubscriptionDeliveryAmendments", x => x.Id);
                    table.ForeignKey(
                        name: "FK_FamilySubscriptionDeliveryAmendments_Subscriptions_Subscrip~",
                        column: x => x.SubscriptionId,
                        principalTable: "Subscriptions",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateIndex(
                name: "IX_WalletTransactions_ReferenceType_ReferenceId_CustomerUserId",
                table: "WalletTransactions",
                columns: new[] { "ReferenceType", "ReferenceId", "CustomerUserId" },
                unique: true,
                filter: "\"ReferenceType\" = 'FamilyCancellationRefund'");

            migrationBuilder.CreateIndex(
                name: "IX_SubscriptionCancellations_SubscriptionId_SettlementKind",
                table: "SubscriptionCancellations",
                columns: new[] { "SubscriptionId", "SettlementKind" },
                unique: true,
                filter: "\"SettlementKind\" = 'FamilyCancellation'");

            migrationBuilder.CreateIndex(
                name: "IX_FamilySubscriptionDeliveryAmendments_SubscriptionId_Version",
                table: "FamilySubscriptionDeliveryAmendments",
                columns: new[] { "SubscriptionId", "Version" },
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "FamilySubscriptionDeliveryAmendments");

            migrationBuilder.DropIndex(
                name: "IX_WalletTransactions_ReferenceType_ReferenceId_CustomerUserId",
                table: "WalletTransactions");

            migrationBuilder.DropIndex(
                name: "IX_SubscriptionCancellations_SubscriptionId_SettlementKind",
                table: "SubscriptionCancellations");

            migrationBuilder.DropColumn(
                name: "EffectiveServiceDate",
                table: "SubscriptionTierUpgrades");

            migrationBuilder.DropColumn(
                name: "EffectiveServiceDatesJson",
                table: "SubscriptionTierUpgrades");

            migrationBuilder.DropColumn(
                name: "PayerCustomerUserId",
                table: "SubscriptionTierUpgrades");

            migrationBuilder.DropColumn(
                name: "VerifiedCurrencyCode",
                table: "SubscriptionTierUpgrades");

            migrationBuilder.DropColumn(
                name: "VerifiedInvoiceId",
                table: "SubscriptionTierUpgrades");

            migrationBuilder.DropColumn(
                name: "VerifiedPaidAmount",
                table: "SubscriptionTierUpgrades");

            migrationBuilder.DropColumn(
                name: "VerifiedPaidAtUtc",
                table: "SubscriptionTierUpgrades");

            migrationBuilder.DropColumn(
                name: "ActorUserId",
                table: "SubscriptionCancellations");

            migrationBuilder.DropColumn(
                name: "BatchId",
                table: "SubscriptionCancellations");

            migrationBuilder.DropColumn(
                name: "BatchLineId",
                table: "SubscriptionCancellations");

            migrationBuilder.DropColumn(
                name: "ContributionRefundsJson",
                table: "SubscriptionCancellations");

            migrationBuilder.DropColumn(
                name: "FamilyGroupId",
                table: "SubscriptionCancellations");

            migrationBuilder.DropColumn(
                name: "PayerCustomerUserId",
                table: "SubscriptionCancellations");

            migrationBuilder.DropColumn(
                name: "ResultMetadataJson",
                table: "SubscriptionCancellations");

            migrationBuilder.DropColumn(
                name: "SettlementKind",
                table: "SubscriptionCancellations");

            migrationBuilder.DropColumn(
                name: "BlockRegionId",
                table: "FamilyCheckoutBatchLines");
        }
    }
}
