using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace MealMate.Persistence.Migrations;

public partial class ResidualModelSync : Migration
{
    protected override void Up(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.Sql("""
            DO $$ BEGIN
              IF EXISTS (SELECT 1 FROM information_schema.columns WHERE table_name='Ingredients' AND column_name='OriginCountryCode') THEN
                ALTER TABLE "Ingredients" ALTER COLUMN "OriginCountryCode" TYPE character varying(3);
              END IF;
              IF EXISTS (SELECT 1 FROM information_schema.columns WHERE table_name='Ingredients' AND column_name='SupplierName') THEN
                ALTER TABLE "Ingredients" ALTER COLUMN "SupplierName" TYPE character varying(200);
              END IF;
              IF EXISTS (SELECT 1 FROM information_schema.columns WHERE table_name='Ingredients' AND column_name='TraceabilityNote') THEN
                ALTER TABLE "Ingredients" ALTER COLUMN "TraceabilityNote" TYPE character varying(1000);
              END IF;
            END $$;
            """);
    }

    protected override void Down(MigrationBuilder migrationBuilder) { }
}
