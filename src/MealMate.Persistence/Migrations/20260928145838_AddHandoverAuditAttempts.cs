using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace MealMate.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddHandoverAuditAttempts : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "IX_OrderHandovers_DailyOrderId_AttemptNumber",
                table: "OrderHandovers");

            migrationBuilder.CreateTable(
                name: "OrderHandoverRevocations",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    HandoverId = table.Column<Guid>(type: "uuid", nullable: false),
                    RevokedAtUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    RevokedByUserId = table.Column<Guid>(type: "uuid", nullable: false),
                    Reason = table.Column<string>(type: "character varying(512)", maxLength: 512, nullable: false),
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
                    table.PrimaryKey("PK_OrderHandoverRevocations", x => x.Id);
                    table.ForeignKey(
                        name: "FK_OrderHandoverRevocations_OrderHandovers_HandoverId",
                        column: x => x.HandoverId,
                        principalTable: "OrderHandovers",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateIndex(
                name: "IX_OrderHandovers_DailyOrderId_AttemptNumber",
                table: "OrderHandovers",
                columns: new[] { "DailyOrderId", "AttemptNumber" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_OrderHandoverRevocations_HandoverId",
                table: "OrderHandoverRevocations",
                column: "HandoverId");

            migrationBuilder.CreateIndex(
                name: "IX_OrderHandoverRevocations_RevokedAtUtc",
                table: "OrderHandoverRevocations",
                column: "RevokedAtUtc");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "OrderHandoverRevocations");

            migrationBuilder.DropIndex(
                name: "IX_OrderHandovers_DailyOrderId_AttemptNumber",
                table: "OrderHandovers");

            migrationBuilder.CreateIndex(
                name: "IX_OrderHandovers_DailyOrderId_AttemptNumber",
                table: "OrderHandovers",
                columns: new[] { "DailyOrderId", "AttemptNumber" });
        }
    }
}
