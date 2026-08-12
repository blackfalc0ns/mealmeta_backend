using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace MealMate.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class NormalizeMealSlotsToFourCategories : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql("""
                UPDATE "Meals"
                SET "Slot" = CASE lower(trim("Slot"))
                    WHEN 'main' THEN 'lunch'
                    WHEN 'salad' THEN 'dinner'
                    WHEN 'juice' THEN 'snack'
                    WHEN 'افطار' THEN 'breakfast'
                    WHEN 'فطار' THEN 'breakfast'
                    WHEN 'غداء' THEN 'lunch'
                    WHEN 'غدا' THEN 'lunch'
                    WHEN 'عشاء' THEN 'dinner'
                    WHEN 'عشا' THEN 'dinner'
                    WHEN 'سناك' THEN 'snack'
                    ELSE "Slot"
                END
                WHERE "Slot" IS NOT NULL;
                """);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            // The old categories collapse into the new taxonomy, so this data migration is intentionally irreversible.
        }
    }
}
