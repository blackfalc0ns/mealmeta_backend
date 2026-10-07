using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace MealMate.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddDriverReassignmentRequestLocationAndUniqueGuard : Migration
    {
        // Hand-pruned to this feature's own schema work (screens 08.01/08.02):
        // the reassignment request's optional handover coordinates on Complaints
        // and the database guard against duplicate active reassignment requests.
        // The model snapshot carries other features' pending in-flight model
        // edits; their migration operations intentionally stay with those tasks.
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<decimal>(
                name: "Latitude",
                table: "Complaints",
                type: "numeric(10,7)",
                precision: 10,
                scale: 7,
                nullable: true);

            migrationBuilder.AddColumn<decimal>(
                name: "Longitude",
                table: "Complaints",
                type: "numeric(10,7)",
                precision: 10,
                scale: 7,
                nullable: true);

            migrationBuilder.CreateIndex(
                name: "IX_Complaints_Active_ReassignmentRequest",
                table: "Complaints",
                columns: new[] { "OrderId", "Category" },
                filter: "\"Category\" = 'DriverReassignmentRequested' AND \"Status\" IN (1, 2) AND \"OrderId\" IS NOT NULL");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "IX_Complaints_Active_ReassignmentRequest",
                table: "Complaints");

            migrationBuilder.DropColumn(
                name: "Latitude",
                table: "Complaints");

            migrationBuilder.DropColumn(
                name: "Longitude",
                table: "Complaints");
        }
    }
}
