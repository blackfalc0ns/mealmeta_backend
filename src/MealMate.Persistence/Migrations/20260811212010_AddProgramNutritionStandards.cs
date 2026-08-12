using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace MealMate.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddProgramNutritionStandards : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "ShortCode",
                table: "Programs",
                type: "character varying(10)",
                maxLength: 10,
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "BreakfastCount",
                table: "Bundles",
                type: "integer",
                nullable: false,
                defaultValue: 0);

            migrationBuilder.AddColumn<int>(
                name: "DinnerCount",
                table: "Bundles",
                type: "integer",
                nullable: false,
                defaultValue: 0);

            migrationBuilder.AddColumn<int>(
                name: "LunchCount",
                table: "Bundles",
                type: "integer",
                nullable: false,
                defaultValue: 0);

            migrationBuilder.AddColumn<int>(
                name: "SnackCount",
                table: "Bundles",
                type: "integer",
                nullable: false,
                defaultValue: 0);

            migrationBuilder.CreateTable(
                name: "ProgramNutritionStandards",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    ProgramId = table.Column<Guid>(type: "uuid", nullable: false),
                    MealCategory = table.Column<string>(type: "character varying(10)", maxLength: 10, nullable: false),
                    KcalMin = table.Column<decimal>(type: "numeric(18,3)", precision: 18, scale: 3, nullable: false),
                    KcalMax = table.Column<decimal>(type: "numeric(18,3)", precision: 18, scale: 3, nullable: false),
                    ProteinGMin = table.Column<decimal>(type: "numeric(18,3)", precision: 18, scale: 3, nullable: false),
                    ProteinGMax = table.Column<decimal>(type: "numeric(18,3)", precision: 18, scale: 3, nullable: false),
                    CarbMode = table.Column<string>(type: "character varying(10)", maxLength: 10, nullable: false),
                    CarbsGMin = table.Column<decimal>(type: "numeric(18,3)", precision: 18, scale: 3, nullable: false),
                    CarbsGMax = table.Column<decimal>(type: "numeric(18,3)", precision: 18, scale: 3, nullable: false),
                    FatGMin = table.Column<decimal>(type: "numeric(18,3)", precision: 18, scale: 3, nullable: false),
                    FatGMax = table.Column<decimal>(type: "numeric(18,3)", precision: 18, scale: 3, nullable: false),
                    FiberGMin = table.Column<decimal>(type: "numeric(18,3)", precision: 18, scale: 3, nullable: false),
                    CookedProteinGMin = table.Column<decimal>(type: "numeric(18,3)", precision: 18, scale: 3, nullable: false),
                    CookedProteinGMax = table.Column<decimal>(type: "numeric(18,3)", precision: 18, scale: 3, nullable: false),
                    CookedCarbsGMin = table.Column<decimal>(type: "numeric(18,3)", precision: 18, scale: 3, nullable: false),
                    CookedCarbsGMax = table.Column<decimal>(type: "numeric(18,3)", precision: 18, scale: 3, nullable: false),
                    KetoDayNetCarbsLimit = table.Column<decimal>(type: "numeric(18,3)", precision: 18, scale: 3, nullable: true),
                    StandardVersion = table.Column<string>(type: "character varying(40)", maxLength: 40, nullable: false),
                    IsActive = table.Column<bool>(type: "boolean", nullable: false),
                    CreatedAtUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    CreatedByUserId = table.Column<Guid>(type: "uuid", nullable: true),
                    ModifiedAtUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    ModifiedByUserId = table.Column<Guid>(type: "uuid", nullable: true),
                    IsDeleted = table.Column<bool>(type: "boolean", nullable: false),
                    DeletedAtUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    DeletedByUserId = table.Column<Guid>(type: "uuid", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_ProgramNutritionStandards", x => x.Id);
                });

            migrationBuilder.CreateIndex(
                name: "IX_Programs_ShortCode",
                table: "Programs",
                column: "ShortCode");

            migrationBuilder.CreateIndex(
                name: "IX_ProgramNutritionStandards_ProgramId_IsActive",
                table: "ProgramNutritionStandards",
                columns: new[] { "ProgramId", "IsActive" });

            migrationBuilder.CreateIndex(
                name: "IX_ProgramNutritionStandards_ProgramId_MealCategory_StandardVe~",
                table: "ProgramNutritionStandards",
                columns: new[] { "ProgramId", "MealCategory", "StandardVersion" },
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "ProgramNutritionStandards");

            migrationBuilder.DropIndex(
                name: "IX_Programs_ShortCode",
                table: "Programs");

            migrationBuilder.DropColumn(
                name: "ShortCode",
                table: "Programs");

            migrationBuilder.DropColumn(
                name: "BreakfastCount",
                table: "Bundles");

            migrationBuilder.DropColumn(
                name: "DinnerCount",
                table: "Bundles");

            migrationBuilder.DropColumn(
                name: "LunchCount",
                table: "Bundles");

            migrationBuilder.DropColumn(
                name: "SnackCount",
                table: "Bundles");
        }
    }
}
