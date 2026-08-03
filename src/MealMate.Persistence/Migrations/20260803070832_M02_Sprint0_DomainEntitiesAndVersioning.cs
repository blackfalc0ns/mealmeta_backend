using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace MealMate.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class M02_Sprint0_DomainEntitiesAndVersioning : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<DateTime>(
                name: "LastOperationAtUtc",
                table: "Subscriptions",
                type: "timestamp with time zone",
                nullable: true);

            migrationBuilder.AddColumn<Guid>(
                name: "PackageContentVersionId",
                table: "Subscriptions",
                type: "uuid",
                nullable: true);

            migrationBuilder.AddColumn<byte[]>(
                name: "RowVersion",
                table: "Subscriptions",
                type: "bytea",
                rowVersion: true,
                nullable: false,
                defaultValue: new byte[0]);

            migrationBuilder.AddColumn<Guid>(
                name: "CurrentVersionId",
                table: "Programs",
                type: "uuid",
                nullable: true);

            migrationBuilder.AddColumn<byte[]>(
                name: "RowVersion",
                table: "Programs",
                type: "bytea",
                rowVersion: true,
                nullable: false,
                defaultValue: new byte[0]);

            migrationBuilder.AddColumn<int>(
                name: "Status",
                table: "Programs",
                type: "integer",
                nullable: false,
                defaultValue: 0);

            migrationBuilder.AddColumn<string>(
                name: "StatusReason",
                table: "Programs",
                type: "character varying(1000)",
                maxLength: 1000,
                nullable: true);

            migrationBuilder.AddColumn<byte[]>(
                name: "RowVersion",
                table: "Bundles",
                type: "bytea",
                rowVersion: true,
                nullable: false,
                defaultValue: new byte[0]);

            migrationBuilder.AddColumn<int>(
                name: "SortOrder",
                table: "Bundles",
                type: "integer",
                nullable: false,
                defaultValue: 0);

            migrationBuilder.AddColumn<int>(
                name: "Status",
                table: "Bundles",
                type: "integer",
                nullable: false,
                defaultValue: 0);

            migrationBuilder.AddColumn<string>(
                name: "StatusReason",
                table: "Bundles",
                type: "character varying(1000)",
                maxLength: 1000,
                nullable: true);

            migrationBuilder.CreateTable(
                name: "DiscountsAndReferralLogs",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    SubscriptionId = table.Column<Guid>(type: "uuid", nullable: false),
                    DiscountType = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false),
                    Code = table.Column<string>(type: "character varying(50)", maxLength: 50, nullable: true),
                    Amount_Amount = table.Column<decimal>(type: "numeric(18,3)", precision: 18, scale: 3, nullable: false),
                    Amount_CurrencyCode = table.Column<string>(type: "character varying(3)", maxLength: 3, nullable: false),
                    FundingMode = table.Column<int>(type: "integer", nullable: false),
                    AppliedAtUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    MetadataJson = table.Column<string>(type: "jsonb", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_DiscountsAndReferralLogs", x => x.Id);
                    table.ForeignKey(
                        name: "FK_DiscountsAndReferralLogs_Subscriptions_SubscriptionId",
                        column: x => x.SubscriptionId,
                        principalTable: "Subscriptions",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "PackageContentVersions",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    ProgramId = table.Column<Guid>(type: "uuid", nullable: false),
                    BundleId = table.Column<Guid>(type: "uuid", nullable: true),
                    VersionNumber = table.Column<int>(type: "integer", nullable: false),
                    ContentSnapshotJson = table.Column<string>(type: "jsonb", nullable: false),
                    EffectiveFromUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    EffectiveToUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    IsActive = table.Column<bool>(type: "boolean", nullable: false),
                    CreatedByAdminUserId = table.Column<Guid>(type: "uuid", nullable: true),
                    ChangeSummary = table.Column<string>(type: "character varying(1000)", maxLength: 1000, nullable: true),
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
                    table.PrimaryKey("PK_PackageContentVersions", x => x.Id);
                    table.ForeignKey(
                        name: "FK_PackageContentVersions_Bundles_BundleId",
                        column: x => x.BundleId,
                        principalTable: "Bundles",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_PackageContentVersions_Programs_ProgramId",
                        column: x => x.ProgramId,
                        principalTable: "Programs",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "SubscriptionOperations",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    SubscriptionId = table.Column<Guid>(type: "uuid", nullable: false),
                    OperationType = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false),
                    PreviousStatus = table.Column<int>(type: "integer", nullable: true),
                    NewStatus = table.Column<int>(type: "integer", nullable: false),
                    Reason = table.Column<string>(type: "character varying(1000)", maxLength: 1000, nullable: true),
                    PerformedByUserId = table.Column<Guid>(type: "uuid", nullable: false),
                    PerformedByUserType = table.Column<int>(type: "integer", nullable: false),
                    CorrelationId = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: true),
                    MetadataJson = table.Column<string>(type: "jsonb", nullable: true),
                    TimestampUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_SubscriptionOperations", x => x.Id);
                    table.ForeignKey(
                        name: "FK_SubscriptionOperations_Subscriptions_SubscriptionId",
                        column: x => x.SubscriptionId,
                        principalTable: "Subscriptions",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateIndex(
                name: "IX_Subscriptions_PackageContentVersionId",
                table: "Subscriptions",
                column: "PackageContentVersionId");

            migrationBuilder.CreateIndex(
                name: "IX_Programs_Status",
                table: "Programs",
                column: "Status");

            migrationBuilder.CreateIndex(
                name: "IX_Bundles_Status",
                table: "Bundles",
                column: "Status");

            migrationBuilder.CreateIndex(
                name: "IX_DiscountsAndReferralLogs_AppliedAtUtc",
                table: "DiscountsAndReferralLogs",
                column: "AppliedAtUtc");

            migrationBuilder.CreateIndex(
                name: "IX_DiscountsAndReferralLogs_Code",
                table: "DiscountsAndReferralLogs",
                column: "Code");

            migrationBuilder.CreateIndex(
                name: "IX_DiscountsAndReferralLogs_SubscriptionId",
                table: "DiscountsAndReferralLogs",
                column: "SubscriptionId");

            migrationBuilder.CreateIndex(
                name: "IX_PackageContentVersions_BundleId",
                table: "PackageContentVersions",
                column: "BundleId");

            migrationBuilder.CreateIndex(
                name: "IX_PackageContentVersions_IsActive",
                table: "PackageContentVersions",
                column: "IsActive");

            migrationBuilder.CreateIndex(
                name: "IX_PackageContentVersions_ProgramId_VersionNumber",
                table: "PackageContentVersions",
                columns: new[] { "ProgramId", "VersionNumber" });

            migrationBuilder.CreateIndex(
                name: "IX_SubscriptionOperations_CorrelationId",
                table: "SubscriptionOperations",
                column: "CorrelationId");

            migrationBuilder.CreateIndex(
                name: "IX_SubscriptionOperations_SubscriptionId",
                table: "SubscriptionOperations",
                column: "SubscriptionId");

            migrationBuilder.CreateIndex(
                name: "IX_SubscriptionOperations_TimestampUtc",
                table: "SubscriptionOperations",
                column: "TimestampUtc");

            migrationBuilder.AddForeignKey(
                name: "FK_Subscriptions_PackageContentVersions_PackageContentVersionId",
                table: "Subscriptions",
                column: "PackageContentVersionId",
                principalTable: "PackageContentVersions",
                principalColumn: "Id",
                onDelete: ReferentialAction.Restrict);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "FK_Subscriptions_PackageContentVersions_PackageContentVersionId",
                table: "Subscriptions");

            migrationBuilder.DropTable(
                name: "DiscountsAndReferralLogs");

            migrationBuilder.DropTable(
                name: "PackageContentVersions");

            migrationBuilder.DropTable(
                name: "SubscriptionOperations");

            migrationBuilder.DropIndex(
                name: "IX_Subscriptions_PackageContentVersionId",
                table: "Subscriptions");

            migrationBuilder.DropIndex(
                name: "IX_Programs_Status",
                table: "Programs");

            migrationBuilder.DropIndex(
                name: "IX_Bundles_Status",
                table: "Bundles");

            migrationBuilder.DropColumn(
                name: "LastOperationAtUtc",
                table: "Subscriptions");

            migrationBuilder.DropColumn(
                name: "PackageContentVersionId",
                table: "Subscriptions");

            migrationBuilder.DropColumn(
                name: "RowVersion",
                table: "Subscriptions");

            migrationBuilder.DropColumn(
                name: "CurrentVersionId",
                table: "Programs");

            migrationBuilder.DropColumn(
                name: "RowVersion",
                table: "Programs");

            migrationBuilder.DropColumn(
                name: "Status",
                table: "Programs");

            migrationBuilder.DropColumn(
                name: "StatusReason",
                table: "Programs");

            migrationBuilder.DropColumn(
                name: "RowVersion",
                table: "Bundles");

            migrationBuilder.DropColumn(
                name: "SortOrder",
                table: "Bundles");

            migrationBuilder.DropColumn(
                name: "Status",
                table: "Bundles");

            migrationBuilder.DropColumn(
                name: "StatusReason",
                table: "Bundles");
        }
    }
}
