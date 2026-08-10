using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace MealMate.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddRestaurantReviewCenter : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "RestaurantNotifications",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    UserId = table.Column<Guid>(type: "uuid", nullable: false),
                    Type = table.Column<string>(type: "character varying(80)", maxLength: 80, nullable: false),
                    TitleAr = table.Column<string>(type: "character varying(300)", maxLength: 300, nullable: false),
                    TitleEn = table.Column<string>(type: "character varying(300)", maxLength: 300, nullable: false),
                    BodyAr = table.Column<string>(type: "character varying(4000)", maxLength: 4000, nullable: false),
                    BodyEn = table.Column<string>(type: "character varying(4000)", maxLength: 4000, nullable: false),
                    Section = table.Column<string>(type: "character varying(80)", maxLength: 80, nullable: true),
                    SubjectId = table.Column<Guid>(type: "uuid", nullable: true),
                    IsRead = table.Column<bool>(type: "boolean", nullable: false),
                    SentAtUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
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
                    table.PrimaryKey("PK_RestaurantNotifications", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "RestaurantReviewHistories",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    ReviewItemId = table.Column<Guid>(type: "uuid", nullable: false),
                    FromStatus = table.Column<int>(type: "integer", nullable: false),
                    ToStatus = table.Column<int>(type: "integer", nullable: false),
                    Reason = table.Column<string>(type: "character varying(4000)", maxLength: 4000, nullable: true),
                    ActorUserId = table.Column<Guid>(type: "uuid", nullable: true),
                    CreatedAtUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_RestaurantReviewHistories", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "RestaurantReviewItems",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    RestaurantId = table.Column<Guid>(type: "uuid", nullable: false),
                    RegistrationId = table.Column<Guid>(type: "uuid", nullable: true),
                    Section = table.Column<string>(type: "character varying(80)", maxLength: 80, nullable: false),
                    SubjectType = table.Column<string>(type: "character varying(80)", maxLength: 80, nullable: false),
                    SubjectId = table.Column<Guid>(type: "uuid", nullable: true),
                    FieldKey = table.Column<string>(type: "character varying(120)", maxLength: 120, nullable: true),
                    VersionId = table.Column<string>(type: "character varying(120)", maxLength: 120, nullable: true),
                    Status = table.Column<int>(type: "integer", nullable: false),
                    Reason = table.Column<string>(type: "character varying(4000)", maxLength: 4000, nullable: true),
                    ReviewedByUserId = table.Column<Guid>(type: "uuid", nullable: true),
                    ReviewedAtUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    SubmittedAtUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
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
                    table.PrimaryKey("PK_RestaurantReviewItems", x => x.Id);
                });

            migrationBuilder.CreateIndex(
                name: "IX_RestaurantNotifications_UserId_IsRead_SentAtUtc",
                table: "RestaurantNotifications",
                columns: new[] { "UserId", "IsRead", "SentAtUtc" });

            migrationBuilder.CreateIndex(
                name: "IX_RestaurantReviewHistories_ReviewItemId_CreatedAtUtc",
                table: "RestaurantReviewHistories",
                columns: new[] { "ReviewItemId", "CreatedAtUtc" });

            migrationBuilder.CreateIndex(
                name: "IX_RestaurantReviewItems_RegistrationId_SubjectType_SubjectId_~",
                table: "RestaurantReviewItems",
                columns: new[] { "RegistrationId", "SubjectType", "SubjectId", "FieldKey" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_RestaurantReviewItems_RestaurantId_Section_Status",
                table: "RestaurantReviewItems",
                columns: new[] { "RestaurantId", "Section", "Status" });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "RestaurantNotifications");

            migrationBuilder.DropTable(
                name: "RestaurantReviewHistories");

            migrationBuilder.DropTable(
                name: "RestaurantReviewItems");
        }
    }
}
