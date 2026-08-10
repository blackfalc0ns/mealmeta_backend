using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace MealMate.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddRestaurantOperatingScheduleAndExceptions : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "RestaurantOperatingDayExceptions",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    RestaurantId = table.Column<Guid>(type: "uuid", nullable: false),
                    Date = table.Column<DateOnly>(type: "date", nullable: false),
                    IsWorkingDay = table.Column<bool>(type: "boolean", nullable: false),
                    Reason = table.Column<string>(type: "character varying(1000)", maxLength: 1000, nullable: true),
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
                    table.PrimaryKey("PK_RestaurantOperatingDayExceptions", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "RestaurantOperatingSchedules",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    RestaurantId = table.Column<Guid>(type: "uuid", nullable: false),
                    DayOfWeek = table.Column<int>(type: "integer", nullable: false),
                    IsWorkingDay = table.Column<bool>(type: "boolean", nullable: false),
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
                    table.PrimaryKey("PK_RestaurantOperatingSchedules", x => x.Id);
                });

            migrationBuilder.CreateIndex(
                name: "IX_RestaurantOperatingDayExceptions_RestaurantId",
                table: "RestaurantOperatingDayExceptions",
                column: "RestaurantId");

            migrationBuilder.CreateIndex(
                name: "IX_RestaurantOperatingDayExceptions_RestaurantId_Date",
                table: "RestaurantOperatingDayExceptions",
                columns: new[] { "RestaurantId", "Date" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_RestaurantOperatingSchedules_RestaurantId",
                table: "RestaurantOperatingSchedules",
                column: "RestaurantId");

            migrationBuilder.CreateIndex(
                name: "IX_RestaurantOperatingSchedules_RestaurantId_DayOfWeek",
                table: "RestaurantOperatingSchedules",
                columns: new[] { "RestaurantId", "DayOfWeek" },
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "RestaurantOperatingDayExceptions");

            migrationBuilder.DropTable(
                name: "RestaurantOperatingSchedules");
        }
    }
}
