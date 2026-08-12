using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace MealMate.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddMultiProgramDailyMenuCycles : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<Guid>(
                name: "ProgramId",
                table: "Meals",
                type: "uuid",
                nullable: true);

            migrationBuilder.CreateTable(
                name: "MenuCycleAudits",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    MenuCycleVersionId = table.Column<Guid>(type: "uuid", nullable: false),
                    Action = table.Column<string>(type: "character varying(80)", maxLength: 80, nullable: false),
                    Notes = table.Column<string>(type: "character varying(2000)", maxLength: 2000, nullable: true),
                    PerformedByUserId = table.Column<Guid>(type: "uuid", nullable: true),
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
                    table.PrimaryKey("PK_MenuCycleAudits", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "MenuCycles",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    RestaurantId = table.Column<Guid>(type: "uuid", nullable: true),
                    NameAr = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false),
                    NameEn = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false),
                    Source = table.Column<int>(type: "integer", nullable: false),
                    IsTemplate = table.Column<bool>(type: "boolean", nullable: false),
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
                    table.PrimaryKey("PK_MenuCycles", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "ProgramMealSlotRequirements",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    ProgramId = table.Column<Guid>(type: "uuid", nullable: false),
                    Slot = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false),
                    IsRequired = table.Column<bool>(type: "boolean", nullable: false),
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
                    table.PrimaryKey("PK_ProgramMealSlotRequirements", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "MenuCycleVersions",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    MenuCycleId = table.Column<Guid>(type: "uuid", nullable: false),
                    VersionNumber = table.Column<int>(type: "integer", nullable: false),
                    EffectiveFrom = table.Column<DateOnly>(type: "date", nullable: false),
                    Status = table.Column<int>(type: "integer", nullable: false),
                    ReviewNotes = table.Column<string>(type: "character varying(2000)", maxLength: 2000, nullable: true),
                    ReviewedByUserId = table.Column<Guid>(type: "uuid", nullable: true),
                    ReviewedAtUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    IsPaused = table.Column<bool>(type: "boolean", nullable: false),
                    PausedAtUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
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
                    table.PrimaryKey("PK_MenuCycleVersions", x => x.Id);
                    table.ForeignKey(
                        name: "FK_MenuCycleVersions_MenuCycles_MenuCycleId",
                        column: x => x.MenuCycleId,
                        principalTable: "MenuCycles",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "MenuDays",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    MenuCycleVersionId = table.Column<Guid>(type: "uuid", nullable: false),
                    DayNumber = table.Column<int>(type: "integer", nullable: false),
                    NameAr = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false),
                    NameEn = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false),
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
                    table.PrimaryKey("PK_MenuDays", x => x.Id);
                    table.ForeignKey(
                        name: "FK_MenuDays_MenuCycleVersions_MenuCycleVersionId",
                        column: x => x.MenuCycleVersionId,
                        principalTable: "MenuCycleVersions",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "MenuDayMeals",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    MenuDayId = table.Column<Guid>(type: "uuid", nullable: false),
                    MealId = table.Column<Guid>(type: "uuid", nullable: false),
                    ProgramId = table.Column<Guid>(type: "uuid", nullable: false),
                    Slot = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false),
                    SortOrder = table.Column<int>(type: "integer", nullable: false),
                    IsPrimary = table.Column<bool>(type: "boolean", nullable: false),
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
                    table.PrimaryKey("PK_MenuDayMeals", x => x.Id);
                    table.ForeignKey(
                        name: "FK_MenuDayMeals_Meals_MealId",
                        column: x => x.MealId,
                        principalTable: "Meals",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_MenuDayMeals_MenuDays_MenuDayId",
                        column: x => x.MenuDayId,
                        principalTable: "MenuDays",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateIndex(
                name: "IX_Meals_ProgramId",
                table: "Meals",
                column: "ProgramId");

            migrationBuilder.CreateIndex(
                name: "IX_MenuCycleAudits_MenuCycleVersionId",
                table: "MenuCycleAudits",
                column: "MenuCycleVersionId");

            migrationBuilder.CreateIndex(
                name: "IX_MenuCycles_RestaurantId_IsTemplate",
                table: "MenuCycles",
                columns: new[] { "RestaurantId", "IsTemplate" });

            migrationBuilder.CreateIndex(
                name: "IX_MenuCycleVersions_MenuCycleId_VersionNumber",
                table: "MenuCycleVersions",
                columns: new[] { "MenuCycleId", "VersionNumber" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_MenuDayMeals_MealId",
                table: "MenuDayMeals",
                column: "MealId");

            migrationBuilder.CreateIndex(
                name: "IX_MenuDayMeals_MenuDayId_MealId",
                table: "MenuDayMeals",
                columns: new[] { "MenuDayId", "MealId" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_MenuDayMeals_MenuDayId_ProgramId_Slot",
                table: "MenuDayMeals",
                columns: new[] { "MenuDayId", "ProgramId", "Slot" });

            migrationBuilder.CreateIndex(
                name: "IX_MenuDays_MenuCycleVersionId_DayNumber",
                table: "MenuDays",
                columns: new[] { "MenuCycleVersionId", "DayNumber" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_ProgramMealSlotRequirements_ProgramId_Slot",
                table: "ProgramMealSlotRequirements",
                columns: new[] { "ProgramId", "Slot" },
                unique: true);

            migrationBuilder.Sql("""
                UPDATE "Meals" AS meal
                SET "ProgramId" = menu."ProgramId"
                FROM "Menus" AS menu
                WHERE meal."MenuId" = menu."Id"
                  AND meal."ProgramId" IS NULL
                  AND menu."ProgramId" IS NOT NULL;

                CREATE TEMP TABLE legacy_cycle_map (
                    restaurant_id uuid PRIMARY KEY, cycle_id uuid NOT NULL, version_id uuid NOT NULL
                ) ON COMMIT DROP;
                INSERT INTO legacy_cycle_map
                SELECT meal."RestaurantId", gen_random_uuid(), gen_random_uuid()
                FROM "Meals" meal
                GROUP BY meal."RestaurantId";

                INSERT INTO "MenuCycles" ("Id", "RestaurantId", "NameAr", "NameEn", "Source", "IsTemplate", "CreatedAtUtc", "IsDeleted")
                SELECT cycle_id, restaurant_id, 'دورة المنيوات المرحلة', 'Imported menu cycle', 2, false, NOW(), false
                FROM legacy_cycle_map;
                INSERT INTO "MenuCycleVersions" ("Id", "MenuCycleId", "VersionNumber", "EffectiveFrom", "Status", "IsPaused", "CreatedAtUtc", "IsDeleted")
                SELECT version_id, cycle_id, 1, CURRENT_DATE, 1, false, NOW(), false FROM legacy_cycle_map;

                CREATE TEMP TABLE legacy_day_map (
                    menu_id uuid PRIMARY KEY, day_id uuid NOT NULL, version_id uuid NOT NULL, day_number integer NOT NULL
                ) ON COMMIT DROP;
                INSERT INTO legacy_day_map
                SELECT menu."Id", gen_random_uuid(), map.version_id,
                       ROW_NUMBER() OVER (PARTITION BY menu."RestaurantId" ORDER BY menu."CreatedAtUtc", menu."Id")::integer
                FROM "Menus" menu
                JOIN legacy_cycle_map map ON map.restaurant_id = menu."RestaurantId"
                WHERE EXISTS (SELECT 1 FROM "Meals" meal WHERE meal."MenuId" = menu."Id");

                INSERT INTO "MenuDays" ("Id", "MenuCycleVersionId", "DayNumber", "NameAr", "NameEn", "CreatedAtUtc", "IsDeleted")
                SELECT days.day_id, days.version_id, days.day_number, menu."NameAr", menu."NameEn", NOW(), false
                FROM legacy_day_map days JOIN "Menus" menu ON menu."Id" = days.menu_id;

                INSERT INTO "MenuDayMeals" ("Id", "MenuDayId", "MealId", "ProgramId", "Slot", "SortOrder", "IsPrimary", "CreatedAtUtc", "IsDeleted")
                SELECT gen_random_uuid(), days.day_id, meal."Id",
                       COALESCE(meal."ProgramId", '00000000-0000-0000-0000-000000000000'::uuid),
                       COALESCE(NULLIF(meal."Slot", ''), 'lunch'),
                       ROW_NUMBER() OVER (PARTITION BY days.day_id ORDER BY meal."CreatedAtUtc", meal."Id")::integer,
                       false, NOW(), false
                FROM "Meals" meal JOIN legacy_day_map days ON days.menu_id = meal."MenuId";
                """);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "MenuCycleAudits");

            migrationBuilder.DropTable(
                name: "MenuDayMeals");

            migrationBuilder.DropTable(
                name: "ProgramMealSlotRequirements");

            migrationBuilder.DropTable(
                name: "MenuDays");

            migrationBuilder.DropTable(
                name: "MenuCycleVersions");

            migrationBuilder.DropTable(
                name: "MenuCycles");

            migrationBuilder.DropIndex(
                name: "IX_Meals_ProgramId",
                table: "Meals");

            migrationBuilder.DropColumn(
                name: "ProgramId",
                table: "Meals");
        }
    }
}
