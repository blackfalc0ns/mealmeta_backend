using Microsoft.EntityFrameworkCore.Migrations;
using Microsoft.EntityFrameworkCore.Infrastructure;

#nullable disable

namespace MealMate.Persistence.Migrations
{
    /// <inheritdoc />
    [DbContext(typeof(AppDbContext))]
    [Migration("20261004030952_AddUniqueActiveDriverWorkSession")]
    public partial class AddUniqueActiveDriverWorkSession : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateIndex(
                name: "IX_DriverWorkSessions_DriverProfileId_Active",
                table: "DriverWorkSessions",
                column: "DriverProfileId",
                unique: true,
                filter: "\"Status\" = 'Active'");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "IX_DriverWorkSessions_DriverProfileId_Active",
                table: "DriverWorkSessions");
        }
    }
}
