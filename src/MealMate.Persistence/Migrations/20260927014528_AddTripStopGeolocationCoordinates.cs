using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace MealMate.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddTripStopGeolocationCoordinates : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql(@"
                ALTER TABLE ""TripStops"" ADD COLUMN IF NOT EXISTS ""Latitude"" numeric(10,7) NULL;
                ALTER TABLE ""TripStops"" ADD COLUMN IF NOT EXISTS ""Longitude"" numeric(10,7) NULL;
                ALTER TABLE ""DriverProfiles"" ADD COLUMN IF NOT EXISTS ""ContractExpiryUtc"" timestamp with time zone NULL;
                ALTER TABLE ""DriverProfiles"" ADD COLUMN IF NOT EXISTS ""DateOfBirthUtc"" timestamp with time zone NULL;
                ALTER TABLE ""DriverProfiles"" ADD COLUMN IF NOT EXISTS ""IsVehicleOwned"" boolean NULL;
                ALTER TABLE ""DriverProfiles"" ADD COLUMN IF NOT EXISTS ""LicenseExpiryUtc"" timestamp with time zone NULL;
                ALTER TABLE ""DriverProfiles"" ADD COLUMN IF NOT EXISTS ""NationalIdExpiryUtc"" timestamp with time zone NULL;
                ALTER TABLE ""DriverProfiles"" ADD COLUMN IF NOT EXISTS ""Nationality"" character varying(100) NULL;
                ALTER TABLE ""DriverProfiles"" ADD COLUMN IF NOT EXISTS ""VehicleColor"" character varying(50) NULL;
                ALTER TABLE ""DriverProfiles"" ADD COLUMN IF NOT EXISTS ""VehicleRegistrationExpiryUtc"" timestamp with time zone NULL;
                ALTER TABLE ""DriverProfiles"" ADD COLUMN IF NOT EXISTS ""VehicleYear"" integer NULL;
                ALTER TABLE ""CallSessions"" ADD COLUMN IF NOT EXISTS ""ConnectingAtUtc"" timestamp with time zone NULL;
                CREATE INDEX IF NOT EXISTS ""IX_CallSessions_Status_AcceptedAtUtc"" ON ""CallSessions"" (""Status"", ""AcceptedAtUtc"");
                CREATE INDEX IF NOT EXISTS ""IX_CallSessions_Status_ConnectingAtUtc"" ON ""CallSessions"" (""Status"", ""ConnectingAtUtc"");
                CREATE INDEX IF NOT EXISTS ""IX_CallSessions_Status_RingingAtUtc"" ON ""CallSessions"" (""Status"", ""RingingAtUtc"");
            ");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql(@"
                ALTER TABLE ""TripStops"" DROP COLUMN IF EXISTS ""Latitude"";
                ALTER TABLE ""TripStops"" DROP COLUMN IF EXISTS ""Longitude"";
                DROP INDEX IF EXISTS ""IX_CallSessions_Status_AcceptedAtUtc"";
                DROP INDEX IF EXISTS ""IX_CallSessions_Status_ConnectingAtUtc"";
                DROP INDEX IF EXISTS ""IX_CallSessions_Status_RingingAtUtc"";
                ALTER TABLE ""CallSessions"" DROP COLUMN IF EXISTS ""ConnectingAtUtc"";
            ");
        }
    }
}
