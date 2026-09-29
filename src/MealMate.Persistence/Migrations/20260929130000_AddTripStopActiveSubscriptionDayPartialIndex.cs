using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace MealMate.Persistence.Migrations
{
    /// <inheritdoc />
    /// <remarks>
    /// This migration is intentionally idempotent.
    /// IX_TripStops_SubscriptionDayId was already dropped and
    /// IX_TripStops_Active_SubscriptionDayId was already created by migration
    /// 20260929043658_FixOrderLifecycleSentinelsAndFilters.
    /// Raw SQL with IF EXISTS / IF NOT EXISTS guards are used here to be safe
    /// on any database state.
    /// </remarks>
    public partial class AddTripStopActiveSubscriptionDayPartialIndex : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            // Idempotent: remove the old non-partial index if it still exists.
            migrationBuilder.Sql(@"DROP INDEX IF EXISTS ""IX_TripStops_SubscriptionDayId"";");

            // Idempotent: create the partial unique index only if not yet present.
            migrationBuilder.Sql(@"
CREATE UNIQUE INDEX IF NOT EXISTS ""IX_TripStops_Active_SubscriptionDayId""
    ON ""TripStops"" (""SubscriptionDayId"")
    WHERE ""SubscriptionDayId"" IS NOT NULL AND ""Status"" IN (1, 2);
");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql(@"DROP INDEX IF EXISTS ""IX_TripStops_Active_SubscriptionDayId"";");

            migrationBuilder.Sql(@"
CREATE INDEX IF NOT EXISTS ""IX_TripStops_SubscriptionDayId""
    ON ""TripStops"" (""SubscriptionDayId"");
");
        }
    }
}
