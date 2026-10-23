using System;
using Microsoft.EntityFrameworkCore.Migrations;
using Microsoft.EntityFrameworkCore.Infrastructure;

#nullable disable

namespace MealMate.Persistence.Migrations
{
    /// <inheritdoc />
    [DbContext(typeof(MealMate.Persistence.AppDbContext))]
    [Migration("20261023023106_AddUnifiedSupportCases")]
    public partial class AddUnifiedSupportCases : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "SupportCases",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    Channel = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: false),
                    Kind = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: false),
                    Status = table.Column<string>(type: "character varying(40)", maxLength: 40, nullable: false),
                    OwnerTeam = table.Column<string>(type: "character varying(48)", maxLength: 48, nullable: false),
                    Priority = table.Column<string>(type: "character varying(8)", maxLength: 8, nullable: true),
                    Category = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false),
                    Subject = table.Column<string>(type: "character varying(300)", maxLength: 300, nullable: false),
                    Body = table.Column<string>(type: "character varying(4000)", maxLength: 4000, nullable: false),
                    RequesterUserId = table.Column<Guid>(type: "uuid", nullable: false),
                    RestaurantId = table.Column<Guid>(type: "uuid", nullable: true),
                    AssignedToUserId = table.Column<Guid>(type: "uuid", nullable: true),
                    RelatedResourceType = table.Column<string>(type: "character varying(80)", maxLength: 80, nullable: true),
                    RelatedResourceId = table.Column<Guid>(type: "uuid", nullable: true),
                    xmin = table.Column<uint>(type: "xid", rowVersion: true, nullable: false),
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
                    table.PrimaryKey("PK_SupportCases", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "SupportCaseEvents",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    SupportCaseId = table.Column<Guid>(type: "uuid", nullable: false),
                    ActorUserId = table.Column<Guid>(type: "uuid", nullable: false),
                    EventType = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    FromValue = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: true),
                    ToValue = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: true),
                    IdempotencyKey = table.Column<string>(type: "character varying(128)", maxLength: 128, nullable: true),
                    PayloadHash = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: true),
                    CreatedAtUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_SupportCaseEvents", x => x.Id);
                    table.ForeignKey(
                        name: "FK_SupportCaseEvents_SupportCases_SupportCaseId",
                        column: x => x.SupportCaseId,
                        principalTable: "SupportCases",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "SupportCaseLinks",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    SupportCaseId = table.Column<Guid>(type: "uuid", nullable: false),
                    Relation = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: false),
                    ResourceType = table.Column<string>(type: "character varying(80)", maxLength: 80, nullable: false),
                    ResourceId = table.Column<Guid>(type: "uuid", nullable: false),
                    CreatedByUserId = table.Column<Guid>(type: "uuid", nullable: false),
                    CreatedAtUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_SupportCaseLinks", x => x.Id);
                    table.ForeignKey(
                        name: "FK_SupportCaseLinks_SupportCases_SupportCaseId",
                        column: x => x.SupportCaseId,
                        principalTable: "SupportCases",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "SupportCaseTasks",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    SupportCaseId = table.Column<Guid>(type: "uuid", nullable: false),
                    Type = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: false),
                    RecipientType = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: false),
                    RecipientUserId = table.Column<Guid>(type: "uuid", nullable: true),
                    RestaurantId = table.Column<Guid>(type: "uuid", nullable: true),
                    Status = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false),
                    Subject = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false),
                    Question = table.Column<string>(type: "character varying(2000)", maxLength: 2000, nullable: false),
                    Result = table.Column<string>(type: "character varying(4000)", maxLength: 4000, nullable: true),
                    CreatedByUserId = table.Column<Guid>(type: "uuid", nullable: false),
                    CreatedAtUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    DueAtUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    CompletedAtUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    CompletedByUserId = table.Column<Guid>(type: "uuid", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_SupportCaseTasks", x => x.Id);
                    table.UniqueConstraint("AK_SupportCaseTasks_SupportCaseId_Id", x => new { x.SupportCaseId, x.Id });
                    table.ForeignKey(
                        name: "FK_SupportCaseTasks_SupportCases_SupportCaseId",
                        column: x => x.SupportCaseId,
                        principalTable: "SupportCases",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "SupportCaseMessages",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    SupportCaseId = table.Column<Guid>(type: "uuid", nullable: false),
                    SupportCaseTaskId = table.Column<Guid>(type: "uuid", nullable: true),
                    AuthorUserId = table.Column<Guid>(type: "uuid", nullable: false),
                    Visibility = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false),
                    Body = table.Column<string>(type: "character varying(4000)", maxLength: 4000, nullable: false),
                    AttachmentKeys = table.Column<string[]>(type: "text[]", nullable: false),
                    CreatedAtUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_SupportCaseMessages", x => x.Id);
                    table.ForeignKey(
                        name: "FK_SupportCaseMessages_SupportCases_SupportCaseId",
                        column: x => x.SupportCaseId,
                        principalTable: "SupportCases",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "FK_SupportCaseMessages_SupportCaseTasks_SupportCaseId_SupportCaseTaskId",
                        columns: x => new { x.SupportCaseId, x.SupportCaseTaskId },
                        principalTable: "SupportCaseTasks",
                        principalColumns: new[] { "SupportCaseId", "Id" },
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateIndex(
                name: "IX_SupportCaseEvents_ActorUserId_IdempotencyKey",
                table: "SupportCaseEvents",
                columns: new[] { "ActorUserId", "IdempotencyKey" },
                unique: true,
                filter: "\"IdempotencyKey\" IS NOT NULL");

            migrationBuilder.CreateIndex(
                name: "IX_SupportCaseEvents_SupportCaseId_CreatedAtUtc",
                table: "SupportCaseEvents",
                columns: new[] { "SupportCaseId", "CreatedAtUtc" });

            migrationBuilder.CreateIndex(
                name: "IX_SupportCaseLinks_ResourceType_ResourceId",
                table: "SupportCaseLinks",
                columns: new[] { "ResourceType", "ResourceId" });

            migrationBuilder.CreateIndex(
                name: "IX_SupportCaseLinks_SupportCaseId_Relation_ResourceType_Resour~",
                table: "SupportCaseLinks",
                columns: new[] { "SupportCaseId", "Relation", "ResourceType", "ResourceId" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_SupportCaseMessages_SupportCaseId_CreatedAtUtc",
                table: "SupportCaseMessages",
                columns: new[] { "SupportCaseId", "CreatedAtUtc" });

            migrationBuilder.CreateIndex(
                name: "IX_SupportCaseMessages_SupportCaseTaskId",
                table: "SupportCaseMessages",
                column: "SupportCaseTaskId");

            migrationBuilder.CreateIndex(
                name: "IX_SupportCaseMessages_SupportCaseId_SupportCaseTaskId",
                table: "SupportCaseMessages",
                columns: new[] { "SupportCaseId", "SupportCaseTaskId" });

            migrationBuilder.CreateIndex(
                name: "IX_SupportCases_AssignedToUserId",
                table: "SupportCases",
                column: "AssignedToUserId");

            migrationBuilder.CreateIndex(
                name: "IX_SupportCases_OwnerTeam_Status_Priority_CreatedAtUtc",
                table: "SupportCases",
                columns: new[] { "OwnerTeam", "Status", "Priority", "CreatedAtUtc" });

            migrationBuilder.CreateIndex(
                name: "IX_SupportCases_RequesterUserId_CreatedAtUtc",
                table: "SupportCases",
                columns: new[] { "RequesterUserId", "CreatedAtUtc" });

            migrationBuilder.CreateIndex(
                name: "IX_SupportCases_RestaurantId_OwnerTeam_Status_CreatedAtUtc",
                table: "SupportCases",
                columns: new[] { "RestaurantId", "OwnerTeam", "Status", "CreatedAtUtc" });

            migrationBuilder.CreateIndex(
                name: "IX_SupportCaseTasks_RestaurantId_RecipientUserId_Status_Create~",
                table: "SupportCaseTasks",
                columns: new[] { "RestaurantId", "RecipientUserId", "Status", "CreatedAtUtc" });

            migrationBuilder.CreateIndex(
                name: "IX_SupportCaseTasks_SupportCaseId_Status",
                table: "SupportCaseTasks",
                columns: new[] { "SupportCaseId", "Status" });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "SupportCaseEvents");

            migrationBuilder.DropTable(
                name: "SupportCaseLinks");

            migrationBuilder.DropTable(
                name: "SupportCaseMessages");

            migrationBuilder.DropTable(
                name: "SupportCaseTasks");

            migrationBuilder.DropTable(
                name: "SupportCases");
        }
    }
}
