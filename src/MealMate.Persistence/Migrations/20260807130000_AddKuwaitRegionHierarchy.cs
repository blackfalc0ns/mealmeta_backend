using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace MealMate.Persistence.Migrations;

public partial class AddKuwaitRegionHierarchy : Migration
{
    protected override void Up(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.AddColumn<Guid>(
            name: "ParentRegionId",
            table: "Regions",
            type: "uuid",
            nullable: true);

        migrationBuilder.AddColumn<string>(
            name: "Type",
            table: "Regions",
            type: "character varying(20)",
            maxLength: 20,
            nullable: false,
            defaultValue: "Area");

        migrationBuilder.CreateIndex(
            name: "IX_Regions_CountryId_ParentRegionId_IsActive",
            table: "Regions",
            columns: new[] { "CountryId", "ParentRegionId", "IsActive" });

        migrationBuilder.CreateIndex(
            name: "IX_Regions_ParentRegionId",
            table: "Regions",
            column: "ParentRegionId");

        migrationBuilder.AddForeignKey(
            name: "FK_Regions_Regions_ParentRegionId",
            table: "Regions",
            column: "ParentRegionId",
            principalTable: "Regions",
            principalColumn: "Id",
            onDelete: ReferentialAction.Restrict);
    }

    protected override void Down(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.DropForeignKey("FK_Regions_Regions_ParentRegionId", "Regions");
        migrationBuilder.DropIndex("IX_Regions_CountryId_ParentRegionId_IsActive", "Regions");
        migrationBuilder.DropIndex("IX_Regions_ParentRegionId", "Regions");
        migrationBuilder.DropColumn("ParentRegionId", "Regions");
        migrationBuilder.DropColumn("Type", "Regions");
    }
}
