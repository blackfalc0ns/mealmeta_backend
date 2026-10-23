using Microsoft.EntityFrameworkCore.Migrations;
using Microsoft.EntityFrameworkCore.Infrastructure;

#nullable disable

namespace MealMate.Persistence.Migrations;

[DbContext(typeof(MealMate.Persistence.AppDbContext))]
[Migration("20261023143911_AddSupportEvidenceScanReceipts")]
public partial class AddSupportEvidenceScanReceipts : Migration
{
    protected override void Up(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.CreateTable(
            name: "SupportEvidenceScanReceipts",
            columns: table => new
            {
                Id = table.Column<Guid>(type: "uuid", nullable: false),
                UploadFileRecordId = table.Column<Guid>(type: "uuid", nullable: false),
                Sha256 = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                SizeBytes = table.Column<long>(type: "bigint", nullable: false),
                ContentType = table.Column<string>(type: "character varying(128)", maxLength: 128, nullable: false),
                Verdict = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: false),
                Provider = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                FailureCode = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                ScannedAtUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                ValidationVersion = table.Column<int>(type: "integer", nullable: false)
            },
            constraints: table =>
            {
                table.PrimaryKey("PK_SupportEvidenceScanReceipts", x => x.Id);
                table.ForeignKey(
                    name: "FK_SupportEvidenceScanReceipts_UploadFileRecords_UploadFileRecordId",
                    column: x => x.UploadFileRecordId,
                    principalTable: "UploadFileRecords",
                    principalColumn: "Id",
                    onDelete: ReferentialAction.Restrict);
            });

        migrationBuilder.CreateIndex(
            name: "IX_SupportEvidenceScanReceipts_Record_Digest",
            table: "SupportEvidenceScanReceipts",
            columns: new[] { "UploadFileRecordId", "Sha256", "SizeBytes", "ContentType", "ValidationVersion", "Verdict" });
    }

    protected override void Down(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.DropTable(name: "SupportEvidenceScanReceipts");
    }
}
