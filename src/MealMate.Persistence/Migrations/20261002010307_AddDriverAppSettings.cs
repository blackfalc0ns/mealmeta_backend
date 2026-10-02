using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace MealMate.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddDriverAppSettings : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "DriverAppSettings",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    DriverProfileId = table.Column<Guid>(type: "uuid", nullable: false),
                    PreferredLanguage = table.Column<string>(type: "character varying(10)", maxLength: 10, nullable: true),
                    PreferredNavigationApp = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: true),
                    AutoLaunchNavigation = table.Column<bool>(type: "boolean", nullable: true),
                    SoundAlertsEnabled = table.Column<bool>(type: "boolean", nullable: true),
                    VibrationEnabled = table.Column<bool>(type: "boolean", nullable: true),
                    BiometricLoginEnabled = table.Column<bool>(type: "boolean", nullable: true),
                    AutoSyncOnReconnect = table.Column<bool>(type: "boolean", nullable: true),
                    CreatedAtUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    CreatedByUserId = table.Column<Guid>(type: "uuid", nullable: true),
                    ModifiedAtUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    ModifiedByUserId = table.Column<Guid>(type: "uuid", nullable: true),
                    IsDeleted = table.Column<bool>(type: "boolean", nullable: false),
                    DeletedAtUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    DeletedByUserId = table.Column<Guid>(type: "uuid", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_DriverAppSettings", x => x.Id);
                    table.CheckConstraint("CK_DriverAppSettings_PreferredLanguage", "\"PreferredLanguage\" IS NULL OR \"PreferredLanguage\" IN ('ar', 'en')");
                    table.CheckConstraint("CK_DriverAppSettings_PreferredNavigationApp", "\"PreferredNavigationApp\" IS NULL OR \"PreferredNavigationApp\" IN ('GoogleMaps', 'Waze', 'AppleMaps')");
                });

            migrationBuilder.CreateIndex(
                name: "IX_DriverAppSettings_DriverProfileId",
                table: "DriverAppSettings",
                column: "DriverProfileId",
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "DriverAppSettings");
        }
    }
}
