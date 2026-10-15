using System;
using MealMate.Persistence;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace MealMate.Persistence.Migrations;

[DbContext(typeof(AppDbContext))]
[Migration("20261015120000_AddFamilyCredentialOperations")]
public sealed class AddFamilyCredentialOperations : Migration
{
    protected override void Up(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.CreateTable(
            name: "FamilyCredentialOperations",
            columns: table => new
            {
                Id = table.Column<Guid>(type: "uuid", nullable: false),
                FamilyMemberId = table.Column<Guid>(type: "uuid", nullable: false),
                ActorUserId = table.Column<Guid>(type: "uuid", nullable: false),
                OperationKeyHash = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                CompletedAtUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                CreatedAtUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                ModifiedAtUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                CreatedByUserId = table.Column<Guid>(type: "uuid", nullable: true),
                ModifiedByUserId = table.Column<Guid>(type: "uuid", nullable: true),
                IsDeleted = table.Column<bool>(type: "boolean", nullable: false),
                DeletedAtUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                DeletedByUserId = table.Column<Guid>(type: "uuid", nullable: true)
            },
            constraints: table => table.PrimaryKey("PK_FamilyCredentialOperations", x => x.Id));

        migrationBuilder.CreateIndex(
            name: "IX_FamilyCredentialOperations_ActorUserId",
            table: "FamilyCredentialOperations",
            column: "ActorUserId");
        migrationBuilder.CreateIndex(
            name: "IX_FamilyCredentialOperations_FamilyMemberId_OperationKeyHash",
            table: "FamilyCredentialOperations",
            columns: new[] { "FamilyMemberId", "OperationKeyHash" },
            unique: true);
    }

    protected override void Down(MigrationBuilder migrationBuilder)
        => migrationBuilder.DropTable(name: "FamilyCredentialOperations");
}
