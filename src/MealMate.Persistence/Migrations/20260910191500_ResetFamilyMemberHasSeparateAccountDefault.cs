using MealMate.Persistence;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace MealMate.Persistence.Migrations;

[DbContext(typeof(AppDbContext))]
[Migration("20260910191500_ResetFamilyMemberHasSeparateAccountDefault")]
public partial class ResetFamilyMemberHasSeparateAccountDefault : Migration
{
    protected override void Up(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.Sql(@"UPDATE ""FamilyMemberAccesses"" SET ""HasSeparateAccount"" = false WHERE ""InviteTokenHash"" IS NULL;");
    }

    protected override void Down(MigrationBuilder migrationBuilder)
    {
    }
}
