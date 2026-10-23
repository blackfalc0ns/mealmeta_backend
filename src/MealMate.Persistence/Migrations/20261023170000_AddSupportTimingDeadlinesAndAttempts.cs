using System;
using Microsoft.EntityFrameworkCore.Migrations;
using Microsoft.EntityFrameworkCore.Infrastructure;

#nullable disable

namespace MealMate.Persistence.Migrations;

/// <inheritdoc />
[DbContext(typeof(MealMate.Persistence.AppDbContext))]
[Migration("20261023170000_AddSupportTimingDeadlinesAndAttempts")]
public partial class AddSupportTimingDeadlinesAndAttempts : Migration
{
    /// <inheritdoc />
    protected override void Up(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.AddColumn<DateTime>(
            name: "EscalationDeadlineUtc",
            table: "SupportCaseTimingCycles",
            type: "timestamp with time zone",
            nullable: true);

        migrationBuilder.AddColumn<DateTime>(
            name: "LastWorkerAttemptAtUtc",
            table: "SupportCaseTimingCycles",
            type: "timestamp with time zone",
            nullable: true);

        migrationBuilder.CreateIndex(
            name: "IX_SupportCaseTimingCycles_LastWorkerAttemptAtUtc",
            table: "SupportCaseTimingCycles",
            column: "LastWorkerAttemptAtUtc");
    }

    /// <inheritdoc />
    protected override void Down(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.DropIndex(
            name: "IX_SupportCaseTimingCycles_LastWorkerAttemptAtUtc",
            table: "SupportCaseTimingCycles");

        migrationBuilder.DropColumn(name: "EscalationDeadlineUtc", table: "SupportCaseTimingCycles");
        migrationBuilder.DropColumn(name: "LastWorkerAttemptAtUtc", table: "SupportCaseTimingCycles");
    }
}
