using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace MealMate.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class SyncSecureDriverPickupAndVoiceCallSnapshot : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "RowVersion",
                table: "CallSessions");

            migrationBuilder.AddColumn<bool>(
                name: "IsManualEntry",
                table: "PickupScans",
                type: "boolean",
                nullable: false,
                defaultValue: false);

            migrationBuilder.AddColumn<string>(
                name: "ManualEntryReason",
                table: "PickupScans",
                type: "character varying(500)",
                maxLength: 500,
                nullable: true);

            migrationBuilder.AddColumn<uint>(
                name: "xmin",
                table: "CallSessions",
                type: "xid",
                rowVersion: true,
                nullable: false,
                defaultValue: 0u);

            migrationBuilder.CreateTable(
                name: "PickupConditionPhotos",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    PickupValidationId = table.Column<Guid>(type: "uuid", nullable: false),
                    TripId = table.Column<Guid>(type: "uuid", nullable: false),
                    BoxId = table.Column<Guid>(type: "uuid", nullable: false),
                    DriverProfileId = table.Column<Guid>(type: "uuid", nullable: false),
                    StorageKey = table.Column<string>(type: "character varying(500)", maxLength: 500, nullable: false),
                    ContentType = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false),
                    SizeBytes = table.Column<long>(type: "bigint", nullable: false),
                    Checksum = table.Column<string>(type: "character varying(128)", maxLength: 128, nullable: true),
                    UploadedAtUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    ConfirmedAtUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    DeletedAtUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_PickupConditionPhotos", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "PickupValidations",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    TripId = table.Column<Guid>(type: "uuid", nullable: false),
                    BoxId = table.Column<Guid>(type: "uuid", nullable: false),
                    DriverProfileId = table.Column<Guid>(type: "uuid", nullable: false),
                    DriverAssignmentId = table.Column<Guid>(type: "uuid", nullable: false),
                    BarcodeValue = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false),
                    TokenHash = table.Column<string>(type: "character varying(128)", maxLength: 128, nullable: false),
                    ValidatedAtUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    ExpiresAtUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    ConsumedAtUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    IsManualEntry = table.Column<bool>(type: "boolean", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_PickupValidations", x => x.Id);
                });

            migrationBuilder.CreateIndex(
                name: "IX_PickupConditionPhotos_StorageKey",
                table: "PickupConditionPhotos",
                column: "StorageKey",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_PickupConditionPhotos_TripId_BoxId_DriverProfileId_Uploaded~",
                table: "PickupConditionPhotos",
                columns: new[] { "TripId", "BoxId", "DriverProfileId", "UploadedAtUtc" });

            migrationBuilder.CreateIndex(
                name: "IX_PickupValidations_TokenHash",
                table: "PickupValidations",
                column: "TokenHash",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_PickupValidations_TripId_BoxId_DriverProfileId_ValidatedAtU~",
                table: "PickupValidations",
                columns: new[] { "TripId", "BoxId", "DriverProfileId", "ValidatedAtUtc" });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "PickupConditionPhotos");

            migrationBuilder.DropTable(
                name: "PickupValidations");

            migrationBuilder.DropColumn(
                name: "IsManualEntry",
                table: "PickupScans");

            migrationBuilder.DropColumn(
                name: "ManualEntryReason",
                table: "PickupScans");

            migrationBuilder.DropColumn(
                name: "xmin",
                table: "CallSessions");

            migrationBuilder.AddColumn<byte[]>(
                name: "RowVersion",
                table: "CallSessions",
                type: "bytea",
                rowVersion: true,
                nullable: false,
                defaultValue: new byte[0]);
        }
    }
}
