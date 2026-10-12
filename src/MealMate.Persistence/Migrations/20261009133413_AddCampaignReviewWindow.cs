using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace MealMate.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddCampaignReviewWindow : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<int>(
                name: "ApprovedSnapshotVersion",
                table: "Campaigns",
                type: "integer",
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "CurrentSnapshotVersion",
                table: "Campaigns",
                type: "integer",
                nullable: false,
                defaultValue: 1);

            migrationBuilder.AddColumn<DateTime>(
                name: "ReviewEndsAtUtc",
                table: "Campaigns",
                type: "timestamp with time zone",
                nullable: true);

            migrationBuilder.AddColumn<DateTime>(
                name: "ReviewStartsAtUtc",
                table: "Campaigns",
                type: "timestamp with time zone",
                nullable: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "ApprovedSnapshotVersion",
                table: "Campaigns");

            migrationBuilder.DropColumn(
                name: "CurrentSnapshotVersion",
                table: "Campaigns");

            migrationBuilder.DropColumn(
                name: "ReviewEndsAtUtc",
                table: "Campaigns");

            migrationBuilder.DropColumn(
                name: "ReviewStartsAtUtc",
                table: "Campaigns");
        }
    }
}
