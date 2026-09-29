using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace MealMate.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddOutboxAtomicLeaseColumns : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "LockedByWorkerId",
                table: "OutboxMessages",
                type: "character varying(128)",
                maxLength: 128,
                nullable: true);

            migrationBuilder.AddColumn<DateTime>(
                name: "LockExpiresAtUtc",
                table: "OutboxMessages",
                type: "timestamp with time zone",
                nullable: true);

            migrationBuilder.AddColumn<Guid>(
                name: "LeaseToken",
                table: "OutboxMessages",
                type: "uuid",
                nullable: true);

            migrationBuilder.CreateIndex(
                name: "IX_OutboxMessages_Lease_Pending",
                table: "OutboxMessages",
                columns: new[] { "ProcessedAtUtc", "PoisonedAtUtc", "LockExpiresAtUtc", "NextAttemptAtUtc" });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "IX_OutboxMessages_Lease_Pending",
                table: "OutboxMessages");

            migrationBuilder.DropColumn(
                name: "LockedByWorkerId",
                table: "OutboxMessages");

            migrationBuilder.DropColumn(
                name: "LockExpiresAtUtc",
                table: "OutboxMessages");

            migrationBuilder.DropColumn(
                name: "LeaseToken",
                table: "OutboxMessages");
        }
    }
}
