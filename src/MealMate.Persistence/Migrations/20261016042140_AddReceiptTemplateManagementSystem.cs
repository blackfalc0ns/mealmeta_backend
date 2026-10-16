using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace MealMate.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddReceiptTemplateManagementSystem : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql(@"
DO $$
BEGIN
    IF NOT EXISTS (SELECT FROM pg_tables WHERE schemaname = 'public' AND tablename = 'OfferPackageTargets') THEN
        CREATE TABLE ""OfferPackageTargets"" (
            ""Id"" uuid NOT NULL,
            ""OfferId"" uuid NOT NULL,
            ""SubscriptionType"" character varying(32) NOT NULL,
            ""ProgramId"" uuid NOT NULL,
            ""BundleId"" uuid NOT NULL,
            ""Tier"" integer NOT NULL,
            ""AllowedDurationDays"" integer[] NOT NULL,
            CONSTRAINT ""PK_OfferPackageTargets"" PRIMARY KEY (""Id""),
            CONSTRAINT ""FK_OfferPackageTargets_Bundles_BundleId"" FOREIGN KEY (""BundleId"") REFERENCES ""Bundles"" (""Id"") ON DELETE RESTRICT,
            CONSTRAINT ""FK_OfferPackageTargets_Offers_OfferId"" FOREIGN KEY (""OfferId"") REFERENCES ""Offers"" (""Id"") ON DELETE CASCADE,
            CONSTRAINT ""FK_OfferPackageTargets_Programs_ProgramId"" FOREIGN KEY (""ProgramId"") REFERENCES ""Programs"" (""Id"") ON DELETE RESTRICT
        );
        CREATE INDEX IF NOT EXISTS ""IX_OfferPackageTargets_BundleId"" ON ""OfferPackageTargets"" (""BundleId"");
        CREATE UNIQUE INDEX IF NOT EXISTS ""IX_OfferPackageTargets_OfferId_SubscriptionType_ProgramId_Bund~"" ON ""OfferPackageTargets"" (""OfferId"", ""SubscriptionType"", ""ProgramId"", ""BundleId"", ""Tier"");
        CREATE INDEX IF NOT EXISTS ""IX_OfferPackageTargets_ProgramId_BundleId_Tier"" ON ""OfferPackageTargets"" (""ProgramId"", ""BundleId"", ""Tier"");
    END IF;
END $$;
");

            migrationBuilder.CreateTable(
                name: "ReceiptTemplates",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    Key = table.Column<string>(type: "character varying(50)", maxLength: 50, nullable: false),
                    NameAr = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false),
                    NameEn = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false),
                    DescriptionAr = table.Column<string>(type: "character varying(500)", maxLength: 500, nullable: true),
                    DescriptionEn = table.Column<string>(type: "character varying(500)", maxLength: 500, nullable: true),
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
                    table.PrimaryKey("PK_ReceiptTemplates", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "ReceiptPrintLogs",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    OrderId = table.Column<Guid>(type: "uuid", nullable: false),
                    RestaurantId = table.Column<Guid>(type: "uuid", nullable: false),
                    TemplateId = table.Column<Guid>(type: "uuid", nullable: false),
                    TemplateVersion = table.Column<int>(type: "integer", nullable: false),
                    PrintedByUserId = table.Column<Guid>(type: "uuid", nullable: false),
                    PrintedAtUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    PrintStatus = table.Column<string>(type: "character varying(50)", maxLength: 50, nullable: false),
                    ClientIp = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: true),
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
                    table.PrimaryKey("PK_ReceiptPrintLogs", x => x.Id);
                    table.ForeignKey(
                        name: "FK_ReceiptPrintLogs_ReceiptTemplates_TemplateId",
                        column: x => x.TemplateId,
                        principalTable: "ReceiptTemplates",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "RestaurantReceiptSettings",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    RestaurantId = table.Column<Guid>(type: "uuid", nullable: false),
                    SelectedTemplateId = table.Column<Guid>(type: "uuid", nullable: true),
                    AdminAssignedTemplateId = table.Column<Guid>(type: "uuid", nullable: true),
                    AllowRestaurantSelection = table.Column<bool>(type: "boolean", nullable: false),
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
                    table.PrimaryKey("PK_RestaurantReceiptSettings", x => x.Id);
                    table.ForeignKey(
                        name: "FK_RestaurantReceiptSettings_ReceiptTemplates_AdminAssignedTem~",
                        column: x => x.AdminAssignedTemplateId,
                        principalTable: "ReceiptTemplates",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.SetNull);
                    table.ForeignKey(
                        name: "FK_RestaurantReceiptSettings_ReceiptTemplates_SelectedTemplate~",
                        column: x => x.SelectedTemplateId,
                        principalTable: "ReceiptTemplates",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.SetNull);
                });


            migrationBuilder.CreateIndex(
                name: "IX_ReceiptPrintLogs_OrderId",
                table: "ReceiptPrintLogs",
                column: "OrderId");

            migrationBuilder.CreateIndex(
                name: "IX_ReceiptPrintLogs_PrintedAtUtc",
                table: "ReceiptPrintLogs",
                column: "PrintedAtUtc");

            migrationBuilder.CreateIndex(
                name: "IX_ReceiptPrintLogs_RestaurantId",
                table: "ReceiptPrintLogs",
                column: "RestaurantId");

            migrationBuilder.CreateIndex(
                name: "IX_ReceiptPrintLogs_TemplateId",
                table: "ReceiptPrintLogs",
                column: "TemplateId");

            migrationBuilder.CreateIndex(
                name: "IX_ReceiptTemplates_IsEnabled",
                table: "ReceiptTemplates",
                column: "IsEnabled");

            migrationBuilder.CreateIndex(
                name: "IX_ReceiptTemplates_IsGlobalDefault",
                table: "ReceiptTemplates",
                column: "IsGlobalDefault");

            migrationBuilder.CreateIndex(
                name: "IX_ReceiptTemplates_IsPublished",
                table: "ReceiptTemplates",
                column: "IsPublished");

            migrationBuilder.CreateIndex(
                name: "IX_ReceiptTemplates_IsRestaurantVisible",
                table: "ReceiptTemplates",
                column: "IsRestaurantVisible");

            migrationBuilder.CreateIndex(
                name: "IX_ReceiptTemplates_Key",
                table: "ReceiptTemplates",
                column: "Key",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_RestaurantReceiptSettings_AdminAssignedTemplateId",
                table: "RestaurantReceiptSettings",
                column: "AdminAssignedTemplateId");

            migrationBuilder.CreateIndex(
                name: "IX_RestaurantReceiptSettings_RestaurantId",
                table: "RestaurantReceiptSettings",
                column: "RestaurantId",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_RestaurantReceiptSettings_SelectedTemplateId",
                table: "RestaurantReceiptSettings",
                column: "SelectedTemplateId");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "OfferPackageTargets");

            migrationBuilder.DropTable(
                name: "ReceiptPrintLogs");

            migrationBuilder.DropTable(
                name: "RestaurantReceiptSettings");

            migrationBuilder.DropTable(
                name: "ReceiptTemplates");
        }
    }
}
