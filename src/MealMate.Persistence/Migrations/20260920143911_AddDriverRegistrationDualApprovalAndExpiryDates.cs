using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace MealMate.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddDriverRegistrationDualApprovalAndExpiryDates : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<int>(
                name: "AdminApprovalStatus",
                table: "DriverRegistrations",
                type: "integer",
                nullable: false,
                defaultValue: 0);

            migrationBuilder.AddColumn<string>(
                name: "AdminReviewNotes",
                table: "DriverRegistrations",
                type: "text",
                nullable: true);

            migrationBuilder.AddColumn<DateTime>(
                name: "DateOfBirthUtc",
                table: "DriverRegistrations",
                type: "timestamp with time zone",
                nullable: true);

            migrationBuilder.AddColumn<bool>(
                name: "IsVehicleOwned",
                table: "DriverRegistrations",
                type: "boolean",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "Nationality",
                table: "DriverRegistrations",
                type: "text",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "RegistrationSource",
                table: "DriverRegistrations",
                type: "text",
                nullable: false,
                defaultValue: "");

            migrationBuilder.AddColumn<int>(
                name: "RestaurantApprovalStatus",
                table: "DriverRegistrations",
                type: "integer",
                nullable: false,
                defaultValue: 0);

            migrationBuilder.AddColumn<string>(
                name: "RestaurantReviewNotes",
                table: "DriverRegistrations",
                type: "text",
                nullable: true);

            migrationBuilder.AddColumn<DateTime>(
                name: "RestaurantReviewedAtUtc",
                table: "DriverRegistrations",
                type: "timestamp with time zone",
                nullable: true);

            migrationBuilder.AddColumn<Guid>(
                name: "RestaurantReviewedByUserId",
                table: "DriverRegistrations",
                type: "uuid",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "VehicleColor",
                table: "DriverRegistrations",
                type: "text",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "VehiclePhotoStorageKey",
                table: "DriverRegistrations",
                type: "text",
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "VehicleYear",
                table: "DriverRegistrations",
                type: "integer",
                nullable: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "AdminApprovalStatus",
                table: "DriverRegistrations");

            migrationBuilder.DropColumn(
                name: "AdminReviewNotes",
                table: "DriverRegistrations");

            migrationBuilder.DropColumn(
                name: "DateOfBirthUtc",
                table: "DriverRegistrations");

            migrationBuilder.DropColumn(
                name: "IsVehicleOwned",
                table: "DriverRegistrations");

            migrationBuilder.DropColumn(
                name: "Nationality",
                table: "DriverRegistrations");

            migrationBuilder.DropColumn(
                name: "RegistrationSource",
                table: "DriverRegistrations");

            migrationBuilder.DropColumn(
                name: "RestaurantApprovalStatus",
                table: "DriverRegistrations");

            migrationBuilder.DropColumn(
                name: "RestaurantReviewNotes",
                table: "DriverRegistrations");

            migrationBuilder.DropColumn(
                name: "RestaurantReviewedAtUtc",
                table: "DriverRegistrations");

            migrationBuilder.DropColumn(
                name: "RestaurantReviewedByUserId",
                table: "DriverRegistrations");

            migrationBuilder.DropColumn(
                name: "VehicleColor",
                table: "DriverRegistrations");

            migrationBuilder.DropColumn(
                name: "VehiclePhotoStorageKey",
                table: "DriverRegistrations");

            migrationBuilder.DropColumn(
                name: "VehicleYear",
                table: "DriverRegistrations");
        }
    }
}
