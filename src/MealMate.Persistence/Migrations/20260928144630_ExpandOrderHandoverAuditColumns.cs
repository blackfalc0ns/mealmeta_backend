using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace MealMate.Persistence.Migrations;

/// <inheritdoc />
public partial class ExpandOrderHandoverAuditColumns : Migration
{
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<int>(
                name: "AttemptNumber",
                table: "OrderHandovers",
                type: "integer",
                nullable: false,
                defaultValue: 1);

            migrationBuilder.AddColumn<Guid>(
                name: "MealBarcodeId",
                table: "OrderHandovers",
                type: "uuid",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "RevocationReason",
                table: "OrderHandovers",
                type: "character varying(512)",
                maxLength: 512,
                nullable: true);

            migrationBuilder.AddColumn<DateTime>(
                name: "RevokedAtUtc",
                table: "OrderHandovers",
                type: "timestamp with time zone",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "RevokedBy",
                table: "OrderHandovers",
                type: "character varying(128)",
                maxLength: 128,
                nullable: true);

            migrationBuilder.AddColumn<Guid>(
                name: "SupersedesHandoverId",
                table: "OrderHandovers",
                type: "uuid",
                nullable: true);

            migrationBuilder.AddColumn<Guid>(
                name: "TripStopId",
                table: "OrderHandovers",
                type: "uuid",
                nullable: true);

            migrationBuilder.CreateIndex(
                name: "IX_OrderHandovers_DailyOrderId_AttemptNumber",
                table: "OrderHandovers",
                columns: new[] { "DailyOrderId", "AttemptNumber" });

            migrationBuilder.CreateIndex(
                name: "IX_OrderHandovers_MealBarcodeId",
                table: "OrderHandovers",
                column: "MealBarcodeId");

            migrationBuilder.CreateIndex(
                name: "IX_OrderHandovers_TripStopId",
                table: "OrderHandovers",
                column: "TripStopId");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "IX_OrderHandovers_DailyOrderId_AttemptNumber",
                table: "OrderHandovers");

            migrationBuilder.DropIndex(
                name: "IX_OrderHandovers_MealBarcodeId",
                table: "OrderHandovers");

            migrationBuilder.DropIndex(
                name: "IX_OrderHandovers_TripStopId",
                table: "OrderHandovers");

            migrationBuilder.DropColumn(
                name: "AttemptNumber",
                table: "OrderHandovers");

            migrationBuilder.DropColumn(
                name: "MealBarcodeId",
                table: "OrderHandovers");

            migrationBuilder.DropColumn(
                name: "RevocationReason",
                table: "OrderHandovers");

            migrationBuilder.DropColumn(
                name: "RevokedAtUtc",
                table: "OrderHandovers");

            migrationBuilder.DropColumn(
                name: "RevokedBy",
                table: "OrderHandovers");

            migrationBuilder.DropColumn(
                name: "SupersedesHandoverId",
                table: "OrderHandovers");

            migrationBuilder.DropColumn(
                name: "TripStopId",
                table: "OrderHandovers");
        }
    }
