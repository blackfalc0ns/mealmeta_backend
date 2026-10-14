using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace MealMate.Persistence.Migrations;

/// <summary>Backfills mandatory first-login setup for existing separate member accounts.</summary>
public partial class BackfillFamilyMemberPasswordSetup : Migration
{
    protected override void Up(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.Sql("""
UPDATE "FamilyMemberAccesses" AS access
SET "RequiresPasswordSetup" = TRUE
FROM "FamilyMembers" AS member
JOIN "FamilyGroups" AS family ON family."Id" = member."FamilyGroupId"
WHERE access."FamilyMemberId" = member."Id"
  AND member."Role" = 2
  AND access."HasSeparateAccount" = TRUE
  AND access."LinkedCustomerUserId" IS NOT NULL
  AND access."RemovedAtUtc" IS NULL
  AND access."IsDeleted" = FALSE
  AND member."IsDeleted" = FALSE
  AND family."IsDeleted" = FALSE
  AND family."Status" <> 7;
""");
    }

    protected override void Down(MigrationBuilder migrationBuilder)
    {
        // Keep security state on rollback; clearing it would reopen temporary credentials.
    }
}
