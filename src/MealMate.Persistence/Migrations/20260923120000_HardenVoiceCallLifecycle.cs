using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace MealMate.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class HardenVoiceCallLifecycle : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<DateTime>(
                name: "ConnectingAtUtc",
                table: "CallSessions",
                type: "timestamp with time zone",
                nullable: true);

            migrationBuilder.CreateIndex(
                name: "IX_CallSessions_Status_RingingAtUtc",
                table: "CallSessions",
                columns: new[] { "Status", "RingingAtUtc" });

            migrationBuilder.CreateIndex(
                name: "IX_CallSessions_Status_AcceptedAtUtc",
                table: "CallSessions",
                columns: new[] { "Status", "AcceptedAtUtc" });

            migrationBuilder.CreateIndex(
                name: "IX_CallSessions_Status_ConnectingAtUtc",
                table: "CallSessions",
                columns: new[] { "Status", "ConnectingAtUtc" });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "IX_CallSessions_Status_ConnectingAtUtc",
                table: "CallSessions");

            migrationBuilder.DropIndex(
                name: "IX_CallSessions_Status_AcceptedAtUtc",
                table: "CallSessions");

            migrationBuilder.DropIndex(
                name: "IX_CallSessions_Status_RingingAtUtc",
                table: "CallSessions");

            migrationBuilder.DropColumn(
                name: "ConnectingAtUtc",
                table: "CallSessions");
        }
    }
}
