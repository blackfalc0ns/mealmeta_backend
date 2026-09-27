using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace MealMate.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddPickupScanManualEntryFields : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql(@"
                ALTER TABLE ""PickupScans"" ADD COLUMN IF NOT EXISTS ""IsManualEntry"" boolean NOT NULL DEFAULT FALSE;
                ALTER TABLE ""PickupScans"" ADD COLUMN IF NOT EXISTS ""ManualEntryReason"" character varying(500) NULL;
            ");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql(@"
                ALTER TABLE ""PickupScans"" DROP COLUMN IF EXISTS ""IsManualEntry"";
                ALTER TABLE ""PickupScans"" DROP COLUMN IF EXISTS ""ManualEntryReason"";
            ");
        }
    }
}
