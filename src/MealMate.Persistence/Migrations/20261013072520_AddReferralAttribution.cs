using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace MealMate.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddReferralAttribution : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "ReferralAttributions",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    Code = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    ReferrerUserId = table.Column<Guid>(type: "uuid", nullable: false),
                    ReferredCustomerUserId = table.Column<Guid>(type: "uuid", nullable: false),
                    AttributedAtUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    ReferralCodeId = table.Column<Guid>(type: "uuid", nullable: true),
                    CreatedAtUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    CreatedByUserId = table.Column<Guid>(type: "uuid", nullable: true),
                    ModifiedAtUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    ModifiedByUserId = table.Column<Guid>(type: "uuid", nullable: true),
                    IsDeleted = table.Column<bool>(type: "boolean", nullable: false),
                    DeletedAtUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    DeletedByUserId = table.Column<Guid>(type: "uuid", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_ReferralAttributions", x => x.Id);
                });

            migrationBuilder.CreateIndex(
                name: "IX_ReferralAttributions_ReferredCustomerUserId",
                table: "ReferralAttributions",
                column: "ReferredCustomerUserId",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_ReferralAttributions_ReferrerUserId",
                table: "ReferralAttributions",
                column: "ReferrerUserId");

            migrationBuilder.CreateIndex(
                name: "IX_ReferralAttributions_ReferrerUserId_ReferredCustomerUserId",
                table: "ReferralAttributions",
                columns: new[] { "ReferrerUserId", "ReferredCustomerUserId" },
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "ReferralAttributions");
        }
    }
}
