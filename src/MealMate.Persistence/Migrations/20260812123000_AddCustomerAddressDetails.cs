using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace MealMate.Persistence.Migrations;

public partial class AddCustomerAddressDetails : Migration
{
    protected override void Up(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.AddColumn<string>(name: "Governorate", table: "CustomerAddresses", type: "character varying(100)", maxLength: 100, nullable: true);
        migrationBuilder.AddColumn<string>(name: "Area", table: "CustomerAddresses", type: "character varying(100)", maxLength: 100, nullable: true);
        migrationBuilder.AddColumn<string>(name: "Block", table: "CustomerAddresses", type: "character varying(50)", maxLength: 50, nullable: true);
        migrationBuilder.AddColumn<string>(name: "Street", table: "CustomerAddresses", type: "character varying(150)", maxLength: 150, nullable: true);
        migrationBuilder.AddColumn<string>(name: "Building", table: "CustomerAddresses", type: "character varying(50)", maxLength: 50, nullable: true);
        migrationBuilder.AddColumn<string>(name: "Unit", table: "CustomerAddresses", type: "character varying(50)", maxLength: 50, nullable: true);
        migrationBuilder.AddColumn<string>(name: "Floor", table: "CustomerAddresses", type: "character varying(50)", maxLength: 50, nullable: true);
    }

    protected override void Down(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.DropColumn(name: "Governorate", table: "CustomerAddresses");
        migrationBuilder.DropColumn(name: "Area", table: "CustomerAddresses");
        migrationBuilder.DropColumn(name: "Block", table: "CustomerAddresses");
        migrationBuilder.DropColumn(name: "Street", table: "CustomerAddresses");
        migrationBuilder.DropColumn(name: "Building", table: "CustomerAddresses");
        migrationBuilder.DropColumn(name: "Unit", table: "CustomerAddresses");
        migrationBuilder.DropColumn(name: "Floor", table: "CustomerAddresses");
    }
}
