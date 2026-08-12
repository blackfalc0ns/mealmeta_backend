using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace MealMate.Persistence.Migrations;

/// <inheritdoc />
public partial class EvolveMenuCycleToMenuPlan : Migration
{
    /// <inheritdoc />
    protected override void Up(MigrationBuilder migrationBuilder)
    {
        // Drop FKs that block renames.
        migrationBuilder.DropForeignKey(
            name: "FK_MenuDays_MenuCycleVersions_MenuCycleVersionId",
            table: "MenuDays");

        migrationBuilder.DropForeignKey(
            name: "FK_MenuCycleVersions_MenuCycles_MenuCycleId",
            table: "MenuCycleVersions");

        // Rename core tables.
        migrationBuilder.RenameTable(name: "MenuCycles", newName: "MenuPlans");
        migrationBuilder.RenameTable(name: "MenuCycleVersions", newName: "MenuPlanVersions");
        migrationBuilder.RenameTable(name: "MenuCycleAudits", newName: "MenuPlanAudits");

        migrationBuilder.RenameColumn(
            name: "MenuCycleId",
            table: "MenuPlanVersions",
            newName: "MenuPlanId");

        migrationBuilder.RenameIndex(
            name: "IX_MenuCycleVersions_MenuCycleId_VersionNumber",
            table: "MenuPlanVersions",
            newName: "IX_MenuPlanVersions_MenuPlanId_VersionNumber");

        migrationBuilder.RenameColumn(
            name: "MenuCycleVersionId",
            table: "MenuPlanAudits",
            newName: "MenuPlanVersionId");

        migrationBuilder.RenameIndex(
            name: "IX_MenuCycleAudits_MenuCycleVersionId",
            table: "MenuPlanAudits",
            newName: "IX_MenuPlanAudits_MenuPlanVersionId");

        migrationBuilder.RenameColumn(
            name: "MenuCycleVersionId",
            table: "MenuDays",
            newName: "MenuPlanVersionId");

        migrationBuilder.RenameIndex(
            name: "IX_MenuDays_MenuCycleVersionId_DayNumber",
            table: "MenuDays",
            newName: "IX_MenuDays_MenuPlanVersionId_DayNumber");

        migrationBuilder.RenameIndex(
            name: "IX_MenuCycles_RestaurantId_IsTemplate",
            table: "MenuPlans",
            newName: "IX_MenuPlans_RestaurantId_IsTemplate");

        // Mode defaults to Cycle (=1).
        migrationBuilder.AddColumn<int>(
            name: "Mode",
            table: "MenuPlans",
            type: "integer",
            nullable: false,
            defaultValue: 1);

        // Preserve resolved version IDs; add program + schedule mode.
        migrationBuilder.RenameColumn(
            name: "ResolvedMenuCycleVersionId",
            table: "DailyMealSelections",
            newName: "ResolvedMenuPlanVersionId");

        migrationBuilder.RenameIndex(
            name: "IX_DailyMealSelections_ResolvedMenuCycleVersionId",
            table: "DailyMealSelections",
            newName: "IX_DailyMealSelections_ResolvedMenuPlanVersionId");

        migrationBuilder.AddColumn<Guid>(
            name: "ResolvedProgramId",
            table: "DailyMealSelections",
            type: "uuid",
            nullable: true);

        migrationBuilder.AddColumn<int>(
            name: "ScheduleMode",
            table: "DailyMealSelections",
            type: "integer",
            nullable: true);

        migrationBuilder.CreateIndex(
            name: "IX_DailyMealSelections_ResolvedProgramId",
            table: "DailyMealSelections",
            column: "ResolvedProgramId");

        migrationBuilder.CreateIndex(
            name: "IX_DailyMealSelections_ScheduleMode",
            table: "DailyMealSelections",
            column: "ScheduleMode");

        migrationBuilder.RenameColumn(
            name: "ResolvedMenuCycleVersionId",
            table: "RestaurantDailyOrders",
            newName: "ResolvedMenuPlanVersionId");

        migrationBuilder.RenameIndex(
            name: "IX_RestaurantDailyOrders_ResolvedMenuCycleVersionId",
            table: "RestaurantDailyOrders",
            newName: "IX_RestaurantDailyOrders_ResolvedMenuPlanVersionId");

        migrationBuilder.AddColumn<Guid>(
            name: "ResolvedProgramId",
            table: "RestaurantDailyOrders",
            type: "uuid",
            nullable: true);

        migrationBuilder.AddColumn<int>(
            name: "ScheduleMode",
            table: "RestaurantDailyOrders",
            type: "integer",
            nullable: true);

        migrationBuilder.CreateIndex(
            name: "IX_RestaurantDailyOrders_ResolvedProgramId",
            table: "RestaurantDailyOrders",
            column: "ResolvedProgramId");

        migrationBuilder.CreateIndex(
            name: "IX_RestaurantDailyOrders_ScheduleMode",
            table: "RestaurantDailyOrders",
            column: "ScheduleMode");

        migrationBuilder.CreateTable(
            name: "MealProgramAssignments",
            columns: table => new
            {
                Id = table.Column<Guid>(type: "uuid", nullable: false),
                MealId = table.Column<Guid>(type: "uuid", nullable: false),
                ProgramId = table.Column<Guid>(type: "uuid", nullable: false),
                BundleId = table.Column<Guid>(type: "uuid", nullable: true),
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
                table.PrimaryKey("PK_MealProgramAssignments", x => x.Id);
                table.ForeignKey(
                    name: "FK_MealProgramAssignments_Meals_MealId",
                    column: x => x.MealId,
                    principalTable: "Meals",
                    principalColumn: "Id",
                    onDelete: ReferentialAction.Restrict);
            });

        migrationBuilder.CreateIndex(
            name: "IX_MealProgramAssignments_MealId_ProgramId",
            table: "MealProgramAssignments",
            columns: new[] { "MealId", "ProgramId" },
            unique: true);

        migrationBuilder.CreateTable(
            name: "MenuPlanPrograms",
            columns: table => new
            {
                Id = table.Column<Guid>(type: "uuid", nullable: false),
                MenuPlanId = table.Column<Guid>(type: "uuid", nullable: false),
                ProgramId = table.Column<Guid>(type: "uuid", nullable: false),
                EffectiveFrom = table.Column<DateOnly>(type: "date", nullable: false),
                EffectiveTo = table.Column<DateOnly>(type: "date", nullable: true),
                IsPrimaryOwner = table.Column<bool>(type: "boolean", nullable: false),
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
                table.PrimaryKey("PK_MenuPlanPrograms", x => x.Id);
                table.ForeignKey(
                    name: "FK_MenuPlanPrograms_MenuPlans_MenuPlanId",
                    column: x => x.MenuPlanId,
                    principalTable: "MenuPlans",
                    principalColumn: "Id",
                    onDelete: ReferentialAction.Restrict);
            });

        migrationBuilder.CreateIndex(
            name: "IX_MenuPlanPrograms_MenuPlanId_ProgramId",
            table: "MenuPlanPrograms",
            columns: new[] { "MenuPlanId", "ProgramId" },
            unique: true);

        migrationBuilder.CreateIndex(
            name: "IX_MenuPlanPrograms_ProgramId_EffectiveFrom_EffectiveTo",
            table: "MenuPlanPrograms",
            columns: new[] { "ProgramId", "EffectiveFrom", "EffectiveTo" });

        migrationBuilder.CreateTable(
            name: "WeekdayMealAssignments",
            columns: table => new
            {
                Id = table.Column<Guid>(type: "uuid", nullable: false),
                MenuPlanVersionId = table.Column<Guid>(type: "uuid", nullable: false),
                DayOfWeek = table.Column<int>(type: "integer", nullable: false),
                ProgramId = table.Column<Guid>(type: "uuid", nullable: false),
                MealId = table.Column<Guid>(type: "uuid", nullable: false),
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
                table.PrimaryKey("PK_WeekdayMealAssignments", x => x.Id);
                table.ForeignKey(
                    name: "FK_WeekdayMealAssignments_Meals_MealId",
                    column: x => x.MealId,
                    principalTable: "Meals",
                    principalColumn: "Id",
                    onDelete: ReferentialAction.Restrict);
                table.ForeignKey(
                    name: "FK_WeekdayMealAssignments_MenuPlanVersions_MenuPlanVersionId",
                    column: x => x.MenuPlanVersionId,
                    principalTable: "MenuPlanVersions",
                    principalColumn: "Id",
                    onDelete: ReferentialAction.Restrict);
            });

        migrationBuilder.CreateIndex(
            name: "IX_WeekdayMealAssignments_MealId",
            table: "WeekdayMealAssignments",
            column: "MealId");

        migrationBuilder.CreateIndex(
            name: "IX_WeekdayMealAssignments_MenuPlanVersionId_DayOfWeek_ProgramI~",
            table: "WeekdayMealAssignments",
            columns: new[] { "MenuPlanVersionId", "DayOfWeek", "ProgramId", "MealId" },
            unique: true);

        migrationBuilder.CreateIndex(
            name: "IX_WeekdayMealAssignments_MenuPlanVersionId_DayOfWeek_Program~1",
            table: "WeekdayMealAssignments",
            columns: new[] { "MenuPlanVersionId", "DayOfWeek", "ProgramId", "Slot", "IsPrimary" },
            unique: true,
            filter: "\"IsPrimary\" = true");

        migrationBuilder.DropIndex(
            name: "IX_MenuDayMeals_MenuDayId_MealId",
            table: "MenuDayMeals");

        migrationBuilder.CreateIndex(
            name: "IX_MenuDayMeals_MenuDayId_ProgramId_MealId",
            table: "MenuDayMeals",
            columns: new[] { "MenuDayId", "ProgramId", "MealId" },
            unique: true);

        migrationBuilder.CreateIndex(
            name: "IX_MenuDayMeals_MenuDayId_ProgramId_Slot_IsPrimary",
            table: "MenuDayMeals",
            columns: new[] { "MenuDayId", "ProgramId", "Slot", "IsPrimary" },
            unique: true,
            filter: "\"IsPrimary\" = true");

        migrationBuilder.AddForeignKey(
            name: "FK_MenuPlanVersions_MenuPlans_MenuPlanId",
            table: "MenuPlanVersions",
            column: "MenuPlanId",
            principalTable: "MenuPlans",
            principalColumn: "Id",
            onDelete: ReferentialAction.Restrict);

        migrationBuilder.AddForeignKey(
            name: "FK_MenuDays_MenuPlanVersions_MenuPlanVersionId",
            table: "MenuDays",
            column: "MenuPlanVersionId",
            principalTable: "MenuPlanVersions",
            principalColumn: "Id",
            onDelete: ReferentialAction.Restrict);

        // Data backfill + per-program plan split (PostgreSQL).
        migrationBuilder.Sql("""
            INSERT INTO "MealProgramAssignments"
                ("Id", "MealId", "ProgramId", "BundleId", "CreatedAtUtc", "IsDeleted")
            SELECT gen_random_uuid(), m."Id", m."ProgramId", m."BundleId", NOW() AT TIME ZONE 'UTC', FALSE
            FROM "Meals" m
            WHERE m."ProgramId" IS NOT NULL
              AND m."IsDeleted" = FALSE
              AND NOT EXISTS (
                  SELECT 1 FROM "MealProgramAssignments" a
                  WHERE a."MealId" = m."Id" AND a."ProgramId" = m."ProgramId" AND a."IsDeleted" = FALSE);

            -- Mark restaurant plans with no program-linked meals as NeedsChanges (8).
            UPDATE "MenuPlanVersions" v
            SET "Status" = 8,
                "ReviewNotes" = COALESCE("ReviewNotes", '') ||
                    CASE WHEN "ReviewNotes" IS NULL OR "ReviewNotes" = '' THEN '' ELSE E'\n' END ||
                    'Migration: no program meals found; requires restaurant review.'
            WHERE v."Id" IN (
                SELECT latest."Id"
                FROM (
                    SELECT DISTINCT ON (v2."MenuPlanId") v2."Id", v2."MenuPlanId"
                    FROM "MenuPlanVersions" v2
                    WHERE v2."IsDeleted" = FALSE
                    ORDER BY v2."MenuPlanId", v2."VersionNumber" DESC
                ) latest
                JOIN "MenuPlans" p ON p."Id" = latest."MenuPlanId"
                WHERE p."IsTemplate" = FALSE
                  AND p."IsDeleted" = FALSE
                  AND NOT EXISTS (
                      SELECT 1
                      FROM "MenuDays" d
                      JOIN "MenuDayMeals" l ON l."MenuDayId" = d."Id" AND l."IsDeleted" = FALSE
                      WHERE d."MenuPlanVersionId" = latest."Id" AND d."IsDeleted" = FALSE));

            -- Link each distinct program found on a plan.
            INSERT INTO "MenuPlanPrograms"
                ("Id", "MenuPlanId", "ProgramId", "EffectiveFrom", "EffectiveTo", "IsPrimaryOwner", "CreatedAtUtc", "IsDeleted")
            SELECT gen_random_uuid(),
                   src."MenuPlanId",
                   src."ProgramId",
                   src."EffectiveFrom",
                   NULL,
                   src.rn = 1,
                   NOW() AT TIME ZONE 'UTC',
                   FALSE
            FROM (
                SELECT p."Id" AS "MenuPlanId",
                       l."ProgramId",
                       MIN(v."EffectiveFrom") AS "EffectiveFrom",
                       ROW_NUMBER() OVER (PARTITION BY p."Id" ORDER BY MIN(v."EffectiveFrom"), l."ProgramId") AS rn
                FROM "MenuPlans" p
                JOIN "MenuPlanVersions" v ON v."MenuPlanId" = p."Id" AND v."IsDeleted" = FALSE
                JOIN "MenuDays" d ON d."MenuPlanVersionId" = v."Id" AND d."IsDeleted" = FALSE
                JOIN "MenuDayMeals" l ON l."MenuDayId" = d."Id" AND l."IsDeleted" = FALSE
                WHERE p."IsTemplate" = FALSE AND p."IsDeleted" = FALSE
                GROUP BY p."Id", l."ProgramId"
            ) src
            WHERE NOT EXISTS (
                SELECT 1 FROM "MenuPlanPrograms" mpp
                WHERE mpp."MenuPlanId" = src."MenuPlanId" AND mpp."ProgramId" = src."ProgramId" AND mpp."IsDeleted" = FALSE);

            -- Split multi-program restaurant plans into one plan per extra program.
            DO $$
            DECLARE
                rec RECORD;
                new_plan_id uuid;
                ver RECORD;
                new_version_id uuid;
                day_rec RECORD;
                new_day_id uuid;
            BEGIN
                FOR rec IN
                    SELECT mpp."MenuPlanId", mpp."ProgramId", mpp."EffectiveFrom", p."RestaurantId", p."NameAr", p."NameEn", p."Source"
                    FROM "MenuPlanPrograms" mpp
                    JOIN "MenuPlans" p ON p."Id" = mpp."MenuPlanId"
                    WHERE mpp."IsPrimaryOwner" = FALSE
                      AND mpp."IsDeleted" = FALSE
                      AND p."IsTemplate" = FALSE
                      AND p."IsDeleted" = FALSE
                LOOP
                    new_plan_id := gen_random_uuid();
                    INSERT INTO "MenuPlans"
                        ("Id", "RestaurantId", "NameAr", "NameEn", "Source", "IsTemplate", "Mode", "CreatedAtUtc", "IsDeleted")
                    VALUES
                        (new_plan_id, rec."RestaurantId",
                         rec."NameAr" || ' / split', rec."NameEn" || ' / split',
                         rec."Source", FALSE, 1, NOW() AT TIME ZONE 'UTC', FALSE);

                    UPDATE "MenuPlanPrograms"
                    SET "MenuPlanId" = new_plan_id,
                        "IsPrimaryOwner" = TRUE,
                        "ModifiedAtUtc" = NOW() AT TIME ZONE 'UTC'
                    WHERE "MenuPlanId" = rec."MenuPlanId"
                      AND "ProgramId" = rec."ProgramId"
                      AND "IsDeleted" = FALSE;

                    FOR ver IN
                        SELECT * FROM "MenuPlanVersions"
                        WHERE "MenuPlanId" = rec."MenuPlanId" AND "IsDeleted" = FALSE
                    LOOP
                        new_version_id := gen_random_uuid();
                        INSERT INTO "MenuPlanVersions"
                            ("Id", "MenuPlanId", "VersionNumber", "EffectiveFrom", "Status", "ReviewNotes",
                             "ReviewedByUserId", "ReviewedAtUtc", "IsPaused", "PausedAtUtc",
                             "CreatedAtUtc", "IsDeleted")
                        VALUES
                            (new_version_id, new_plan_id, ver."VersionNumber", ver."EffectiveFrom", ver."Status",
                             ver."ReviewNotes", ver."ReviewedByUserId", ver."ReviewedAtUtc",
                             ver."IsPaused", ver."PausedAtUtc", NOW() AT TIME ZONE 'UTC', FALSE);

                        FOR day_rec IN
                            SELECT * FROM "MenuDays"
                            WHERE "MenuPlanVersionId" = ver."Id" AND "IsDeleted" = FALSE
                        LOOP
                            new_day_id := gen_random_uuid();
                            INSERT INTO "MenuDays"
                                ("Id", "MenuPlanVersionId", "DayNumber", "NameAr", "NameEn", "CreatedAtUtc", "IsDeleted")
                            VALUES
                                (new_day_id, new_version_id, day_rec."DayNumber", day_rec."NameAr", day_rec."NameEn",
                                 NOW() AT TIME ZONE 'UTC', FALSE);

                            INSERT INTO "MenuDayMeals"
                                ("Id", "MenuDayId", "MealId", "ProgramId", "Slot", "SortOrder", "IsPrimary",
                                 "CreatedAtUtc", "IsDeleted")
                            SELECT gen_random_uuid(), new_day_id, l."MealId", l."ProgramId", l."Slot",
                                   l."SortOrder", l."IsPrimary", NOW() AT TIME ZONE 'UTC', FALSE
                            FROM "MenuDayMeals" l
                            WHERE l."MenuDayId" = day_rec."Id"
                              AND l."ProgramId" = rec."ProgramId"
                              AND l."IsDeleted" = FALSE;

                            -- Remove moved program links from original day.
                            UPDATE "MenuDayMeals"
                            SET "IsDeleted" = TRUE,
                                "DeletedAtUtc" = NOW() AT TIME ZONE 'UTC'
                            WHERE "MenuDayId" = day_rec."Id"
                              AND "ProgramId" = rec."ProgramId"
                              AND "IsDeleted" = FALSE;
                        END LOOP;
                    END LOOP;
                END LOOP;
            END $$;
            """);
    }

