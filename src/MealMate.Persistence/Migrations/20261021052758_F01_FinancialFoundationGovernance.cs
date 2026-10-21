using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace MealMate.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class F01_FinancialFoundationGovernance : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<DateTime>(
                name: "ApprovedAtUtc",
                table: "RuleVersions",
                type: "timestamp with time zone",
                nullable: true);

            migrationBuilder.AddColumn<Guid>(
                name: "CheckerUserId",
                table: "RuleVersions",
                type: "uuid",
                nullable: true);

            migrationBuilder.AddColumn<Guid>(
                name: "LegalEntityId",
                table: "RuleVersions",
                type: "uuid",
                nullable: false,
                defaultValue: new Guid("00000000-0000-0000-0000-000000000000"));

            migrationBuilder.AddColumn<string>(
                name: "NormalizedScopeId",
                table: "RuleVersions",
                type: "character varying(50)",
                maxLength: 50,
                nullable: false,
                defaultValue: "");

            migrationBuilder.AddColumn<string>(
                name: "PayloadHash",
                table: "RuleVersions",
                type: "character varying(64)",
                maxLength: 64,
                nullable: true);

            migrationBuilder.AddColumn<DateTime>(
                name: "RejectedAtUtc",
                table: "RuleVersions",
                type: "timestamp with time zone",
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "RowVersion",
                table: "RuleVersions",
                type: "integer",
                nullable: false,
                defaultValue: 0);

            migrationBuilder.AddColumn<int>(
                name: "SchemaVersion",
                table: "RuleVersions",
                type: "integer",
                nullable: false,
                defaultValue: 0);

            migrationBuilder.AddColumn<DateTime>(
                name: "SubmittedAtUtc",
                table: "RuleVersions",
                type: "timestamp with time zone",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "SubmittedPayloadHash",
                table: "RuleVersions",
                type: "character varying(64)",
                maxLength: 64,
                nullable: true);

            migrationBuilder.CreateTable(
                name: "CancellationSettlementDecisions",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    QuoteId = table.Column<Guid>(type: "uuid", nullable: false),
                    SubscriptionId = table.Column<Guid>(type: "uuid", nullable: false),
                    ConfirmedBy = table.Column<Guid>(type: "uuid", nullable: false),
                    ConfirmedAtUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    Reason = table.Column<string>(type: "text", nullable: false),
                    FinalDaySetHash = table.Column<string>(type: "text", nullable: false),
                    OperationId = table.Column<string>(type: "text", nullable: false),
                    FinancialState = table.Column<string>(type: "text", nullable: false),
                    ListUnused = table.Column<decimal>(type: "numeric", nullable: false),
                    NetUnused = table.Column<decimal>(type: "numeric", nullable: false),
                    RepriceCalculated = table.Column<decimal>(type: "numeric", nullable: false),
                    RepriceApplied = table.Column<decimal>(type: "numeric", nullable: false),
                    RepriceUnapplied = table.Column<decimal>(type: "numeric", nullable: false),
                    CancelFeeCalculated = table.Column<decimal>(type: "numeric", nullable: false),
                    CancelFeeApplied = table.Column<decimal>(type: "numeric", nullable: false),
                    OtherFees = table.Column<decimal>(type: "numeric", nullable: false),
                    RefundDue = table.Column<decimal>(type: "numeric", nullable: false),
                    IsZeroRefund = table.Column<bool>(type: "boolean", nullable: false),
                    ExecutedRepriceShare = table.Column<decimal>(type: "numeric", nullable: false),
                    LockedRepriceShare = table.Column<decimal>(type: "numeric", nullable: false),
                    SourceAllocationsJson = table.Column<string>(type: "text", nullable: true),
                    JournalRef = table.Column<string>(type: "text", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_CancellationSettlementDecisions", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "CarriedOverServiceObligations",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    SubscriptionId = table.Column<Guid>(type: "uuid", nullable: false),
                    BeneficiaryId = table.Column<Guid>(type: "uuid", nullable: false),
                    OriginalDayId = table.Column<Guid>(type: "uuid", nullable: false),
                    OriginalServiceDate = table.Column<DateOnly>(type: "date", nullable: false),
                    TargetDate = table.Column<DateOnly>(type: "date", nullable: true),
                    LiabilityAmount = table.Column<decimal>(type: "numeric", nullable: false),
                    Currency = table.Column<string>(type: "text", nullable: false),
                    Reason = table.Column<int>(type: "integer", nullable: false),
                    State = table.Column<int>(type: "integer", nullable: false),
                    JournalRef = table.Column<string>(type: "text", nullable: true),
                    CreatedAtUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    ExecutedAtUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_CarriedOverServiceObligations", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "LegalEntities",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    LegalNameAr = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false),
                    LegalNameEn = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false),
                    RegistrationNo = table.Column<string>(type: "character varying(50)", maxLength: 50, nullable: false),
                    Currency = table.Column<string>(type: "character varying(3)", maxLength: 3, nullable: false),
                    BusinessTimezone = table.Column<string>(type: "character varying(50)", maxLength: 50, nullable: false),
                    Status = table.Column<int>(type: "integer", nullable: false),
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
                    table.PrimaryKey("PK_LegalEntities", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "RefundSourceAllocations",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    RefundOperationId = table.Column<Guid>(type: "uuid", nullable: false),
                    OriginalSourceId = table.Column<Guid>(type: "uuid", nullable: false),
                    SourceType = table.Column<int>(type: "integer", nullable: false),
                    Amount = table.Column<decimal>(type: "numeric", nullable: false),
                    Currency = table.Column<string>(type: "text", nullable: false),
                    State = table.Column<int>(type: "integer", nullable: false),
                    JournalRef = table.Column<string>(type: "text", nullable: true),
                    ProviderRef = table.Column<string>(type: "text", nullable: true),
                    RestoredExpiryUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    ConcurrencyVersion = table.Column<long>(type: "bigint", nullable: false),
                    CreatedAtUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    SettledAtUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_RefundSourceAllocations", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "RegulatoryRequirements",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    Authority = table.Column<string>(type: "character varying(50)", maxLength: 50, nullable: false),
                    RequirementCode = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false),
                    SourceUrl = table.Column<string>(type: "character varying(500)", maxLength: 500, nullable: false),
                    CapturedAtUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    VerifiedByUserId = table.Column<Guid>(type: "uuid", nullable: true),
                    EvidenceHash = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    EffectiveFromUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    EffectiveToUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
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
                    table.PrimaryKey("PK_RegulatoryRequirements", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "ReportingProfiles",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    LegalEntityId = table.Column<Guid>(type: "uuid", nullable: false),
                    Code = table.Column<string>(type: "character varying(50)", maxLength: 50, nullable: false),
                    ReportType = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false),
                    Applicability = table.Column<int>(type: "integer", nullable: false),
                    PackageId = table.Column<Guid>(type: "uuid", nullable: true),
                    EntryPointId = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: true),
                    ValidatorProfile = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: true),
                    Status = table.Column<int>(type: "integer", nullable: false),
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
                    table.PrimaryKey("PK_ReportingProfiles", x => x.Id);
                    table.ForeignKey(
                        name: "FK_ReportingProfiles_LegalEntities_LegalEntityId",
                        column: x => x.LegalEntityId,
                        principalTable: "LegalEntities",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateIndex(
                name: "IX_RuleVersions_LegalEntityId",
                table: "RuleVersions",
                column: "LegalEntityId");

            migrationBuilder.CreateIndex(
                name: "IX_RuleVersions_RuleKey_ScopeType_NormalizedScopeId",
                table: "RuleVersions",
                columns: new[] { "RuleKey", "ScopeType", "NormalizedScopeId" });

            migrationBuilder.CreateIndex(
                name: "IX_LegalEntities_RegistrationNo",
                table: "LegalEntities",
                column: "RegistrationNo",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_RegulatoryRequirements_Authority_RequirementCode",
                table: "RegulatoryRequirements",
                columns: new[] { "Authority", "RequirementCode" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_RegulatoryRequirements_EffectiveFromUtc",
                table: "RegulatoryRequirements",
                column: "EffectiveFromUtc");

            migrationBuilder.CreateIndex(
                name: "IX_ReportingProfiles_LegalEntityId_Code_ReportType",
                table: "ReportingProfiles",
                columns: new[] { "LegalEntityId", "Code", "ReportType" },
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "CancellationSettlementDecisions");

            migrationBuilder.DropTable(
                name: "CarriedOverServiceObligations");

            migrationBuilder.DropTable(
                name: "RefundSourceAllocations");

            migrationBuilder.DropTable(
                name: "RegulatoryRequirements");

            migrationBuilder.DropTable(
                name: "ReportingProfiles");

            migrationBuilder.DropTable(
                name: "LegalEntities");

            migrationBuilder.DropIndex(
                name: "IX_RuleVersions_LegalEntityId",
                table: "RuleVersions");

            migrationBuilder.DropIndex(
                name: "IX_RuleVersions_RuleKey_ScopeType_NormalizedScopeId",
                table: "RuleVersions");

            migrationBuilder.DropColumn(
                name: "ApprovedAtUtc",
                table: "RuleVersions");

            migrationBuilder.DropColumn(
                name: "CheckerUserId",
                table: "RuleVersions");

            migrationBuilder.DropColumn(
                name: "LegalEntityId",
                table: "RuleVersions");

            migrationBuilder.DropColumn(
                name: "NormalizedScopeId",
                table: "RuleVersions");

            migrationBuilder.DropColumn(
                name: "PayloadHash",
                table: "RuleVersions");

            migrationBuilder.DropColumn(
                name: "RejectedAtUtc",
                table: "RuleVersions");

            migrationBuilder.DropColumn(
                name: "RowVersion",
                table: "RuleVersions");

            migrationBuilder.DropColumn(
                name: "SchemaVersion",
                table: "RuleVersions");

            migrationBuilder.DropColumn(
                name: "SubmittedAtUtc",
                table: "RuleVersions");

            migrationBuilder.DropColumn(
                name: "SubmittedPayloadHash",
                table: "RuleVersions");
        }
    }
}
