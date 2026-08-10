using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace MealMate.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddMyFatoorahPaymentGatewayConfig : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<int>(
                name: "MaxMealsAllowedPerDay",
                table: "FamilySubscriptions",
                type: "integer",
                nullable: false,
                defaultValue: 0);

            migrationBuilder.AddColumn<string>(
                name: "Relationship",
                table: "FamilySubscriptions",
                type: "character varying(100)",
                maxLength: 100,
                nullable: true);

            migrationBuilder.CreateTable(
                name: "MyFatoorahGatewayConfigs",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    GatewayName = table.Column<string>(type: "text", nullable: false),
                    IsSandbox = table.Column<bool>(type: "boolean", nullable: false),
                    BaseUrl = table.Column<string>(type: "text", nullable: false),
                    EncryptedApiToken = table.Column<string>(type: "text", nullable: false),
                    EncryptedWebhookSecret = table.Column<string>(type: "text", nullable: false),
                    IsKnetEnabled = table.Column<bool>(type: "boolean", nullable: false),
                    IsVisaMasterEnabled = table.Column<bool>(type: "boolean", nullable: false),
                    IsApplePayEnabled = table.Column<bool>(type: "boolean", nullable: false),
                    DefaultCurrency = table.Column<string>(type: "text", nullable: false),
                    CountryCode = table.Column<string>(type: "text", nullable: false),
                    MaskedToken = table.Column<string>(type: "text", nullable: false),
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
                    table.PrimaryKey("PK_MyFatoorahGatewayConfigs", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "SubscriptionFreezeRequests",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    SubscriptionId = table.Column<Guid>(type: "uuid", nullable: false),
                    FreezeStartDate = table.Column<DateOnly>(type: "date", nullable: false),
                    FreezeEndDate = table.Column<DateOnly>(type: "date", nullable: false),
                    FreezeDays = table.Column<int>(type: "integer", nullable: false),
                    Reason = table.Column<string>(type: "character varying(500)", maxLength: 500, nullable: true),
                    Status = table.Column<int>(type: "integer", nullable: false),
                    ApprovedByUserId = table.Column<Guid>(type: "uuid", nullable: true),
                    ProcessedAtUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
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
                    table.PrimaryKey("PK_SubscriptionFreezeRequests", x => x.Id);
                    table.ForeignKey(
                        name: "FK_SubscriptionFreezeRequests_Subscriptions_SubscriptionId",
                        column: x => x.SubscriptionId,
                        principalTable: "Subscriptions",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateIndex(
                name: "IX_SubscriptionFreezeRequests_Status",
                table: "SubscriptionFreezeRequests",
                column: "Status");

            migrationBuilder.CreateIndex(
                name: "IX_SubscriptionFreezeRequests_SubscriptionId",
                table: "SubscriptionFreezeRequests",
                column: "SubscriptionId");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "MyFatoorahGatewayConfigs");

            migrationBuilder.DropTable(
                name: "SubscriptionFreezeRequests");

            migrationBuilder.DropColumn(
                name: "MaxMealsAllowedPerDay",
                table: "FamilySubscriptions");

            migrationBuilder.DropColumn(
                name: "Relationship",
                table: "FamilySubscriptions");
        }
    }
}
