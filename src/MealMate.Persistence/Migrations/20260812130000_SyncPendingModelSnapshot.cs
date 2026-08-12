using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace MealMate.Persistence.Migrations;

/// <inheritdoc />
public partial class SyncPendingModelSnapshot : Migration
{
    /// <inheritdoc />
    protected override void Up(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.Sql("""
            ALTER TABLE "Ingredients" ADD COLUMN IF NOT EXISTS "IsHalalCertified" boolean NOT NULL DEFAULT false;
            ALTER TABLE "Ingredients" ADD COLUMN IF NOT EXISTS "IsLocal" boolean NOT NULL DEFAULT false;
            ALTER TABLE "Ingredients" ADD COLUMN IF NOT EXISTS "IsOriginVisibleToCustomers" boolean NOT NULL DEFAULT false;
            ALTER TABLE "Ingredients" ADD COLUMN IF NOT EXISTS "OriginCountryCode" character varying(3) NULL;
            ALTER TABLE "Ingredients" ADD COLUMN IF NOT EXISTS "SupplierName" character varying(200) NULL;
            ALTER TABLE "Ingredients" ADD COLUMN IF NOT EXISTS "TraceabilityNote" character varying(1000) NULL;

            CREATE TABLE IF NOT EXISTS "CapacityHolds" (
                "Id" uuid NOT NULL,
                "RestaurantId" uuid NOT NULL,
                "SubscriptionId" uuid NOT NULL,
                "ServiceDate" date NOT NULL,
                "Quantity" integer NOT NULL,
                "Status" integer NOT NULL,
                "ExpiresAtUtc" timestamp with time zone NOT NULL,
                "ConfirmedAtUtc" timestamp with time zone NULL,
                "ReleasedAtUtc" timestamp with time zone NULL,
                "CreatedAtUtc" timestamp with time zone NOT NULL,
                "CreatedByUserId" uuid NULL,
                "ModifiedAtUtc" timestamp with time zone NULL,
                "ModifiedByUserId" uuid NULL,
                "IsDeleted" boolean NOT NULL,
                "DeletedAtUtc" timestamp with time zone NULL,
                "DeletedByUserId" uuid NULL,
                CONSTRAINT "PK_CapacityHolds" PRIMARY KEY ("Id")
            );

            CREATE INDEX IF NOT EXISTS "IX_CapacityHolds_ExpiresAtUtc" ON "CapacityHolds" ("ExpiresAtUtc");
            CREATE INDEX IF NOT EXISTS "IX_CapacityHolds_RestaurantId_ServiceDate_Status" ON "CapacityHolds" ("RestaurantId", "ServiceDate", "Status");
            CREATE UNIQUE INDEX IF NOT EXISTS "IX_CapacityHolds_SubscriptionId_ServiceDate" ON "CapacityHolds" ("SubscriptionId", "ServiceDate");
            """);
    }

    /// <inheritdoc />
    protected override void Down(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.Sql("""
            DROP TABLE IF EXISTS "CapacityHolds";
            ALTER TABLE "Ingredients" DROP COLUMN IF EXISTS "IsHalalCertified";
            ALTER TABLE "Ingredients" DROP COLUMN IF EXISTS "IsLocal";
            ALTER TABLE "Ingredients" DROP COLUMN IF EXISTS "IsOriginVisibleToCustomers";
            ALTER TABLE "Ingredients" DROP COLUMN IF EXISTS "OriginCountryCode";
            ALTER TABLE "Ingredients" DROP COLUMN IF EXISTS "SupplierName";
            ALTER TABLE "Ingredients" DROP COLUMN IF EXISTS "TraceabilityNote";
            """);
    }
}
