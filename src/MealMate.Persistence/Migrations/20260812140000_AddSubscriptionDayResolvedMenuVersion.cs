using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace MealMate.Persistence.Migrations;

public partial class AddSubscriptionDayResolvedMenuVersion : Migration
{
    protected override void Up(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.Sql("""
            ALTER TABLE "SubscriptionDays" ADD COLUMN IF NOT EXISTS "ResolvedMenuPlanVersionId" uuid NULL;
            CREATE INDEX IF NOT EXISTS "IX_SubscriptionDays_ResolvedMenuPlanVersionId" ON "SubscriptionDays" ("ResolvedMenuPlanVersionId");
            """);
    }

    protected override void Down(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.Sql("""
            DROP INDEX IF EXISTS "IX_SubscriptionDays_ResolvedMenuPlanVersionId";
            ALTER TABLE "SubscriptionDays" DROP COLUMN IF EXISTS "ResolvedMenuPlanVersionId";
            """);
    }
}
