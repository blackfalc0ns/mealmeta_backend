using System;
using MealMate.Persistence;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace MealMate.Persistence.Migrations;

[DbContext(typeof(AppDbContext))]
[Migration("20260820140000_AddSelectedFamilyPackageIdToFamilyGroup")]
public partial class AddSelectedFamilyPackageIdToFamilyGroup : Migration
{
    protected override void Up(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.AddColumn<Guid>(
            name: "SelectedFamilyPackageId",
            table: "FamilyGroups",
            type: "uuid",
            nullable: true);

        migrationBuilder.CreateIndex(
            name: "IX_FamilyGroups_SelectedFamilyPackageId",
            table: "FamilyGroups",
            column: "SelectedFamilyPackageId");
    }

    protected override void Down(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.DropIndex(
            name: "IX_FamilyGroups_SelectedFamilyPackageId",
            table: "FamilyGroups");

        migrationBuilder.DropColumn(
            name: "SelectedFamilyPackageId",
            table: "FamilyGroups");
    }
}
