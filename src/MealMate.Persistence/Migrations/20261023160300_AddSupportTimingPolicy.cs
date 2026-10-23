using System;
using Microsoft.EntityFrameworkCore.Migrations;
using Microsoft.EntityFrameworkCore.Infrastructure;

#nullable disable

namespace MealMate.Persistence.Migrations
{
    /// <inheritdoc />
    [DbContext(typeof(MealMate.Persistence.AppDbContext))]
    [Migration("20261023160300_AddSupportTimingPolicy")]
    public partial class AddSupportTimingPolicy : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<Guid>(
                name: "ActiveTimingCycleId",
                table: "SupportCases",
                type: "uuid",
                nullable: true);

            migrationBuilder.CreateIndex(
                name: "IX_SupportCases_ActiveTimingCycleId",
                table: "SupportCases",
                column: "ActiveTimingCycleId");

            migrationBuilder.CreateTable(
                name: "SupportTimingPolicies",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    Version = table.Column<int>(type: "integer", nullable: false),
                    Name = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false),
                    Description = table.Column<string>(type: "character varying(1000)", maxLength: 1000, nullable: true),
                    IsActive = table.Column<bool>(type: "boolean", nullable: false),
                    PublishedAtUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    PublishedByUserId = table.Column<Guid>(type: "uuid", nullable: false),
                    PublicationReason = table.Column<string>(type: "character varying(1000)", maxLength: 1000, nullable: false),
                    IdempotencyKey = table.Column<string>(type: "character varying(128)", maxLength: 128, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_SupportTimingPolicies", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "SupportTimingRules",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    PolicyId = table.Column<Guid>(type: "uuid", nullable: false),
                    Channel = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: true),
                    Priority = table.Column<string>(type: "character varying(8)", maxLength: 8, nullable: true),
                    Category = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: true),
                    FirstResponseDeadline = table.Column<TimeSpan>(type: "interval", nullable: false),
                    ResolutionDeadline = table.Column<TimeSpan>(type: "interval", nullable: false),
                    EscalationThreshold = table.Column<TimeSpan>(type: "interval", nullable: false),
                    RequiresImmediateAck = table.Column<bool>(type: "boolean", nullable: false),
                    EvaluationOrder = table.Column<int>(type: "integer", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_SupportTimingRules", x => x.Id);
                    table.ForeignKey(
                        name: "FK_SupportTimingRules_SupportTimingPolicies_PolicyId",
                        column: x => x.PolicyId,
                        principalTable: "SupportTimingPolicies",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "SupportCaseTimingCycles",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    SupportCaseId = table.Column<Guid>(type: "uuid", nullable: false),
                    CycleNumber = table.Column<int>(type: "integer", nullable: false),
                    PolicyId = table.Column<Guid>(type: "uuid", nullable: true),
                    PolicyVersion = table.Column<int>(type: "integer", nullable: true),
                    MatchedRuleId = table.Column<Guid>(type: "uuid", nullable: true),
                    StartedAtUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    FirstResponseDeadlineUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    ResolutionDeadlineUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    FirstResponseAtUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    ResolvedAtUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    ClosedAtUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    IsFirstResponseBreached = table.Column<bool>(type: "boolean", nullable: false),
                    IsResolutionBreached = table.Column<bool>(type: "boolean", nullable: false),
                    IsEscalated = table.Column<bool>(type: "boolean", nullable: false),
                    EscalatedAtUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    EscalationReason = table.Column<string>(type: "character varying(500)", maxLength: 500, nullable: true),
                    IsPolicyUnconfigured = table.Column<bool>(type: "boolean", nullable: false),
                    IsUnrouted = table.Column<bool>(type: "boolean", nullable: false),
                    Notes = table.Column<string>(type: "character varying(1000)", maxLength: 1000, nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_SupportCaseTimingCycles", x => x.Id);
                    table.ForeignKey(
                        name: "FK_SupportCaseTimingCycles_SupportCases_SupportCaseId",
                        column: x => x.SupportCaseId,
                        principalTable: "SupportCases",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "IX_SupportTimingPolicies_IdempotencyKey",
                table: "SupportTimingPolicies",
                column: "IdempotencyKey",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_SupportTimingPolicies_IsActive",
                table: "SupportTimingPolicies",
                column: "IsActive");

            migrationBuilder.CreateIndex(
                name: "IX_SupportTimingPolicies_Version",
                table: "SupportTimingPolicies",
                column: "Version",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_SupportTimingRules_PolicyId_EvaluationOrder",
                table: "SupportTimingRules",
                columns: new[] { "PolicyId", "EvaluationOrder" });

            migrationBuilder.CreateIndex(
                name: "IX_SupportCaseTimingCycles_SupportCaseId_CycleNumber",
                table: "SupportCaseTimingCycles",
                columns: new[] { "SupportCaseId", "CycleNumber" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_SupportCaseTimingCycles_IsFirstResponseBreached_FirstResponseAtUtc",
                table: "SupportCaseTimingCycles",
                columns: new[] { "IsFirstResponseBreached", "FirstResponseAtUtc" });

            migrationBuilder.CreateIndex(
                name: "IX_SupportCaseTimingCycles_IsResolutionBreached_ResolvedAtUtc",
                table: "SupportCaseTimingCycles",
                columns: new[] { "IsResolutionBreached", "ResolvedAtUtc" });

            migrationBuilder.CreateIndex(
                name: "IX_SupportCaseTimingCycles_IsEscalated_EscalatedAtUtc",
                table: "SupportCaseTimingCycles",
                columns: new[] { "IsEscalated", "EscalatedAtUtc" });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(name: "SupportCaseTimingCycles");
            migrationBuilder.DropTable(name: "SupportTimingRules");
            migrationBuilder.DropTable(name: "SupportTimingPolicies");
            migrationBuilder.DropIndex(name: "IX_SupportCases_ActiveTimingCycleId", table: "SupportCases");
            migrationBuilder.DropColumn(name: "ActiveTimingCycleId", table: "SupportCases");
        }
    }
}
