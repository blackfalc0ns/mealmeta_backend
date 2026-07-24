using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace MealMate.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class M01_AuthRefreshOtpAndFields : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "Address",
                table: "RestaurantRegistrations",
                type: "character varying(500)",
                maxLength: 500,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "CommercialRegistrationNumber",
                table: "RestaurantRegistrations",
                type: "character varying(100)",
                maxLength: 100,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "OwnerName",
                table: "RestaurantRegistrations",
                type: "character varying(200)",
                maxLength: 200,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "InfluencerCode",
                table: "InfluencerProfiles",
                type: "character varying(50)",
                maxLength: 50,
                nullable: true);

            migrationBuilder.AddColumn<Guid>(
                name: "CountryId",
                table: "DriverRegistrations",
                type: "uuid",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "ProfileImageStorageKey",
                table: "DriverRegistrations",
                type: "character varying(500)",
                maxLength: 500,
                nullable: true);

            migrationBuilder.AddColumn<Guid>(
                name: "RegionId",
                table: "DriverRegistrations",
                type: "uuid",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "VehicleType",
                table: "DriverRegistrations",
                type: "character varying(50)",
                maxLength: 50,
                nullable: true);

            migrationBuilder.AddColumn<Guid>(
                name: "CountryId",
                table: "DriverProfiles",
                type: "uuid",
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "ProfileImageStatus",
                table: "DriverProfiles",
                type: "integer",
                nullable: false,
                defaultValue: 0);

            migrationBuilder.AddColumn<string>(
                name: "ProfileImageStorageKey",
                table: "DriverProfiles",
                type: "character varying(500)",
                maxLength: 500,
                nullable: true);

            migrationBuilder.AddColumn<Guid>(
                name: "RegionId",
                table: "DriverProfiles",
                type: "uuid",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "VehicleType",
                table: "DriverProfiles",
                type: "character varying(50)",
                maxLength: 50,
                nullable: true);

            migrationBuilder.CreateTable(
                name: "OtpChallenges",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    UserId = table.Column<Guid>(type: "uuid", nullable: true),
                    Destination = table.Column<string>(type: "character varying(256)", maxLength: 256, nullable: false),
                    Channel = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false),
                    Purpose = table.Column<string>(type: "character varying(50)", maxLength: 50, nullable: false),
                    CodeHash = table.Column<string>(type: "character varying(128)", maxLength: 128, nullable: false),
                    ExpiresAtUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    CreatedAtUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    ConsumedAtUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    AttemptCount = table.Column<int>(type: "integer", nullable: false),
                    MaxAttempts = table.Column<int>(type: "integer", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_OtpChallenges", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "RefreshTokens",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    UserId = table.Column<Guid>(type: "uuid", nullable: false),
                    TokenHash = table.Column<string>(type: "character varying(128)", maxLength: 128, nullable: false),
                    ExpiresAtUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    CreatedAtUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    RevokedAtUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    ReplacedByTokenId = table.Column<Guid>(type: "uuid", nullable: true),
                    DeviceInfo = table.Column<string>(type: "character varying(500)", maxLength: 500, nullable: true),
                    IpAddress = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_RefreshTokens", x => x.Id);
                });

            migrationBuilder.CreateIndex(
                name: "IX_RestaurantRegistrations_CommercialRegistrationNumber",
                table: "RestaurantRegistrations",
                column: "CommercialRegistrationNumber");

            migrationBuilder.CreateIndex(
                name: "IX_InfluencerProfiles_InfluencerCode",
                table: "InfluencerProfiles",
                column: "InfluencerCode",
                unique: true,
                filter: "\"InfluencerCode\" IS NOT NULL");

            migrationBuilder.CreateIndex(
                name: "IX_OtpChallenges_Destination_Purpose_CreatedAtUtc",
                table: "OtpChallenges",
                columns: new[] { "Destination", "Purpose", "CreatedAtUtc" });

            migrationBuilder.CreateIndex(
                name: "IX_OtpChallenges_UserId",
                table: "OtpChallenges",
                column: "UserId");

            migrationBuilder.CreateIndex(
                name: "IX_RefreshTokens_ExpiresAtUtc",
                table: "RefreshTokens",
                column: "ExpiresAtUtc");

            migrationBuilder.CreateIndex(
                name: "IX_RefreshTokens_TokenHash",
                table: "RefreshTokens",
                column: "TokenHash",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_RefreshTokens_UserId",
                table: "RefreshTokens",
                column: "UserId");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "OtpChallenges");

            migrationBuilder.DropTable(
                name: "RefreshTokens");

            migrationBuilder.DropIndex(
                name: "IX_RestaurantRegistrations_CommercialRegistrationNumber",
                table: "RestaurantRegistrations");

            migrationBuilder.DropIndex(
                name: "IX_InfluencerProfiles_InfluencerCode",
                table: "InfluencerProfiles");

            migrationBuilder.DropColumn(
                name: "Address",
                table: "RestaurantRegistrations");

            migrationBuilder.DropColumn(
                name: "CommercialRegistrationNumber",
                table: "RestaurantRegistrations");

            migrationBuilder.DropColumn(
                name: "OwnerName",
                table: "RestaurantRegistrations");

            migrationBuilder.DropColumn(
                name: "InfluencerCode",
                table: "InfluencerProfiles");

            migrationBuilder.DropColumn(
                name: "CountryId",
                table: "DriverRegistrations");

            migrationBuilder.DropColumn(
                name: "ProfileImageStorageKey",
                table: "DriverRegistrations");

            migrationBuilder.DropColumn(
                name: "RegionId",
                table: "DriverRegistrations");

            migrationBuilder.DropColumn(
                name: "VehicleType",
                table: "DriverRegistrations");

            migrationBuilder.DropColumn(
                name: "CountryId",
                table: "DriverProfiles");

            migrationBuilder.DropColumn(
                name: "ProfileImageStatus",
                table: "DriverProfiles");

            migrationBuilder.DropColumn(
                name: "ProfileImageStorageKey",
                table: "DriverProfiles");

            migrationBuilder.DropColumn(
                name: "RegionId",
                table: "DriverProfiles");

            migrationBuilder.DropColumn(
                name: "VehicleType",
                table: "DriverProfiles");
        }
    }
}
