using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace MealMate.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddCapacityRenewalCompensationModels : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<int>(
                name: "RowVersion",
                table: "RestaurantBundlePricings",
                type: "integer",
                nullable: false,
                defaultValue: 0);

            migrationBuilder.AlterColumn<decimal>(
                name: "UpperBound",
                table: "ClassificationRuns",
                type: "numeric(27,15)",
                precision: 27,
                scale: 15,
                nullable: false,
                oldClrType: typeof(decimal),
                oldType: "numeric(18,3)",
                oldPrecision: 18,
                oldScale: 3);

            migrationBuilder.AlterColumn<decimal>(
                name: "SdReferenceDaily",
                table: "ClassificationRuns",
                type: "numeric(27,15)",
                precision: 27,
                scale: 15,
                nullable: false,
                oldClrType: typeof(decimal),
                oldType: "numeric(18,3)",
                oldPrecision: 18,
                oldScale: 3);

            migrationBuilder.AlterColumn<decimal>(
                name: "MeanReferenceDaily",
                table: "ClassificationRuns",
                type: "numeric(27,15)",
                precision: 27,
                scale: 15,
                nullable: false,
                oldClrType: typeof(decimal),
                oldType: "numeric(18,3)",
                oldPrecision: 18,
                oldScale: 3);

            migrationBuilder.AlterColumn<decimal>(
                name: "LowerBound",
                table: "ClassificationRuns",
                type: "numeric(27,15)",
                precision: 27,
                scale: 15,
                nullable: false,
                oldClrType: typeof(decimal),
                oldType: "numeric(18,3)",
                oldPrecision: 18,
                oldScale: 3);

            migrationBuilder.AddColumn<string>(
                name: "AlgorithmVersion",
                table: "ClassificationRuns",
                type: "character varying(80)",
                maxLength: 80,
                nullable: false,
                defaultValue: "");

            migrationBuilder.AddColumn<string>(
                name: "CohortHash",
                table: "ClassificationRuns",
                type: "character varying(64)",
                maxLength: 64,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "InputsJson",
                table: "ClassificationRuns",
                type: "jsonb",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "QuartileMethod",
                table: "ClassificationRuns",
                type: "character varying(80)",
                maxLength: 80,
                nullable: false,
                defaultValue: "");

            migrationBuilder.CreateTable(
                name: "CapacityReductionRequests",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    RestaurantId = table.Column<Guid>(type: "uuid", nullable: false),
                    CurrentCapacity = table.Column<int>(type: "integer", nullable: false),
                    RequestedCapacity = table.Column<int>(type: "integer", nullable: false),
                    Reason = table.Column<string>(type: "text", nullable: false),
                    RequestedEffectiveDate = table.Column<DateOnly>(type: "date", nullable: false),
                    Status = table.Column<int>(type: "integer", nullable: false),
                    ReviewedByAdminUserId = table.Column<Guid>(type: "uuid", nullable: true),
                    ReviewedAtUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    ReviewNotes = table.Column<string>(type: "text", nullable: true),
                    ApprovedEffectiveDate = table.Column<DateOnly>(type: "date", nullable: true),
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
                    table.PrimaryKey("PK_CapacityReductionRequests", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "PlatformCapacityPolicies",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    PlatformDailyBoxCeiling = table.Column<int>(type: "integer", nullable: false),
                    RenewalReserveMinimumPercent = table.Column<decimal>(type: "numeric", nullable: false),
                    Version = table.Column<long>(type: "bigint", nullable: false),
                    EffectiveFromDate = table.Column<DateOnly>(type: "date", nullable: false),
                    IsActive = table.Column<bool>(type: "boolean", nullable: false),
                    Reason = table.Column<string>(type: "text", nullable: true),
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
                    table.PrimaryKey("PK_PlatformCapacityPolicies", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "PricingAcquisitionBudgets",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    CampaignId = table.Column<Guid>(type: "uuid", nullable: false),
                    BundleId = table.Column<Guid>(type: "uuid", nullable: false),
                    LegalEntityId = table.Column<Guid>(type: "uuid", nullable: false),
                    RuleVersionId = table.Column<Guid>(type: "uuid", nullable: false),
                    MakerUserId = table.Column<Guid>(type: "uuid", nullable: false),
                    CheckerUserId = table.Column<Guid>(type: "uuid", nullable: true),
                    ApprovalReason = table.Column<string>(type: "character varying(2000)", maxLength: 2000, nullable: false),
                    AuthorizedLimitKwd = table.Column<decimal>(type: "numeric(18,3)", precision: 18, scale: 3, nullable: false),
                    MaximumSales = table.Column<int>(type: "integer", nullable: false),
                    EffectiveFromUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    EffectiveToUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    IsActive = table.Column<bool>(type: "boolean", nullable: false),
                    ApprovedAtUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    RowVersion = table.Column<int>(type: "integer", nullable: false),
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
                    table.PrimaryKey("PK_PricingAcquisitionBudgets", x => x.Id);
                    table.ForeignKey(
                        name: "FK_PricingAcquisitionBudgets_RuleVersions_RuleVersionId",
                        column: x => x.RuleVersionId,
                        principalTable: "RuleVersions",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "PricingQuotes",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    CustomerId = table.Column<Guid>(type: "uuid", nullable: false),
                    Route = table.Column<byte>(type: "smallint", nullable: false),
                    ProgramId = table.Column<Guid>(type: "uuid", nullable: false),
                    BundleId = table.Column<Guid>(type: "uuid", nullable: false),
                    Tier = table.Column<int>(type: "integer", nullable: false),
                    DurationDays = table.Column<int>(type: "integer", nullable: false),
                    StartDate = table.Column<DateOnly>(type: "date", nullable: false),
                    EndDate = table.Column<DateOnly>(type: "date", nullable: false),
                    ServiceDates = table.Column<DateOnly[]>(type: "date[]", nullable: false),
                    OfferId = table.Column<Guid>(type: "uuid", nullable: true),
                    CampaignId = table.Column<Guid>(type: "uuid", nullable: true),
                    FamilyGroupId = table.Column<Guid>(type: "uuid", nullable: true),
                    SelectionSnapshotJson = table.Column<string>(type: "jsonb", nullable: false),
                    SelectionHash = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    RuleVersionId = table.Column<Guid>(type: "uuid", nullable: false),
                    RuleVersionContentHash = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    ClassificationRunId = table.Column<Guid>(type: "uuid", nullable: true),
                    SourceVersionsHash = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    SourceVersionsJson = table.Column<string>(type: "jsonb", nullable: false),
                    ReferencePrice26Kwd = table.Column<decimal>(type: "numeric(38,18)", precision: 38, scale: 18, nullable: false),
                    CohortMeanReferencePrice26Kwd = table.Column<decimal>(type: "numeric(38,18)", precision: 38, scale: 18, nullable: false),
                    ContractedSupplyPrice26Kwd = table.Column<decimal>(type: "numeric(38,18)", precision: 38, scale: 18, nullable: false),
                    CustomerListPriceKwd = table.Column<decimal>(type: "numeric(18,3)", precision: 18, scale: 3, nullable: false),
                    SelectedDiscountSource = table.Column<string>(type: "text", nullable: false),
                    SelectedDiscountKwd = table.Column<decimal>(type: "numeric(18,3)", precision: 18, scale: 3, nullable: false),
                    CustomerNetPriceKwd = table.Column<decimal>(type: "numeric(18,3)", precision: 18, scale: 3, nullable: false),
                    ExternalCaptureAmountKwd = table.Column<decimal>(type: "numeric(18,3)", precision: 18, scale: 3, nullable: false),
                    ExpectedGatewayCostKwd = table.Column<decimal>(type: "numeric(38,18)", precision: 38, scale: 18, nullable: false),
                    CurrencyCode = table.Column<string>(type: "character varying(3)", maxLength: 3, nullable: false),
                    FinancialSnapshotJson = table.Column<string>(type: "jsonb", nullable: false),
                    FinancialSnapshotHash = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    Status = table.Column<byte>(type: "smallint", nullable: false),
                    RiskState = table.Column<byte>(type: "smallint", nullable: false),
                    RiskBlockerCodes = table.Column<string[]>(type: "text[]", nullable: false),
                    ExpiresAtUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    CreatedAtUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    ConsumedAtUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    RowVersion = table.Column<int>(type: "integer", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_PricingQuotes", x => x.Id);
                    table.ForeignKey(
                        name: "FK_PricingQuotes_ClassificationRuns_ClassificationRunId",
                        column: x => x.ClassificationRunId,
                        principalTable: "ClassificationRuns",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_PricingQuotes_RuleVersions_RuleVersionId",
                        column: x => x.RuleVersionId,
                        principalTable: "RuleVersions",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "RestaurantCapacityPolicies",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    RestaurantId = table.Column<Guid>(type: "uuid", nullable: false),
                    DailyBoxLimit = table.Column<int>(type: "integer", nullable: false),
                    Version = table.Column<long>(type: "bigint", nullable: false),
                    EffectiveFromDate = table.Column<DateOnly>(type: "date", nullable: false),
                    IsActive = table.Column<bool>(type: "boolean", nullable: false),
                    Reason = table.Column<string>(type: "text", nullable: true),
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
                    table.PrimaryKey("PK_RestaurantCapacityPolicies", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "ServiceCapacityCommitments",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    ServiceDate = table.Column<DateOnly>(type: "date", nullable: false),
                    CustomerUserId = table.Column<Guid>(type: "uuid", nullable: false),
                    FamilyMemberId = table.Column<Guid>(type: "uuid", nullable: true),
                    SubscriptionId = table.Column<Guid>(type: "uuid", nullable: false),
                    Source = table.Column<int>(type: "integer", nullable: false),
                    Status = table.Column<int>(type: "integer", nullable: false),
                    RestaurantId = table.Column<Guid>(type: "uuid", nullable: true),
                    RegionId = table.Column<Guid>(type: "uuid", nullable: false),
                    DeliveryAddressId = table.Column<Guid>(type: "uuid", nullable: true),
                    DietaryRestrictionsSnapshot = table.Column<string>(type: "text", nullable: true),
                    LeadPolicyVersion = table.Column<long>(type: "bigint", nullable: false),
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
                    table.PrimaryKey("PK_ServiceCapacityCommitments", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "PricingAcquisitionBudgetReservations",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    BudgetId = table.Column<Guid>(type: "uuid", nullable: false),
                    QuoteId = table.Column<Guid>(type: "uuid", nullable: false),
                    AmountKwd = table.Column<decimal>(type: "numeric(18,3)", precision: 18, scale: 3, nullable: false),
                    QuoteSnapshotHash = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    ReservedSales = table.Column<int>(type: "integer", nullable: false),
                    Status = table.Column<byte>(type: "smallint", nullable: false),
                    ExpiresAtUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    CreatedAtUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    CompletedAtUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    RowVersion = table.Column<int>(type: "integer", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_PricingAcquisitionBudgetReservations", x => x.Id);
                    table.ForeignKey(
                        name: "FK_PricingAcquisitionBudgetReservations_PricingAcquisitionBudg~",
                        column: x => x.BudgetId,
                        principalTable: "PricingAcquisitionBudgets",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_PricingAcquisitionBudgetReservations_PricingQuotes_QuoteId",
                        column: x => x.QuoteId,
                        principalTable: "PricingQuotes",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "PricingQuoteConsumptions",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    QuoteId = table.Column<Guid>(type: "uuid", nullable: false),
                    ConsumerType = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: true),
                    ConsumerId = table.Column<Guid>(type: "uuid", nullable: true),
                    CustomerId = table.Column<Guid>(type: "uuid", nullable: false),
                    AcceptedAtUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    AcceptedSnapshotHash = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_PricingQuoteConsumptions", x => x.Id);
                    table.ForeignKey(
                        name: "FK_PricingQuoteConsumptions_PricingQuotes_QuoteId",
                        column: x => x.QuoteId,
                        principalTable: "PricingQuotes",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "PricingRiskExceptions",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    QuoteId = table.Column<Guid>(type: "uuid", nullable: false),
                    BudgetReservationId = table.Column<Guid>(type: "uuid", nullable: false),
                    MakerUserId = table.Column<Guid>(type: "uuid", nullable: false),
                    CheckerUserId = table.Column<Guid>(type: "uuid", nullable: true),
                    AuthorizedLossKwd = table.Column<decimal>(type: "numeric(18,3)", precision: 18, scale: 3, nullable: false),
                    Reason = table.Column<string>(type: "character varying(2000)", maxLength: 2000, nullable: false),
                    QuoteSnapshotHash = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    RequestedAtUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    DecidedAtUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    IsApproved = table.Column<bool>(type: "boolean", nullable: true),
                    RowVersion = table.Column<int>(type: "integer", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_PricingRiskExceptions", x => x.Id);
                    table.ForeignKey(
                        name: "FK_PricingRiskExceptions_PricingAcquisitionBudgetReservations_~",
                        column: x => x.BudgetReservationId,
                        principalTable: "PricingAcquisitionBudgetReservations",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_PricingRiskExceptions_PricingQuotes_QuoteId",
                        column: x => x.QuoteId,
                        principalTable: "PricingQuotes",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateIndex(
                name: "IX_PricingAcquisitionBudgetReservations_BudgetId_Status_Expire~",
                table: "PricingAcquisitionBudgetReservations",
                columns: new[] { "BudgetId", "Status", "ExpiresAtUtc" });

            migrationBuilder.CreateIndex(
                name: "IX_PricingAcquisitionBudgetReservations_QuoteId",
                table: "PricingAcquisitionBudgetReservations",
                column: "QuoteId",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_PricingAcquisitionBudgets_CampaignId_LegalEntityId_IsActive",
                table: "PricingAcquisitionBudgets",
                columns: new[] { "CampaignId", "LegalEntityId", "IsActive" });

            migrationBuilder.CreateIndex(
                name: "IX_PricingAcquisitionBudgets_RuleVersionId",
                table: "PricingAcquisitionBudgets",
                column: "RuleVersionId");

            migrationBuilder.CreateIndex(
                name: "IX_PricingQuoteConsumptions_QuoteId",
                table: "PricingQuoteConsumptions",
                column: "QuoteId",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_PricingQuotes_ClassificationRunId",
                table: "PricingQuotes",
                column: "ClassificationRunId");

            migrationBuilder.CreateIndex(
                name: "IX_PricingQuotes_CustomerId_CreatedAtUtc",
                table: "PricingQuotes",
                columns: new[] { "CustomerId", "CreatedAtUtc" });

            migrationBuilder.CreateIndex(
                name: "IX_PricingQuotes_CustomerId_FinancialSnapshotHash",
                table: "PricingQuotes",
                columns: new[] { "CustomerId", "FinancialSnapshotHash" });

            migrationBuilder.CreateIndex(
                name: "IX_PricingQuotes_Route_Status_ExpiresAtUtc",
                table: "PricingQuotes",
                columns: new[] { "Route", "Status", "ExpiresAtUtc" });

            migrationBuilder.CreateIndex(
                name: "IX_PricingQuotes_RuleVersionId",
                table: "PricingQuotes",
                column: "RuleVersionId");

            migrationBuilder.CreateIndex(
                name: "IX_PricingRiskExceptions_BudgetReservationId",
                table: "PricingRiskExceptions",
                column: "BudgetReservationId");

            migrationBuilder.CreateIndex(
                name: "IX_PricingRiskExceptions_QuoteId",
                table: "PricingRiskExceptions",
                column: "QuoteId");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "CapacityReductionRequests");

            migrationBuilder.DropTable(
                name: "PlatformCapacityPolicies");

            migrationBuilder.DropTable(
                name: "PricingQuoteConsumptions");

            migrationBuilder.DropTable(
                name: "PricingRiskExceptions");

            migrationBuilder.DropTable(
                name: "RestaurantCapacityPolicies");

            migrationBuilder.DropTable(
                name: "ServiceCapacityCommitments");

            migrationBuilder.DropTable(
                name: "PricingAcquisitionBudgetReservations");

            migrationBuilder.DropTable(
                name: "PricingAcquisitionBudgets");

            migrationBuilder.DropTable(
                name: "PricingQuotes");

            migrationBuilder.DropColumn(
                name: "RowVersion",
                table: "RestaurantBundlePricings");

            migrationBuilder.DropColumn(
                name: "AlgorithmVersion",
                table: "ClassificationRuns");

            migrationBuilder.DropColumn(
                name: "CohortHash",
                table: "ClassificationRuns");

            migrationBuilder.DropColumn(
                name: "InputsJson",
                table: "ClassificationRuns");

            migrationBuilder.DropColumn(
                name: "QuartileMethod",
                table: "ClassificationRuns");

            migrationBuilder.AlterColumn<decimal>(
                name: "UpperBound",
                table: "ClassificationRuns",
                type: "numeric(18,3)",
                precision: 18,
                scale: 3,
                nullable: false,
                oldClrType: typeof(decimal),
                oldType: "numeric(27,15)",
                oldPrecision: 27,
                oldScale: 15);

            migrationBuilder.AlterColumn<decimal>(
                name: "SdReferenceDaily",
                table: "ClassificationRuns",
                type: "numeric(18,3)",
                precision: 18,
                scale: 3,
                nullable: false,
                oldClrType: typeof(decimal),
                oldType: "numeric(27,15)",
                oldPrecision: 27,
                oldScale: 15);

            migrationBuilder.AlterColumn<decimal>(
                name: "MeanReferenceDaily",
                table: "ClassificationRuns",
                type: "numeric(18,3)",
                precision: 18,
                scale: 3,
                nullable: false,
                oldClrType: typeof(decimal),
                oldType: "numeric(27,15)",
                oldPrecision: 27,
                oldScale: 15);

            migrationBuilder.AlterColumn<decimal>(
                name: "LowerBound",
                table: "ClassificationRuns",
                type: "numeric(18,3)",
                precision: 18,
                scale: 3,
                nullable: false,
                oldClrType: typeof(decimal),
                oldType: "numeric(27,15)",
                oldPrecision: 27,
                oldScale: 15);
        }
    }
}
