using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace MealMate.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AdRefundReviewAuditV2 : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "ProviderRefundId",
                table: "AdFinancialLedgerEntries",
                type: "character varying(128)",
                maxLength: 128,
                nullable: true);

            migrationBuilder.AddColumn<decimal>(
                name: "RefundApprovedAmount",
                table: "AdFinancialLedgerEntries",
                type: "numeric",
                nullable: true);

            migrationBuilder.AddColumn<Guid>(
                name: "RefundApprovedPolicyId",
                table: "AdFinancialLedgerEntries",
                type: "uuid",
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "RefundApprovedPolicyVersion",
                table: "AdFinancialLedgerEntries",
                type: "integer",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "RefundReviewReason",
                table: "AdFinancialLedgerEntries",
                type: "character varying(500)",
                maxLength: 500,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "RefundReviewStatus",
                table: "AdFinancialLedgerEntries",
                type: "character varying(16)",
                maxLength: 16,
                nullable: true);

            migrationBuilder.AddColumn<DateTime>(
                name: "RefundReviewedAtUtc",
                table: "AdFinancialLedgerEntries",
                type: "timestamp with time zone",
                nullable: true);

            migrationBuilder.AddColumn<Guid>(
                name: "RefundReviewedByUserId",
                table: "AdFinancialLedgerEntries",
                type: "uuid",
                nullable: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "ProviderRefundId",
                table: "AdFinancialLedgerEntries");

            migrationBuilder.DropColumn(
                name: "RefundApprovedAmount",
                table: "AdFinancialLedgerEntries");

            migrationBuilder.DropColumn(
                name: "RefundApprovedPolicyId",
                table: "AdFinancialLedgerEntries");

            migrationBuilder.DropColumn(
                name: "RefundApprovedPolicyVersion",
                table: "AdFinancialLedgerEntries");

            migrationBuilder.DropColumn(
                name: "RefundReviewReason",
                table: "AdFinancialLedgerEntries");

            migrationBuilder.DropColumn(
                name: "RefundReviewStatus",
                table: "AdFinancialLedgerEntries");

            migrationBuilder.DropColumn(
                name: "RefundReviewedAtUtc",
                table: "AdFinancialLedgerEntries");

            migrationBuilder.DropColumn(
                name: "RefundReviewedByUserId",
                table: "AdFinancialLedgerEntries");
        }
    }
}
