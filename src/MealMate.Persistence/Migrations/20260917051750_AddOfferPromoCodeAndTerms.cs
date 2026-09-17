using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace MealMate.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddOfferPromoCodeAndTerms : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "PromoCode",
                table: "Offers",
                type: "character varying(50)",
                maxLength: 50,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "TermsAndConditionsAr",
                table: "Offers",
                type: "character varying(2000)",
                maxLength: 2000,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "TermsAndConditionsEn",
                table: "Offers",
                type: "character varying(2000)",
                maxLength: 2000,
                nullable: true);

            migrationBuilder.CreateIndex(
                name: "IX_Offers_PromoCode",
                table: "Offers",
                column: "PromoCode");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "IX_Offers_PromoCode",
                table: "Offers");

            migrationBuilder.DropColumn(
                name: "PromoCode",
                table: "Offers");

            migrationBuilder.DropColumn(
                name: "TermsAndConditionsAr",
                table: "Offers");

            migrationBuilder.DropColumn(
                name: "TermsAndConditionsEn",
                table: "Offers");
        }
    }
}
