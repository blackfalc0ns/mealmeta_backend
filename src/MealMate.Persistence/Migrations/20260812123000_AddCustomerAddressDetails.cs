using System;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;
using MealMate.Persistence;

#nullable disable

namespace MealMate.Persistence.Migrations;

[DbContext(typeof(AppDbContext))]
[Migration("20260812123000_AddCustomerAddressDetails")]
public partial class AddCustomerAddressDetails : Migration
{
    protected override void Up(MigrationBuilder migrationBuilder)
    {
        // IF NOT EXISTS keeps local DBs safe if columns were added manually earlier.
        migrationBuilder.Sql("""
            ALTER TABLE "CustomerAddresses" ADD COLUMN IF NOT EXISTS "Governorate" character varying(100) NULL;
            ALTER TABLE "CustomerAddresses" ADD COLUMN IF NOT EXISTS "Area" character varying(100) NULL;
            ALTER TABLE "CustomerAddresses" ADD COLUMN IF NOT EXISTS "Block" character varying(50) NULL;
            ALTER TABLE "CustomerAddresses" ADD COLUMN IF NOT EXISTS "Street" character varying(150) NULL;
            ALTER TABLE "CustomerAddresses" ADD COLUMN IF NOT EXISTS "Building" character varying(50) NULL;
            ALTER TABLE "CustomerAddresses" ADD COLUMN IF NOT EXISTS "Unit" character varying(50) NULL;
            ALTER TABLE "CustomerAddresses" ADD COLUMN IF NOT EXISTS "Floor" character varying(50) NULL;
            """);
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
