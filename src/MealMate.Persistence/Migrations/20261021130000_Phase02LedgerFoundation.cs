using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace MealMate.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class Phase02LedgerFoundation : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "IX_ChartOfAccounts_Code",
                table: "ChartOfAccounts");

            migrationBuilder.AddColumn<Guid>(
                name: "AllocationId",
                table: "JournalEntryLines",
                type: "uuid",
                nullable: true);

            migrationBuilder.AddColumn<Guid>(
                name: "LegalEntityId",
                table: "JournalEntryLines",
                type: "uuid",
                nullable: true);

            migrationBuilder.AddColumn<Guid>(
                name: "PartyId",
                table: "JournalEntryLines",
                type: "uuid",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "PartyType",
                table: "JournalEntryLines",
                type: "character varying(50)",
                maxLength: 50,
                nullable: true);

            migrationBuilder.AddColumn<Guid>(
                name: "AccountingPeriodId",
                table: "JournalEntries",
                type: "uuid",
                nullable: true);

            migrationBuilder.AddColumn<DateOnly>(
                name: "BusinessDate",
                table: "JournalEntries",
                type: "date",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "CurrencyCode",
                table: "JournalEntries",
                type: "character varying(3)",
                maxLength: 3,
                nullable: true);

            migrationBuilder.AddColumn<Guid>(
                name: "LegalEntityId",
                table: "JournalEntries",
                type: "uuid",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "PayloadHash",
                table: "JournalEntries",
                type: "character varying(64)",
                maxLength: 64,
                nullable: true);

            migrationBuilder.AddColumn<DateTime>(
                name: "SourceOccurredAtUtc",
                table: "JournalEntries",
                type: "timestamp with time zone",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "FinancePayloadHash",
                table: "InboxProcessedEvents",
                type: "character varying(64)",
                maxLength: 64,
                nullable: true);

            migrationBuilder.AddColumn<Guid>(
                name: "ResultJournalEntryId",
                table: "InboxProcessedEvents",
                type: "uuid",
                nullable: true);

            migrationBuilder.AddColumn<DateOnly>(
                name: "EffectiveFrom",
                table: "ChartOfAccounts",
                type: "date",
                nullable: true);

            migrationBuilder.AddColumn<DateOnly>(
                name: "EffectiveToExclusive",
                table: "ChartOfAccounts",
                type: "date",
                nullable: true);

            migrationBuilder.AddColumn<bool>(
                name: "IsPostable",
                table: "ChartOfAccounts",
                type: "boolean",
                nullable: false,
                defaultValue: false);

            migrationBuilder.AddColumn<Guid>(
                name: "LegalEntityId",
                table: "ChartOfAccounts",
                type: "uuid",
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "NormalBalance",
                table: "ChartOfAccounts",
                type: "integer",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "ReportingLineCode",
                table: "ChartOfAccounts",
                type: "character varying(100)",
                maxLength: 100,
                nullable: true);

            migrationBuilder.AddColumn<bool>(
                name: "RequiresParty",
                table: "ChartOfAccounts",
                type: "boolean",
                nullable: false,
                defaultValue: false);

            migrationBuilder.CreateTable(
                name: "AccountingPeriods",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    LegalEntityId = table.Column<Guid>(type: "uuid", nullable: false),
                    StartDate = table.Column<DateOnly>(type: "date", nullable: false),
                    EndDateExclusive = table.Column<DateOnly>(type: "date", nullable: false),
                    State = table.Column<int>(type: "integer", nullable: false),
                    CloseVersion = table.Column<long>(type: "bigint", nullable: false),
                    PendingCloseAtUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    PendingCloseByUserId = table.Column<Guid>(type: "uuid", nullable: true),
                    PendingCloseReason = table.Column<string>(type: "character varying(1000)", maxLength: 1000, nullable: true),
                    ClosedAtUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    ClosedByUserId = table.Column<Guid>(type: "uuid", nullable: true),
                    CloseReason = table.Column<string>(type: "character varying(1000)", maxLength: 1000, nullable: true),
                    ClosePostedJournalCount = table.Column<long>(type: "bigint", nullable: true),
                    CloseDebitTotal = table.Column<decimal>(type: "numeric", nullable: true),
                    CloseCreditTotal = table.Column<decimal>(type: "numeric", nullable: true),
                    CloseWatermarkUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
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
                    table.PrimaryKey("PK_AccountingPeriods", x => x.Id);
                    table.CheckConstraint("CK_AccountingPeriods_CloseTotals", "(\"CloseDebitTotal\" IS NULL AND \"CloseCreditTotal\" IS NULL) OR (\"CloseDebitTotal\" >= 0 AND \"CloseDebitTotal\" = \"CloseCreditTotal\")");
                    table.CheckConstraint("CK_AccountingPeriods_DateWindow", "\"EndDateExclusive\" > \"StartDate\"");
                    table.ForeignKey(
                        name: "FK_AccountingPeriods_LegalEntities_LegalEntityId",
                        column: x => x.LegalEntityId,
                        principalTable: "LegalEntities",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "JournalSourceDocuments",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    DocumentNumber = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    Type = table.Column<int>(type: "integer", nullable: false),
                    Status = table.Column<int>(type: "integer", nullable: false),
                    LegalEntityId = table.Column<Guid>(type: "uuid", nullable: false),
                    BusinessDate = table.Column<DateOnly>(type: "date", nullable: false),
                    CurrencyCode = table.Column<string>(type: "character varying(3)", maxLength: 3, nullable: false),
                    Reason = table.Column<string>(type: "character varying(1000)", maxLength: 1000, nullable: false),
                    EvidenceReference = table.Column<string>(type: "character varying(500)", maxLength: 500, nullable: false),
                    ContentJson = table.Column<string>(type: "jsonb", nullable: false),
                    ContentFingerprint = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    SubmittedFingerprint = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: true),
                    CreatedByUserId = table.Column<Guid>(type: "uuid", nullable: false),
                    CreatedAtUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    SubmittedAtUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    SubmittedByUserId = table.Column<Guid>(type: "uuid", nullable: true),
                    ApprovedAtUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    ApprovedByUserId = table.Column<Guid>(type: "uuid", nullable: true),
                    RejectedAtUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    RejectedByUserId = table.Column<Guid>(type: "uuid", nullable: true),
                    RejectionReason = table.Column<string>(type: "character varying(1000)", maxLength: 1000, nullable: true),
                    OriginalJournalEntryId = table.Column<Guid>(type: "uuid", nullable: true),
                    RowVersion = table.Column<long>(type: "bigint", nullable: false),
                    PostedJournalEntryId = table.Column<Guid>(type: "uuid", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_JournalSourceDocuments", x => x.Id);
                    table.CheckConstraint("CK_JournalSourceDocuments_Currency", "\"CurrencyCode\" = 'KWD'");
                    table.CheckConstraint("CK_JournalSourceDocuments_Fingerprints", "length(\"ContentFingerprint\") = 64 AND (\"SubmittedFingerprint\" IS NULL OR length(\"SubmittedFingerprint\") = 64)");
                    table.ForeignKey(
                        name: "FK_JournalSourceDocuments_JournalEntries_OriginalJournalEntryId",
                        column: x => x.OriginalJournalEntryId,
                        principalTable: "JournalEntries",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_JournalSourceDocuments_JournalEntries_PostedJournalEntryId",
                        column: x => x.PostedJournalEntryId,
                        principalTable: "JournalEntries",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_JournalSourceDocuments_LegalEntities_LegalEntityId",
                        column: x => x.LegalEntityId,
                        principalTable: "LegalEntities",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "PostingIdentities",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    LegalEntityId = table.Column<Guid>(type: "uuid", nullable: false),
                    SourceType = table.Column<string>(type: "character varying(80)", maxLength: 80, nullable: false),
                    SourceId = table.Column<Guid>(type: "uuid", nullable: false),
                    ServiceDayKey = table.Column<Guid>(type: "uuid", nullable: false),
                    Effect = table.Column<int>(type: "integer", nullable: false),
                    AdjustmentKey = table.Column<Guid>(type: "uuid", nullable: false),
                    EconomicHash = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    JournalEntryId = table.Column<Guid>(type: "uuid", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_PostingIdentities", x => x.Id);
                    table.CheckConstraint("CK_PostingIdentities_Hash", "length(\"EconomicHash\") = 64");
                    table.ForeignKey(
                        name: "FK_PostingIdentities_JournalEntries_JournalEntryId",
                        column: x => x.JournalEntryId,
                        principalTable: "JournalEntries",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_PostingIdentities_LegalEntities_LegalEntityId",
                        column: x => x.LegalEntityId,
                        principalTable: "LegalEntities",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "JournalReversals",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    LegalEntityId = table.Column<Guid>(type: "uuid", nullable: false),
                    OriginalJournalEntryId = table.Column<Guid>(type: "uuid", nullable: false),
                    ReversalJournalEntryId = table.Column<Guid>(type: "uuid", nullable: false),
                    SourceDocumentId = table.Column<Guid>(type: "uuid", nullable: false),
                    Reason = table.Column<string>(type: "character varying(1000)", maxLength: 1000, nullable: false),
                    EvidenceReference = table.Column<string>(type: "character varying(500)", maxLength: 500, nullable: false),
                    OriginalBusinessDate = table.Column<DateOnly>(type: "date", nullable: false),
                    ReversalBusinessDate = table.Column<DateOnly>(type: "date", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_JournalReversals", x => x.Id);
                    table.ForeignKey(
                        name: "FK_JournalReversals_JournalEntries_OriginalJournalEntryId",
                        column: x => x.OriginalJournalEntryId,
                        principalTable: "JournalEntries",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_JournalReversals_JournalEntries_ReversalJournalEntryId",
                        column: x => x.ReversalJournalEntryId,
                        principalTable: "JournalEntries",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_JournalReversals_JournalSourceDocuments_SourceDocumentId",
                        column: x => x.SourceDocumentId,
                        principalTable: "JournalSourceDocuments",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_JournalReversals_LegalEntities_LegalEntityId",
                        column: x => x.LegalEntityId,
                        principalTable: "LegalEntities",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "JournalSourceLinks",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    LegalEntityId = table.Column<Guid>(type: "uuid", nullable: false),
                    SourceDocumentId = table.Column<Guid>(type: "uuid", nullable: false),
                    JournalEntryId = table.Column<Guid>(type: "uuid", nullable: false),
                    Type = table.Column<int>(type: "integer", nullable: false),
                    SourceFingerprint = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    LinkedAtUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_JournalSourceLinks", x => x.Id);
                    table.ForeignKey(
                        name: "FK_JournalSourceLinks_JournalEntries_JournalEntryId",
                        column: x => x.JournalEntryId,
                        principalTable: "JournalEntries",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_JournalSourceLinks_JournalSourceDocuments_SourceDocumentId",
                        column: x => x.SourceDocumentId,
                        principalTable: "JournalSourceDocuments",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_JournalSourceLinks_LegalEntities_LegalEntityId",
                        column: x => x.LegalEntityId,
                        principalTable: "LegalEntities",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateIndex(
                name: "IX_JournalEntryLines_LegalEntityId",
                table: "JournalEntryLines",
                column: "LegalEntityId");

            migrationBuilder.CreateIndex(
                name: "UX_JournalEntryLines_Entity_Id",
                table: "JournalEntryLines",
                columns: new[] { "LegalEntityId", "Id" },
                unique: true);

            migrationBuilder.AddCheckConstraint(
                name: "CK_JournalEntryLines_GovernedAmount",
                table: "JournalEntryLines",
                sql: "\"LegalEntityId\" IS NULL OR ((\"Debit_Amount\" > 0 AND \"Credit_Amount\" IS NULL) OR (\"Credit_Amount\" > 0 AND \"Debit_Amount\" IS NULL))");

            migrationBuilder.CreateIndex(
                name: "IX_JournalEntries_AccountingPeriodId",
                table: "JournalEntries",
                column: "AccountingPeriodId");

            migrationBuilder.CreateIndex(
                name: "IX_JournalEntries_LegalEntityId",
                table: "JournalEntries",
                column: "LegalEntityId");

            migrationBuilder.CreateIndex(
                name: "IX_JournalEntries_LegalEntityId_AccountingPeriodId_BusinessDat~",
                table: "JournalEntries",
                columns: new[] { "LegalEntityId", "AccountingPeriodId", "BusinessDate", "Status" });

            migrationBuilder.CreateIndex(
                name: "UX_JournalEntries_Entity_Id",
                table: "JournalEntries",
                columns: new[] { "LegalEntityId", "Id" },
                unique: true);

            migrationBuilder.AddCheckConstraint(
                name: "CK_JournalEntries_GovernedFields",
                table: "JournalEntries",
                sql: "\"LegalEntityId\" IS NULL OR (\"AccountingPeriodId\" IS NOT NULL AND \"BusinessDate\" IS NOT NULL AND \"CurrencyCode\" = 'KWD' AND \"PayloadHash\" IS NOT NULL)");

            migrationBuilder.CreateIndex(
                name: "IX_InboxProcessedEvents_ResultJournalEntryId",
                table: "InboxProcessedEvents",
                column: "ResultJournalEntryId");

            migrationBuilder.CreateIndex(
                name: "IX_ChartOfAccounts_LegacyCode",
                table: "ChartOfAccounts",
                column: "Code",
                unique: true,
                filter: "\"LegalEntityId\" IS NULL");

            migrationBuilder.CreateIndex(
                name: "IX_ChartOfAccounts_LegalEntity_Code",
                table: "ChartOfAccounts",
                columns: new[] { "LegalEntityId", "Code" },
                unique: true,
                filter: "\"LegalEntityId\" IS NOT NULL");

            migrationBuilder.CreateIndex(
                name: "UX_ChartOfAccounts_Entity_Id",
                table: "ChartOfAccounts",
                columns: new[] { "LegalEntityId", "Id" },
                unique: true);

            migrationBuilder.AddCheckConstraint(
                name: "CK_ChartOfAccounts_EffectiveWindow",
                table: "ChartOfAccounts",
                sql: "\"EffectiveToExclusive\" IS NULL OR \"EffectiveFrom\" IS NULL OR \"EffectiveToExclusive\" > \"EffectiveFrom\"");

            migrationBuilder.AddCheckConstraint(
                name: "CK_ChartOfAccounts_GovernedNormalBalance",
                table: "ChartOfAccounts",
                sql: "\"LegalEntityId\" IS NULL OR \"NormalBalance\" IS NOT NULL");

            migrationBuilder.CreateIndex(
                name: "IX_AccountingPeriods_LegalEntityId_StartDate_EndDateExclusive",
                table: "AccountingPeriods",
                columns: new[] { "LegalEntityId", "StartDate", "EndDateExclusive" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_AccountingPeriods_LegalEntityId_State_StartDate",
                table: "AccountingPeriods",
                columns: new[] { "LegalEntityId", "State", "StartDate" });

            migrationBuilder.CreateIndex(
                name: "IX_JournalReversals_LegalEntityId",
                table: "JournalReversals",
                column: "LegalEntityId");

            migrationBuilder.CreateIndex(
                name: "IX_JournalReversals_OriginalJournalEntryId",
                table: "JournalReversals",
                column: "OriginalJournalEntryId",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_JournalReversals_ReversalJournalEntryId",
                table: "JournalReversals",
                column: "ReversalJournalEntryId",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_JournalReversals_SourceDocumentId",
                table: "JournalReversals",
                column: "SourceDocumentId",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_JournalSourceDocuments_DocumentNumber",
                table: "JournalSourceDocuments",
                column: "DocumentNumber",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_JournalSourceDocuments_LegalEntityId_Status_BusinessDate",
                table: "JournalSourceDocuments",
                columns: new[] { "LegalEntityId", "Status", "BusinessDate" });

            migrationBuilder.CreateIndex(
                name: "IX_JournalSourceDocuments_OriginalJournalEntryId",
                table: "JournalSourceDocuments",
                column: "OriginalJournalEntryId");

            migrationBuilder.CreateIndex(
                name: "IX_JournalSourceDocuments_PostedJournalEntryId",
                table: "JournalSourceDocuments",
                column: "PostedJournalEntryId",
                unique: true,
                filter: "\"PostedJournalEntryId\" IS NOT NULL");

            migrationBuilder.CreateIndex(
                name: "IX_JournalSourceLinks_JournalEntryId",
                table: "JournalSourceLinks",
                column: "JournalEntryId",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_JournalSourceLinks_LegalEntityId",
                table: "JournalSourceLinks",
                column: "LegalEntityId");

            migrationBuilder.CreateIndex(
                name: "IX_JournalSourceLinks_SourceDocumentId",
                table: "JournalSourceLinks",
                column: "SourceDocumentId",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_PostingIdentities_JournalEntryId",
                table: "PostingIdentities",
                column: "JournalEntryId",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "UX_PostingIdentities_EconomicKey",
                table: "PostingIdentities",
                columns: new[] { "LegalEntityId", "SourceType", "SourceId", "ServiceDayKey", "Effect", "AdjustmentKey" },
                unique: true);

            migrationBuilder.AddForeignKey(
                name: "FK_ChartOfAccounts_LegalEntities_LegalEntityId",
                table: "ChartOfAccounts",
                column: "LegalEntityId",
                principalTable: "LegalEntities",
                principalColumn: "Id",
                onDelete: ReferentialAction.Restrict);

            migrationBuilder.AddForeignKey(
                name: "FK_InboxProcessedEvents_JournalEntries_ResultJournalEntryId",
                table: "InboxProcessedEvents",
                column: "ResultJournalEntryId",
                principalTable: "JournalEntries",
                principalColumn: "Id",
                onDelete: ReferentialAction.Restrict);

            migrationBuilder.AddForeignKey(
                name: "FK_JournalEntries_AccountingPeriods_AccountingPeriodId",
                table: "JournalEntries",
                column: "AccountingPeriodId",
                principalTable: "AccountingPeriods",
                principalColumn: "Id",
                onDelete: ReferentialAction.Restrict);

            migrationBuilder.AddForeignKey(
                name: "FK_JournalEntries_LegalEntities_LegalEntityId",
                table: "JournalEntries",
                column: "LegalEntityId",
                principalTable: "LegalEntities",
                principalColumn: "Id",
                onDelete: ReferentialAction.Restrict);

            migrationBuilder.AddForeignKey(
                name: "FK_JournalEntryLines_LegalEntities_LegalEntityId",
                table: "JournalEntryLines",
                column: "LegalEntityId",
                principalTable: "LegalEntities",
                principalColumn: "Id",
                onDelete: ReferentialAction.Restrict);

            migrationBuilder.Sql(LedgerGuardsSql);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql("""
                DO $$
                BEGIN
                    IF EXISTS (SELECT 1 FROM "JournalEntries" WHERE "LegalEntityId" IS NOT NULL)
                       OR EXISTS (SELECT 1 FROM "ChartOfAccounts" WHERE "LegalEntityId" IS NOT NULL)
                       OR EXISTS (SELECT 1 FROM "AccountingPeriods")
                       OR EXISTS (SELECT 1 FROM "JournalSourceDocuments")
                       OR EXISTS (SELECT 1 FROM "JournalSourceLinks")
                       OR EXISTS (SELECT 1 FROM "PostingIdentities")
                       OR EXISTS (SELECT 1 FROM "JournalReversals")
                       OR EXISTS (SELECT 1 FROM "InboxProcessedEvents" WHERE "FinancePayloadHash" IS NOT NULL OR "ResultJournalEntryId" IS NOT NULL)
                    THEN
                        RAISE EXCEPTION 'ledger.migration_down_refused_data_exists';
                    END IF;
                END $$;
                DROP TRIGGER IF EXISTS "TR_JournalEntryLines_LedgerGuard" ON "JournalEntryLines";
                DROP TRIGGER IF EXISTS "TR_JournalEntries_LedgerGuard" ON "JournalEntries";
                DROP TRIGGER IF EXISTS "TR_JournalEntries_LedgerBalance" ON "JournalEntries";
                DROP TRIGGER IF EXISTS "TR_JournalEntryLines_LedgerBalance" ON "JournalEntryLines";
                DROP TRIGGER IF EXISTS "TR_JournalSourceDocuments_LedgerGuard" ON "JournalSourceDocuments";
                DROP TRIGGER IF EXISTS "TR_AccountingPeriods_LedgerGuard" ON "AccountingPeriods";
                DROP TRIGGER IF EXISTS "TR_ChartOfAccounts_LedgerGuard" ON "ChartOfAccounts";
                DROP TRIGGER IF EXISTS "TR_JournalSourceLinks_AppendOnly" ON "JournalSourceLinks";
                DROP TRIGGER IF EXISTS "TR_PostingIdentities_AppendOnly" ON "PostingIdentities";
                DROP TRIGGER IF EXISTS "TR_JournalReversals_AppendOnly" ON "JournalReversals";
                DROP TRIGGER IF EXISTS "TR_LedgerAudit_AppendOnly" ON "FinancialAuditTrails";
                DROP FUNCTION IF EXISTS mealmate_ledger_guard_journal();
                DROP FUNCTION IF EXISTS mealmate_ledger_guard_line();
                DROP FUNCTION IF EXISTS mealmate_ledger_deferred_balance();
                DROP FUNCTION IF EXISTS mealmate_ledger_guard_source();
                DROP FUNCTION IF EXISTS mealmate_ledger_guard_period();
                DROP FUNCTION IF EXISTS mealmate_ledger_guard_account();
                DROP FUNCTION IF EXISTS mealmate_ledger_guard_trace();
                DROP FUNCTION IF EXISTS mealmate_ledger_guard_audit();
                """);

            migrationBuilder.Sql("""
                ALTER TABLE "JournalEntries" DROP CONSTRAINT IF EXISTS "FK_JournalEntries_Entity_Period";
                ALTER TABLE "JournalEntryLines" DROP CONSTRAINT IF EXISTS "FK_JournalEntryLines_Entity_Journal";
                ALTER TABLE "JournalEntryLines" DROP CONSTRAINT IF EXISTS "FK_JournalEntryLines_Entity_Account";
                ALTER TABLE "JournalSourceDocuments" DROP CONSTRAINT IF EXISTS "FK_JournalSourceDocuments_Entity_OriginalJournal";
                ALTER TABLE "JournalSourceDocuments" DROP CONSTRAINT IF EXISTS "FK_JournalSourceDocuments_Entity_PostedJournal";
                ALTER TABLE "JournalSourceLinks" DROP CONSTRAINT IF EXISTS "FK_JournalSourceLinks_Entity_Source";
                ALTER TABLE "JournalSourceLinks" DROP CONSTRAINT IF EXISTS "FK_JournalSourceLinks_Entity_Journal";
                ALTER TABLE "JournalReversals" DROP CONSTRAINT IF EXISTS "FK_JournalReversals_Entity_OriginalJournal";
                ALTER TABLE "JournalReversals" DROP CONSTRAINT IF EXISTS "FK_JournalReversals_Entity_ReversalJournal";
                ALTER TABLE "JournalReversals" DROP CONSTRAINT IF EXISTS "FK_JournalReversals_Entity_Source";
                DROP INDEX IF EXISTS "UX_AccountingPeriods_Entity_Id";
                DROP INDEX IF EXISTS "UX_JournalSourceDocuments_Entity_Id";
                """);

            migrationBuilder.DropForeignKey(
                name: "FK_ChartOfAccounts_LegalEntities_LegalEntityId",
                table: "ChartOfAccounts");

            migrationBuilder.DropForeignKey(
                name: "FK_InboxProcessedEvents_JournalEntries_ResultJournalEntryId",
                table: "InboxProcessedEvents");

            migrationBuilder.DropForeignKey(
                name: "FK_JournalEntries_AccountingPeriods_AccountingPeriodId",
                table: "JournalEntries");

            migrationBuilder.DropForeignKey(
                name: "FK_JournalEntries_LegalEntities_LegalEntityId",
                table: "JournalEntries");

            migrationBuilder.DropForeignKey(
                name: "FK_JournalEntryLines_LegalEntities_LegalEntityId",
                table: "JournalEntryLines");

            migrationBuilder.DropTable(
                name: "AccountingPeriods");

            migrationBuilder.DropTable(
                name: "JournalReversals");

            migrationBuilder.DropTable(
                name: "JournalSourceLinks");

            migrationBuilder.DropTable(
                name: "PostingIdentities");

            migrationBuilder.DropTable(
                name: "JournalSourceDocuments");

            migrationBuilder.DropIndex(
                name: "IX_JournalEntryLines_LegalEntityId",
                table: "JournalEntryLines");

            migrationBuilder.DropIndex(
                name: "UX_JournalEntryLines_Entity_Id",
                table: "JournalEntryLines");

            migrationBuilder.DropCheckConstraint(
                name: "CK_JournalEntryLines_GovernedAmount",
                table: "JournalEntryLines");

            migrationBuilder.DropIndex(
                name: "IX_JournalEntries_AccountingPeriodId",
                table: "JournalEntries");

            migrationBuilder.DropIndex(
                name: "IX_JournalEntries_LegalEntityId",
                table: "JournalEntries");

            migrationBuilder.DropIndex(
                name: "IX_JournalEntries_LegalEntityId_AccountingPeriodId_BusinessDat~",
                table: "JournalEntries");

            migrationBuilder.DropIndex(
                name: "UX_JournalEntries_Entity_Id",
                table: "JournalEntries");

            migrationBuilder.DropCheckConstraint(
                name: "CK_JournalEntries_GovernedFields",
                table: "JournalEntries");

            migrationBuilder.DropIndex(
                name: "IX_InboxProcessedEvents_ResultJournalEntryId",
                table: "InboxProcessedEvents");

            migrationBuilder.DropIndex(
                name: "IX_ChartOfAccounts_LegacyCode",
                table: "ChartOfAccounts");

            migrationBuilder.DropIndex(
                name: "IX_ChartOfAccounts_LegalEntity_Code",
                table: "ChartOfAccounts");

            migrationBuilder.DropIndex(
                name: "UX_ChartOfAccounts_Entity_Id",
                table: "ChartOfAccounts");

            migrationBuilder.DropCheckConstraint(
                name: "CK_ChartOfAccounts_EffectiveWindow",
                table: "ChartOfAccounts");

            migrationBuilder.DropCheckConstraint(
                name: "CK_ChartOfAccounts_GovernedNormalBalance",
                table: "ChartOfAccounts");

            migrationBuilder.DropColumn(
                name: "AllocationId",
                table: "JournalEntryLines");

            migrationBuilder.DropColumn(
                name: "LegalEntityId",
                table: "JournalEntryLines");

            migrationBuilder.DropColumn(
                name: "PartyId",
                table: "JournalEntryLines");

            migrationBuilder.DropColumn(
                name: "PartyType",
                table: "JournalEntryLines");

            migrationBuilder.DropColumn(
                name: "AccountingPeriodId",
                table: "JournalEntries");

            migrationBuilder.DropColumn(
                name: "BusinessDate",
                table: "JournalEntries");

            migrationBuilder.DropColumn(
                name: "CurrencyCode",
                table: "JournalEntries");

            migrationBuilder.DropColumn(
                name: "LegalEntityId",
                table: "JournalEntries");

            migrationBuilder.DropColumn(
                name: "PayloadHash",
                table: "JournalEntries");

            migrationBuilder.DropColumn(
                name: "SourceOccurredAtUtc",
                table: "JournalEntries");

            migrationBuilder.DropColumn(
                name: "FinancePayloadHash",
                table: "InboxProcessedEvents");

            migrationBuilder.DropColumn(
                name: "ResultJournalEntryId",
                table: "InboxProcessedEvents");

            migrationBuilder.DropColumn(
                name: "EffectiveFrom",
                table: "ChartOfAccounts");

            migrationBuilder.DropColumn(
                name: "EffectiveToExclusive",
                table: "ChartOfAccounts");

            migrationBuilder.DropColumn(
                name: "IsPostable",
                table: "ChartOfAccounts");

            migrationBuilder.DropColumn(
                name: "LegalEntityId",
                table: "ChartOfAccounts");

            migrationBuilder.DropColumn(
                name: "NormalBalance",
                table: "ChartOfAccounts");

            migrationBuilder.DropColumn(
                name: "ReportingLineCode",
                table: "ChartOfAccounts");

            migrationBuilder.DropColumn(
                name: "RequiresParty",
                table: "ChartOfAccounts");

            migrationBuilder.CreateIndex(
                name: "IX_ChartOfAccounts_Code",
                table: "ChartOfAccounts",
                column: "Code",
                unique: true);
        }

        private const string LedgerGuardsSql = """
            CREATE OR REPLACE FUNCTION mealmate_ledger_guard_journal() RETURNS trigger LANGUAGE plpgsql AS $$
            BEGIN
                IF TG_OP = 'DELETE' THEN
                    IF OLD."LegalEntityId" IS NOT NULL THEN RAISE EXCEPTION 'ledger.governed_journal_delete_forbidden'; END IF;
                    RETURN OLD;
                END IF;
                IF TG_OP = 'INSERT' AND NEW."LegalEntityId" IS NOT NULL AND NEW."Status" <> 1 THEN
                    RAISE EXCEPTION 'ledger.governed_journal_must_start_draft';
                END IF;
                IF TG_OP = 'UPDATE' AND OLD."LegalEntityId" IS NOT NULL THEN
                    IF OLD."Status" = 2 THEN RAISE EXCEPTION 'ledger.posted_journal_immutable'; END IF;
                    IF OLD."LegalEntityId" IS DISTINCT FROM NEW."LegalEntityId" OR OLD."AccountingPeriodId" IS DISTINCT FROM NEW."AccountingPeriodId"
                       OR OLD."BusinessDate" IS DISTINCT FROM NEW."BusinessDate" OR OLD."CurrencyCode" IS DISTINCT FROM NEW."CurrencyCode"
                       OR OLD."PayloadHash" IS DISTINCT FROM NEW."PayloadHash" THEN
                        RAISE EXCEPTION 'ledger.governed_journal_identity_immutable';
                    END IF;
                    IF NEW."Status" NOT IN (1, 2) THEN RAISE EXCEPTION 'ledger.governed_journal_status_invalid'; END IF;
                    IF OLD."Status" = 1 AND NEW."Status" = 2 THEN
                        PERFORM pg_advisory_xact_lock(hashtextextended('ledger-period:' || replace(NEW."AccountingPeriodId"::text, '-', ''), 0));
                        IF NOT EXISTS (SELECT 1 FROM "AccountingPeriods" p WHERE p."Id" = NEW."AccountingPeriodId" AND p."LegalEntityId" = NEW."LegalEntityId"
                            AND p."State" = 1 AND NEW."BusinessDate" >= p."StartDate" AND NEW."BusinessDate" < p."EndDateExclusive")
                        THEN RAISE EXCEPTION 'ledger.period_not_open'; END IF;
                    END IF;
                END IF;
                RETURN NEW;
            END $$;
            CREATE TRIGGER "TR_JournalEntries_LedgerGuard" BEFORE INSERT OR UPDATE OR DELETE ON "JournalEntries"
                FOR EACH ROW EXECUTE FUNCTION mealmate_ledger_guard_journal();

            CREATE OR REPLACE FUNCTION mealmate_ledger_guard_line() RETURNS trigger LANGUAGE plpgsql AS $$
            DECLARE header_entity uuid; header_status integer;
            BEGIN
                IF TG_OP = 'DELETE' THEN
                    SELECT "LegalEntityId", "Status" INTO header_entity, header_status FROM "JournalEntries" WHERE "Id" = OLD."JournalEntryId";
                    IF OLD."LegalEntityId" IS NOT NULL OR header_entity IS NOT NULL THEN RAISE EXCEPTION 'ledger.journal_line_append_only'; END IF;
                    RETURN OLD;
                END IF;
                SELECT "LegalEntityId", "Status" INTO header_entity, header_status FROM "JournalEntries" WHERE "Id" = NEW."JournalEntryId";
                IF header_entity IS NOT NULL OR NEW."LegalEntityId" IS NOT NULL THEN
                    IF NEW."LegalEntityId" IS DISTINCT FROM header_entity THEN RAISE EXCEPTION 'ledger.cross_entity_journal_line'; END IF;
                    IF header_status <> 1 THEN RAISE EXCEPTION 'ledger.journal_line_append_only'; END IF;
                END IF;
                IF TG_OP = 'UPDATE' AND (OLD."LegalEntityId" IS NOT NULL OR NEW."LegalEntityId" IS NOT NULL) THEN
                    RAISE EXCEPTION 'ledger.journal_line_append_only';
                END IF;
                RETURN NEW;
            END $$;
            CREATE TRIGGER "TR_JournalEntryLines_LedgerGuard" BEFORE INSERT OR UPDATE OR DELETE ON "JournalEntryLines"
                FOR EACH ROW EXECUTE FUNCTION mealmate_ledger_guard_line();

            CREATE OR REPLACE FUNCTION mealmate_ledger_deferred_balance() RETURNS trigger LANGUAGE plpgsql AS $$
            DECLARE journal_id uuid; entity_id uuid; journal_status integer; period_id uuid; business_date date; line_count bigint; debit_total numeric; credit_total numeric; bad_line boolean;
            BEGIN
                IF TG_TABLE_NAME = 'JournalEntries' THEN
                    IF TG_OP = 'DELETE' THEN journal_id := OLD."Id"; ELSE journal_id := NEW."Id"; END IF;
                ELSE
                    IF TG_OP = 'DELETE' THEN journal_id := OLD."JournalEntryId"; ELSE journal_id := NEW."JournalEntryId"; END IF;
                END IF;
                SELECT "LegalEntityId", "Status", "AccountingPeriodId", "BusinessDate" INTO entity_id, journal_status, period_id, business_date FROM "JournalEntries" WHERE "Id" = journal_id;
                IF entity_id IS NULL OR journal_status <> 2 THEN RETURN NULL; END IF;
                IF TG_TABLE_NAME = 'JournalEntries' THEN
                  IF TG_OP = 'UPDATE' AND OLD."Status" = 1 AND NEW."Status" = 2 THEN
                    PERFORM pg_advisory_xact_lock(hashtextextended('ledger-period:' || replace(period_id::text, '-', ''), 0));
                    IF NOT EXISTS (SELECT 1 FROM "AccountingPeriods" p WHERE p."Id" = period_id AND p."LegalEntityId" = entity_id
                        AND p."State" = 1 AND business_date >= p."StartDate" AND business_date < p."EndDateExclusive")
                    THEN RAISE EXCEPTION 'ledger.period_not_open'; END IF;
                  END IF;
                END IF;
                SELECT count(*), COALESCE(sum("Debit_Amount"), 0), COALESCE(sum("Credit_Amount"), 0),
                       bool_or("LegalEntityId" IS DISTINCT FROM entity_id OR ("Debit_Amount" IS NOT NULL AND "Credit_Amount" IS NOT NULL)
                           OR ("Debit_Amount" IS NULL AND "Credit_Amount" IS NULL)
                           OR COALESCE("Debit_Amount", "Credit_Amount") <= 0
                           OR COALESCE("Debit_CurrencyCode", "Credit_CurrencyCode") <> 'KWD'
                           OR NOT EXISTS (SELECT 1 FROM "ChartOfAccounts" a WHERE a."Id" = "JournalEntryLines"."AccountId"
                               AND a."LegalEntityId" = entity_id AND a."IsActive" AND a."IsPostable"
                               AND (a."EffectiveFrom" IS NULL OR business_date >= a."EffectiveFrom")
                               AND (a."EffectiveToExclusive" IS NULL OR business_date < a."EffectiveToExclusive")
                               AND NOT EXISTS (SELECT 1 FROM "ChartOfAccounts" child WHERE child."ParentId" = a."Id")
                               AND (NOT a."RequiresParty" OR "JournalEntryLines"."PartyId" IS NOT NULL)))
                  INTO line_count, debit_total, credit_total, bad_line
                  FROM "JournalEntryLines" WHERE "JournalEntryId" = journal_id;
                IF line_count < 2 OR debit_total <> credit_total OR COALESCE(bad_line, true) THEN
                    RAISE EXCEPTION 'ledger.posted_journal_not_balanced';
                END IF;
                RETURN NULL;
            END $$;
            CREATE CONSTRAINT TRIGGER "TR_JournalEntries_LedgerBalance" AFTER INSERT OR UPDATE ON "JournalEntries"
                DEFERRABLE INITIALLY DEFERRED FOR EACH ROW EXECUTE FUNCTION mealmate_ledger_deferred_balance();
            CREATE CONSTRAINT TRIGGER "TR_JournalEntryLines_LedgerBalance" AFTER INSERT OR UPDATE ON "JournalEntryLines"
                DEFERRABLE INITIALLY DEFERRED FOR EACH ROW EXECUTE FUNCTION mealmate_ledger_deferred_balance();

            CREATE OR REPLACE FUNCTION mealmate_ledger_guard_source() RETURNS trigger LANGUAGE plpgsql AS $$
            BEGIN
                IF TG_OP = 'DELETE' THEN
                IF OLD."Status" <> 1 THEN RAISE EXCEPTION 'ledger.submitted_source_delete_forbidden'; END IF;
                    RETURN OLD;
                END IF;
                IF TG_OP = 'INSERT' THEN
                    IF NEW."Status" <> 1 THEN RAISE EXCEPTION 'ledger.source_must_start_draft'; END IF;
                    IF NEW."Type" = 3 AND NEW."OriginalJournalEntryId" IS NOT NULL THEN
                        PERFORM pg_advisory_xact_lock(hashtextextended('ledger-reversal:' || replace(NEW."OriginalJournalEntryId"::text, '-', ''), 0));
                        IF EXISTS (SELECT 1 FROM "JournalSourceDocuments" d WHERE d."OriginalJournalEntryId" = NEW."OriginalJournalEntryId" AND d."Status" <> 4)
                        THEN RAISE EXCEPTION 'ledger.original_already_reversed'; END IF;
                    END IF;
                    RETURN NEW;
                END IF;
                IF OLD."Status" <> 1 AND (OLD."Type", OLD."LegalEntityId", OLD."BusinessDate", OLD."CurrencyCode", OLD."Reason",
                    OLD."EvidenceReference", OLD."ContentJson", OLD."ContentFingerprint", OLD."CreatedByUserId", OLD."CreatedAtUtc",
                    OLD."OriginalJournalEntryId", OLD."SubmittedFingerprint") IS DISTINCT FROM
                    (NEW."Type", NEW."LegalEntityId", NEW."BusinessDate", NEW."CurrencyCode", NEW."Reason", NEW."EvidenceReference",
                    NEW."ContentJson", NEW."ContentFingerprint", NEW."CreatedByUserId", NEW."CreatedAtUtc", NEW."OriginalJournalEntryId", NEW."SubmittedFingerprint")
                THEN RAISE EXCEPTION 'ledger.submitted_source_immutable'; END IF;
                IF OLD."Status" <> NEW."Status" AND NOT ((OLD."Status" = 1 AND NEW."Status" = 2) OR (OLD."Status" = 2 AND NEW."Status" IN (3,4)) OR (OLD."Status" = 3 AND NEW."Status" = 5))
                THEN RAISE EXCEPTION 'ledger.source_lifecycle_invalid'; END IF;
                IF OLD."Status" = 1 AND NEW."Status" = 2 AND (NEW."SubmittedFingerprint" IS DISTINCT FROM NEW."ContentFingerprint"
                    OR NEW."SubmittedByUserId" IS DISTINCT FROM NEW."CreatedByUserId" OR NEW."SubmittedAtUtc" IS NULL)
                THEN RAISE EXCEPTION 'ledger.source_submit_invalid'; END IF;
                IF OLD."Status" = 2 AND NEW."Status" = 3 AND (NEW."ApprovedByUserId" IS NULL OR NEW."ApprovedAtUtc" IS NULL
                    OR NEW."ApprovedByUserId" = NEW."CreatedByUserId" OR NEW."ApprovedByUserId" = NEW."SubmittedByUserId")
                THEN RAISE EXCEPTION 'ledger.source_approval_invalid'; END IF;
                IF OLD."Status" >= 3 AND (OLD."ApprovedByUserId", OLD."ApprovedAtUtc", OLD."SubmittedByUserId", OLD."SubmittedAtUtc",
                    OLD."SubmittedFingerprint") IS DISTINCT FROM (NEW."ApprovedByUserId", NEW."ApprovedAtUtc", NEW."SubmittedByUserId",
                    NEW."SubmittedAtUtc", NEW."SubmittedFingerprint")
                THEN RAISE EXCEPTION 'ledger.source_approval_immutable'; END IF;
                IF NEW."ApprovedByUserId" IS NOT NULL AND NEW."ApprovedByUserId" = NEW."CreatedByUserId"
                THEN RAISE EXCEPTION 'ledger.maker_checker_required'; END IF;
                RETURN NEW;
            END $$;
            CREATE TRIGGER "TR_JournalSourceDocuments_LedgerGuard" BEFORE INSERT OR UPDATE OR DELETE ON "JournalSourceDocuments"
                FOR EACH ROW EXECUTE FUNCTION mealmate_ledger_guard_source();

            CREATE OR REPLACE FUNCTION mealmate_ledger_guard_period() RETURNS trigger LANGUAGE plpgsql AS $$
            BEGIN
                IF TG_OP = 'DELETE' THEN RAISE EXCEPTION 'ledger.period_delete_forbidden'; END IF;
                IF TG_OP = 'INSERT' THEN
                    PERFORM pg_advisory_xact_lock(hashtextextended('ledger-period-catalog:' || replace(NEW."LegalEntityId"::text, '-', ''), 0));
                ELSE
                    IF OLD."LegalEntityId" IS DISTINCT FROM NEW."LegalEntityId" THEN RAISE EXCEPTION 'ledger.period_entity_immutable'; END IF;
                    IF OLD."State" IS DISTINCT FROM NEW."State" THEN
                        PERFORM pg_advisory_xact_lock(hashtextextended('ledger-period:' || replace(OLD."Id"::text, '-', ''), 0));
                    END IF;
                    IF OLD."StartDate" IS DISTINCT FROM NEW."StartDate" OR OLD."EndDateExclusive" IS DISTINCT FROM NEW."EndDateExclusive" THEN
                        PERFORM pg_advisory_xact_lock(hashtextextended('ledger-period-catalog:' || replace(OLD."LegalEntityId"::text, '-', ''), 0));
                    END IF;
                END IF;
                IF TG_OP = 'UPDATE' THEN
                    IF OLD."LegalEntityId" IS DISTINCT FROM NEW."LegalEntityId" OR OLD."StartDate" IS DISTINCT FROM NEW."StartDate"
                        OR OLD."EndDateExclusive" IS DISTINCT FROM NEW."EndDateExclusive"
                    THEN RAISE EXCEPTION 'ledger.period_window_immutable'; END IF;
                    IF NEW."IsDeleted" THEN RAISE EXCEPTION 'ledger.period_soft_delete_forbidden'; END IF;
                    IF OLD."State" IS DISTINCT FROM NEW."State" AND NOT ((OLD."State" = 1 AND NEW."State" = 2)
                        OR (OLD."State" = 2 AND NEW."State" IN (1,3)))
                    THEN RAISE EXCEPTION 'ledger.period_lifecycle_invalid'; END IF;
                    IF OLD."State" = 3 AND (OLD."LegalEntityId", OLD."StartDate", OLD."EndDateExclusive", OLD."State", OLD."CloseVersion", OLD."ClosedAtUtc",
                        OLD."ClosedByUserId", OLD."CloseReason", OLD."ClosePostedJournalCount", OLD."CloseDebitTotal", OLD."CloseCreditTotal", OLD."CloseWatermarkUtc", OLD."IsDeleted") IS DISTINCT FROM
                        (NEW."LegalEntityId", NEW."StartDate", NEW."EndDateExclusive", NEW."State", NEW."CloseVersion", NEW."ClosedAtUtc", NEW."ClosedByUserId",
                        NEW."CloseReason", NEW."ClosePostedJournalCount", NEW."CloseDebitTotal", NEW."CloseCreditTotal", NEW."CloseWatermarkUtc", NEW."IsDeleted")
                    THEN RAISE EXCEPTION 'ledger.closed_period_immutable'; END IF;
                END IF;
                IF TG_OP = 'INSERT' OR (TG_OP = 'UPDATE' AND (OLD."StartDate" IS DISTINCT FROM NEW."StartDate" OR OLD."EndDateExclusive" IS DISTINCT FROM NEW."EndDateExclusive")) THEN
                    IF EXISTS (SELECT 1 FROM "AccountingPeriods" p WHERE p."LegalEntityId" = NEW."LegalEntityId" AND p."Id" <> NEW."Id"
                        AND p."StartDate" < NEW."EndDateExclusive" AND NEW."StartDate" < p."EndDateExclusive")
                    THEN RAISE EXCEPTION 'ledger.period_overlap'; END IF;
                END IF;
                RETURN NEW;
            END $$;
            CREATE TRIGGER "TR_AccountingPeriods_LedgerGuard" BEFORE INSERT OR UPDATE OR DELETE ON "AccountingPeriods"
                FOR EACH ROW EXECUTE FUNCTION mealmate_ledger_guard_period();

            CREATE OR REPLACE FUNCTION mealmate_ledger_guard_account() RETURNS trigger LANGUAGE plpgsql AS $$
            BEGIN
                IF TG_OP = 'DELETE' THEN
                    IF OLD."LegalEntityId" IS NOT NULL THEN RAISE EXCEPTION 'ledger.governed_account_delete_forbidden'; END IF;
                    RETURN OLD;
                END IF;
                IF TG_OP = 'UPDATE' AND OLD."LegalEntityId" IS NOT NULL AND OLD."LegalEntityId" IS DISTINCT FROM NEW."LegalEntityId"
                THEN RAISE EXCEPTION 'ledger.account_entity_immutable'; END IF;
                IF TG_OP = 'UPDATE' AND OLD."LegalEntityId" IS NOT NULL AND NEW."IsDeleted"
                THEN RAISE EXCEPTION 'ledger.governed_account_soft_delete_forbidden'; END IF;
                IF NEW."ParentId" IS NOT NULL AND NOT EXISTS (SELECT 1 FROM "ChartOfAccounts" parent WHERE parent."Id" = NEW."ParentId" AND parent."LegalEntityId" IS NOT DISTINCT FROM NEW."LegalEntityId")
                THEN RAISE EXCEPTION 'ledger.account_parent_entity_mismatch'; END IF;
                RETURN NEW;
            END $$;
            CREATE TRIGGER "TR_ChartOfAccounts_LedgerGuard" BEFORE INSERT OR UPDATE OR DELETE ON "ChartOfAccounts"
                FOR EACH ROW EXECUTE FUNCTION mealmate_ledger_guard_account();

            CREATE OR REPLACE FUNCTION mealmate_ledger_guard_trace() RETURNS trigger LANGUAGE plpgsql AS $$
            BEGIN
                RAISE EXCEPTION 'ledger.posting_trace_append_only';
            END $$;
            CREATE TRIGGER "TR_JournalSourceLinks_AppendOnly" BEFORE UPDATE OR DELETE ON "JournalSourceLinks"
                FOR EACH ROW EXECUTE FUNCTION mealmate_ledger_guard_trace();
            CREATE TRIGGER "TR_PostingIdentities_AppendOnly" BEFORE UPDATE OR DELETE ON "PostingIdentities"
                FOR EACH ROW EXECUTE FUNCTION mealmate_ledger_guard_trace();
            CREATE TRIGGER "TR_JournalReversals_AppendOnly" BEFORE UPDATE OR DELETE ON "JournalReversals"
                FOR EACH ROW EXECUTE FUNCTION mealmate_ledger_guard_trace();

            CREATE OR REPLACE FUNCTION mealmate_ledger_guard_audit() RETURNS trigger LANGUAGE plpgsql AS $$
            BEGIN
                IF OLD."Action" LIKE 'Ledger%' THEN RAISE EXCEPTION 'ledger.audit_append_only'; END IF;
                RETURN OLD;
            END $$;
            CREATE TRIGGER "TR_LedgerAudit_AppendOnly" BEFORE UPDATE OR DELETE ON "FinancialAuditTrails"
                FOR EACH ROW EXECUTE FUNCTION mealmate_ledger_guard_audit();

            CREATE UNIQUE INDEX "UX_AccountingPeriods_Entity_Id" ON "AccountingPeriods" ("LegalEntityId", "Id");
            CREATE UNIQUE INDEX "UX_JournalSourceDocuments_Entity_Id" ON "JournalSourceDocuments" ("LegalEntityId", "Id");
            ALTER TABLE "JournalEntries" ADD CONSTRAINT "FK_JournalEntries_Entity_Period"
                FOREIGN KEY ("LegalEntityId", "AccountingPeriodId") REFERENCES "AccountingPeriods" ("LegalEntityId", "Id") ON DELETE RESTRICT;
            ALTER TABLE "JournalEntryLines" ADD CONSTRAINT "FK_JournalEntryLines_Entity_Journal"
                FOREIGN KEY ("LegalEntityId", "JournalEntryId") REFERENCES "JournalEntries" ("LegalEntityId", "Id") ON DELETE RESTRICT;
            ALTER TABLE "JournalEntryLines" ADD CONSTRAINT "FK_JournalEntryLines_Entity_Account"
                FOREIGN KEY ("LegalEntityId", "AccountId") REFERENCES "ChartOfAccounts" ("LegalEntityId", "Id") ON DELETE RESTRICT;
            ALTER TABLE "JournalSourceDocuments" ADD CONSTRAINT "FK_JournalSourceDocuments_Entity_OriginalJournal"
                FOREIGN KEY ("LegalEntityId", "OriginalJournalEntryId") REFERENCES "JournalEntries" ("LegalEntityId", "Id") ON DELETE RESTRICT;
            ALTER TABLE "JournalSourceDocuments" ADD CONSTRAINT "FK_JournalSourceDocuments_Entity_PostedJournal"
                FOREIGN KEY ("LegalEntityId", "PostedJournalEntryId") REFERENCES "JournalEntries" ("LegalEntityId", "Id") ON DELETE RESTRICT;
            ALTER TABLE "JournalSourceLinks" ADD CONSTRAINT "FK_JournalSourceLinks_Entity_Source"
                FOREIGN KEY ("LegalEntityId", "SourceDocumentId") REFERENCES "JournalSourceDocuments" ("LegalEntityId", "Id") ON DELETE RESTRICT;
            ALTER TABLE "JournalSourceLinks" ADD CONSTRAINT "FK_JournalSourceLinks_Entity_Journal"
                FOREIGN KEY ("LegalEntityId", "JournalEntryId") REFERENCES "JournalEntries" ("LegalEntityId", "Id") ON DELETE RESTRICT;
            ALTER TABLE "JournalReversals" ADD CONSTRAINT "FK_JournalReversals_Entity_OriginalJournal"
                FOREIGN KEY ("LegalEntityId", "OriginalJournalEntryId") REFERENCES "JournalEntries" ("LegalEntityId", "Id") ON DELETE RESTRICT;
            ALTER TABLE "JournalReversals" ADD CONSTRAINT "FK_JournalReversals_Entity_ReversalJournal"
                FOREIGN KEY ("LegalEntityId", "ReversalJournalEntryId") REFERENCES "JournalEntries" ("LegalEntityId", "Id") ON DELETE RESTRICT;
            ALTER TABLE "JournalReversals" ADD CONSTRAINT "FK_JournalReversals_Entity_Source"
                FOREIGN KEY ("LegalEntityId", "SourceDocumentId") REFERENCES "JournalSourceDocuments" ("LegalEntityId", "Id") ON DELETE RESTRICT;
            """;
    }
}
