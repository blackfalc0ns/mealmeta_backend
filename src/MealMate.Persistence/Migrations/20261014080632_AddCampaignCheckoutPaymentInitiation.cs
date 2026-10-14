using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace MealMate.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddCampaignCheckoutPaymentInitiation : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "PaymentCallbackUrl",
                table: "CampaignCheckoutSessions",
                type: "character varying(2048)",
                maxLength: 2048,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "PaymentErrorUrl",
                table: "CampaignCheckoutSessions",
                type: "character varying(2048)",
                maxLength: 2048,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "PaymentId",
                table: "CampaignCheckoutSessions",
                type: "character varying(200)",
                maxLength: 200,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "PaymentIdempotencyKey",
                table: "CampaignCheckoutSessions",
                type: "character varying(200)",
                maxLength: 200,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "PaymentInitializationErrorCode",
                table: "CampaignCheckoutSessions",
                type: "character varying(100)",
                maxLength: 100,
                nullable: true);

            migrationBuilder.AddColumn<DateTime>(
                name: "PaymentInitializationStartedAtUtc",
                table: "CampaignCheckoutSessions",
                type: "timestamp with time zone",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "PaymentRequestHash",
                table: "CampaignCheckoutSessions",
                type: "character varying(128)",
                maxLength: 128,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "PaymentUrl",
                table: "CampaignCheckoutSessions",
                type: "character varying(2048)",
                maxLength: 2048,
                nullable: true);

            migrationBuilder.AddColumn<bool>(
                name: "PaymentWasCancelled",
                table: "CampaignCheckoutSessions",
                type: "boolean",
                nullable: false,
                defaultValue: false);

            migrationBuilder.AddColumn<DateTime>(
                name: "ReservationsReleasedAtUtc",
                table: "CampaignCheckoutSessions",
                type: "timestamp with time zone",
                nullable: true);

            migrationBuilder.CreateIndex(
                name: "IX_CampaignCheckoutSessions_CustomerId_PaymentIdempotencyKey",
                table: "CampaignCheckoutSessions",
                columns: new[] { "CustomerId", "PaymentIdempotencyKey" },
                unique: true,
                filter: "\"PaymentIdempotencyKey\" IS NOT NULL");

            migrationBuilder.CreateIndex(
                name: "IX_CampaignCheckoutSessions_PaymentId",
                table: "CampaignCheckoutSessions",
                column: "PaymentId",
                unique: true,
                filter: "\"PaymentId\" IS NOT NULL");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "IX_CampaignCheckoutSessions_CustomerId_PaymentIdempotencyKey",
                table: "CampaignCheckoutSessions");

            migrationBuilder.DropIndex(
                name: "IX_CampaignCheckoutSessions_PaymentId",
                table: "CampaignCheckoutSessions");

            migrationBuilder.DropColumn(
                name: "PaymentCallbackUrl",
                table: "CampaignCheckoutSessions");

            migrationBuilder.DropColumn(
                name: "PaymentErrorUrl",
                table: "CampaignCheckoutSessions");

            migrationBuilder.DropColumn(
                name: "PaymentId",
                table: "CampaignCheckoutSessions");

            migrationBuilder.DropColumn(
                name: "PaymentIdempotencyKey",
                table: "CampaignCheckoutSessions");

            migrationBuilder.DropColumn(
                name: "PaymentInitializationErrorCode",
                table: "CampaignCheckoutSessions");

            migrationBuilder.DropColumn(
                name: "PaymentInitializationStartedAtUtc",
                table: "CampaignCheckoutSessions");

            migrationBuilder.DropColumn(
                name: "PaymentRequestHash",
                table: "CampaignCheckoutSessions");

            migrationBuilder.DropColumn(
                name: "PaymentUrl",
                table: "CampaignCheckoutSessions");

            migrationBuilder.DropColumn(
                name: "PaymentWasCancelled",
                table: "CampaignCheckoutSessions");

            migrationBuilder.DropColumn(
                name: "ReservationsReleasedAtUtc",
                table: "CampaignCheckoutSessions");
        }
    }
}
