using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace MealMate.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddUniqueActiveVoiceDeviceSessionPerInstallation : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            // The partial-unique replacement is created by
            // AddDriverArrivalAndUploadRecords (the sessions were generated concurrently);
            // this migration only removes the plain non-unique index it replaces.
            migrationBuilder.DropIndex(
                name: "IX_VoiceCallDeviceSessions_UserId_InstallationId",
                table: "VoiceCallDeviceSessions");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateIndex(
                name: "IX_VoiceCallDeviceSessions_UserId_InstallationId",
                table: "VoiceCallDeviceSessions",
                columns: new[] { "UserId", "InstallationId" });
        }
    }
}
