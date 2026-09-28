using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace MealMate.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class ExpandCanonicalMealBarcode : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "IX_MealBarcodes_SubscriptionDayId",
                table: "MealBarcodes");

            migrationBuilder.AddColumn<Guid>(
                name: "MealBarcodeId",
                table: "TripStops",
                type: "uuid",
                nullable: true);

            migrationBuilder.AddColumn<uint>(
                name: "xmin",
                table: "TripStops",
                type: "xid",
                rowVersion: true,
                nullable: false,
                defaultValue: 0u);

            migrationBuilder.AddColumn<uint>(
                name: "xmin",
                table: "Trips",
                type: "xid",
                rowVersion: true,
                nullable: false,
                defaultValue: 0u);

            migrationBuilder.AddColumn<uint>(
                name: "xmin",
                table: "SubscriptionDays",
                type: "xid",
                rowVersion: true,
                nullable: false,
                defaultValue: 0u);

            migrationBuilder.AddColumn<uint>(
                name: "xmin",
                table: "RestaurantDailyOrders",
                type: "xid",
                rowVersion: true,
                nullable: false,
                defaultValue: 0u);

            migrationBuilder.AddColumn<bool>(
                name: "IsRevoked",
                table: "MealBarcodes",
                type: "boolean",
                nullable: false,
                defaultValue: false);

            migrationBuilder.AddColumn<DateTime>(
                name: "PrintedAtUtc",
                table: "MealBarcodes",
                type: "timestamp with time zone",
                nullable: true);

            migrationBuilder.AddColumn<Guid>(
                name: "ReissuedBarcodeId",
                table: "MealBarcodes",
                type: "uuid",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "RevocationReason",
                table: "MealBarcodes",
                type: "character varying(500)",
                maxLength: 500,
                nullable: true);

            migrationBuilder.AddColumn<DateTime>(
                name: "RevokedAtUtc",
                table: "MealBarcodes",
                type: "timestamp with time zone",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "RevokedBy",
                table: "MealBarcodes",
                type: "character varying(100)",
                maxLength: 100,
                nullable: true);

            migrationBuilder.AddColumn<DateTime>(
                name: "ScannedAtUtc",
                table: "MealBarcodes",
                type: "timestamp with time zone",
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "Status",
                table: "MealBarcodes",
                type: "integer",
                nullable: false,
                defaultValue: 1);

            migrationBuilder.AddColumn<Guid>(
                name: "TripStopId",
                table: "MealBarcodes",
                type: "uuid",
                nullable: true);

            migrationBuilder.CreateIndex(
                name: "IX_TripStops_MealBarcodeId",
                table: "TripStops",
                column: "MealBarcodeId");

            migrationBuilder.CreateIndex(
                name: "IX_MealBarcodes_SubscriptionDayId",
                table: "MealBarcodes",
                column: "SubscriptionDayId");

            migrationBuilder.CreateIndex(
                name: "IX_MealBarcodes_SubscriptionDayId_Status",
                table: "MealBarcodes",
                columns: new[] { "SubscriptionDayId", "Status" });

            migrationBuilder.AddForeignKey(
                name: "FK_TripStops_MealBarcodes_MealBarcodeId",
                table: "TripStops",
                column: "MealBarcodeId",
                principalTable: "MealBarcodes",
                principalColumn: "Id",
                onDelete: ReferentialAction.SetNull);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "FK_TripStops_MealBarcodes_MealBarcodeId",
                table: "TripStops");

            migrationBuilder.DropIndex(
                name: "IX_TripStops_MealBarcodeId",
                table: "TripStops");

            migrationBuilder.DropIndex(
                name: "IX_MealBarcodes_SubscriptionDayId",
                table: "MealBarcodes");

            migrationBuilder.DropIndex(
                name: "IX_MealBarcodes_SubscriptionDayId_Status",
                table: "MealBarcodes");

            migrationBuilder.DropColumn(
                name: "MealBarcodeId",
                table: "TripStops");

            migrationBuilder.DropColumn(
                name: "xmin",
                table: "TripStops");

            migrationBuilder.DropColumn(
                name: "xmin",
                table: "Trips");

            migrationBuilder.DropColumn(
                name: "xmin",
                table: "SubscriptionDays");

            migrationBuilder.DropColumn(
                name: "xmin",
                table: "RestaurantDailyOrders");

            migrationBuilder.DropColumn(
                name: "IsRevoked",
                table: "MealBarcodes");

            migrationBuilder.DropColumn(
                name: "PrintedAtUtc",
                table: "MealBarcodes");

            migrationBuilder.DropColumn(
                name: "ReissuedBarcodeId",
                table: "MealBarcodes");

            migrationBuilder.DropColumn(
                name: "RevocationReason",
                table: "MealBarcodes");

            migrationBuilder.DropColumn(
                name: "RevokedAtUtc",
                table: "MealBarcodes");

            migrationBuilder.DropColumn(
                name: "RevokedBy",
                table: "MealBarcodes");

            migrationBuilder.DropColumn(
                name: "ScannedAtUtc",
                table: "MealBarcodes");

            migrationBuilder.DropColumn(
                name: "Status",
                table: "MealBarcodes");

            migrationBuilder.DropColumn(
                name: "TripStopId",
                table: "MealBarcodes");

            migrationBuilder.CreateIndex(
                name: "IX_MealBarcodes_SubscriptionDayId",
                table: "MealBarcodes",
                column: "SubscriptionDayId",
                unique: true);
        }
    }
}
