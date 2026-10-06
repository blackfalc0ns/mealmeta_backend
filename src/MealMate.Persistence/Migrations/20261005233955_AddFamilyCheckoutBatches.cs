using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace MealMate.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddFamilyCheckoutBatches : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<Guid>(
                name: "ActorUserId",
                table: "DeliveryHolds",
                type: "uuid",
                nullable: true);

            migrationBuilder.AddColumn<Guid>(
                name: "ContactCaseId",
                table: "DeliveryHolds",
                type: "uuid",
                nullable: true);

            migrationBuilder.AddColumn<Guid>(
                name: "FirstAttemptCallId",
                table: "DeliveryHolds",
                type: "uuid",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "Notes",
                table: "DeliveryHolds",
                type: "text",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "ResolutionReason",
                table: "DeliveryHolds",
                type: "text",
                nullable: true);

            migrationBuilder.AddColumn<DateTime>(
                name: "ResolvedAtUtc",
                table: "DeliveryHolds",
                type: "timestamp with time zone",
                nullable: true);

            migrationBuilder.AddColumn<DateTime>(
                name: "ResumedAtUtc",
                table: "DeliveryHolds",
                type: "timestamp with time zone",
                nullable: true);

            migrationBuilder.AddColumn<Guid>(
                name: "TripStopId",
                table: "DeliveryHolds",
                type: "uuid",
                nullable: true);

            migrationBuilder.AddColumn<DateTime>(
                name: "CallerConnectedReportedAtUtc",
                table: "CallSessions",
                type: "timestamp with time zone",
                nullable: true);

            migrationBuilder.AddColumn<Guid>(
                name: "CallerDeviceSessionId",
                table: "CallSessions",
                type: "uuid",
                nullable: true);

            migrationBuilder.AddColumn<DateTime>(
                name: "CallerLastHeartbeatAtUtc",
                table: "CallSessions",
                type: "timestamp with time zone",
                nullable: true);

            migrationBuilder.AddColumn<Guid>(
                name: "EndedByUserId",
                table: "CallSessions",
                type: "uuid",
                nullable: true);

            migrationBuilder.AddColumn<DateTime>(
                name: "MediaLeaseExpiresAtUtc",
                table: "CallSessions",
                type: "timestamp with time zone",
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "ProtocolVersion",
                table: "CallSessions",
                type: "integer",
                nullable: false,
                defaultValue: 1);

            migrationBuilder.AddColumn<DateTime>(
                name: "ReceiverConnectedReportedAtUtc",
                table: "CallSessions",
                type: "timestamp with time zone",
                nullable: true);

            migrationBuilder.AddColumn<Guid>(
                name: "ReceiverDeviceSessionId",
                table: "CallSessions",
                type: "uuid",
                nullable: true);

            migrationBuilder.AddColumn<DateTime>(
                name: "ReceiverLastHeartbeatAtUtc",
                table: "CallSessions",
                type: "timestamp with time zone",
                nullable: true);

            migrationBuilder.AddColumn<DateTime>(
                name: "ReceiverRingingAckAtUtc",
                table: "CallSessions",
                type: "timestamp with time zone",
                nullable: true);

            migrationBuilder.AddColumn<DateTime>(
                name: "RingExpiresAtUtc",
                table: "CallSessions",
                type: "timestamp with time zone",
                nullable: true);

            migrationBuilder.AddColumn<long>(
                name: "Sequence",
                table: "CallSessions",
                type: "bigint",
                nullable: false,
                defaultValue: 0L);

            migrationBuilder.AddColumn<DateTime>(
                name: "SetupExpiresAtUtc",
                table: "CallSessions",
                type: "timestamp with time zone",
                nullable: true);

            migrationBuilder.AddColumn<Guid>(
                name: "TerminalEventId",
                table: "CallSessions",
                type: "uuid",
                nullable: true);

            migrationBuilder.CreateTable(
                name: "DeliveryContactAttempts",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    CallId = table.Column<Guid>(type: "uuid", nullable: false),
                    ContactCaseId = table.Column<Guid>(type: "uuid", nullable: false),
                    InitiatorUserId = table.Column<Guid>(type: "uuid", nullable: false),
                    InitiatorType = table.Column<int>(type: "integer", nullable: false),
                    Outcome = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: false),
                    QualifiesAsNoAnswer = table.Column<bool>(type: "boolean", nullable: false),
                    Ordinal = table.Column<int>(type: "integer", nullable: false),
                    StartedAtUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    EndedAtUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
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
                    table.PrimaryKey("PK_DeliveryContactAttempts", x => x.Id);
                    table.ForeignKey(
                        name: "FK_DeliveryContactAttempts_CallSessions_CallId",
                        column: x => x.CallId,
                        principalTable: "CallSessions",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "DeliveryContactCases",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    TripStopId = table.Column<Guid>(type: "uuid", nullable: false),
                    ActiveDriverAssignmentId = table.Column<Guid>(type: "uuid", nullable: false),
                    ContactCycleId = table.Column<int>(type: "integer", nullable: false),
                    State = table.Column<string>(type: "character varying(16)", maxLength: 16, nullable: false),
                    QualifiedMissedCount = table.Column<int>(type: "integer", nullable: false),
                    HoldId = table.Column<Guid>(type: "uuid", nullable: true),
                    LastCallId = table.Column<Guid>(type: "uuid", nullable: true),
                    Version = table.Column<long>(type: "bigint", nullable: false),
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
                    table.PrimaryKey("PK_DeliveryContactCases", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "FamilyCheckoutBatches",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    FamilyGroupId = table.Column<Guid>(type: "uuid", nullable: false),
                    PayerCustomerUserId = table.Column<Guid>(type: "uuid", nullable: false),
                    IdempotencyKey = table.Column<string>(type: "character varying(128)", maxLength: 128, nullable: false),
                    Status = table.Column<int>(type: "integer", nullable: false),
                    Version = table.Column<long>(type: "bigint", nullable: false),
                    PackageId = table.Column<Guid>(type: "uuid", nullable: true),
                    PackageNameAr = table.Column<string>(type: "character varying(120)", maxLength: 120, nullable: true),
                    PackageNameEn = table.Column<string>(type: "character varying(120)", maxLength: 120, nullable: true),
                    PackageMinMembers = table.Column<int>(type: "integer", nullable: true),
                    PackageMaxMembers = table.Column<int>(type: "integer", nullable: true),
                    FamilyDiscountPercent = table.Column<decimal>(type: "numeric(5,4)", precision: 5, scale: 4, nullable: false),
                    CurrencyCode = table.Column<string>(type: "character varying(3)", maxLength: 3, nullable: false),
                    Subtotal = table.Column<decimal>(type: "numeric(18,3)", precision: 18, scale: 3, nullable: false),
                    FamilyDiscountAmount = table.Column<decimal>(type: "numeric(18,3)", precision: 18, scale: 3, nullable: false),
                    FinalPrice = table.Column<decimal>(type: "numeric(18,3)", precision: 18, scale: 3, nullable: false),
                    PaymentAnchorSubscriptionId = table.Column<Guid>(type: "uuid", nullable: true),
                    GatewayInvoiceId = table.Column<string>(type: "character varying(128)", maxLength: 128, nullable: true),
                    GatewayPaymentId = table.Column<string>(type: "character varying(128)", maxLength: 128, nullable: true),
                    ExpiresAtUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    PaidAtUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    CreatedAtUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_FamilyCheckoutBatches", x => x.Id);
                    table.ForeignKey(
                        name: "FK_FamilyCheckoutBatches_FamilyGroups_FamilyGroupId",
                        column: x => x.FamilyGroupId,
                        principalTable: "FamilyGroups",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "VoiceCallBusyParticipants",
                columns: table => new
                {
                    UserId = table.Column<Guid>(type: "uuid", nullable: false),
                    CallId = table.Column<Guid>(type: "uuid", nullable: false),
                    CreatedAtUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    Id = table.Column<Guid>(type: "uuid", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_VoiceCallBusyParticipants", x => x.UserId);
                    table.ForeignKey(
                        name: "FK_VoiceCallBusyParticipants_CallSessions_CallId",
                        column: x => x.CallId,
                        principalTable: "CallSessions",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "VoiceCallDeviceReceipts",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    CallId = table.Column<Guid>(type: "uuid", nullable: false),
                    DeviceSessionId = table.Column<Guid>(type: "uuid", nullable: false),
                    RingingAckAtUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    LastTerminalAckSequence = table.Column<long>(type: "bigint", nullable: false),
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
                    table.PrimaryKey("PK_VoiceCallDeviceReceipts", x => x.Id);
                    table.ForeignKey(
                        name: "FK_VoiceCallDeviceReceipts_CallSessions_CallId",
                        column: x => x.CallId,
                        principalTable: "CallSessions",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "VoiceCallEventDeliveries",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    EventId = table.Column<Guid>(type: "uuid", nullable: false),
                    Channel = table.Column<string>(type: "character varying(16)", maxLength: 16, nullable: false),
                    RecipientDeviceSessionId = table.Column<Guid>(type: "uuid", nullable: true),
                    CallId = table.Column<Guid>(type: "uuid", nullable: false),
                    Sequence = table.Column<long>(type: "bigint", nullable: false),
                    Status = table.Column<string>(type: "character varying(24)", maxLength: 24, nullable: false),
                    AttemptCount = table.Column<int>(type: "integer", nullable: false),
                    NextAttemptAtUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    ProviderMessageId = table.Column<string>(type: "character varying(256)", maxLength: 256, nullable: true),
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
                    table.PrimaryKey("PK_VoiceCallEventDeliveries", x => x.Id);
                    table.ForeignKey(
                        name: "FK_VoiceCallEventDeliveries_CallSessions_CallId",
                        column: x => x.CallId,
                        principalTable: "CallSessions",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "CustomerPhoneRevealGrants",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    ContactCaseId = table.Column<Guid>(type: "uuid", nullable: false),
                    DriverUserId = table.Column<Guid>(type: "uuid", nullable: false),
                    FirstAttemptCallId = table.Column<Guid>(type: "uuid", nullable: false),
                    SecondAttemptCallId = table.Column<Guid>(type: "uuid", nullable: false),
                    ClientRequestId = table.Column<Guid>(type: "uuid", nullable: false),
                    Reason = table.Column<string>(type: "character varying(500)", maxLength: 500, nullable: false),
                    ExpiresAtUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    ConsumedAtUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
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
                    table.PrimaryKey("PK_CustomerPhoneRevealGrants", x => x.Id);
                    table.ForeignKey(
                        name: "FK_CustomerPhoneRevealGrants_DeliveryContactCases_ContactCaseId",
                        column: x => x.ContactCaseId,
                        principalTable: "DeliveryContactCases",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "FamilyCheckoutBatchLines",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    BatchId = table.Column<Guid>(type: "uuid", nullable: false),
                    FamilyMemberId = table.Column<Guid>(type: "uuid", nullable: false),
                    SubscriptionId = table.Column<Guid>(type: "uuid", nullable: true),
                    CheckoutSelected = table.Column<bool>(type: "boolean", nullable: false),
                    ProgramId = table.Column<Guid>(type: "uuid", nullable: true),
                    BundleId = table.Column<Guid>(type: "uuid", nullable: true),
                    Tier = table.Column<int>(type: "integer", nullable: true),
                    DurationDays = table.Column<int>(type: "integer", nullable: true),
                    IncludeFriday = table.Column<bool>(type: "boolean", nullable: false),
                    StartDate = table.Column<DateOnly>(type: "date", nullable: true),
                    DeliveryAddressId = table.Column<Guid>(type: "uuid", nullable: true),
                    AddressLabel = table.Column<string>(type: "character varying(120)", maxLength: 120, nullable: true),
                    AddressLine = table.Column<string>(type: "character varying(300)", maxLength: 300, nullable: true),
                    RegionId = table.Column<Guid>(type: "uuid", nullable: true),
                    Governorate = table.Column<string>(type: "character varying(80)", maxLength: 80, nullable: true),
                    Area = table.Column<string>(type: "character varying(80)", maxLength: 80, nullable: true),
                    Block = table.Column<string>(type: "character varying(80)", maxLength: 80, nullable: true),
                    Street = table.Column<string>(type: "character varying(120)", maxLength: 120, nullable: true),
                    Building = table.Column<string>(type: "character varying(120)", maxLength: 120, nullable: true),
                    Unit = table.Column<string>(type: "character varying(80)", maxLength: 80, nullable: true),
                    Floor = table.Column<string>(type: "character varying(80)", maxLength: 80, nullable: true),
                    ResolvedRestaurantId = table.Column<Guid>(type: "uuid", nullable: true),
                    MenuPlanVersionsJson = table.Column<string>(type: "jsonb", nullable: true),
                    PriceBeforeFamilyDiscount = table.Column<decimal>(type: "numeric(18,3)", precision: 18, scale: 3, nullable: false),
                    FamilyDiscountAllocated = table.Column<decimal>(type: "numeric(18,3)", precision: 18, scale: 3, nullable: false),
                    NetPaidAmount = table.Column<decimal>(type: "numeric(18,3)", precision: 18, scale: 3, nullable: false),
                    CurrencyCode = table.Column<string>(type: "character varying(3)", maxLength: 3, nullable: false),
                    CreatedAtUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_FamilyCheckoutBatchLines", x => x.Id);
                    table.CheckConstraint("CK_FamilyCheckoutBatchLines_NetPaid", "\"NetPaidAmount\" >= 0 AND \"FamilyDiscountAllocated\" >= 0 AND \"PriceBeforeFamilyDiscount\" >= 0 AND \"NetPaidAmount\" <= \"PriceBeforeFamilyDiscount\"");
                    table.ForeignKey(
                        name: "FK_FamilyCheckoutBatchLines_FamilyCheckoutBatches_BatchId",
                        column: x => x.BatchId,
                        principalTable: "FamilyCheckoutBatches",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_FamilyCheckoutBatchLines_FamilyMembers_FamilyMemberId",
                        column: x => x.FamilyMemberId,
                        principalTable: "FamilyMembers",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_FamilyCheckoutBatchLines_Subscriptions_SubscriptionId",
                        column: x => x.SubscriptionId,
                        principalTable: "Subscriptions",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.SetNull);
                });

            migrationBuilder.CreateTable(
                name: "FamilyCheckoutPaymentAttempts",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    BatchId = table.Column<Guid>(type: "uuid", nullable: false),
                    AttemptKey = table.Column<string>(type: "character varying(128)", maxLength: 128, nullable: false),
                    GatewayInvoiceId = table.Column<string>(type: "character varying(128)", maxLength: 128, nullable: true),
                    GatewayPaymentId = table.Column<string>(type: "character varying(128)", maxLength: 128, nullable: true),
                    RequestedAtUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    VerifiedAtUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    ResultCode = table.Column<string>(type: "character varying(128)", maxLength: 128, nullable: true),
                    CreatedAtUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_FamilyCheckoutPaymentAttempts", x => x.Id);
                    table.ForeignKey(
                        name: "FK_FamilyCheckoutPaymentAttempts_FamilyCheckoutBatches_BatchId",
                        column: x => x.BatchId,
                        principalTable: "FamilyCheckoutBatches",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateIndex(
                name: "IX_CallSessions_CallerDeviceSessionId",
                table: "CallSessions",
                column: "CallerDeviceSessionId");

            migrationBuilder.CreateIndex(
                name: "IX_CallSessions_ReceiverDeviceSessionId",
                table: "CallSessions",
                column: "ReceiverDeviceSessionId");

            migrationBuilder.CreateIndex(
                name: "IX_CallSessions_Status_MediaLeaseExpiresAtUtc",
                table: "CallSessions",
                columns: new[] { "Status", "MediaLeaseExpiresAtUtc" });

            migrationBuilder.CreateIndex(
                name: "IX_CallSessions_Status_RingExpiresAtUtc",
                table: "CallSessions",
                columns: new[] { "Status", "RingExpiresAtUtc" });

            migrationBuilder.CreateIndex(
                name: "IX_CallSessions_Status_SetupExpiresAtUtc",
                table: "CallSessions",
                columns: new[] { "Status", "SetupExpiresAtUtc" });

            migrationBuilder.CreateIndex(
                name: "IX_CallSessions_TerminalEventId",
                table: "CallSessions",
                column: "TerminalEventId");

            migrationBuilder.CreateIndex(
                name: "IX_CustomerPhoneRevealGrants_ContactCaseId",
                table: "CustomerPhoneRevealGrants",
                column: "ContactCaseId");

            migrationBuilder.CreateIndex(
                name: "IX_CustomerPhoneRevealGrants_DriverUserId_ClientRequestId",
                table: "CustomerPhoneRevealGrants",
                columns: new[] { "DriverUserId", "ClientRequestId" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_CustomerPhoneRevealGrants_ExpiresAtUtc",
                table: "CustomerPhoneRevealGrants",
                column: "ExpiresAtUtc");

            migrationBuilder.CreateIndex(
                name: "IX_DeliveryContactAttempts_CallId",
                table: "DeliveryContactAttempts",
                column: "CallId",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_DeliveryContactAttempts_ContactCaseId_Ordinal",
                table: "DeliveryContactAttempts",
                columns: new[] { "ContactCaseId", "Ordinal" });

            migrationBuilder.CreateIndex(
                name: "IX_DeliveryContactCases_TripStopId_ActiveDriverAssignmentId",
                table: "DeliveryContactCases",
                columns: new[] { "TripStopId", "ActiveDriverAssignmentId" },
                unique: true,
                filter: "\"State\" = 'Open'");

            migrationBuilder.CreateIndex(
                name: "IX_DeliveryContactCases_TripStopId_ActiveDriverAssignmentId_Co~",
                table: "DeliveryContactCases",
                columns: new[] { "TripStopId", "ActiveDriverAssignmentId", "ContactCycleId" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_DeliveryContactCases_TripStopId_State",
                table: "DeliveryContactCases",
                columns: new[] { "TripStopId", "State" });

            migrationBuilder.CreateIndex(
                name: "IX_FamilyCheckoutBatches_FamilyGroupId",
                table: "FamilyCheckoutBatches",
                column: "FamilyGroupId",
                unique: true,
                filter: "\"Status\" IN (0, 1)");

            migrationBuilder.CreateIndex(
                name: "IX_FamilyCheckoutBatches_FamilyGroupId_IdempotencyKey",
                table: "FamilyCheckoutBatches",
                columns: new[] { "FamilyGroupId", "IdempotencyKey" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_FamilyCheckoutBatches_GatewayPaymentId",
                table: "FamilyCheckoutBatches",
                column: "GatewayPaymentId",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_FamilyCheckoutBatches_PayerCustomerUserId",
                table: "FamilyCheckoutBatches",
                column: "PayerCustomerUserId");

            migrationBuilder.CreateIndex(
                name: "IX_FamilyCheckoutBatches_Status",
                table: "FamilyCheckoutBatches",
                column: "Status");

            migrationBuilder.CreateIndex(
                name: "IX_FamilyCheckoutBatchLines_BatchId",
                table: "FamilyCheckoutBatchLines",
                column: "BatchId");

            migrationBuilder.CreateIndex(
                name: "IX_FamilyCheckoutBatchLines_FamilyMemberId",
                table: "FamilyCheckoutBatchLines",
                column: "FamilyMemberId");

            migrationBuilder.CreateIndex(
                name: "IX_FamilyCheckoutBatchLines_SubscriptionId",
                table: "FamilyCheckoutBatchLines",
                column: "SubscriptionId");

            migrationBuilder.CreateIndex(
                name: "IX_FamilyCheckoutPaymentAttempts_BatchId",
                table: "FamilyCheckoutPaymentAttempts",
                column: "BatchId");

            migrationBuilder.CreateIndex(
                name: "IX_FamilyCheckoutPaymentAttempts_BatchId_AttemptKey",
                table: "FamilyCheckoutPaymentAttempts",
                columns: new[] { "BatchId", "AttemptKey" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_FamilyCheckoutPaymentAttempts_GatewayPaymentId",
                table: "FamilyCheckoutPaymentAttempts",
                column: "GatewayPaymentId",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_VoiceCallBusyParticipants_CallId",
                table: "VoiceCallBusyParticipants",
                column: "CallId");

            migrationBuilder.CreateIndex(
                name: "IX_VoiceCallDeviceReceipts_CallId_DeviceSessionId",
                table: "VoiceCallDeviceReceipts",
                columns: new[] { "CallId", "DeviceSessionId" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_VoiceCallEventDeliveries_CallId",
                table: "VoiceCallEventDeliveries",
                column: "CallId");

            migrationBuilder.CreateIndex(
                name: "IX_VoiceCallEventDeliveries_EventId_Channel_RecipientDeviceSes~",
                table: "VoiceCallEventDeliveries",
                columns: new[] { "EventId", "Channel", "RecipientDeviceSessionId" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_VoiceCallEventDeliveries_Status_NextAttemptAtUtc",
                table: "VoiceCallEventDeliveries",
                columns: new[] { "Status", "NextAttemptAtUtc" });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "CustomerPhoneRevealGrants");

            migrationBuilder.DropTable(
                name: "DeliveryContactAttempts");

            migrationBuilder.DropTable(
                name: "FamilyCheckoutBatchLines");

            migrationBuilder.DropTable(
                name: "FamilyCheckoutPaymentAttempts");

            migrationBuilder.DropTable(
                name: "VoiceCallBusyParticipants");

            migrationBuilder.DropTable(
                name: "VoiceCallDeviceReceipts");

            migrationBuilder.DropTable(
                name: "VoiceCallEventDeliveries");

            migrationBuilder.DropTable(
                name: "DeliveryContactCases");

            migrationBuilder.DropTable(
                name: "FamilyCheckoutBatches");

            migrationBuilder.DropIndex(
                name: "IX_CallSessions_CallerDeviceSessionId",
                table: "CallSessions");

            migrationBuilder.DropIndex(
                name: "IX_CallSessions_ReceiverDeviceSessionId",
                table: "CallSessions");

            migrationBuilder.DropIndex(
                name: "IX_CallSessions_Status_MediaLeaseExpiresAtUtc",
                table: "CallSessions");

            migrationBuilder.DropIndex(
                name: "IX_CallSessions_Status_RingExpiresAtUtc",
                table: "CallSessions");

            migrationBuilder.DropIndex(
                name: "IX_CallSessions_Status_SetupExpiresAtUtc",
                table: "CallSessions");

            migrationBuilder.DropIndex(
                name: "IX_CallSessions_TerminalEventId",
                table: "CallSessions");

            migrationBuilder.DropColumn(
                name: "ActorUserId",
                table: "DeliveryHolds");

            migrationBuilder.DropColumn(
                name: "ContactCaseId",
                table: "DeliveryHolds");

            migrationBuilder.DropColumn(
                name: "FirstAttemptCallId",
                table: "DeliveryHolds");

            migrationBuilder.DropColumn(
                name: "Notes",
                table: "DeliveryHolds");

            migrationBuilder.DropColumn(
                name: "ResolutionReason",
                table: "DeliveryHolds");

            migrationBuilder.DropColumn(
                name: "ResolvedAtUtc",
                table: "DeliveryHolds");

            migrationBuilder.DropColumn(
                name: "ResumedAtUtc",
                table: "DeliveryHolds");

            migrationBuilder.DropColumn(
                name: "TripStopId",
                table: "DeliveryHolds");

            migrationBuilder.DropColumn(
                name: "CallerConnectedReportedAtUtc",
                table: "CallSessions");

            migrationBuilder.DropColumn(
                name: "CallerDeviceSessionId",
                table: "CallSessions");

            migrationBuilder.DropColumn(
                name: "CallerLastHeartbeatAtUtc",
                table: "CallSessions");

            migrationBuilder.DropColumn(
                name: "EndedByUserId",
                table: "CallSessions");

            migrationBuilder.DropColumn(
                name: "MediaLeaseExpiresAtUtc",
                table: "CallSessions");

            migrationBuilder.DropColumn(
                name: "ProtocolVersion",
                table: "CallSessions");

            migrationBuilder.DropColumn(
                name: "ReceiverConnectedReportedAtUtc",
                table: "CallSessions");

            migrationBuilder.DropColumn(
                name: "ReceiverDeviceSessionId",
                table: "CallSessions");

            migrationBuilder.DropColumn(
                name: "ReceiverLastHeartbeatAtUtc",
                table: "CallSessions");

            migrationBuilder.DropColumn(
                name: "ReceiverRingingAckAtUtc",
                table: "CallSessions");

            migrationBuilder.DropColumn(
                name: "RingExpiresAtUtc",
                table: "CallSessions");

            migrationBuilder.DropColumn(
                name: "Sequence",
                table: "CallSessions");

            migrationBuilder.DropColumn(
                name: "SetupExpiresAtUtc",
                table: "CallSessions");

            migrationBuilder.DropColumn(
                name: "TerminalEventId",
                table: "CallSessions");
        }
    }
}
