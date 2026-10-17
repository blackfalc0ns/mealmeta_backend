using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace MealMate.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddMealBoxLabelTemplateSystem : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "MealBoxLabelTemplates",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    Key = table.Column<string>(type: "character varying(50)", maxLength: 50, nullable: false),
                    NameAr = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false),
                    NameEn = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false),
                    DescriptionAr = table.Column<string>(type: "character varying(500)", maxLength: 500, nullable: true),
                    DescriptionEn = table.Column<string>(type: "character varying(500)", maxLength: 500, nullable: true),
                    DefaultWidthMm = table.Column<int>(type: "integer", nullable: false),
                    DefaultHeightMm = table.Column<int>(type: "integer", nullable: false),
                    DefaultOrientation = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false, defaultValue: "portrait"),
                    IsEnabled = table.Column<bool>(type: "boolean", nullable: false),
                    IsPublished = table.Column<bool>(type: "boolean", nullable: false),
                    IsRestaurantVisible = table.Column<bool>(type: "boolean", nullable: false),
                    IsGlobalDefault = table.Column<bool>(type: "boolean", nullable: false),
                    Version = table.Column<int>(type: "integer", nullable: false),
                    ConfigurationJson = table.Column<string>(type: "text", nullable: false, defaultValue: "{}"),
                    PublishedAtUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
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
                    table.PrimaryKey("PK_MealBoxLabelTemplates", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "MealLabelPrintLogs",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    RestaurantId = table.Column<Guid>(type: "uuid", nullable: false),
                    OrderId = table.Column<Guid>(type: "uuid", nullable: true),
                    MealId = table.Column<Guid>(type: "uuid", nullable: true),
                    TemplateId = table.Column<Guid>(type: "uuid", nullable: false),
                    TemplateVersion = table.Column<int>(type: "integer", nullable: false),
                    PrintedByUserId = table.Column<Guid>(type: "uuid", nullable: false),
                    PrintedAtUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    ClientIp = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: true),
                    PrintStatus = table.Column<string>(type: "character varying(50)", maxLength: 50, nullable: false),
                    Notes = table.Column<string>(type: "character varying(1000)", maxLength: 1000, nullable: true),
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
                    table.PrimaryKey("PK_MealLabelPrintLogs", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "RestaurantMealLabelSettings",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    RestaurantId = table.Column<Guid>(type: "uuid", nullable: false),
                    SelectedTemplateId = table.Column<Guid>(type: "uuid", nullable: true),
                    AdminAssignedTemplateId = table.Column<Guid>(type: "uuid", nullable: true),
                    AllowRestaurantSelection = table.Column<bool>(type: "boolean", nullable: false),
                    ConfiguredWidthMm = table.Column<int>(type: "integer", nullable: false),
                    ConfiguredHeightMm = table.Column<int>(type: "integer", nullable: false),
                    Orientation = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false, defaultValue: "portrait"),
                    ShowPackagingDate = table.Column<bool>(type: "boolean", nullable: false),
                    ShowOrderReference = table.Column<bool>(type: "boolean", nullable: false),
                    ShowQuantity = table.Column<bool>(type: "boolean", nullable: false),
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
                    table.PrimaryKey("PK_RestaurantMealLabelSettings", x => x.Id);
                    table.ForeignKey(
                        name: "FK_RestaurantMealLabelSettings_MealBoxLabelTemplates_AdminAssi~",
                        column: x => x.AdminAssignedTemplateId,
                        principalTable: "MealBoxLabelTemplates",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.SetNull);
                    table.ForeignKey(
                        name: "FK_RestaurantMealLabelSettings_MealBoxLabelTemplates_SelectedT~",
                        column: x => x.SelectedTemplateId,
                        principalTable: "MealBoxLabelTemplates",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.SetNull);
                });

            migrationBuilder.CreateIndex(
                name: "IX_MealBoxLabelTemplates_IsEnabled",
                table: "MealBoxLabelTemplates",
                column: "IsEnabled");

            migrationBuilder.CreateIndex(
                name: "IX_MealBoxLabelTemplates_IsGlobalDefault",
                table: "MealBoxLabelTemplates",
                column: "IsGlobalDefault");

            migrationBuilder.CreateIndex(
                name: "IX_MealBoxLabelTemplates_IsPublished",
                table: "MealBoxLabelTemplates",
                column: "IsPublished");

            migrationBuilder.CreateIndex(
                name: "IX_MealBoxLabelTemplates_IsRestaurantVisible",
                table: "MealBoxLabelTemplates",
                column: "IsRestaurantVisible");

            migrationBuilder.CreateIndex(
                name: "IX_MealBoxLabelTemplates_Key",
                table: "MealBoxLabelTemplates",
                column: "Key",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_MealLabelPrintLogs_MealId",
                table: "MealLabelPrintLogs",
                column: "MealId");

            migrationBuilder.CreateIndex(
                name: "IX_MealLabelPrintLogs_OrderId",
                table: "MealLabelPrintLogs",
                column: "OrderId");

            migrationBuilder.CreateIndex(
                name: "IX_MealLabelPrintLogs_PrintedAtUtc",
                table: "MealLabelPrintLogs",
                column: "PrintedAtUtc");

            migrationBuilder.CreateIndex(
                name: "IX_MealLabelPrintLogs_RestaurantId",
                table: "MealLabelPrintLogs",
                column: "RestaurantId");

            migrationBuilder.CreateIndex(
                name: "IX_RestaurantMealLabelSettings_AdminAssignedTemplateId",
                table: "RestaurantMealLabelSettings",
                column: "AdminAssignedTemplateId");

            migrationBuilder.CreateIndex(
                name: "IX_RestaurantMealLabelSettings_RestaurantId",
                table: "RestaurantMealLabelSettings",
                column: "RestaurantId",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_RestaurantMealLabelSettings_SelectedTemplateId",
                table: "RestaurantMealLabelSettings",
                column: "SelectedTemplateId");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "MealLabelPrintLogs");

            migrationBuilder.DropTable(
                name: "RestaurantMealLabelSettings");

            migrationBuilder.DropTable(
                name: "MealBoxLabelTemplates");
        }
    }
}
