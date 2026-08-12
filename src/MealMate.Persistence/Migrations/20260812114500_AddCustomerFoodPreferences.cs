using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace MealMate.Persistence.Migrations;

[DbContext(typeof(AppDbContext))]
[Migration("20260812114500_AddCustomerFoodPreferences")]
public partial class AddCustomerFoodPreferences : Migration
{
    protected override void Up(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.AddColumn<string>(name: "DietaryNotes", table: "CustomerProfiles", type: "character varying(1000)", maxLength: 1000, nullable: true);
        migrationBuilder.AddColumn<string>(name: "DietaryPattern", table: "CustomerProfiles", type: "character varying(80)", maxLength: 80, nullable: true);
    }

    protected override void Down(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.DropColumn(name: "DietaryNotes", table: "CustomerProfiles");
        migrationBuilder.DropColumn(name: "DietaryPattern", table: "CustomerProfiles");
    }
}
