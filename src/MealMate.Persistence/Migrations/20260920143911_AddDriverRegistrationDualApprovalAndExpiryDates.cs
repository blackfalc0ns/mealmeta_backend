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
            migrationBuilder.Sql(@"
                ALTER TABLE ""DriverRegistrations"" ADD COLUMN IF NOT EXISTS ""AdminApprovalStatus"" integer NOT NULL DEFAULT 0;
                ALTER TABLE ""DriverRegistrations"" ADD COLUMN IF NOT EXISTS ""AdminReviewNotes"" text NULL;
                ALTER TABLE ""DriverRegistrations"" ADD COLUMN IF NOT EXISTS ""DateOfBirthUtc"" timestamp with time zone NULL;
                ALTER TABLE ""DriverRegistrations"" ADD COLUMN IF NOT EXISTS ""IsVehicleOwned"" boolean NULL;
                ALTER TABLE ""DriverRegistrations"" ADD COLUMN IF NOT EXISTS ""Nationality"" text NULL;
                ALTER TABLE ""DriverRegistrations"" ADD COLUMN IF NOT EXISTS ""RegistrationSource"" text NOT NULL DEFAULT '';
                ALTER TABLE ""DriverRegistrations"" ADD COLUMN IF NOT EXISTS ""RestaurantApprovalStatus"" integer NOT NULL DEFAULT 0;
                ALTER TABLE ""DriverRegistrations"" ADD COLUMN IF NOT EXISTS ""RestaurantReviewNotes"" text NULL;
                ALTER TABLE ""DriverRegistrations"" ADD COLUMN IF NOT EXISTS ""RestaurantReviewedAtUtc"" timestamp with time zone NULL;
                ALTER TABLE ""DriverRegistrations"" ADD COLUMN IF NOT EXISTS ""RestaurantReviewedByUserId"" uuid NULL;
                ALTER TABLE ""DriverRegistrations"" ADD COLUMN IF NOT EXISTS ""VehicleColor"" text NULL;
                ALTER TABLE ""DriverRegistrations"" ADD COLUMN IF NOT EXISTS ""VehiclePhotoStorageKey"" text NULL;
                ALTER TABLE ""DriverRegistrations"" ADD COLUMN IF NOT EXISTS ""VehicleYear"" integer NULL;
            ");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql(@"
                ALTER TABLE ""DriverRegistrations"" DROP COLUMN IF EXISTS ""AdminApprovalStatus"";
                ALTER TABLE ""DriverRegistrations"" DROP COLUMN IF EXISTS ""AdminReviewNotes"";
                ALTER TABLE ""DriverRegistrations"" DROP COLUMN IF EXISTS ""DateOfBirthUtc"";
                ALTER TABLE ""DriverRegistrations"" DROP COLUMN IF EXISTS ""IsVehicleOwned"";
                ALTER TABLE ""DriverRegistrations"" DROP COLUMN IF EXISTS ""Nationality"";
                ALTER TABLE ""DriverRegistrations"" DROP COLUMN IF EXISTS ""RegistrationSource"";
                ALTER TABLE ""DriverRegistrations"" DROP COLUMN IF EXISTS ""RestaurantApprovalStatus"";
                ALTER TABLE ""DriverRegistrations"" DROP COLUMN IF EXISTS ""RestaurantReviewNotes"";
                ALTER TABLE ""DriverRegistrations"" DROP COLUMN IF EXISTS ""RestaurantReviewedAtUtc"";
                ALTER TABLE ""DriverRegistrations"" DROP COLUMN IF EXISTS ""RestaurantReviewedByUserId"";
                ALTER TABLE ""DriverRegistrations"" DROP COLUMN IF EXISTS ""VehicleColor"";
                ALTER TABLE ""DriverRegistrations"" DROP COLUMN IF EXISTS ""VehiclePhotoStorageKey"";
                ALTER TABLE ""DriverRegistrations"" DROP COLUMN IF EXISTS ""VehicleYear"";
            ");
        }
    }
}
