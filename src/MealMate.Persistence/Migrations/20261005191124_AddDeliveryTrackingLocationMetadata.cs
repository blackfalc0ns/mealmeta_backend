using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace MealMate.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddDeliveryTrackingLocationMetadata : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<double>(
                name: "AccuracyMeters",
                table: "DeliveryTrackingPoints",
                type: "double precision",
                nullable: true);

            migrationBuilder.AddColumn<DateTime>(
                name: "CapturedAtUtc",
                table: "DeliveryTrackingPoints",
                type: "timestamp with time zone",
                nullable: true);

            migrationBuilder.AddColumn<DateTime>(
                name: "ReceivedAtUtc",
                table: "DeliveryTrackingPoints",
                type: "timestamp with time zone",
                nullable: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "AccuracyMeters",
                table: "DeliveryTrackingPoints");

            migrationBuilder.DropColumn(
                name: "CapturedAtUtc",
                table: "DeliveryTrackingPoints");

            migrationBuilder.DropColumn(
                name: "ReceivedAtUtc",
                table: "DeliveryTrackingPoints");
        }
    }
}
