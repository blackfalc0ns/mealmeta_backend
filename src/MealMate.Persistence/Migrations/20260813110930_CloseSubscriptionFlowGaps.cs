using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace MealMate.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class CloseSubscriptionFlowGaps : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<int>(
                name: "FulfillmentAttemptCount",
                table: "Subscriptions",
                type: "integer",
                nullable: false,
                defaultValue: 0);

            migrationBuilder.AddColumn<DateTime>(
                name: "FulfillmentCompletedAtUtc",
                table: "Subscriptions",
                type: "timestamp with time zone",
                nullable: true);

            migrationBuilder.AddColumn<DateTime>(
                name: "LastFulfillmentAttemptUtc",
                table: "Subscriptions",
                type: "timestamp with time zone",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "LastFulfillmentErrorCode",
                table: "Subscriptions",
                type: "character varying(100)",
                maxLength: 100,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "PaymentId",
                table: "Subscriptions",
                type: "character varying(100)",
                maxLength: 100,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "PaymentInvoiceId",
                table: "Subscriptions",
                type: "character varying(100)",
                maxLength: 100,
                nullable: true);

            migrationBuilder.AddColumn<DateTime>(
                name: "PaymentVerifiedAtUtc",
                table: "Subscriptions",
                type: "timestamp with time zone",
                nullable: true);

            migrationBuilder.AddColumn<decimal>(
                name: "Amount",
                table: "PaymentWebhookReceipts",
                type: "numeric(18,3)",
                precision: 18,
                scale: 3,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "CurrencyCode",
                table: "PaymentWebhookReceipts",
                type: "character varying(3)",
                maxLength: 3,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "CustomerReference",
                table: "PaymentWebhookReceipts",
                type: "character varying(100)",
                maxLength: 100,
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "FulfillmentAttemptCount",
                table: "PaymentWebhookReceipts",
                type: "integer",
                nullable: false,
                defaultValue: 0);

            migrationBuilder.AddColumn<DateTime>(
                name: "FulfillmentCompletedAtUtc",
                table: "PaymentWebhookReceipts",
                type: "timestamp with time zone",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "InvoiceId",
                table: "PaymentWebhookReceipts",
                type: "character varying(100)",
                maxLength: 100,
                nullable: true);

            migrationBuilder.AddColumn<DateTime>(
                name: "LastFulfillmentAttemptUtc",
                table: "PaymentWebhookReceipts",
                type: "timestamp with time zone",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "LastFulfillmentErrorCode",
                table: "PaymentWebhookReceipts",
                type: "character varying(100)",
                maxLength: 100,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "PaymentId",
                table: "PaymentWebhookReceipts",
                type: "character varying(100)",
                maxLength: 100,
                nullable: true);

            migrationBuilder.AddColumn<DateTime>(
                name: "VerifiedAtUtc",
                table: "PaymentWebhookReceipts",
                type: "timestamp with time zone",
                nullable: true);

            migrationBuilder.CreateTable(
                name: "SubscriptionRestaurantPoolSnapshots",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    SubscriptionId = table.Column<Guid>(type: "uuid", nullable: false),
                    EligibilityCheckedAtUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    RestaurantCount = table.Column<int>(type: "integer", nullable: false),
                    IsBackfilled = table.Column<bool>(type: "boolean", nullable: false),
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
                    table.PrimaryKey("PK_SubscriptionRestaurantPoolSnapshots", x => x.Id);
                    table.ForeignKey(
                        name: "FK_SubscriptionRestaurantPoolSnapshots_Subscriptions_Subscript~",
                        column: x => x.SubscriptionId,
                        principalTable: "Subscriptions",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "SubscriptionSnapshotMigrationIssues",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    SubscriptionId = table.Column<Guid>(type: "uuid", nullable: false),
                    ReasonCode = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false),
                    DetailsJson = table.Column<string>(type: "jsonb", nullable: false),
                    RecordedAtUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    ResolvedAtUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
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
                    table.PrimaryKey("PK_SubscriptionSnapshotMigrationIssues", x => x.Id);
                    table.ForeignKey(
                        name: "FK_SubscriptionSnapshotMigrationIssues_Subscriptions_Subscript~",
                        column: x => x.SubscriptionId,
                        principalTable: "Subscriptions",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "SubscriptionRestaurantSnapshots",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    PoolSnapshotId = table.Column<Guid>(type: "uuid", nullable: false),
                    SubscriptionId = table.Column<Guid>(type: "uuid", nullable: false),
                    RestaurantId = table.Column<Guid>(type: "uuid", nullable: false),
                    ProgramId = table.Column<Guid>(type: "uuid", nullable: false),
                    BundleId = table.Column<Guid>(type: "uuid", nullable: false),
                    RegionId = table.Column<Guid>(type: "uuid", nullable: false),
                    Tier = table.Column<int>(type: "integer", nullable: false),
                    NameAr = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false),
                    NameEn = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false),
                    IsInitiallySelected = table.Column<bool>(type: "boolean", nullable: false),
                    OfferingSnapshotJson = table.Column<string>(type: "jsonb", nullable: false),
                    CapturedAtUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
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
                    table.PrimaryKey("PK_SubscriptionRestaurantSnapshots", x => x.Id);
                    table.ForeignKey(
                        name: "FK_SubscriptionRestaurantSnapshots_SubscriptionRestaurantPoolS~",
                        column: x => x.PoolSnapshotId,
                        principalTable: "SubscriptionRestaurantPoolSnapshots",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_SubscriptionRestaurantSnapshots_Subscriptions_SubscriptionId",
                        column: x => x.SubscriptionId,
                        principalTable: "Subscriptions",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "SubscriptionRestaurantDaySnapshots",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    RestaurantSnapshotId = table.Column<Guid>(type: "uuid", nullable: false),
                    SubscriptionId = table.Column<Guid>(type: "uuid", nullable: false),
                    RestaurantId = table.Column<Guid>(type: "uuid", nullable: false),
                    ServiceDate = table.Column<DateOnly>(type: "date", nullable: false),
                    MenuPlanVersionId = table.Column<Guid>(type: "uuid", nullable: false),
                    SlotRequirementsJson = table.Column<string>(type: "jsonb", nullable: false),
                    MealsJson = table.Column<string>(type: "jsonb", nullable: false),
                    ContentHash = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    CapturedAtUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
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
                    table.PrimaryKey("PK_SubscriptionRestaurantDaySnapshots", x => x.Id);
                    table.ForeignKey(
                        name: "FK_SubscriptionRestaurantDaySnapshots_SubscriptionRestaurantSn~",
                        column: x => x.RestaurantSnapshotId,
                        principalTable: "SubscriptionRestaurantSnapshots",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_SubscriptionRestaurantDaySnapshots_Subscriptions_Subscripti~",
                        column: x => x.SubscriptionId,
                        principalTable: "Subscriptions",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateIndex(
                name: "IX_Subscriptions_PaymentId",
                table: "Subscriptions",
                column: "PaymentId",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_Subscriptions_PaymentInvoiceId",
                table: "Subscriptions",
                column: "PaymentInvoiceId");

            migrationBuilder.CreateIndex(
                name: "IX_PaymentWebhookReceipts_Gateway_InvoiceId",
                table: "PaymentWebhookReceipts",
                columns: new[] { "Gateway", "InvoiceId" });

            migrationBuilder.CreateIndex(
                name: "IX_PaymentWebhookReceipts_Gateway_PaymentId",
                table: "PaymentWebhookReceipts",
                columns: new[] { "Gateway", "PaymentId" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_SubscriptionRestaurantDaySnapshots_ContentHash",
                table: "SubscriptionRestaurantDaySnapshots",
                column: "ContentHash");

            migrationBuilder.CreateIndex(
                name: "IX_SubscriptionRestaurantDaySnapshots_MenuPlanVersionId",
                table: "SubscriptionRestaurantDaySnapshots",
                column: "MenuPlanVersionId");

            migrationBuilder.CreateIndex(
                name: "IX_SubscriptionRestaurantDaySnapshots_RestaurantSnapshotId_Ser~",
                table: "SubscriptionRestaurantDaySnapshots",
                columns: new[] { "RestaurantSnapshotId", "ServiceDate" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_SubscriptionRestaurantDaySnapshots_SubscriptionId_Restauran~",
                table: "SubscriptionRestaurantDaySnapshots",
                columns: new[] { "SubscriptionId", "RestaurantId", "ServiceDate" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_SubscriptionRestaurantPoolSnapshots_EligibilityCheckedAtUtc",
                table: "SubscriptionRestaurantPoolSnapshots",
                column: "EligibilityCheckedAtUtc");

            migrationBuilder.CreateIndex(
                name: "IX_SubscriptionRestaurantPoolSnapshots_SubscriptionId",
                table: "SubscriptionRestaurantPoolSnapshots",
                column: "SubscriptionId",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_SubscriptionRestaurantSnapshots_PoolSnapshotId_RestaurantId",
                table: "SubscriptionRestaurantSnapshots",
                columns: new[] { "PoolSnapshotId", "RestaurantId" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_SubscriptionRestaurantSnapshots_RestaurantId",
                table: "SubscriptionRestaurantSnapshots",
                column: "RestaurantId");

            migrationBuilder.CreateIndex(
                name: "IX_SubscriptionRestaurantSnapshots_SubscriptionId_RestaurantId",
                table: "SubscriptionRestaurantSnapshots",
                columns: new[] { "SubscriptionId", "RestaurantId" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_SubscriptionSnapshotMigrationIssues_ResolvedAtUtc_RecordedA~",
                table: "SubscriptionSnapshotMigrationIssues",
                columns: new[] { "ResolvedAtUtc", "RecordedAtUtc" });

            migrationBuilder.CreateIndex(
                name: "IX_SubscriptionSnapshotMigrationIssues_SubscriptionId",
                table: "SubscriptionSnapshotMigrationIssues",
                column: "SubscriptionId",
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "SubscriptionRestaurantDaySnapshots");

            migrationBuilder.DropTable(
                name: "SubscriptionSnapshotMigrationIssues");

            migrationBuilder.DropTable(
                name: "SubscriptionRestaurantSnapshots");

            migrationBuilder.DropTable(
                name: "SubscriptionRestaurantPoolSnapshots");

            migrationBuilder.DropIndex(
                name: "IX_Subscriptions_PaymentId",
                table: "Subscriptions");

            migrationBuilder.DropIndex(
                name: "IX_Subscriptions_PaymentInvoiceId",
                table: "Subscriptions");

            migrationBuilder.DropIndex(
                name: "IX_PaymentWebhookReceipts_Gateway_InvoiceId",
                table: "PaymentWebhookReceipts");

            migrationBuilder.DropIndex(
                name: "IX_PaymentWebhookReceipts_Gateway_PaymentId",
                table: "PaymentWebhookReceipts");

            migrationBuilder.DropColumn(
                name: "FulfillmentAttemptCount",
                table: "Subscriptions");

            migrationBuilder.DropColumn(
                name: "FulfillmentCompletedAtUtc",
                table: "Subscriptions");

            migrationBuilder.DropColumn(
                name: "LastFulfillmentAttemptUtc",
                table: "Subscriptions");

            migrationBuilder.DropColumn(
                name: "LastFulfillmentErrorCode",
                table: "Subscriptions");

            migrationBuilder.DropColumn(
                name: "PaymentId",
                table: "Subscriptions");

            migrationBuilder.DropColumn(
                name: "PaymentInvoiceId",
                table: "Subscriptions");

            migrationBuilder.DropColumn(
                name: "PaymentVerifiedAtUtc",
                table: "Subscriptions");

            migrationBuilder.DropColumn(
                name: "Amount",
                table: "PaymentWebhookReceipts");

            migrationBuilder.DropColumn(
                name: "CurrencyCode",
                table: "PaymentWebhookReceipts");

            migrationBuilder.DropColumn(
                name: "CustomerReference",
                table: "PaymentWebhookReceipts");

            migrationBuilder.DropColumn(
                name: "FulfillmentAttemptCount",
                table: "PaymentWebhookReceipts");

            migrationBuilder.DropColumn(
                name: "FulfillmentCompletedAtUtc",
                table: "PaymentWebhookReceipts");

            migrationBuilder.DropColumn(
                name: "InvoiceId",
                table: "PaymentWebhookReceipts");

            migrationBuilder.DropColumn(
                name: "LastFulfillmentAttemptUtc",
                table: "PaymentWebhookReceipts");

            migrationBuilder.DropColumn(
                name: "LastFulfillmentErrorCode",
                table: "PaymentWebhookReceipts");

            migrationBuilder.DropColumn(
                name: "PaymentId",
                table: "PaymentWebhookReceipts");

            migrationBuilder.DropColumn(
                name: "VerifiedAtUtc",
                table: "PaymentWebhookReceipts");
        }
    }
}
