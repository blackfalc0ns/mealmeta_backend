using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace MealMate.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddDriverSupportHandovers : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<Guid>(
                name: "DriverSupportHandoverBoxId",
                table: "DeliveryHolds",
                type: "uuid",
                nullable: true);

            migrationBuilder.CreateTable(
                name: "DriverSupportHandovers",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    ComplaintId = table.Column<Guid>(type: "uuid", nullable: false),
                    RequestedIssueId = table.Column<Guid>(type: "uuid", nullable: false),
                    TripId = table.Column<Guid>(type: "uuid", nullable: false),
                    OriginalDriverProfileId = table.Column<Guid>(type: "uuid", nullable: false),
                    ReplacementDriverProfileId = table.Column<Guid>(type: "uuid", nullable: false),
                    RestaurantId = table.Column<Guid>(type: "uuid", nullable: false),
                    RequestedByUserId = table.Column<Guid>(type: "uuid", nullable: false),
                    IdempotencyKey = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false),
                    Status = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false),
                    Notes = table.Column<string>(type: "character varying(500)", maxLength: 500, nullable: true),
                    PickupLatitude = table.Column<decimal>(type: "numeric(10,7)", precision: 10, scale: 7, nullable: false),
                    PickupLongitude = table.Column<decimal>(type: "numeric(10,7)", precision: 10, scale: 7, nullable: false),
                    PickupLocationSource = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false),
                    PickupLocationRecordedAtUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    AssignedAtUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    ArrivedAtUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    ArrivalLatitude = table.Column<decimal>(type: "numeric(10,7)", precision: 10, scale: 7, nullable: true),
                    ArrivalLongitude = table.Column<decimal>(type: "numeric(10,7)", precision: 10, scale: 7, nullable: true),
                    CompletedAtUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    CancelledAtUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    CancellationReason = table.Column<string>(type: "character varying(500)", maxLength: 500, nullable: true),
                    ReplacementTripId = table.Column<Guid>(type: "uuid", nullable: true),
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
                    table.PrimaryKey("PK_DriverSupportHandovers", x => x.Id);
                    table.CheckConstraint("CK_DriverSupportHandovers_DifferentDrivers", "\"OriginalDriverProfileId\" <> \"ReplacementDriverProfileId\"");
                    table.CheckConstraint("CK_DriverSupportHandovers_Location", "\"PickupLatitude\" BETWEEN -90 AND 90 AND \"PickupLongitude\" BETWEEN -180 AND 180");
                    table.CheckConstraint("CK_DriverSupportHandovers_Status", "\"Status\" IN ('Assigned','Arrived','Receiving','Completed','Cancelled')");
                    table.ForeignKey(
                        name: "FK_DriverSupportHandovers_Complaints_ComplaintId",
                        column: x => x.ComplaintId,
                        principalTable: "Complaints",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_DriverSupportHandovers_DriverProfiles_OriginalDriverProfile~",
                        column: x => x.OriginalDriverProfileId,
                        principalTable: "DriverProfiles",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_DriverSupportHandovers_DriverProfiles_ReplacementDriverProf~",
                        column: x => x.ReplacementDriverProfileId,
                        principalTable: "DriverProfiles",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_DriverSupportHandovers_Trips_TripId",
                        column: x => x.TripId,
                        principalTable: "Trips",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "DriverSupportHandoverBoxes",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    HandoverId = table.Column<Guid>(type: "uuid", nullable: false),
                    TripStopId = table.Column<Guid>(type: "uuid", nullable: false),
                    ManifestCode = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false),
                    Status = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false),
                    ScannedAtUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    ScanMethod = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: true),
                    FollowUpNote = table.Column<string>(type: "character varying(500)", maxLength: 500, nullable: true),
                    FollowUpId = table.Column<Guid>(type: "uuid", nullable: true),
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
                    table.PrimaryKey("PK_DriverSupportHandoverBoxes", x => x.Id);
                    table.CheckConstraint("CK_DriverSupportHandoverBoxes_Status", "\"Status\" IN ('Pending','Received','Missing','Damaged')");
                    table.ForeignKey(
                        name: "FK_DriverSupportHandoverBoxes_DeliveryHolds_FollowUpId",
                        column: x => x.FollowUpId,
                        principalTable: "DeliveryHolds",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_DriverSupportHandoverBoxes_DriverSupportHandovers_HandoverId",
                        column: x => x.HandoverId,
                        principalTable: "DriverSupportHandovers",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "FK_DriverSupportHandoverBoxes_TripStops_TripStopId",
                        column: x => x.TripStopId,
                        principalTable: "TripStops",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateIndex(
                name: "IX_DeliveryHolds_DriverSupportHandoverBoxId",
                table: "DeliveryHolds",
                column: "DriverSupportHandoverBoxId");

            migrationBuilder.CreateIndex(
                name: "IX_DriverSupportHandoverBoxes_FollowUpId",
                table: "DriverSupportHandoverBoxes",
                column: "FollowUpId");

            migrationBuilder.CreateIndex(
                name: "IX_DriverSupportHandoverBoxes_HandoverId_TripStopId",
                table: "DriverSupportHandoverBoxes",
                columns: new[] { "HandoverId", "TripStopId" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_DriverSupportHandoverBoxes_TripStopId",
                table: "DriverSupportHandoverBoxes",
                column: "TripStopId");

            migrationBuilder.CreateIndex(
                name: "IX_DriverSupportHandovers_ComplaintId_Status",
                table: "DriverSupportHandovers",
                columns: new[] { "ComplaintId", "Status" });

            migrationBuilder.CreateIndex(
                name: "IX_DriverSupportHandovers_OriginalDriverProfileId",
                table: "DriverSupportHandovers",
                column: "OriginalDriverProfileId");

            migrationBuilder.CreateIndex(
                name: "IX_DriverSupportHandovers_RequestedByUserId_IdempotencyKey",
                table: "DriverSupportHandovers",
                columns: new[] { "RequestedByUserId", "IdempotencyKey" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_DriverSupportHandovers_TripId_Status",
                table: "DriverSupportHandovers",
                columns: new[] { "TripId", "Status" });

            migrationBuilder.CreateIndex(
                name: "UX_DriverSupportHandovers_ActiveComplaint",
                table: "DriverSupportHandovers",
                column: "ComplaintId",
                unique: true,
                filter: "\"Status\" IN ('Assigned','Arrived','Receiving')");

            migrationBuilder.CreateIndex(
                name: "UX_DriverSupportHandovers_ActiveReplacement",
                table: "DriverSupportHandovers",
                column: "ReplacementDriverProfileId",
                unique: true,
                filter: "\"Status\" IN ('Assigned','Arrived','Receiving')");

            migrationBuilder.CreateIndex(
                name: "UX_DriverSupportHandovers_ActiveTrip",
                table: "DriverSupportHandovers",
                column: "TripId",
                unique: true,
                filter: "\"Status\" IN ('Assigned','Arrived','Receiving')");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "DriverSupportHandoverBoxes");

            migrationBuilder.DropTable(
                name: "DriverSupportHandovers");

            migrationBuilder.DropIndex(
                name: "IX_DeliveryHolds_DriverSupportHandoverBoxId",
                table: "DeliveryHolds");

            migrationBuilder.DropColumn(
                name: "DriverSupportHandoverBoxId",
                table: "DeliveryHolds");
        }
    }
}
