using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace MealMate.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddCollaborativeCampaignsCheckoutAndOutbox : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "DeclineReason",
                table: "CampaignRestaurantEnrollments",
                type: "text",
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "DedicatedDailyCapacity",
                table: "CampaignRestaurantEnrollments",
                type: "integer",
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "ParticipationStatus",
                table: "CampaignRestaurantEnrollments",
                type: "integer",
                nullable: false,
                defaultValue: 0);

            migrationBuilder.AddColumn<Guid>(
                name: "CampaignCheckoutSessionId",
                table: "CampaignPaymentTransactions",
                type: "uuid",
                nullable: true);

            migrationBuilder.CreateTable(
                name: "CampaignCheckoutSessions",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    CampaignId = table.Column<Guid>(type: "uuid", nullable: false),
                    BundleId = table.Column<Guid>(type: "uuid", nullable: false),
                    CustomerId = table.Column<Guid>(type: "uuid", nullable: false),
                    Status = table.Column<int>(type: "integer", nullable: false),
                    ExpiresAtUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    ProcessingLeaseExpiresAtUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    PriceQuoteSnapshotId = table.Column<Guid>(type: "uuid", nullable: true),
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
                    table.PrimaryKey("PK_CampaignCheckoutSessions", x => x.Id);
                    table.ForeignKey(
                        name: "FK_CampaignCheckoutSessions_Campaigns_CampaignId",
                        column: x => x.CampaignId,
                        principalTable: "Campaigns",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "CampaignProfitabilitySnapshots",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    CampaignId = table.Column<Guid>(type: "uuid", nullable: false),
                    ProfitabilityVersion = table.Column<int>(type: "integer", nullable: false),
                    MinimumExpectedMarginPct = table.Column<decimal>(type: "numeric", nullable: false),
                    OverallExpectedMarginPct = table.Column<decimal>(type: "numeric", nullable: false),
                    OverallWorstCaseMarginPct = table.Column<decimal>(type: "numeric", nullable: false),
                    IsViable = table.Column<bool>(type: "boolean", nullable: false),
                    RequiresOverride = table.Column<bool>(type: "boolean", nullable: false),
                    OverrideReason = table.Column<string>(type: "text", nullable: true),
                    OverrideApprovedByUserId = table.Column<Guid>(type: "uuid", nullable: true),
                    OverrideApprovedAtUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    DetailsJson = table.Column<string>(type: "text", nullable: false),
                    EvaluatedAtUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
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
                    table.PrimaryKey("PK_CampaignProfitabilitySnapshots", x => x.Id);
                    table.ForeignKey(
                        name: "FK_CampaignProfitabilitySnapshots_Campaigns_CampaignId",
                        column: x => x.CampaignId,
                        principalTable: "Campaigns",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "CampaignTierSnapshots",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    CampaignId = table.Column<Guid>(type: "uuid", nullable: false),
                    TierVersion = table.Column<int>(type: "integer", nullable: false),
                    CapturedAtUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    RankingsJson = table.Column<string>(type: "text", nullable: false),
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
                    table.PrimaryKey("PK_CampaignTierSnapshots", x => x.Id);
                    table.ForeignKey(
                        name: "FK_CampaignTierSnapshots_Campaigns_CampaignId",
                        column: x => x.CampaignId,
                        principalTable: "Campaigns",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "InboxProcessedEvents",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    ConsumerName = table.Column<string>(type: "character varying(256)", maxLength: 256, nullable: false),
                    EventId = table.Column<Guid>(type: "uuid", nullable: false),
                    ProcessedAtUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_InboxProcessedEvents", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "OutboxMessages",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    EventType = table.Column<string>(type: "character varying(256)", maxLength: 256, nullable: false),
                    PayloadJson = table.Column<string>(type: "text", nullable: false),
                    CreatedAtUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    ProcessedAtUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    RetryCount = table.Column<int>(type: "integer", nullable: false),
                    LastAttemptedAtUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    LastError = table.Column<string>(type: "character varying(4000)", maxLength: 4000, nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_OutboxMessages", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "CampaignReservationAllocations",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    CheckoutSessionId = table.Column<Guid>(type: "uuid", nullable: false),
                    CampaignId = table.Column<Guid>(type: "uuid", nullable: false),
                    BundleId = table.Column<Guid>(type: "uuid", nullable: false),
                    ServiceDate = table.Column<DateOnly>(type: "date", nullable: false),
                    RestaurantId = table.Column<Guid>(type: "uuid", nullable: false),
                    Units = table.Column<int>(type: "integer", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_CampaignReservationAllocations", x => x.Id);
                    table.ForeignKey(
                        name: "FK_CampaignReservationAllocations_CampaignCheckoutSessions_Che~",
                        column: x => x.CheckoutSessionId,
                        principalTable: "CampaignCheckoutSessions",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "IX_CampaignPaymentTransactions_CampaignCheckoutSessionId",
                table: "CampaignPaymentTransactions",
                column: "CampaignCheckoutSessionId");

            migrationBuilder.CreateIndex(
                name: "IX_CampaignCheckoutSessions_CampaignId",
                table: "CampaignCheckoutSessions",
                column: "CampaignId");

            migrationBuilder.CreateIndex(
                name: "IX_CampaignProfitabilitySnapshots_CampaignId",
                table: "CampaignProfitabilitySnapshots",
                column: "CampaignId");

            migrationBuilder.CreateIndex(
                name: "IX_CampaignReservationAllocations_CheckoutSessionId",
                table: "CampaignReservationAllocations",
                column: "CheckoutSessionId");

            migrationBuilder.CreateIndex(
                name: "IX_CampaignTierSnapshots_CampaignId",
                table: "CampaignTierSnapshots",
                column: "CampaignId");

            migrationBuilder.CreateIndex(
                name: "IX_InboxProcessedEvents_ConsumerName_EventId",
                table: "InboxProcessedEvents",
                columns: new[] { "ConsumerName", "EventId" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_OutboxMessages_ProcessedAtUtc_CreatedAtUtc",
                table: "OutboxMessages",
                columns: new[] { "ProcessedAtUtc", "CreatedAtUtc" });

            migrationBuilder.AddForeignKey(
                name: "FK_CampaignPaymentTransactions_CampaignCheckoutSessions_Campai~",
                table: "CampaignPaymentTransactions",
                column: "CampaignCheckoutSessionId",
                principalTable: "CampaignCheckoutSessions",
                principalColumn: "Id");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "FK_CampaignPaymentTransactions_CampaignCheckoutSessions_Campai~",
                table: "CampaignPaymentTransactions");

            migrationBuilder.DropTable(
                name: "CampaignProfitabilitySnapshots");

            migrationBuilder.DropTable(
                name: "CampaignReservationAllocations");

            migrationBuilder.DropTable(
                name: "CampaignTierSnapshots");

            migrationBuilder.DropTable(
                name: "InboxProcessedEvents");

            migrationBuilder.DropTable(
                name: "OutboxMessages");

            migrationBuilder.DropTable(
                name: "CampaignCheckoutSessions");

            migrationBuilder.DropIndex(
                name: "IX_CampaignPaymentTransactions_CampaignCheckoutSessionId",
                table: "CampaignPaymentTransactions");

            migrationBuilder.DropColumn(
                name: "DeclineReason",
                table: "CampaignRestaurantEnrollments");

            migrationBuilder.DropColumn(
                name: "DedicatedDailyCapacity",
                table: "CampaignRestaurantEnrollments");

            migrationBuilder.DropColumn(
                name: "ParticipationStatus",
                table: "CampaignRestaurantEnrollments");

            migrationBuilder.DropColumn(
                name: "CampaignCheckoutSessionId",
                table: "CampaignPaymentTransactions");
        }
    }
}
