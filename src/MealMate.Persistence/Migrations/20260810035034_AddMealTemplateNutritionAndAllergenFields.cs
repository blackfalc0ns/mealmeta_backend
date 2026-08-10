using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace MealMate.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddMealTemplateNutritionAndAllergenFields : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<decimal>(
                name: "AddedSugarsG",
                table: "Meals",
                type: "numeric(18,3)",
                precision: 18,
                scale: 3,
                nullable: true);

            migrationBuilder.AddColumn<bool>(
                name: "AllergensReviewed",
                table: "Meals",
                type: "boolean",
                nullable: false,
                defaultValue: false);

            migrationBuilder.AddColumn<string>(
                name: "AnalysisReference",
                table: "Meals",
                type: "character varying(500)",
                maxLength: 500,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "CrossContactNotes",
                table: "Meals",
                type: "character varying(2000)",
                maxLength: 2000,
                nullable: true);

            migrationBuilder.AddColumn<bool>(
                name: "DataResponsibilityConfirmed",
                table: "Meals",
                type: "boolean",
                nullable: false,
                defaultValue: false);

            migrationBuilder.AddColumn<decimal>(
                name: "FiberG",
                table: "Meals",
                type: "numeric(18,3)",
                precision: 18,
                scale: 3,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "MayContainAllergenCodes",
                table: "Meals",
                type: "character varying(1000)",
                maxLength: 1000,
                nullable: true);

            migrationBuilder.AddColumn<decimal>(
                name: "NetCarbsG",
                table: "Meals",
                type: "numeric(18,3)",
                precision: 18,
                scale: 3,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "NutritionSource",
                table: "Meals",
                type: "character varying(500)",
                maxLength: 500,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "NutritionTagCodes",
                table: "Meals",
                type: "character varying(1000)",
                maxLength: 1000,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "PresentAllergenCodes",
                table: "Meals",
                type: "character varying(1000)",
                maxLength: 1000,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "RecipeVersion",
                table: "Meals",
                type: "character varying(100)",
                maxLength: 100,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "RestaurantNotes",
                table: "Meals",
                type: "character varying(2000)",
                maxLength: 2000,
                nullable: true);

            migrationBuilder.AddColumn<decimal>(
                name: "SaturatedFatG",
                table: "Meals",
                type: "numeric(18,3)",
                precision: 18,
                scale: 3,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "SearchExclusionTagCodes",
                table: "Meals",
                type: "character varying(1000)",
                maxLength: 1000,
                nullable: true);

            migrationBuilder.AddColumn<decimal>(
                name: "ServingWeightG",
                table: "Meals",
                type: "numeric(18,3)",
                precision: 18,
                scale: 3,
                nullable: true);

            migrationBuilder.AddColumn<decimal>(
                name: "SodiumMg",
                table: "Meals",
                type: "numeric(18,3)",
                precision: 18,
                scale: 3,
                nullable: true);

            migrationBuilder.AddColumn<decimal>(
                name: "SugarAlcoholG",
                table: "Meals",
                type: "numeric(18,3)",
                precision: 18,
                scale: 3,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "SugarAlcoholType",
                table: "Meals",
                type: "character varying(100)",
                maxLength: 100,
                nullable: true);

            migrationBuilder.AddColumn<decimal>(
                name: "TotalSugarsG",
                table: "Meals",
                type: "numeric(18,3)",
                precision: 18,
                scale: 3,
                nullable: true);

            migrationBuilder.AddColumn<DateOnly>(
                name: "VerificationDate",
                table: "Meals",
                type: "date",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "VerifiedBy",
                table: "Meals",
                type: "character varying(200)",
                maxLength: 200,
                nullable: true);

            migrationBuilder.AddColumn<decimal>(
                name: "DefaultWeightGrams",
                table: "Ingredients",
                type: "numeric(18,3)",
                precision: 18,
                scale: 3,
                nullable: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "AddedSugarsG",
                table: "Meals");

            migrationBuilder.DropColumn(
                name: "AllergensReviewed",
                table: "Meals");

            migrationBuilder.DropColumn(
                name: "AnalysisReference",
                table: "Meals");

            migrationBuilder.DropColumn(
                name: "CrossContactNotes",
                table: "Meals");

            migrationBuilder.DropColumn(
                name: "DataResponsibilityConfirmed",
                table: "Meals");

            migrationBuilder.DropColumn(
                name: "FiberG",
                table: "Meals");

            migrationBuilder.DropColumn(
                name: "MayContainAllergenCodes",
                table: "Meals");

            migrationBuilder.DropColumn(
                name: "NetCarbsG",
                table: "Meals");

            migrationBuilder.DropColumn(
                name: "NutritionSource",
                table: "Meals");

            migrationBuilder.DropColumn(
                name: "NutritionTagCodes",
                table: "Meals");

            migrationBuilder.DropColumn(
                name: "PresentAllergenCodes",
                table: "Meals");

            migrationBuilder.DropColumn(
                name: "RecipeVersion",
                table: "Meals");

            migrationBuilder.DropColumn(
                name: "RestaurantNotes",
                table: "Meals");

            migrationBuilder.DropColumn(
                name: "SaturatedFatG",
                table: "Meals");

            migrationBuilder.DropColumn(
                name: "SearchExclusionTagCodes",
                table: "Meals");

            migrationBuilder.DropColumn(
                name: "ServingWeightG",
                table: "Meals");

            migrationBuilder.DropColumn(
                name: "SodiumMg",
                table: "Meals");

            migrationBuilder.DropColumn(
                name: "SugarAlcoholG",
                table: "Meals");

            migrationBuilder.DropColumn(
                name: "SugarAlcoholType",
                table: "Meals");

            migrationBuilder.DropColumn(
                name: "TotalSugarsG",
                table: "Meals");

            migrationBuilder.DropColumn(
                name: "VerificationDate",
                table: "Meals");

            migrationBuilder.DropColumn(
                name: "VerifiedBy",
                table: "Meals");

            migrationBuilder.DropColumn(
                name: "DefaultWeightGrams",
                table: "Ingredients");
        }
    }
}
