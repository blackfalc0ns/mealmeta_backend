using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace MealMate.Persistence.Migrations
{
    /// <inheritdoc />
    /// <remarks>
    /// This migration is intentionally idempotent.
    /// The columns LeaseToken, LockExpiresAtUtc, LockedByWorkerId and the index
    /// IX_OutboxMessages_Lease_Pending were already created by migration
    /// 20260929043658_FixOrderLifecycleSentinelsAndFilters.
    /// Using IF NOT EXISTS guards here so the migration is safe to run on any
    /// database state without raising 42701 (column already exists).
    /// </remarks>
    public partial class AddOutboxAtomicLeaseColumns : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            // Idempotent: add columns only when they don't exist yet.
            migrationBuilder.Sql(@"
ALTER TABLE ""OutboxMessages""
    ADD COLUMN IF NOT EXISTS ""LockedByWorkerId"" character varying(128) NULL;

ALTER TABLE ""OutboxMessages""
    ADD COLUMN IF NOT EXISTS ""LockExpiresAtUtc"" timestamp with time zone NULL;

ALTER TABLE ""OutboxMessages""
    ADD COLUMN IF NOT EXISTS ""LeaseToken"" uuid NULL;
");

            // Idempotent: create index only when it doesn't exist yet.
            migrationBuilder.Sql(@"
CREATE INDEX IF NOT EXISTS ""IX_OutboxMessages_Lease_Pending""
    ON ""OutboxMessages"" (""ProcessedAtUtc"", ""PoisonedAtUtc"", ""LockExpiresAtUtc"", ""NextAttemptAtUtc"");
");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            // Only drop the index; the columns are owned by the earlier migration
            // (20260929043658_FixOrderLifecycleSentinelsAndFilters) and will be
            // dropped by its Down() method.
            migrationBuilder.Sql(@"DROP INDEX IF EXISTS ""IX_OutboxMessages_Lease_Pending"";");
        }
    }
}
