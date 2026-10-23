using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace MealMate.Persistence.Migrations;

[DbContext(typeof(MealMate.Persistence.AppDbContext))]
[Migration("20261023230000_ReconcileSupportMessageAttachments")]
public sealed class ReconcileSupportMessageAttachments : Migration
{
    protected override void Up(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.Sql("ALTER TABLE \"SupportCaseMessages\" ADD COLUMN IF NOT EXISTS \"AttachmentKeys\" text[] NOT NULL DEFAULT ARRAY[]::text[];");
    }

    protected override void Down(MigrationBuilder migrationBuilder)
    {
        // The column may predate this compatibility repair; never remove it on rollback.
    }
}
