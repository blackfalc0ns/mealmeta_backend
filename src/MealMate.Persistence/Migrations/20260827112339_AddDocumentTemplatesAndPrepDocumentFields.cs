using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace MealMate.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddDocumentTemplatesAndPrepDocumentFields : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "IX_MealBarcodes_SubscriptionDayId",
                table: "MealBarcodes");

            migrationBuilder.AddColumn<int>(
                name: "Kind",
                table: "MealLabels",
                type: "integer",
                nullable: false,
                defaultValue: 1);

            migrationBuilder.AddColumn<Guid>(
                name: "MealId",
                table: "MealLabels",
                type: "uuid",
                nullable: true);

            migrationBuilder.AddColumn<Guid>(
                name: "TemplateId",
                table: "MealLabels",
                type: "uuid",
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "TemplateVersion",
                table: "MealLabels",
                type: "integer",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "BarcodeValue",
                table: "MealInvoices",
                type: "character varying(100)",
                maxLength: 100,
                nullable: true);

            migrationBuilder.AddColumn<Guid>(
                name: "SubscriptionDayId",
                table: "MealInvoices",
                type: "uuid",
                nullable: true);

            migrationBuilder.AddColumn<Guid>(
                name: "TemplateId",
                table: "MealInvoices",
                type: "uuid",
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "TemplateVersion",
                table: "MealInvoices",
                type: "integer",
                nullable: true);

            migrationBuilder.CreateTable(
                name: "DocumentTemplates",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    Kind = table.Column<int>(type: "integer", nullable: false),
                    NameAr = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false),
                    NameEn = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false),
                    DescriptionAr = table.Column<string>(type: "character varying(2000)", maxLength: 2000, nullable: true),
                    DescriptionEn = table.Column<string>(type: "character varying(2000)", maxLength: 2000, nullable: true),
                    Language = table.Column<string>(type: "character varying(10)", maxLength: 10, nullable: false),
                    Status = table.Column<int>(type: "integer", nullable: false),
                    PageWidthPx = table.Column<int>(type: "integer", nullable: false),
                    PageHeightPx = table.Column<int>(type: "integer", nullable: false),
                    MarginPx = table.Column<int>(type: "integer", nullable: false),
                    PageSizeLabel = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false),
                    BackgroundColor = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false),
                    AccentColor = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false),
                    ShowAppLogo = table.Column<bool>(type: "boolean", nullable: false),
                    ShowRestaurantLogo = table.Column<bool>(type: "boolean", nullable: false),
                    LayoutJson = table.Column<string>(type: "jsonb", nullable: false),
                    Version = table.Column<int>(type: "integer", nullable: false),
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
                    table.PrimaryKey("PK_DocumentTemplates", x => x.Id);
                });

            migrationBuilder.CreateIndex(
                name: "IX_MealLabels_MealId",
                table: "MealLabels",
                column: "MealId");

            migrationBuilder.CreateIndex(
                name: "IX_MealLabels_SubscriptionDayId_Kind_MealId",
                table: "MealLabels",
                columns: new[] { "SubscriptionDayId", "Kind", "MealId" });

            migrationBuilder.CreateIndex(
                name: "IX_MealLabels_TemplateId",
                table: "MealLabels",
                column: "TemplateId");

            migrationBuilder.CreateIndex(
                name: "IX_MealInvoices_SubscriptionDayId",
                table: "MealInvoices",
                column: "SubscriptionDayId");

            migrationBuilder.CreateIndex(
                name: "IX_MealInvoices_TemplateId",
                table: "MealInvoices",
                column: "TemplateId");

            migrationBuilder.CreateIndex(
                name: "IX_MealBarcodes_SubscriptionDayId",
                table: "MealBarcodes",
                column: "SubscriptionDayId",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_DocumentTemplates_Kind",
                table: "DocumentTemplates",
                column: "Kind",
                unique: true,
                filter: "\"Status\" = 2");

            migrationBuilder.CreateIndex(
                name: "IX_DocumentTemplates_Kind_Status",
                table: "DocumentTemplates",
                columns: new[] { "Kind", "Status" });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "DocumentTemplates");

            migrationBuilder.DropIndex(
                name: "IX_MealLabels_MealId",
                table: "MealLabels");

            migrationBuilder.DropIndex(
                name: "IX_MealLabels_SubscriptionDayId_Kind_MealId",
                table: "MealLabels");

            migrationBuilder.DropIndex(
                name: "IX_MealLabels_TemplateId",
                table: "MealLabels");

            migrationBuilder.DropIndex(
                name: "IX_MealInvoices_SubscriptionDayId",
                table: "MealInvoices");

            migrationBuilder.DropIndex(
                name: "IX_MealInvoices_TemplateId",
                table: "MealInvoices");

            migrationBuilder.DropIndex(
                name: "IX_MealBarcodes_SubscriptionDayId",
                table: "MealBarcodes");

            migrationBuilder.DropColumn(
                name: "Kind",
                table: "MealLabels");

            migrationBuilder.DropColumn(
                name: "MealId",
                table: "MealLabels");

            migrationBuilder.DropColumn(
                name: "TemplateId",
                table: "MealLabels");

            migrationBuilder.DropColumn(
                name: "TemplateVersion",
                table: "MealLabels");

            migrationBuilder.DropColumn(
                name: "BarcodeValue",
                table: "MealInvoices");

            migrationBuilder.DropColumn(
                name: "SubscriptionDayId",
                table: "MealInvoices");

            migrationBuilder.DropColumn(
                name: "TemplateId",
                table: "MealInvoices");

            migrationBuilder.DropColumn(
                name: "TemplateVersion",
                table: "MealInvoices");

            migrationBuilder.CreateIndex(
                name: "IX_MealBarcodes_SubscriptionDayId",
                table: "MealBarcodes",
                column: "SubscriptionDayId");
        }
    }
}
