using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace MealMate.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddDriverWorkSessionAndNotifications : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "DriverDeviceTokens",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    UserId = table.Column<Guid>(type: "uuid", nullable: false),
                    DriverProfileId = table.Column<Guid>(type: "uuid", nullable: true),
                    Token = table.Column<string>(type: "text", nullable: false),
                    Platform = table.Column<int>(type: "integer", nullable: false),
                    DeviceId = table.Column<string>(type: "text", nullable: true),
                    IsActive = table.Column<bool>(type: "boolean", nullable: false),
                    LastSeenAtUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
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
                    table.PrimaryKey("PK_DriverDeviceTokens", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "DriverNotifications",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    UserId = table.Column<Guid>(type: "uuid", nullable: false),
                    DriverProfileId = table.Column<Guid>(type: "uuid", nullable: true),
                    Type = table.Column<string>(type: "text", nullable: false),
                    TitleAr = table.Column<string>(type: "text", nullable: false),
                    TitleEn = table.Column<string>(type: "text", nullable: false),
                    BodyAr = table.Column<string>(type: "text", nullable: false),
                    BodyEn = table.Column<string>(type: "text", nullable: false),
                    Section = table.Column<string>(type: "text", nullable: true),
                    SubjectId = table.Column<Guid>(type: "uuid", nullable: true),
                    DeepLink = table.Column<string>(type: "text", nullable: true),
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
                    table.PrimaryKey("PK_DriverNotifications", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "DriverWorkSessions",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    DriverProfileId = table.Column<Guid>(type: "uuid", nullable: false),
                    RestaurantId = table.Column<Guid>(type: "uuid", nullable: true),
                    StartedAtUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    EndedAtUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    StartLatitude = table.Column<double>(type: "double precision", nullable: true),
                    StartLongitude = table.Column<double>(type: "double precision", nullable: true),
                    EndLatitude = table.Column<double>(type: "double precision", nullable: true),
                    EndLongitude = table.Column<double>(type: "double precision", nullable: true),
                    TotalDistanceKm = table.Column<double>(type: "double precision", nullable: false),
                    TotalAssignedOrders = table.Column<int>(type: "integer", nullable: false),
                    CompletedOrdersCount = table.Column<int>(type: "integer", nullable: false),
                    InDeliveryOrdersCount = table.Column<int>(type: "integer", nullable: false),
                    FailedOrdersCount = table.Column<int>(type: "integer", nullable: false),
                    DailyTargetOrders = table.Column<int>(type: "integer", nullable: false),
                    AverageDeliveryMinutes = table.Column<double>(type: "double precision", nullable: true),
                    OnTimeRatePercentage = table.Column<double>(type: "double precision", nullable: true),
                    Status = table.Column<string>(type: "text", nullable: false),
                    ShiftLabel = table.Column<string>(type: "text", nullable: true),
                    Notes = table.Column<string>(type: "text", nullable: true),
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
                    table.PrimaryKey("PK_DriverWorkSessions", x => x.Id);
                });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "DriverDeviceTokens");

            migrationBuilder.DropTable(
                name: "DriverNotifications");

            migrationBuilder.DropTable(
                name: "DriverWorkSessions");
        }
    }
}
