using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace MealMate.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddCustomerPreferenceCompletion : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<DateTime>(
                name: "AllergiesCompletedAtUtc",
                table: "CustomerProfiles",
                type: "timestamp with time zone",
                nullable: true);

            migrationBuilder.AddColumn<DateTime>(
                name: "DietaryPreferencesCompletedAtUtc",
                table: "CustomerProfiles",
                type: "timestamp with time zone",
                nullable: true);

            migrationBuilder.Sql("""
                UPDATE "CustomerProfiles" AS p
                SET "AllergiesCompletedAtUtc" = NOW() AT TIME ZONE 'utc'
                WHERE "AllergiesCompletedAtUtc" IS NULL
                  AND EXISTS (
                      SELECT 1
                      FROM "CustomerAllergyPreferences" AS a
                      WHERE a."CustomerUserId" = p."UserId"
                        AND a."IsDislike" = FALSE
                        AND a."IsDeleted" = FALSE
                  );

                UPDATE "CustomerProfiles" AS p
                SET "DietaryPreferencesCompletedAtUtc" = NOW() AT TIME ZONE 'utc'
                WHERE "DietaryPreferencesCompletedAtUtc" IS NULL
                  AND EXISTS (
                      SELECT 1
                      FROM "CustomerDietaryPreferences" AS d
                      WHERE d."CustomerUserId" = p."UserId"
                        AND d."IsDeleted" = FALSE
                  );
                """);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "AllergiesCompletedAtUtc",
                table: "CustomerProfiles");

            migrationBuilder.DropColumn(
                name: "DietaryPreferencesCompletedAtUtc",
                table: "CustomerProfiles");
        }
    }
}
