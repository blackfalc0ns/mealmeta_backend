using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace MealMate.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddFamilyRenewalDeliveryLineage : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "IX_FamilyCheckoutBatches_FamilyGroupId",
                table: "FamilyCheckoutBatches");

            migrationBuilder.AddColumn<Guid>(
                name: "BatchId",
                table: "SubscriptionRenewals",
                type: "uuid",
                nullable: true);

            migrationBuilder.AddColumn<long>(
                name: "BatchVersion",
                table: "SubscriptionRenewals",
                type: "bigint",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "CheckoutFingerprint",
                table: "SubscriptionRenewals",
                type: "character varying(64)",
                maxLength: 64,
                nullable: true);

            migrationBuilder.AddColumn<Guid>(
                name: "FamilyGroupId",
                table: "SubscriptionRenewals",
                type: "uuid",
                nullable: true);

            migrationBuilder.AddColumn<Guid>(
                name: "FamilyMemberId",
                table: "SubscriptionRenewals",
                type: "uuid",
                nullable: true);

            migrationBuilder.AddColumn<Guid>(
                name: "RequestedAddressId",
                table: "SubscriptionRenewals",
                type: "uuid",
                nullable: true);

            migrationBuilder.AddColumn<DateOnly>(
                name: "RequestedStartDate",
                table: "SubscriptionRenewals",
                type: "date",
                nullable: true);

            migrationBuilder.AddColumn<Guid>(
                name: "SourceFamilyDeliveryAmendmentId",
                table: "SubscriptionDays",
                type: "uuid",
                nullable: true);

            migrationBuilder.AddColumn<Guid>(
                name: "PreviousSubscriptionId",
                table: "FamilyCheckoutBatchLines",
                type: "uuid",
                nullable: true);

            migrationBuilder.AddColumn<Guid>(
                name: "RenewalRequestId",
                table: "FamilyCheckoutBatchLines",
                type: "uuid",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "Purpose",
                table: "FamilyCheckoutBatches",
                type: "character varying(20)",
                maxLength: 20,
                nullable: false,
                defaultValue: "Initial");

            migrationBuilder.AddColumn<Guid>(
                name: "RenewalRequestId",
                table: "FamilyCheckoutBatches",
                type: "uuid",
                nullable: true);

            migrationBuilder.CreateIndex(
                name: "IX_SubscriptionRenewals_BatchId",
                table: "SubscriptionRenewals",
                column: "BatchId",
                unique: true,
                filter: "\"BatchId\" IS NOT NULL");

            migrationBuilder.CreateIndex(
                name: "IX_SubscriptionRenewals_FamilyGroupId",
                table: "SubscriptionRenewals",
                column: "FamilyGroupId");

            migrationBuilder.CreateIndex(
                name: "IX_SubscriptionRenewals_FamilyMemberId",
                table: "SubscriptionRenewals",
                column: "FamilyMemberId");

            migrationBuilder.CreateIndex(
                name: "IX_SubscriptionDays_SourceFamilyDeliveryAmendmentId",
                table: "SubscriptionDays",
                column: "SourceFamilyDeliveryAmendmentId");

            migrationBuilder.CreateIndex(
                name: "IX_FamilyCheckoutBatchLines_PreviousSubscriptionId",
                table: "FamilyCheckoutBatchLines",
                column: "PreviousSubscriptionId");

            migrationBuilder.CreateIndex(
                name: "IX_FamilyCheckoutBatchLines_RenewalRequestId",
                table: "FamilyCheckoutBatchLines",
                column: "RenewalRequestId");

            migrationBuilder.CreateIndex(
                name: "IX_FamilyCheckoutBatches_FamilyGroupId",
                table: "FamilyCheckoutBatches",
                column: "FamilyGroupId",
                unique: true,
                filter: "\"Status\" IN (0, 1) AND \"Purpose\" = 'Initial'");


            migrationBuilder.CreateIndex(
                name: "IX_FamilyCheckoutBatches_RenewalRequestId",
                table: "FamilyCheckoutBatches",
                column: "RenewalRequestId",
                unique: true,
                filter: "\"RenewalRequestId\" IS NOT NULL");

            migrationBuilder.AddForeignKey(
                name: "FK_SubscriptionDays_FamilySubscriptionDeliveryAmendments_Sourc~",
                table: "SubscriptionDays",
                column: "SourceFamilyDeliveryAmendmentId",
                principalTable: "FamilySubscriptionDeliveryAmendments",
                principalColumn: "Id",
                onDelete: ReferentialAction.Restrict);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "FK_SubscriptionDays_FamilySubscriptionDeliveryAmendments_Sourc~",
                table: "SubscriptionDays");

            migrationBuilder.DropIndex(
                name: "IX_SubscriptionRenewals_BatchId",
                table: "SubscriptionRenewals");

            migrationBuilder.DropIndex(
                name: "IX_SubscriptionRenewals_FamilyGroupId",
                table: "SubscriptionRenewals");

            migrationBuilder.DropIndex(
                name: "IX_SubscriptionRenewals_FamilyMemberId",
                table: "SubscriptionRenewals");

            migrationBuilder.DropIndex(
                name: "IX_SubscriptionDays_SourceFamilyDeliveryAmendmentId",
                table: "SubscriptionDays");

            migrationBuilder.DropIndex(
                name: "IX_FamilyCheckoutBatchLines_PreviousSubscriptionId",
                table: "FamilyCheckoutBatchLines");

            migrationBuilder.DropIndex(
                name: "IX_FamilyCheckoutBatchLines_RenewalRequestId",
                table: "FamilyCheckoutBatchLines");

            migrationBuilder.DropIndex(
                name: "IX_FamilyCheckoutBatches_FamilyGroupId",
                table: "FamilyCheckoutBatches");


            migrationBuilder.DropIndex(
                name: "IX_FamilyCheckoutBatches_RenewalRequestId",
                table: "FamilyCheckoutBatches");

            migrationBuilder.DropColumn(
                name: "BatchId",
                table: "SubscriptionRenewals");

            migrationBuilder.DropColumn(
                name: "BatchVersion",
                table: "SubscriptionRenewals");

            migrationBuilder.DropColumn(
                name: "CheckoutFingerprint",
                table: "SubscriptionRenewals");

            migrationBuilder.DropColumn(
                name: "FamilyGroupId",
                table: "SubscriptionRenewals");

            migrationBuilder.DropColumn(
                name: "FamilyMemberId",
                table: "SubscriptionRenewals");

            migrationBuilder.DropColumn(
                name: "RequestedAddressId",
                table: "SubscriptionRenewals");

            migrationBuilder.DropColumn(
                name: "RequestedStartDate",
                table: "SubscriptionRenewals");

            migrationBuilder.DropColumn(
                name: "SourceFamilyDeliveryAmendmentId",
                table: "SubscriptionDays");

            migrationBuilder.DropColumn(
                name: "PreviousSubscriptionId",
                table: "FamilyCheckoutBatchLines");

            migrationBuilder.DropColumn(
                name: "RenewalRequestId",
                table: "FamilyCheckoutBatchLines");

            migrationBuilder.DropColumn(
                name: "Purpose",
                table: "FamilyCheckoutBatches");

            migrationBuilder.DropColumn(
                name: "RenewalRequestId",
                table: "FamilyCheckoutBatches");

            migrationBuilder.CreateIndex(
                name: "IX_FamilyCheckoutBatches_FamilyGroupId",
                table: "FamilyCheckoutBatches",
                column: "FamilyGroupId",
                unique: true,
                filter: "\"Status\" IN (0, 1)");
        }
    }
}
