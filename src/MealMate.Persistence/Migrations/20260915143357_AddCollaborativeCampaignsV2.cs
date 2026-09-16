using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace MealMate.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddCollaborativeCampaignsV2 : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql(@"
DO $$
BEGIN
    IF EXISTS (SELECT 1 FROM pg_indexes WHERE tablename = 'IdempotencyRecords' AND indexname = 'IX_IdempotencyRecords_Key') THEN
        DROP INDEX ""IX_IdempotencyRecords_Key"";
    END IF;
    IF NOT EXISTS (SELECT 1 FROM information_schema.columns WHERE table_name = 'IdempotencyRecords' AND column_name = 'ResourceUri') THEN
        ALTER TABLE ""IdempotencyRecords"" ADD ""ResourceUri"" character varying(500) NULL;
    END IF;
    IF NOT EXISTS (SELECT 1 FROM information_schema.columns WHERE table_name = 'IdempotencyRecords' AND column_name = 'Scope') THEN
        ALTER TABLE ""IdempotencyRecords"" ADD ""Scope"" character varying(100) NOT NULL DEFAULT '';
    END IF;
    IF NOT EXISTS (SELECT 1 FROM pg_indexes WHERE tablename = 'IdempotencyRecords' AND indexname = 'IX_IdempotencyRecords_Key') THEN
        CREATE INDEX ""IX_IdempotencyRecords_Key"" ON ""IdempotencyRecords"" (""Key"");
    END IF;
    IF NOT EXISTS (SELECT 1 FROM pg_indexes WHERE tablename = 'IdempotencyRecords' AND indexname = 'IX_IdempotencyRecords_Scope_Key') THEN
        CREATE UNIQUE INDEX ""IX_IdempotencyRecords_Scope_Key"" ON ""IdempotencyRecords"" (""Scope"", ""Key"");
    END IF;
    IF NOT EXISTS (SELECT 1 FROM information_schema.columns WHERE table_name = 'Trips' AND column_name = 'Code') THEN
        ALTER TABLE ""Trips"" ADD ""Code"" character varying(50) NOT NULL DEFAULT '';
    END IF;
    IF NOT EXISTS (SELECT 1 FROM information_schema.columns WHERE table_name = 'Trips' AND column_name = 'ShiftName') THEN
        ALTER TABLE ""Trips"" ADD ""ShiftName"" character varying(100) NULL;
    END IF;
    IF NOT EXISTS (SELECT 1 FROM pg_indexes WHERE tablename = 'Trips' AND indexname = 'IX_Trips_Code') THEN
        CREATE INDEX ""IX_Trips_Code"" ON ""Trips"" (""Code"");
    END IF;
END $$;
");

            migrationBuilder.CreateTable(
                name: "CampaignAuditLogs",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    CampaignId = table.Column<Guid>(type: "uuid", nullable: false),
                    ActorId = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false),
                    Action = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false),
                    BeforeStateJson = table.Column<string>(type: "text", nullable: true),
                    AfterStateJson = table.Column<string>(type: "text", nullable: true),
                    DetailsJson = table.Column<string>(type: "text", nullable: true),
                    JustificationReason = table.Column<string>(type: "character varying(1000)", maxLength: 1000, nullable: true),
                    CreatedAtUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_CampaignAuditLogs", x => x.Id);
                    table.ForeignKey(
                        name: "FK_CampaignAuditLogs_Campaigns_CampaignId",
                        column: x => x.CampaignId,
                        principalTable: "Campaigns",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "CampaignBundleDurationPrices",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    CampaignBundleId = table.Column<Guid>(type: "uuid", nullable: false),
                    DurationDays = table.Column<int>(type: "integer", nullable: false),
                    ReferenceCustomerPrice_Amount = table.Column<decimal>(type: "numeric(18,3)", precision: 18, scale: 3, nullable: false),
                    ReferenceCustomerPrice_CurrencyCode = table.Column<string>(type: "character varying(3)", maxLength: 3, nullable: false),
                    Source = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false),
                    Version = table.Column<int>(type: "integer", nullable: false),
                    CapturedAtUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_CampaignBundleDurationPrices", x => x.Id);
                    table.ForeignKey(
                        name: "FK_CampaignBundleDurationPrices_CampaignBundles_CampaignBundle~",
                        column: x => x.CampaignBundleId,
                        principalTable: "CampaignBundles",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "CampaignFinancialLedgers",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    JournalId = table.Column<Guid>(type: "uuid", nullable: false),
                    CampaignId = table.Column<Guid>(type: "uuid", nullable: false),
                    CheckoutSessionId = table.Column<Guid>(type: "uuid", nullable: true),
                    RestaurantId = table.Column<Guid>(type: "uuid", nullable: true),
                    EntryType = table.Column<int>(type: "integer", nullable: false),
                    Amount = table.Column<decimal>(type: "numeric(18,3)", precision: 18, scale: 3, nullable: false),
                    Currency = table.Column<string>(type: "character varying(3)", maxLength: 3, nullable: false),
                    Description = table.Column<string>(type: "character varying(500)", maxLength: 500, nullable: false),
                    CreatedAtUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    CreatedByUserId = table.Column<Guid>(type: "uuid", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_CampaignFinancialLedgers", x => x.Id);
                    table.ForeignKey(
                        name: "FK_CampaignFinancialLedgers_Campaigns_CampaignId",
                        column: x => x.CampaignId,
                        principalTable: "Campaigns",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "CampaignPaymentTransactions",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    CheckoutSessionId = table.Column<Guid>(type: "uuid", nullable: false),
                    CampaignId = table.Column<Guid>(type: "uuid", nullable: false),
                    PaymentProvider = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false),
                    ProviderTransactionId = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false),
                    ProviderPaymentReference = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: true),
                    Amount = table.Column<decimal>(type: "numeric(18,3)", precision: 18, scale: 3, nullable: false),
                    Currency = table.Column<string>(type: "character varying(3)", maxLength: 3, nullable: false),
                    Status = table.Column<int>(type: "integer", nullable: false),
                    InitiatedAtUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    CompletedAtUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    FailedAtUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    RefundedAtUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    ProcessingLeaseExpiresAtUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_CampaignPaymentTransactions", x => x.Id);
                });

            migrationBuilder.Sql(@"
DO $$
BEGIN
    IF NOT EXISTS (SELECT 1 FROM information_schema.tables WHERE table_name = 'TripStops') THEN
        CREATE TABLE ""TripStops"" (
            ""Id"" uuid NOT NULL,
            ""TripId"" uuid NOT NULL,
            ""DailyOrderId"" uuid NULL,
            ""SubscriptionDayId"" uuid NULL,
            ""OrderCode"" character varying(100) NOT NULL,
            ""CustomerMaskedId"" character varying(100) NOT NULL,
            ""CustomerName"" character varying(200) NULL,
            ""CustomerPhone"" character varying(50) NULL,
            ""ZoneNameAr"" character varying(200) NOT NULL,
            ""ZoneNameEn"" character varying(200) NOT NULL,
            ""MealSummaryAr"" character varying(500) NOT NULL,
            ""MealSummaryEn"" character varying(500) NOT NULL,
            ""BoxCount"" integer NOT NULL,
            ""SlotLabel"" character varying(100) NOT NULL,
            ""SequenceNumber"" integer NOT NULL,
            ""Status"" integer NOT NULL,
            ""DeliveredAtUtc"" timestamp with time zone NULL,
            ""FailureReason"" character varying(1000) NULL,
            ""CreatedAtUtc"" timestamp with time zone NOT NULL,
            ""CreatedByUserId"" uuid NULL,
            ""ModifiedAtUtc"" timestamp with time zone NULL,
            ""ModifiedByUserId"" uuid NULL,
            ""IsDeleted"" boolean NOT NULL,
            ""DeletedAtUtc"" timestamp with time zone NULL,
            ""DeletedByUserId"" uuid NULL,
            CONSTRAINT ""PK_TripStops"" PRIMARY KEY (""Id""),
            CONSTRAINT ""FK_TripStops_Trips_TripId"" FOREIGN KEY (""TripId"") REFERENCES ""Trips"" (""Id"") ON DELETE CASCADE
        );
    END IF;
    IF NOT EXISTS (SELECT 1 FROM pg_indexes WHERE tablename = 'TripStops' AND indexname = 'IX_TripStops_DailyOrderId') THEN
        CREATE INDEX ""IX_TripStops_DailyOrderId"" ON ""TripStops"" (""DailyOrderId"");
    END IF;
    IF NOT EXISTS (SELECT 1 FROM pg_indexes WHERE tablename = 'TripStops' AND indexname = 'IX_TripStops_Status') THEN
        CREATE INDEX ""IX_TripStops_Status"" ON ""TripStops"" (""Status"");
    END IF;
    IF NOT EXISTS (SELECT 1 FROM pg_indexes WHERE tablename = 'TripStops' AND indexname = 'IX_TripStops_SubscriptionDayId') THEN
        CREATE INDEX ""IX_TripStops_SubscriptionDayId"" ON ""TripStops"" (""SubscriptionDayId"");
    END IF;
    IF NOT EXISTS (SELECT 1 FROM pg_indexes WHERE tablename = 'TripStops' AND indexname = 'IX_TripStops_TripId') THEN
        CREATE INDEX ""IX_TripStops_TripId"" ON ""TripStops"" (""TripId"");
    END IF;
END $$;
");

            migrationBuilder.CreateIndex(
                name: "IX_CampaignAuditLogs_Action",
                table: "CampaignAuditLogs",
                column: "Action");

            migrationBuilder.CreateIndex(
                name: "IX_CampaignAuditLogs_ActorId",
                table: "CampaignAuditLogs",
                column: "ActorId");

            migrationBuilder.CreateIndex(
                name: "IX_CampaignAuditLogs_CampaignId",
                table: "CampaignAuditLogs",
                column: "CampaignId");

            migrationBuilder.CreateIndex(
                name: "IX_CampaignAuditLogs_CreatedAtUtc",
                table: "CampaignAuditLogs",
                column: "CreatedAtUtc");

            migrationBuilder.CreateIndex(
                name: "IX_CampaignBundleDurationPrices_CampaignBundleId",
                table: "CampaignBundleDurationPrices",
                column: "CampaignBundleId");

            migrationBuilder.CreateIndex(
                name: "IX_CampaignBundleDurationPrices_CampaignBundleId_DurationDays",
                table: "CampaignBundleDurationPrices",
                columns: new[] { "CampaignBundleId", "DurationDays" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_CampaignFinancialLedgers_CampaignId",
                table: "CampaignFinancialLedgers",
                column: "CampaignId");

            migrationBuilder.CreateIndex(
                name: "IX_CampaignFinancialLedgers_CheckoutSessionId",
                table: "CampaignFinancialLedgers",
                column: "CheckoutSessionId");

            migrationBuilder.CreateIndex(
                name: "IX_CampaignFinancialLedgers_CreatedAtUtc",
                table: "CampaignFinancialLedgers",
                column: "CreatedAtUtc");

            migrationBuilder.CreateIndex(
                name: "IX_CampaignFinancialLedgers_EntryType",
                table: "CampaignFinancialLedgers",
                column: "EntryType");

            migrationBuilder.CreateIndex(
                name: "IX_CampaignFinancialLedgers_JournalId",
                table: "CampaignFinancialLedgers",
                column: "JournalId");

            migrationBuilder.CreateIndex(
                name: "IX_CampaignFinancialLedgers_RestaurantId",
                table: "CampaignFinancialLedgers",
                column: "RestaurantId");

            migrationBuilder.CreateIndex(
                name: "IX_CampaignPaymentTransactions_CampaignId",
                table: "CampaignPaymentTransactions",
                column: "CampaignId");

            migrationBuilder.CreateIndex(
                name: "IX_CampaignPaymentTransactions_CheckoutSessionId",
                table: "CampaignPaymentTransactions",
                column: "CheckoutSessionId");

            migrationBuilder.CreateIndex(
                name: "IX_CampaignPaymentTransactions_PaymentProvider_ProviderTransac~",
                table: "CampaignPaymentTransactions",
                columns: new[] { "PaymentProvider", "ProviderTransactionId" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_CampaignPaymentTransactions_ProcessingLeaseExpiresAtUtc",
                table: "CampaignPaymentTransactions",
                column: "ProcessingLeaseExpiresAtUtc");

            migrationBuilder.CreateIndex(
                name: "IX_CampaignPaymentTransactions_Status",
                table: "CampaignPaymentTransactions",
                column: "Status");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "CampaignAuditLogs");

            migrationBuilder.DropTable(
                name: "CampaignBundleDurationPrices");

            migrationBuilder.DropTable(
                name: "CampaignFinancialLedgers");

            migrationBuilder.DropTable(
                name: "CampaignPaymentTransactions");

            migrationBuilder.DropTable(
                name: "TripStops");

            migrationBuilder.DropIndex(
                name: "IX_Trips_Code",
                table: "Trips");

            migrationBuilder.DropIndex(
                name: "IX_IdempotencyRecords_Key",
                table: "IdempotencyRecords");

            migrationBuilder.DropIndex(
                name: "IX_IdempotencyRecords_Scope_Key",
                table: "IdempotencyRecords");

            migrationBuilder.DropColumn(
                name: "Code",
                table: "Trips");

            migrationBuilder.DropColumn(
                name: "ShiftName",
                table: "Trips");

            migrationBuilder.DropColumn(
                name: "ResourceUri",
                table: "IdempotencyRecords");

            migrationBuilder.DropColumn(
                name: "Scope",
                table: "IdempotencyRecords");

            migrationBuilder.CreateIndex(
                name: "IX_IdempotencyRecords_Key",
                table: "IdempotencyRecords",
                column: "Key",
                unique: true);
        }
    }
}
