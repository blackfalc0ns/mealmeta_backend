using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace MealMate.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddFamilyActionPermissions : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<bool>(
                name: "CancelSubscription",
                table: "FamilyMemberPermissions",
                type: "boolean",
                nullable: false,
                defaultValue: false);

            migrationBuilder.AddColumn<bool>(
                name: "ConfirmMeals",
                table: "FamilyMemberPermissions",
                type: "boolean",
                nullable: false,
                defaultValue: false);

            migrationBuilder.AddColumn<bool>(
                name: "RequestFreeze",
                table: "FamilyMemberPermissions",
                type: "boolean",
                nullable: false,
                defaultValue: false);

            migrationBuilder.AddColumn<bool>(
                name: "RequestRenewal",
                table: "FamilyMemberPermissions",
                type: "boolean",
                nullable: false,
                defaultValue: false);

            migrationBuilder.AddColumn<bool>(
                name: "RequestUpgrade",
                table: "FamilyMemberPermissions",
                type: "boolean",
                nullable: false,
                defaultValue: false);

            migrationBuilder.AddColumn<bool>(
                name: "SelectRestaurant",
                table: "FamilyMemberPermissions",
                type: "boolean",
                nullable: false,
                defaultValue: false);

            migrationBuilder.AddColumn<bool>(
                name: "ViewCalendar",
                table: "FamilyMemberPermissions",
                type: "boolean",
                nullable: false,
                defaultValue: false);

            migrationBuilder.AddColumn<bool>(
                name: "ViewFamilyCalendars",
                table: "FamilyMemberPermissions",
                type: "boolean",
                nullable: false,
                defaultValue: false);

            migrationBuilder.AddColumn<bool>(
                name: "ViewFamilyInvoices",
                table: "FamilyMemberPermissions",
                type: "boolean",
                nullable: false,
                defaultValue: false);

            migrationBuilder.AddColumn<bool>(
                name: "ViewFamilyPlans",
                table: "FamilyMemberPermissions",
                type: "boolean",
                nullable: false,
                defaultValue: false);

            migrationBuilder.AddColumn<bool>(
                name: "ViewInvoices",
                table: "FamilyMemberPermissions",
                type: "boolean",
                nullable: false,
                defaultValue: false);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "CancelSubscription",
                table: "FamilyMemberPermissions");

            migrationBuilder.DropColumn(
                name: "ConfirmMeals",
                table: "FamilyMemberPermissions");

            migrationBuilder.DropColumn(
                name: "RequestFreeze",
                table: "FamilyMemberPermissions");

            migrationBuilder.DropColumn(
                name: "RequestRenewal",
                table: "FamilyMemberPermissions");

            migrationBuilder.DropColumn(
                name: "RequestUpgrade",
                table: "FamilyMemberPermissions");

            migrationBuilder.DropColumn(
                name: "SelectRestaurant",
                table: "FamilyMemberPermissions");

            migrationBuilder.DropColumn(
                name: "ViewCalendar",
                table: "FamilyMemberPermissions");

            migrationBuilder.DropColumn(
                name: "ViewFamilyCalendars",
                table: "FamilyMemberPermissions");

            migrationBuilder.DropColumn(
                name: "ViewFamilyInvoices",
                table: "FamilyMemberPermissions");

            migrationBuilder.DropColumn(
                name: "ViewFamilyPlans",
                table: "FamilyMemberPermissions");

            migrationBuilder.DropColumn(
                name: "ViewInvoices",
                table: "FamilyMemberPermissions");
        }
    }
}
