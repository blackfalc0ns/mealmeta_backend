using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace MealMate.Persistence.Migrations;

[DbContext(typeof(AppDbContext))]
[Migration("20260820110000_AddAvailableForFamilyToProgramAndBundle")]
public partial class AddAvailableForFamilyToProgramAndBundle : Migration
{
    protected override void Up(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.AddColumn<bool>(
            name: "AvailableForFamily",
            table: "Programs",
            type: "boolean",
            nullable: false,
            defaultValue: false);

        migrationBuilder.AddColumn<bool>(
            name: "AvailableForFamily",
            table: "Bundles",
            type: "boolean",
            nullable: false,
            defaultValue: false);

        migrationBuilder.CreateIndex(
            name: "IX_Programs_AvailableForFamily",
            table: "Programs",
            column: "AvailableForFamily");

        migrationBuilder.CreateIndex(
            name: "IX_Bundles_AvailableForFamily",
            table: "Bundles",
            column: "AvailableForFamily");
    }

    protected override void Down(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.DropIndex(
            name: "IX_Bundles_AvailableForFamily",
            table: "Bundles");

        migrationBuilder.DropIndex(
            name: "IX_Programs_AvailableForFamily",
            table: "Programs");

        migrationBuilder.DropColumn(
            name: "AvailableForFamily",
            table: "Bundles");

        migrationBuilder.DropColumn(
            name: "AvailableForFamily",
            table: "Programs");
    }
}
