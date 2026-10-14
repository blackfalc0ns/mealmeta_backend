using Microsoft.EntityFrameworkCore.Migrations;
using Microsoft.EntityFrameworkCore.Infrastructure;
using MealMate.Persistence;

#nullable disable

namespace MealMate.Persistence.Migrations;

[DbContext(typeof(AppDbContext))]
[Migration("20261014001000_AddAdAuctionDisplayExclusion")]
public partial class AddAdAuctionDisplayExclusion : Migration
{
    protected override void Up(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.Sql("CREATE EXTENSION IF NOT EXISTS btree_gist;");
        migrationBuilder.Sql(@"
            ALTER TABLE ""AdAuctions""
            ADD CONSTRAINT ""CK_AdAuctions_DisplayWindowValid""
            CHECK (""DisplayStartsAtUtc"" < ""DisplayEndsAtUtc"");");
        migrationBuilder.Sql(@"
            ALTER TABLE ""AdAuctions""
            ADD CONSTRAINT ""EX_AdAuctions_SpaceDisplayWindow""
            EXCLUDE USING gist
            (""AdSpaceId"" WITH =,
             tstzrange(""DisplayStartsAtUtc"", ""DisplayEndsAtUtc"", '[)') WITH &&)
            WHERE (""Status"" NOT IN (5, 6));");
    }

    protected override void Down(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.Sql("ALTER TABLE \"AdAuctions\" DROP CONSTRAINT IF EXISTS \"EX_AdAuctions_SpaceDisplayWindow\";");
        migrationBuilder.Sql("ALTER TABLE \"AdAuctions\" DROP CONSTRAINT IF EXISTS \"CK_AdAuctions_DisplayWindowValid\";");
        // btree_gist may be shared by other tables; leave the extension installed.
    }
}
