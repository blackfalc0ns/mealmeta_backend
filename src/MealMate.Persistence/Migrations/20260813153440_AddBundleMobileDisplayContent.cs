using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace MealMate.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddBundleMobileDisplayContent : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "DescriptionAr",
                table: "Bundles",
                type: "character varying(2000)",
                maxLength: 2000,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "DescriptionEn",
                table: "Bundles",
                type: "character varying(2000)",
                maxLength: 2000,
                nullable: true);

            migrationBuilder.Sql("""
                UPDATE "Bundles"
                SET
                    "DescriptionAr" = COALESCE(NULLIF("DescriptionAr", ''), "NameAr"),
                    "DescriptionEn" = COALESCE(NULLIF("DescriptionEn", ''), NULLIF("Description", ''), "NameEn");
                """);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "DescriptionAr",
                table: "Bundles");

            migrationBuilder.DropColumn(
                name: "DescriptionEn",
                table: "Bundles");
        }
    }
}
