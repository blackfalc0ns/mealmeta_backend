using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace MealMate.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddMealApprovalStatus : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<int>(
                name: "ApprovalStatus",
                table: "Meals",
                type: "integer",
                nullable: false,
                defaultValue: 0);

            migrationBuilder.AddColumn<string>(
                name: "RejectionReason",
                table: "Meals",
                type: "character varying(1000)",
                maxLength: 1000,
                nullable: true);

            migrationBuilder.AddColumn<DateTime>(
                name: "ReviewedAtUtc",
                table: "Meals",
                type: "timestamp with time zone",
                nullable: true);

            migrationBuilder.AddColumn<Guid>(
                name: "ReviewedByUserId",
                table: "Meals",
                type: "uuid",
                nullable: true);

            migrationBuilder.CreateIndex(
                name: "IX_Meals_ApprovalStatus",
                table: "Meals",
                column: "ApprovalStatus");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "IX_Meals_ApprovalStatus",
                table: "Meals");

            migrationBuilder.DropColumn(
                name: "ApprovalStatus",
                table: "Meals");

            migrationBuilder.DropColumn(
                name: "RejectionReason",
                table: "Meals");

            migrationBuilder.DropColumn(
                name: "ReviewedAtUtc",
                table: "Meals");

            migrationBuilder.DropColumn(
                name: "ReviewedByUserId",
                table: "Meals");
        }
    }
}
