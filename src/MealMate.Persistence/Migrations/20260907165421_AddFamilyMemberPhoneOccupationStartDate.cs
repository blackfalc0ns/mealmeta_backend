using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace MealMate.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddFamilyMemberPhoneOccupationStartDate : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "Occupation",
                table: "FamilyMembers",
                type: "character varying(80)",
                maxLength: 80,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "PhoneNumber",
                table: "FamilyMembers",
                type: "character varying(32)",
                maxLength: 32,
                nullable: true);

            migrationBuilder.AddColumn<DateOnly>(
                name: "StartDate",
                table: "FamilyMembers",
                type: "date",
                nullable: true);

            migrationBuilder.CreateIndex(
                name: "IX_FamilyMembers_PhoneNumber",
                table: "FamilyMembers",
                column: "PhoneNumber");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "IX_FamilyMembers_PhoneNumber",
                table: "FamilyMembers");

            migrationBuilder.DropColumn(
                name: "Occupation",
                table: "FamilyMembers");

            migrationBuilder.DropColumn(
                name: "PhoneNumber",
                table: "FamilyMembers");

            migrationBuilder.DropColumn(
                name: "StartDate",
                table: "FamilyMembers");
        }
    }
}
