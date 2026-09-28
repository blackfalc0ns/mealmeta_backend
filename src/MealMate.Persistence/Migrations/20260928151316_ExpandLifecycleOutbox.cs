using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace MealMate.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class ExpandLifecycleOutbox : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "AggregateId",
                table: "OutboxMessages",
                type: "character varying(128)",
                maxLength: 128,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "AggregateType",
                table: "OutboxMessages",
                type: "character varying(64)",
                maxLength: 64,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "CorrelationId",
                table: "OutboxMessages",
                type: "character varying(128)",
                maxLength: 128,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "DeduplicationKey",
                table: "OutboxMessages",
                type: "character varying(256)",
                maxLength: 256,
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "EventVersion",
                table: "OutboxMessages",
                type: "integer",
                nullable: false,
                defaultValue: 1);

            migrationBuilder.AddColumn<DateTime>(
                name: "NextAttemptAtUtc",
                table: "OutboxMessages",
                type: "timestamp with time zone",
                nullable: true);

            migrationBuilder.AddColumn<DateTime>(
                name: "PoisonedAtUtc",
                table: "OutboxMessages",
                type: "timestamp with time zone",
                nullable: true);

            migrationBuilder.CreateIndex(
                name: "IX_OutboxMessages_DeduplicationKey",
                table: "OutboxMessages",
                column: "DeduplicationKey",
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "IX_OutboxMessages_DeduplicationKey",
                table: "OutboxMessages");

            migrationBuilder.DropColumn(
                name: "AggregateId",
                table: "OutboxMessages");

            migrationBuilder.DropColumn(
                name: "AggregateType",
                table: "OutboxMessages");

            migrationBuilder.DropColumn(
                name: "CorrelationId",
                table: "OutboxMessages");

            migrationBuilder.DropColumn(
                name: "DeduplicationKey",
                table: "OutboxMessages");

            migrationBuilder.DropColumn(
                name: "EventVersion",
                table: "OutboxMessages");

            migrationBuilder.DropColumn(
                name: "NextAttemptAtUtc",
                table: "OutboxMessages");

            migrationBuilder.DropColumn(
                name: "PoisonedAtUtc",
                table: "OutboxMessages");
        }
    }
}
