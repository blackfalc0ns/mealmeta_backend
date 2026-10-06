using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace MealMate.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddVoiceCallDeviceSessions : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "VoiceCallDeviceSessions",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    UserId = table.Column<Guid>(type: "uuid", nullable: false),
                    InstallationId = table.Column<string>(type: "character varying(128)", maxLength: 128, nullable: false),
                    Platform = table.Column<string>(type: "character varying(16)", maxLength: 16, nullable: false),
                    FcmDeviceTokenId = table.Column<Guid>(type: "uuid", nullable: true),
                    ApnsVoipToken = table.Column<string>(type: "character varying(256)", maxLength: 256, nullable: true),
                    ControlProofHash = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    ExpiresAtUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    RevokedAtUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    CapabilityVersion = table.Column<int>(type: "integer", nullable: false),
                    LastSeenAtUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
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
                    table.PrimaryKey("PK_VoiceCallDeviceSessions", x => x.Id);
                });

            migrationBuilder.CreateIndex(
                name: "IX_VoiceCallDeviceSessions_ControlProofHash",
                table: "VoiceCallDeviceSessions",
                column: "ControlProofHash",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_VoiceCallDeviceSessions_ExpiresAtUtc",
                table: "VoiceCallDeviceSessions",
                column: "ExpiresAtUtc");

            migrationBuilder.CreateIndex(
                name: "IX_VoiceCallDeviceSessions_UserId_InstallationId",
                table: "VoiceCallDeviceSessions",
                columns: new[] { "UserId", "InstallationId" });

            migrationBuilder.CreateIndex(
                name: "IX_VoiceCallDeviceSessions_UserId_RevokedAtUtc",
                table: "VoiceCallDeviceSessions",
                columns: new[] { "UserId", "RevokedAtUtc" });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "VoiceCallDeviceSessions");
        }
    }
}
