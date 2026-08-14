using MealMate.Persistence;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace MealMate.Persistence.Migrations
{
    /// <inheritdoc />
    [DbContext(typeof(AppDbContext))]
    [Migration("20260814200000_AddIngredientCoreIcons")]
    public partial class AddIngredientCoreIcons : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<bool>(
                name: "IsCore",
                table: "Ingredients",
                type: "boolean",
                nullable: false,
                defaultValue: false);

            migrationBuilder.AddColumn<string>(
                name: "MealIconUrl",
                table: "Ingredients",
                type: "character varying(2048)",
                maxLength: 2048,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "RestaurantIconUrl",
                table: "Ingredients",
                type: "character varying(2048)",
                maxLength: 2048,
                nullable: true);

            migrationBuilder.CreateIndex(
                name: "IX_Ingredients_RestaurantId_IsCore",
                table: "Ingredients",
                columns: new[] { "RestaurantId", "IsCore" });

            migrationBuilder.Sql("""
                UPDATE "Ingredients"
                SET "IsCore" = TRUE
                WHERE LOWER(COALESCE("Category", '')) IN
                    ('meat', 'beef', 'lamb', 'poultry', 'chicken', 'seafood', 'fish', 'oils', 'oil');
                """);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "IX_Ingredients_RestaurantId_IsCore",
                table: "Ingredients");

            migrationBuilder.DropColumn(
                name: "IsCore",
                table: "Ingredients");

            migrationBuilder.DropColumn(
                name: "MealIconUrl",
                table: "Ingredients");

            migrationBuilder.DropColumn(
                name: "RestaurantIconUrl",
                table: "Ingredients");
        }
    }
}
