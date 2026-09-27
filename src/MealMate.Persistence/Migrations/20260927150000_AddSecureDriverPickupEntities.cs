using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace MealMate.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddSecureDriverPickupEntities : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql(@"
                CREATE TABLE IF NOT EXISTS ""PickupValidations"" (
                    ""Id"" uuid NOT NULL PRIMARY KEY,
                    ""CreatedAtUtc"" timestamp with time zone NOT NULL,
                    ""UpdatedAtUtc"" timestamp with time zone NULL,
                    ""TripId"" uuid NOT NULL,
                    ""BoxId"" uuid NOT NULL,
                    ""DriverProfileId"" uuid NOT NULL,
                    ""DriverAssignmentId"" uuid NOT NULL,
                    ""BarcodeValue"" character varying(100) NOT NULL,
                    ""TokenHash"" character varying(128) NOT NULL,
                    ""ValidatedAtUtc"" timestamp with time zone NOT NULL,
                    ""ExpiresAtUtc"" timestamp with time zone NOT NULL,
                    ""ConsumedAtUtc"" timestamp with time zone NULL,
                    ""IsManualEntry"" boolean NOT NULL DEFAULT FALSE
                );

                CREATE UNIQUE INDEX IF NOT EXISTS ""IX_PickupValidations_TokenHash"" 
                    ON ""PickupValidations"" (""TokenHash"");

                CREATE INDEX IF NOT EXISTS ""IX_PickupValidations_Trip_Box_Driver_ValidatedAtUtc"" 
                    ON ""PickupValidations"" (""TripId"", ""BoxId"", ""DriverProfileId"", ""ValidatedAtUtc"");

                CREATE TABLE IF NOT EXISTS ""PickupConditionPhotos"" (
                    ""Id"" uuid NOT NULL PRIMARY KEY,
                    ""CreatedAtUtc"" timestamp with time zone NOT NULL,
                    ""UpdatedAtUtc"" timestamp with time zone NULL,
                    ""PickupValidationId"" uuid NOT NULL,
                    ""TripId"" uuid NOT NULL,
                    ""BoxId"" uuid NOT NULL,
                    ""DriverProfileId"" uuid NOT NULL,
                    ""StorageKey"" character varying(500) NOT NULL,
                    ""ContentType"" character varying(100) NOT NULL,
                    ""SizeBytes"" bigint NOT NULL,
                    ""Checksum"" character varying(128) NULL,
                    ""UploadedAtUtc"" timestamp with time zone NOT NULL,
                    ""ConfirmedAtUtc"" timestamp with time zone NULL,
                    ""DeletedAtUtc"" timestamp with time zone NULL
                );

                CREATE UNIQUE INDEX IF NOT EXISTS ""IX_PickupConditionPhotos_StorageKey"" 
                    ON ""PickupConditionPhotos"" (""StorageKey"");

                CREATE INDEX IF NOT EXISTS ""IX_PickupConditionPhotos_Trip_Box_Driver_UploadedAtUtc"" 
                    ON ""PickupConditionPhotos"" (""TripId"", ""BoxId"", ""DriverProfileId"", ""UploadedAtUtc"");
            ");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql(@"
                DROP TABLE IF EXISTS ""PickupConditionPhotos"";
                DROP TABLE IF EXISTS ""PickupValidations"";
            ");
        }
    }
}
