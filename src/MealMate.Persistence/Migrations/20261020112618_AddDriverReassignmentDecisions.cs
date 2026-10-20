using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace MealMate.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddDriverReassignmentDecisions : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<DateTime>(
                name: "ReassignmentDecidedAtUtc",
                table: "Complaints",
                type: "timestamp with time zone",
                nullable: true);

            migrationBuilder.AddColumn<Guid>(
                name: "ReassignmentDecidedByUserId",
                table: "Complaints",
                type: "uuid",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "ReassignmentDecision",
                table: "Complaints",
                type: "character varying(20)",
                maxLength: 20,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "ReassignmentDecisionNotes",
                table: "Complaints",
                type: "character varying(500)",
                maxLength: 500,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "ReassignmentNotes",
                table: "Complaints",
                type: "character varying(250)",
                maxLength: 250,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "ReassignmentReason",
                table: "Complaints",
                type: "character varying(100)",
                maxLength: 100,
                nullable: true);

            migrationBuilder.AddColumn<Guid>(
                name: "ReassignmentTripStopId",
                table: "Complaints",
                type: "uuid",
                nullable: true);

            migrationBuilder.CreateIndex(
                name: "IX_Complaints_DriverProfileId_ReassignmentTripStopId_Submitted~",
                table: "Complaints",
                columns: new[] { "DriverProfileId", "ReassignmentTripStopId", "SubmittedAtUtc" });

            migrationBuilder.AddCheckConstraint(
                name: "CK_Complaints_ReassignmentDecision",
                table: "Complaints",
                sql: "\"ReassignmentDecision\" IS NULL OR \"ReassignmentDecision\" IN ('Accepted','Rejected')");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "IX_Complaints_DriverProfileId_ReassignmentTripStopId_Submitted~",
                table: "Complaints");

            migrationBuilder.DropCheckConstraint(
                name: "CK_Complaints_ReassignmentDecision",
                table: "Complaints");

            migrationBuilder.DropColumn(
                name: "ReassignmentDecidedAtUtc",
                table: "Complaints");

            migrationBuilder.DropColumn(
                name: "ReassignmentDecidedByUserId",
                table: "Complaints");

            migrationBuilder.DropColumn(
                name: "ReassignmentDecision",
                table: "Complaints");

            migrationBuilder.DropColumn(
                name: "ReassignmentDecisionNotes",
                table: "Complaints");

            migrationBuilder.DropColumn(
                name: "ReassignmentNotes",
                table: "Complaints");

            migrationBuilder.DropColumn(
                name: "ReassignmentReason",
                table: "Complaints");

            migrationBuilder.DropColumn(
                name: "ReassignmentTripStopId",
                table: "Complaints");
        }
    }
}