    /// <inheritdoc />
    protected override void Down(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.Sql("""
            DELETE FROM "WeekdayMealAssignments";
            DELETE FROM "MenuPlanPrograms";
            DELETE FROM "MealProgramAssignments";
            """);

        migrationBuilder.DropTable(name: "WeekdayMealAssignments");
        migrationBuilder.DropTable(name: "MenuPlanPrograms");
        migrationBuilder.DropTable(name: "MealProgramAssignments");

        migrationBuilder.DropIndex(name: "IX_MenuDayMeals_MenuDayId_ProgramId_MealId", table: "MenuDayMeals");
        migrationBuilder.DropIndex(name: "IX_MenuDayMeals_MenuDayId_ProgramId_Slot_IsPrimary", table: "MenuDayMeals");
        migrationBuilder.CreateIndex(
            name: "IX_MenuDayMeals_MenuDayId_MealId",
            table: "MenuDayMeals",
            columns: new[] { "MenuDayId", "MealId" },
            unique: true);

        migrationBuilder.DropForeignKey(name: "FK_MenuDays_MenuPlanVersions_MenuPlanVersionId", table: "MenuDays");
        migrationBuilder.DropForeignKey(name: "FK_MenuPlanVersions_MenuPlans_MenuPlanId", table: "MenuPlanVersions");

        migrationBuilder.DropColumn(name: "Mode", table: "MenuPlans");
        migrationBuilder.DropColumn(name: "ResolvedProgramId", table: "DailyMealSelections");
        migrationBuilder.DropColumn(name: "ScheduleMode", table: "DailyMealSelections");
        migrationBuilder.DropColumn(name: "ResolvedProgramId", table: "RestaurantDailyOrders");
        migrationBuilder.DropColumn(name: "ScheduleMode", table: "RestaurantDailyOrders");

        migrationBuilder.RenameColumn(name: "ResolvedMenuPlanVersionId", table: "DailyMealSelections", newName: "ResolvedMenuCycleVersionId");
        migrationBuilder.RenameColumn(name: "ResolvedMenuPlanVersionId", table: "RestaurantDailyOrders", newName: "ResolvedMenuCycleVersionId");
        migrationBuilder.RenameColumn(name: "MenuPlanVersionId", table: "MenuDays", newName: "MenuCycleVersionId");
        migrationBuilder.RenameColumn(name: "MenuPlanVersionId", table: "MenuPlanAudits", newName: "MenuCycleVersionId");
        migrationBuilder.RenameColumn(name: "MenuPlanId", table: "MenuPlanVersions", newName: "MenuCycleId");

        migrationBuilder.RenameTable(name: "MenuPlanAudits", newName: "MenuCycleAudits");
        migrationBuilder.RenameTable(name: "MenuPlanVersions", newName: "MenuCycleVersions");
        migrationBuilder.RenameTable(name: "MenuPlans", newName: "MenuCycles");

        migrationBuilder.AddForeignKey(
            name: "FK_MenuCycleVersions_MenuCycles_MenuCycleId",
            table: "MenuCycleVersions",
            column: "MenuCycleId",
            principalTable: "MenuCycles",
            principalColumn: "Id",
            onDelete: ReferentialAction.Restrict);

        migrationBuilder.AddForeignKey(
            name: "FK_MenuDays_MenuCycleVersions_MenuCycleVersionId",
            table: "MenuDays",
            column: "MenuCycleVersionId",
            principalTable: "MenuCycleVersions",
            principalColumn: "Id",
            onDelete: ReferentialAction.Restrict);
    }
}
