using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace MealMate.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddCustomerDeliveryAddressSchedule : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<Guid>(
                name: "ActiveDeliveryAddressId",
                table: "CustomerProfiles",
                type: "uuid",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "ActiveDeliveryAddressLine",
                table: "CustomerProfiles",
                type: "character varying(500)",
                maxLength: 500,
                nullable: true);

            migrationBuilder.AddColumn<Guid>(
                name: "ActiveDeliveryBlockRegionId",
                table: "CustomerProfiles",
                type: "uuid",
                nullable: true);

            migrationBuilder.AddColumn<Guid>(
                name: "ActiveDeliveryRegionId",
                table: "CustomerProfiles",
                type: "uuid",
                nullable: true);

            migrationBuilder.AddColumn<DateTime>(
                name: "PendingDeliveryAddressEffectiveFromUtc",
                table: "CustomerProfiles",
                type: "timestamp with time zone",
                nullable: true);

            migrationBuilder.AddColumn<Guid>(
                name: "PendingDeliveryAddressId",
                table: "CustomerProfiles",
                type: "uuid",
                nullable: true);

            migrationBuilder.AddColumn<Guid>(
                name: "DeliveryAddressId",
                table: "SubscriptionDays",
                type: "uuid",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "DeliveryAddressLine",
                table: "SubscriptionDays",
                type: "character varying(500)",
                maxLength: 500,
                nullable: true);

            migrationBuilder.AddColumn<Guid>(
                name: "DeliveryBlockRegionId",
                table: "SubscriptionDays",
                type: "uuid",
                nullable: true);

            migrationBuilder.AddColumn<Guid>(
                name: "DeliveryRegionId",
                table: "SubscriptionDays",
                type: "uuid",
                nullable: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "ActiveDeliveryAddressId",
                table: "CustomerProfiles");

            migrationBuilder.DropColumn(
                name: "ActiveDeliveryAddressLine",
                table: "CustomerProfiles");

            migrationBuilder.DropColumn(
                name: "ActiveDeliveryBlockRegionId",
                table: "CustomerProfiles");

            migrationBuilder.DropColumn(
                name: "ActiveDeliveryRegionId",
                table: "CustomerProfiles");

            migrationBuilder.DropColumn(
                name: "PendingDeliveryAddressEffectiveFromUtc",
                table: "CustomerProfiles");

            migrationBuilder.DropColumn(
                name: "PendingDeliveryAddressId",
                table: "CustomerProfiles");

            migrationBuilder.DropColumn(
                name: "DeliveryAddressId",
                table: "SubscriptionDays");

            migrationBuilder.DropColumn(
                name: "DeliveryAddressLine",
                table: "SubscriptionDays");

            migrationBuilder.DropColumn(
                name: "DeliveryBlockRegionId",
                table: "SubscriptionDays");

            migrationBuilder.DropColumn(
                name: "DeliveryRegionId",
                table: "SubscriptionDays");
        }
    }
}
