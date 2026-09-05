using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace MealMate.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddDeliveryManagerRegistrations : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<DateTime>(
                name: "ContractExpiryUtc",
                table: "DriverRegistrations",
                type: "timestamp with time zone",
                nullable: true);

            migrationBuilder.AddColumn<DateTime>(
                name: "LicenseExpiryUtc",
                table: "DriverRegistrations",
                type: "timestamp with time zone",
                nullable: true);

            migrationBuilder.AddColumn<DateTime>(
                name: "NationalIdExpiryUtc",
                table: "DriverRegistrations",
                type: "timestamp with time zone",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "VehicleModel",
                table: "DriverRegistrations",
                type: "character varying(100)",
                maxLength: 100,
                nullable: true);

            migrationBuilder.AddColumn<DateTime>(
                name: "VehicleRegistrationExpiryUtc",
                table: "DriverRegistrations",
                type: "timestamp with time zone",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "VehicleModel",
                table: "DriverProfiles",
                type: "character varying(100)",
                maxLength: 100,
                nullable: true);

            migrationBuilder.CreateTable(
                name: "DeliveryManagerRegistrations",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    SubmittedByUserId = table.Column<Guid>(type: "uuid", nullable: true),
                    RestaurantId = table.Column<Guid>(type: "uuid", nullable: false),
                    FullName = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false),
                    Phone = table.Column<string>(type: "character varying(30)", maxLength: 30, nullable: false),
                    Email = table.Column<string>(type: "character varying(256)", maxLength: 256, nullable: false),
                    NationalId = table.Column<string>(type: "character varying(50)", maxLength: 50, nullable: false),
                    NationalIdExpiryUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    ContractExpiryUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    CoverageType = table.Column<int>(type: "integer", nullable: false),
                    CoverageScopeId = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: true),
                    CoverageScopeName = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: true),
                    Status = table.Column<int>(type: "integer", nullable: false),
                    RejectionReason = table.Column<string>(type: "character varying(1000)", maxLength: 1000, nullable: true),
                    ChangeRequestNotes = table.Column<string>(type: "character varying(2000)", maxLength: 2000, nullable: true),
                    ReviewedByUserId = table.Column<Guid>(type: "uuid", nullable: true),
                    ReviewedAtUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    ResultingUserId = table.Column<Guid>(type: "uuid", nullable: true),
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
                    table.PrimaryKey("PK_DeliveryManagerRegistrations", x => x.Id);
                });

            migrationBuilder.CreateIndex(
                name: "IX_DeliveryManagerRegistrations_Email",
                table: "DeliveryManagerRegistrations",
                column: "Email");

            migrationBuilder.CreateIndex(
                name: "IX_DeliveryManagerRegistrations_NationalId",
                table: "DeliveryManagerRegistrations",
                column: "NationalId");

            migrationBuilder.CreateIndex(
                name: "IX_DeliveryManagerRegistrations_Phone",
                table: "DeliveryManagerRegistrations",
                column: "Phone");

            migrationBuilder.CreateIndex(
                name: "IX_DeliveryManagerRegistrations_RestaurantId",
                table: "DeliveryManagerRegistrations",
                column: "RestaurantId");

            migrationBuilder.CreateIndex(
                name: "IX_DeliveryManagerRegistrations_Status",
                table: "DeliveryManagerRegistrations",
                column: "Status");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "DeliveryManagerRegistrations");

            migrationBuilder.DropColumn(
                name: "ContractExpiryUtc",
                table: "DriverRegistrations");

            migrationBuilder.DropColumn(
                name: "LicenseExpiryUtc",
                table: "DriverRegistrations");

            migrationBuilder.DropColumn(
                name: "NationalIdExpiryUtc",
                table: "DriverRegistrations");

            migrationBuilder.DropColumn(
                name: "VehicleModel",
                table: "DriverRegistrations");

            migrationBuilder.DropColumn(
                name: "VehicleRegistrationExpiryUtc",
                table: "DriverRegistrations");

            migrationBuilder.DropColumn(
                name: "VehicleModel",
                table: "DriverProfiles");
        }
    }
}
