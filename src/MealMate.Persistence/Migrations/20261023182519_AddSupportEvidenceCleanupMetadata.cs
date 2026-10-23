using System;
using Microsoft.EntityFrameworkCore.Migrations;
using Microsoft.EntityFrameworkCore.Infrastructure;

#nullable disable

namespace MealMate.Persistence.Migrations
{
    /// <inheritdoc />
    [DbContext(typeof(MealMate.Persistence.AppDbContext))]
    [Migration("20261023182519_AddSupportEvidenceCleanupMetadata")]
    public partial class AddSupportEvidenceCleanupMetadata : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<DateTime>(
                name: "SupportCleanupLastAttemptAtUtc",
                table: "UploadFileRecords",
                type: "timestamp with time zone",
                nullable: true);

            migrationBuilder.AddColumn<DateTime>(
                name: "SupportPayloadDeletedAtUtc",
                table: "UploadFileRecords",
                type: "timestamp with time zone",
                nullable: true);

            migrationBuilder.CreateIndex(
                name: "IX_UploadFileRecords_SupportCleanup",
                table: "UploadFileRecords",
                columns: new[] { "Scope", "Folder", "SupportPayloadDeletedAtUtc", "SupportCleanupLastAttemptAtUtc", "UploadedAtUtc" });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "IX_UploadFileRecords_SupportCleanup",
                table: "UploadFileRecords");

            migrationBuilder.DropColumn(
                name: "SupportCleanupLastAttemptAtUtc",
                table: "UploadFileRecords");

            migrationBuilder.DropColumn(
                name: "SupportPayloadDeletedAtUtc",
                table: "UploadFileRecords");
        }
    }
}
