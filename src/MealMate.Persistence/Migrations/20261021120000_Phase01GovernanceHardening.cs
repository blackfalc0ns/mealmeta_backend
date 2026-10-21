using MealMate.Persistence;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace MealMate.Persistence.Migrations;

[DbContext(typeof(AppDbContext))]
[Migration("20261021120000_Phase01GovernanceHardening")]
public sealed class Phase01GovernanceHardening : Migration
{
    protected override void Up(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.AddColumn<string>(
            name: "SubmittedContentHash",
            table: "RuleVersions",
            type: "character varying(64)",
            maxLength: 64,
            nullable: true);
    }

    protected override void Down(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.DropColumn(name: "SubmittedContentHash", table: "RuleVersions");
    }
}
