using MealMate.Persistence;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable
namespace MealMate.Persistence.Migrations;

[DbContext(typeof(AppDbContext))]
[Migration("20261022100000_AddRestaurantDeliveryWindow")]
public sealed class AddRestaurantDeliveryWindow : Migration
{
    protected override void Up(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.AddColumn<TimeOnly>(name: "DeliveryWindowStart", table: "RestaurantProfiles", type: "time without time zone", nullable: true);
        migrationBuilder.AddColumn<TimeOnly>(name: "DeliveryWindowEnd", table: "RestaurantProfiles", type: "time without time zone", nullable: true);
        migrationBuilder.AddColumn<int>(name: "PreparationLeadTimeMinutes", table: "RestaurantProfiles", type: "integer", nullable: true);
        // Only inherit a complete, unambiguous existing pair. Conflicting restaurants must explicitly choose their window.
        migrationBuilder.Sql("""
            WITH candidates AS (
                SELECT r.* FROM "RestaurantServiceRegions" r
                WHERE r."IsActive" OR NOT EXISTS (
                    SELECT 1 FROM "RestaurantServiceRegions" a
                    WHERE a."RestaurantId" = r."RestaurantId" AND a."IsActive"
                )
            ), uniform_windows AS (
                SELECT "RestaurantId", MIN("DeliveryWindowStart") AS starts, MIN("DeliveryWindowEnd") AS ends
                FROM candidates GROUP BY "RestaurantId"
                HAVING COUNT(DISTINCT ("DeliveryWindowStart", "DeliveryWindowEnd")) = 1
                   AND MIN("DeliveryWindowStart") < MIN("DeliveryWindowEnd")
            )
            UPDATE "RestaurantProfiles" p SET "DeliveryWindowStart" = w.starts, "DeliveryWindowEnd" = w.ends
            FROM uniform_windows w WHERE p."Id" = w."RestaurantId";
            UPDATE "RestaurantServiceRegions" r
            SET "DeliveryWindowStart" = p."DeliveryWindowStart", "DeliveryWindowEnd" = p."DeliveryWindowEnd"
            FROM "RestaurantProfiles" p WHERE r."RestaurantId" = p."Id"
              AND p."DeliveryWindowStart" IS NOT NULL AND p."DeliveryWindowEnd" IS NOT NULL;
            WITH candidates AS (
                SELECT r.* FROM "RestaurantServiceRegions" r
                WHERE r."IsActive" OR NOT EXISTS (
                    SELECT 1 FROM "RestaurantServiceRegions" a
                    WHERE a."RestaurantId" = r."RestaurantId" AND a."IsActive"
                )
            ), uniform_preparation AS (
                SELECT "RestaurantId", MIN("PreparationLeadTimeMinutes") AS minutes
                FROM candidates GROUP BY "RestaurantId"
                HAVING COUNT(*) = COUNT("PreparationLeadTimeMinutes")
                   AND COUNT(DISTINCT "PreparationLeadTimeMinutes") = 1
                   AND MIN("PreparationLeadTimeMinutes") >= 0
            )
            UPDATE "RestaurantProfiles" p SET "PreparationLeadTimeMinutes" = w.minutes
            FROM uniform_preparation w WHERE p."Id" = w."RestaurantId";
            UPDATE "RestaurantServiceRegions" r
            SET "PreparationLeadTimeMinutes" = p."PreparationLeadTimeMinutes"
            FROM "RestaurantProfiles" p WHERE r."RestaurantId" = p."Id"
              AND p."PreparationLeadTimeMinutes" IS NOT NULL;
            """);
    }
    protected override void Down(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.DropColumn(name: "DeliveryWindowStart", table: "RestaurantProfiles");
        migrationBuilder.DropColumn(name: "DeliveryWindowEnd", table: "RestaurantProfiles");
        migrationBuilder.DropColumn(name: "PreparationLeadTimeMinutes", table: "RestaurantProfiles");
    }
}
