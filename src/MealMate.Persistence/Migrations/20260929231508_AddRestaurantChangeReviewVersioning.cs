using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace MealMate.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddRestaurantChangeReviewVersioning : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<long>(
                name: "BaseVersion",
                table: "RestaurantSettingsChangeRequests",
                type: "bigint",
                nullable: false,
                defaultValue: 1L);

            migrationBuilder.AddColumn<int>(
                name: "Category",
                table: "RestaurantSettingsChangeRequests",
                type: "integer",
                nullable: false,
                defaultValue: 1);

            migrationBuilder.AddColumn<string>(
                name: "ChangedFieldsJson",
                table: "RestaurantSettingsChangeRequests",
                type: "character varying(4000)",
                maxLength: 4000,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "DecisionEventsJson",
                table: "RestaurantSettingsChangeRequests",
                type: "text",
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "PayloadVersion",
                table: "RestaurantSettingsChangeRequests",
                type: "integer",
                nullable: false,
                defaultValue: 1);

            migrationBuilder.AddColumn<long>(
                name: "ApprovedVersion",
                table: "RestaurantRegistrations",
                type: "bigint",
                nullable: false,
                defaultValue: 1L);

            migrationBuilder.AddColumn<long>(
                name: "ApprovedVersion",
                table: "RestaurantProfiles",
                type: "bigint",
                nullable: false,
                defaultValue: 1L);

            migrationBuilder.CreateIndex(
                name: "IX_RestaurantSettingsChangeRequests_RestaurantId_Category_Stat~",
                table: "RestaurantSettingsChangeRequests",
                columns: new[] { "RestaurantId", "Category", "Status" });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "IX_RestaurantSettingsChangeRequests_RestaurantId_Category_Stat~",
                table: "RestaurantSettingsChangeRequests");

            migrationBuilder.DropColumn(
                name: "BaseVersion",
                table: "RestaurantSettingsChangeRequests");

            migrationBuilder.DropColumn(
                name: "Category",
                table: "RestaurantSettingsChangeRequests");

            migrationBuilder.DropColumn(
                name: "ChangedFieldsJson",
                table: "RestaurantSettingsChangeRequests");

            migrationBuilder.DropColumn(
                name: "DecisionEventsJson",
                table: "RestaurantSettingsChangeRequests");

            migrationBuilder.DropColumn(
                name: "PayloadVersion",
                table: "RestaurantSettingsChangeRequests");

            migrationBuilder.DropColumn(
                name: "ApprovedVersion",
                table: "RestaurantRegistrations");

            migrationBuilder.DropColumn(
                name: "ApprovedVersion",
                table: "RestaurantProfiles");
        }
    }
}
