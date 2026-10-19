using MealMate.Persistence;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;

namespace MealMate.Persistence.Migrations;

[DbContext(typeof(AppDbContext))]
[Migration("20261019130000_TrackAutomaticFamilyStartDate")]
public sealed class TrackAutomaticFamilyStartDate : Migration
{
    protected override void Up(MigrationBuilder migrationBuilder)
        => migrationBuilder.AddColumn<bool>(
            name: "StartDateIsAutomatic",
            table: "FamilyMembers",
            type: "boolean",
            nullable: false,
            defaultValue: false);

    protected override void Down(MigrationBuilder migrationBuilder)
        => migrationBuilder.DropColumn(
            name: "StartDateIsAutomatic",
            table: "FamilyMembers");
}
