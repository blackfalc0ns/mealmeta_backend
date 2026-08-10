using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace MealMate.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddRestaurantRegistrationOnboardingFields : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "AuthorizedSignatoryCivilId",
                table: "RestaurantRegistrations",
                type: "character varying(50)",
                maxLength: 50,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "AuthorizedSignatoryTitle",
                table: "RestaurantRegistrations",
                type: "character varying(200)",
                maxLength: 200,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "BankAccountNumber",
                table: "RestaurantRegistrations",
                type: "character varying(100)",
                maxLength: 100,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "BankBeneficiaryName",
                table: "RestaurantRegistrations",
                type: "character varying(200)",
                maxLength: 200,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "BankName",
                table: "RestaurantRegistrations",
                type: "character varying(200)",
                maxLength: 200,
                nullable: true);

            migrationBuilder.AddColumn<DateOnly>(
                name: "CommercialLicenseExpiry",
                table: "RestaurantRegistrations",
                type: "date",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "CommercialLicenseNumber",
                table: "RestaurantRegistrations",
                type: "character varying(100)",
                maxLength: 100,
                nullable: true);

            migrationBuilder.AddColumn<DateOnly>(
                name: "CommercialRegistrationExpiry",
                table: "RestaurantRegistrations",
                type: "date",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "CompanyCivilId",
                table: "RestaurantRegistrations",
                type: "character varying(50)",
                maxLength: 50,
                nullable: true);

            migrationBuilder.AddColumn<bool>(
                name: "DataResponsibilityConfirmed",
                table: "RestaurantRegistrations",
                type: "boolean",
                nullable: true);

            migrationBuilder.AddColumn<bool>(
                name: "DeliveryByRestaurantConfirmed",
                table: "RestaurantRegistrations",
                type: "boolean",
                nullable: true);

            migrationBuilder.AddColumn<DateOnly>(
                name: "FoodLicenseExpiry",
                table: "RestaurantRegistrations",
                type: "date",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "FoodLicenseNumber",
                table: "RestaurantRegistrations",
                type: "character varying(100)",
                maxLength: 100,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "Iban",
                table: "RestaurantRegistrations",
                type: "character varying(34)",
                maxLength: 34,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "InstagramHandle",
                table: "RestaurantRegistrations",
                type: "character varying(100)",
                maxLength: 100,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "LegalEntityType",
                table: "RestaurantRegistrations",
                type: "character varying(100)",
                maxLength: 100,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "PrimaryContactEmail",
                table: "RestaurantRegistrations",
                type: "character varying(256)",
                maxLength: 256,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "PrimaryContactName",
                table: "RestaurantRegistrations",
                type: "character varying(200)",
                maxLength: 200,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "PrimaryContactPhone",
                table: "RestaurantRegistrations",
                type: "character varying(30)",
                maxLength: 30,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "PrimaryContactTitle",
                table: "RestaurantRegistrations",
                type: "character varying(200)",
                maxLength: 200,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "RestaurantCode",
                table: "RestaurantRegistrations",
                type: "character varying(50)",
                maxLength: 50,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "RestaurantNotes",
                table: "RestaurantRegistrations",
                type: "character varying(2000)",
                maxLength: 2000,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "SettlementCurrency",
                table: "RestaurantRegistrations",
                type: "character varying(10)",
                maxLength: 10,
                nullable: true);

            migrationBuilder.AddColumn<DateOnly>(
                name: "SubmissionDate",
                table: "RestaurantRegistrations",
                type: "date",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "SwiftCode",
                table: "RestaurantRegistrations",
                type: "character varying(20)",
                maxLength: 20,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "Website",
                table: "RestaurantRegistrations",
                type: "character varying(500)",
                maxLength: 500,
                nullable: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "AuthorizedSignatoryCivilId",
                table: "RestaurantRegistrations");

            migrationBuilder.DropColumn(
                name: "AuthorizedSignatoryTitle",
                table: "RestaurantRegistrations");

            migrationBuilder.DropColumn(
                name: "BankAccountNumber",
                table: "RestaurantRegistrations");

            migrationBuilder.DropColumn(
                name: "BankBeneficiaryName",
                table: "RestaurantRegistrations");

            migrationBuilder.DropColumn(
                name: "BankName",
                table: "RestaurantRegistrations");

            migrationBuilder.DropColumn(
                name: "CommercialLicenseExpiry",
                table: "RestaurantRegistrations");

            migrationBuilder.DropColumn(
                name: "CommercialLicenseNumber",
                table: "RestaurantRegistrations");

            migrationBuilder.DropColumn(
                name: "CommercialRegistrationExpiry",
                table: "RestaurantRegistrations");

            migrationBuilder.DropColumn(
                name: "CompanyCivilId",
                table: "RestaurantRegistrations");

            migrationBuilder.DropColumn(
                name: "DataResponsibilityConfirmed",
                table: "RestaurantRegistrations");

            migrationBuilder.DropColumn(
                name: "DeliveryByRestaurantConfirmed",
                table: "RestaurantRegistrations");

            migrationBuilder.DropColumn(
                name: "FoodLicenseExpiry",
                table: "RestaurantRegistrations");

            migrationBuilder.DropColumn(
                name: "FoodLicenseNumber",
                table: "RestaurantRegistrations");

            migrationBuilder.DropColumn(
                name: "Iban",
                table: "RestaurantRegistrations");

            migrationBuilder.DropColumn(
                name: "InstagramHandle",
                table: "RestaurantRegistrations");

            migrationBuilder.DropColumn(
                name: "LegalEntityType",
                table: "RestaurantRegistrations");

            migrationBuilder.DropColumn(
                name: "PrimaryContactEmail",
                table: "RestaurantRegistrations");

            migrationBuilder.DropColumn(
                name: "PrimaryContactName",
                table: "RestaurantRegistrations");

            migrationBuilder.DropColumn(
                name: "PrimaryContactPhone",
                table: "RestaurantRegistrations");

            migrationBuilder.DropColumn(
                name: "PrimaryContactTitle",
                table: "RestaurantRegistrations");

            migrationBuilder.DropColumn(
                name: "RestaurantCode",
                table: "RestaurantRegistrations");

            migrationBuilder.DropColumn(
                name: "RestaurantNotes",
                table: "RestaurantRegistrations");

            migrationBuilder.DropColumn(
                name: "SettlementCurrency",
                table: "RestaurantRegistrations");

            migrationBuilder.DropColumn(
                name: "SubmissionDate",
                table: "RestaurantRegistrations");

            migrationBuilder.DropColumn(
                name: "SwiftCode",
                table: "RestaurantRegistrations");

            migrationBuilder.DropColumn(
                name: "Website",
                table: "RestaurantRegistrations");
        }
    }
}
