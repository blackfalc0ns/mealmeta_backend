using MealMate.Persistence;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace MealMate.Persistence.Migrations;

[DbContext(typeof(AppDbContext))]
[Migration("20260826143000_FixDailyMealSelectionUniqueSoftDelete")]
public partial class FixDailyMealSelectionUniqueSoftDelete : Migration
{
    protected override void Up(MigrationBuilder migrationBuilder)
    {
        // Soft-deleted rows blocked re-selecting the same meal (unique on day+meal).
        migrationBuilder.Sql("""
            DELETE FROM "DailyMealSelections" WHERE "IsDeleted" = TRUE;
            """);

        migrationBuilder.Sql("""
            DROP INDEX IF EXISTS "IX_DailyMealSelections_SubscriptionDayId_MealId";
            """);

        migrationBuilder.Sql("""
            CREATE UNIQUE INDEX "IX_DailyMealSelections_SubscriptionDayId_MealId"
            ON "DailyMealSelections" ("SubscriptionDayId", "MealId")
            WHERE "IsDeleted" = FALSE;
            """);
    }

    protected override void Down(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.Sql("""
            DROP INDEX IF EXISTS "IX_DailyMealSelections_SubscriptionDayId_MealId";
            """);

        migrationBuilder.Sql("""
            CREATE UNIQUE INDEX "IX_DailyMealSelections_SubscriptionDayId_MealId"
            ON "DailyMealSelections" ("SubscriptionDayId", "MealId");
            """);
    }
}
