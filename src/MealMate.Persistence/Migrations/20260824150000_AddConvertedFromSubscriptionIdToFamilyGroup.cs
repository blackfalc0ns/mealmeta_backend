using System;
using MealMate.Persistence;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace MealMate.Persistence.Migrations;

[DbContext(typeof(AppDbContext))]
[Migration("20260824150000_AddConvertedFromSubscriptionIdToFamilyGroup")]
public partial class AddConvertedFromSubscriptionIdToFamilyGroup : Migration
{
    protected override void Up(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.AddColumn<Guid>(
            name: "ConvertedFromSubscriptionId",
            table: "FamilyGroups",
            type: "uuid",
            nullable: true);

        migrationBuilder.CreateIndex(
            name: "IX_FamilyGroups_ConvertedFromSubscriptionId",
            table: "FamilyGroups",
            column: "ConvertedFromSubscriptionId");
    }

    protected override void Down(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.DropIndex(
            name: "IX_FamilyGroups_ConvertedFromSubscriptionId",
            table: "FamilyGroups");

        migrationBuilder.DropColumn(
            name: "ConvertedFromSubscriptionId",
            table: "FamilyGroups");
    }
}
