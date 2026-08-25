using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace MealMate.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddFamilyMemberAccessAndPermissions : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "FamilyMemberAccesses",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    FamilyMemberId = table.Column<Guid>(type: "uuid", nullable: false),
                    HasSeparateAccount = table.Column<bool>(type: "boolean", nullable: false),
                    LinkedCustomerUserId = table.Column<Guid>(type: "uuid", nullable: true),
                    InviteTokenHash = table.Column<string>(type: "character varying(128)", maxLength: 128, nullable: true),
                    InviteExpiresAtUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    IsPaused = table.Column<bool>(type: "boolean", nullable: false),
                    PausedAtUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    RemovedAtUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
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
                    table.PrimaryKey("PK_FamilyMemberAccesses", x => x.Id);
                    table.ForeignKey(
                        name: "FK_FamilyMemberAccesses_FamilyMembers_FamilyMemberId",
                        column: x => x.FamilyMemberId,
                        principalTable: "FamilyMembers",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "FamilyMemberPermissions",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    FamilyMemberId = table.Column<Guid>(type: "uuid", nullable: false),
                    EditDailyMeals = table.Column<bool>(type: "boolean", nullable: false),
                    ChangeDeliveryTime = table.Column<bool>(type: "boolean", nullable: false),
                    ViewProgram = table.Column<bool>(type: "boolean", nullable: false),
                    ChangeProgram = table.Column<bool>(type: "boolean", nullable: false),
                    EditMealsAndDays = table.Column<bool>(type: "boolean", nullable: false),
                    EditAddress = table.Column<bool>(type: "boolean", nullable: false),
                    UnitExchange = table.Column<bool>(type: "boolean", nullable: false),
                    ChangePassword = table.Column<bool>(type: "boolean", nullable: false),
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
                    table.PrimaryKey("PK_FamilyMemberPermissions", x => x.Id);
                    table.ForeignKey(
                        name: "FK_FamilyMemberPermissions_FamilyMembers_FamilyMemberId",
                        column: x => x.FamilyMemberId,
                        principalTable: "FamilyMembers",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "IX_FamilyMemberAccesses_FamilyMemberId",
                table: "FamilyMemberAccesses",
                column: "FamilyMemberId",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_FamilyMemberAccesses_InviteTokenHash",
                table: "FamilyMemberAccesses",
                column: "InviteTokenHash");

            migrationBuilder.CreateIndex(
                name: "IX_FamilyMemberAccesses_LinkedCustomerUserId",
                table: "FamilyMemberAccesses",
                column: "LinkedCustomerUserId");

            migrationBuilder.CreateIndex(
                name: "IX_FamilyMemberPermissions_FamilyMemberId",
                table: "FamilyMemberPermissions",
                column: "FamilyMemberId",
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "FamilyMemberAccesses");

            migrationBuilder.DropTable(
                name: "FamilyMemberPermissions");
        }
    }
}
