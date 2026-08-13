using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace MealMate.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class EnsureSubscriptionDayResolvedMenuVersion : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql("""
                ALTER TABLE "SubscriptionDays"
                ADD COLUMN IF NOT EXISTS "ResolvedMenuPlanVersionId" uuid NULL;

                CREATE INDEX IF NOT EXISTS "IX_SubscriptionDays_ResolvedMenuPlanVersionId"
                ON "SubscriptionDays" ("ResolvedMenuPlanVersionId");
                """);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            // Intentionally non-destructive. This migration repairs databases where the
            // original non-discoverable migration never added the column.
        }
    }
}
