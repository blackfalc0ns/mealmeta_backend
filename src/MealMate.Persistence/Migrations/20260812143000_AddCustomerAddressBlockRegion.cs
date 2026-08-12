using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace MealMate.Persistence.Migrations;

public partial class AddCustomerAddressBlockRegion : Migration
{
    protected override void Up(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.Sql("""
            ALTER TABLE "CustomerAddresses" ADD COLUMN IF NOT EXISTS "BlockRegionId" uuid NULL;
            CREATE INDEX IF NOT EXISTS "IX_CustomerAddresses_BlockRegionId" ON "CustomerAddresses" ("BlockRegionId");
            """);
    }
    protected override void Down(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.Sql("""
            DROP INDEX IF EXISTS "IX_CustomerAddresses_BlockRegionId";
            ALTER TABLE "CustomerAddresses" DROP COLUMN IF EXISTS "BlockRegionId";
            """);
    }
}
