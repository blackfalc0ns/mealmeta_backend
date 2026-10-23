using Microsoft.EntityFrameworkCore.Migrations;
using Microsoft.EntityFrameworkCore.Infrastructure;

#nullable disable

namespace MealMate.Persistence.Migrations;

/// <inheritdoc />
[DbContext(typeof(MealMate.Persistence.AppDbContext))]
[Migration("20261023123842_CloseUnifiedSupportGaps")]
public partial class CloseUnifiedSupportGaps : Migration
{
    /// <inheritdoc />
    protected override void Up(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.AddColumn<Guid>(
            name: "TriageCountryId", table: "SupportCases", type: "uuid", nullable: true);
        migrationBuilder.AddColumn<Guid>(
            name: "TriageRegionId", table: "SupportCases", type: "uuid", nullable: true);
        migrationBuilder.AddColumn<string>(
            name: "TriageStatus", table: "SupportCases", type: "character varying(32)", maxLength: 32,
            nullable: false, defaultValue: "GlobalTriage");
        migrationBuilder.AddColumn<Guid>(
            name: "PendingTransferToUserId", table: "SupportCases", type: "uuid", nullable: true);
        migrationBuilder.AddColumn<string>(
            name: "PendingTransferReason", table: "SupportCases", type: "character varying(1000)", maxLength: 1000,
            nullable: true);
        migrationBuilder.CreateIndex(
            name: "IX_SupportCases_TriageCountryId_TriageRegionId_Status",
            table: "SupportCases", columns: new[] { "TriageCountryId", "TriageRegionId", "Status" });
    }

    /// <inheritdoc />
    protected override void Down(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.DropIndex(
            name: "IX_SupportCases_TriageCountryId_TriageRegionId_Status", table: "SupportCases");
        migrationBuilder.DropColumn(name: "TriageCountryId", table: "SupportCases");
        migrationBuilder.DropColumn(name: "TriageRegionId", table: "SupportCases");
        migrationBuilder.DropColumn(name: "TriageStatus", table: "SupportCases");
        migrationBuilder.DropColumn(name: "PendingTransferToUserId", table: "SupportCases");
        migrationBuilder.DropColumn(name: "PendingTransferReason", table: "SupportCases");
    }
}
