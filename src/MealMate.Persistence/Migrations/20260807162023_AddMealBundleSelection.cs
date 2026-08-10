using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace MealMate.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddMealBundleSelection : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<Guid>(
                name: "BundleId",
                table: "Meals",
                type: "uuid",
                nullable: true);

            migrationBuilder.CreateIndex(
                name: "IX_Meals_BundleId",
                table: "Meals",
                column: "BundleId");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "IX_Meals_BundleId",
                table: "Meals");

            migrationBuilder.DropColumn(
                name: "BundleId",
                table: "Meals");
        }
    }
}
