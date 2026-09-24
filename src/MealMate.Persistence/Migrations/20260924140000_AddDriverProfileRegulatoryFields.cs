using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace MealMate.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddDriverProfileRegulatoryFields : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql(@"
                ALTER TABLE ""DriverProfiles"" ADD COLUMN IF NOT EXISTS ""NationalIdExpiryUtc"" timestamp with time zone NULL;
                ALTER TABLE ""DriverProfiles"" ADD COLUMN IF NOT EXISTS ""LicenseExpiryUtc"" timestamp with time zone NULL;
                ALTER TABLE ""DriverProfiles"" ADD COLUMN IF NOT EXISTS ""VehicleRegistrationExpiryUtc"" timestamp with time zone NULL;
                ALTER TABLE ""DriverProfiles"" ADD COLUMN IF NOT EXISTS ""ContractExpiryUtc"" timestamp with time zone NULL;
                ALTER TABLE ""DriverProfiles"" ADD COLUMN IF NOT EXISTS ""VehicleYear"" integer NULL;
                ALTER TABLE ""DriverProfiles"" ADD COLUMN IF NOT EXISTS ""VehicleColor"" character varying(50) NULL;
                ALTER TABLE ""DriverProfiles"" ADD COLUMN IF NOT EXISTS ""IsVehicleOwned"" boolean NULL;
                ALTER TABLE ""DriverProfiles"" ADD COLUMN IF NOT EXISTS ""Nationality"" character varying(100) NULL;
                ALTER TABLE ""DriverProfiles"" ADD COLUMN IF NOT EXISTS ""DateOfBirthUtc"" timestamp with time zone NULL;
            ");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql(@"
                ALTER TABLE ""DriverProfiles"" DROP COLUMN IF EXISTS ""NationalIdExpiryUtc"";
                ALTER TABLE ""DriverProfiles"" DROP COLUMN IF EXISTS ""LicenseExpiryUtc"";
                ALTER TABLE ""DriverProfiles"" DROP COLUMN IF EXISTS ""VehicleRegistrationExpiryUtc"";
                ALTER TABLE ""DriverProfiles"" DROP COLUMN IF EXISTS ""ContractExpiryUtc"";
                ALTER TABLE ""DriverProfiles"" DROP COLUMN IF EXISTS ""VehicleYear"";
                ALTER TABLE ""DriverProfiles"" DROP COLUMN IF EXISTS ""VehicleColor"";
                ALTER TABLE ""DriverProfiles"" DROP COLUMN IF EXISTS ""IsVehicleOwned"";
                ALTER TABLE ""DriverProfiles"" DROP COLUMN IF EXISTS ""Nationality"";
                ALTER TABLE ""DriverProfiles"" DROP COLUMN IF EXISTS ""DateOfBirthUtc"";
            ");
        }
    }
}
