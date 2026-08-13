using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace MealMate.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class ClosePreCheckoutGaps : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql("""
                ALTER TABLE "CapacityHolds" ADD COLUMN IF NOT EXISTS "MenuPlanVersionId" uuid NOT NULL DEFAULT '00000000-0000-0000-0000-000000000000';
                """);

            migrationBuilder.CreateTable(
                name: "AllergenDefinitions",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    Code = table.Column<string>(type: "character varying(50)", maxLength: 50, nullable: false),
                    NameAr = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false),
                    NameEn = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false),
                    IsActive = table.Column<bool>(type: "boolean", nullable: false),
                    SortOrder = table.Column<int>(type: "integer", nullable: false),
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
                    table.PrimaryKey("PK_AllergenDefinitions", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "CustomerDietaryPreferences",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    CustomerUserId = table.Column<Guid>(type: "uuid", nullable: false),
                    PreferenceCode = table.Column<string>(type: "character varying(50)", maxLength: 50, nullable: false),
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
                    table.PrimaryKey("PK_CustomerDietaryPreferences", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "FoodPreferenceDefinitions",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    Code = table.Column<string>(type: "character varying(50)", maxLength: 50, nullable: false),
                    NameAr = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false),
                    NameEn = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false),
                    IsActive = table.Column<bool>(type: "boolean", nullable: false),
                    SortOrder = table.Column<int>(type: "integer", nullable: false),
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
                    table.PrimaryKey("PK_FoodPreferenceDefinitions", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "IngredientFoodPreferences",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    IngredientId = table.Column<Guid>(type: "uuid", nullable: false),
                    PreferenceCode = table.Column<string>(type: "character varying(50)", maxLength: 50, nullable: false),
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
                    table.PrimaryKey("PK_IngredientFoodPreferences", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "MealFoodPreferences",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    MealId = table.Column<Guid>(type: "uuid", nullable: false),
                    PreferenceCode = table.Column<string>(type: "character varying(50)", maxLength: 50, nullable: false),
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
                    table.PrimaryKey("PK_MealFoodPreferences", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "PaymentWebhookReceipts",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    Gateway = table.Column<string>(type: "character varying(50)", maxLength: 50, nullable: false),
                    EventKey = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false),
                    SubscriptionId = table.Column<Guid>(type: "uuid", nullable: true),
                    Status = table.Column<string>(type: "character varying(50)", maxLength: 50, nullable: false),
                    PayloadHash = table.Column<string>(type: "character varying(128)", maxLength: 128, nullable: true),
                    ProcessedAtUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
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
                    table.PrimaryKey("PK_PaymentWebhookReceipts", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "SubscriptionDayMealSnapshots",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    SubscriptionId = table.Column<Guid>(type: "uuid", nullable: false),
                    SubscriptionDayId = table.Column<Guid>(type: "uuid", nullable: false),
                    ServiceDate = table.Column<DateOnly>(type: "date", nullable: false),
                    RestaurantId = table.Column<Guid>(type: "uuid", nullable: false),
                    ProgramId = table.Column<Guid>(type: "uuid", nullable: false),
                    MenuPlanVersionId = table.Column<Guid>(type: "uuid", nullable: false),
                    RequiredSlotsJson = table.Column<string>(type: "jsonb", nullable: false),
                    MealsJson = table.Column<string>(type: "jsonb", nullable: false),
                    CapturedAtUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
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
                    table.PrimaryKey("PK_SubscriptionDayMealSnapshots", x => x.Id);
                });

            migrationBuilder.CreateIndex(
                name: "IX_AllergenDefinitions_Code",
                table: "AllergenDefinitions",
                column: "Code",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_AllergenDefinitions_IsActive_SortOrder",
                table: "AllergenDefinitions",
                columns: new[] { "IsActive", "SortOrder" });

            migrationBuilder.CreateIndex(
                name: "IX_CustomerDietaryPreferences_CustomerUserId_PreferenceCode",
                table: "CustomerDietaryPreferences",
                columns: new[] { "CustomerUserId", "PreferenceCode" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_CustomerDietaryPreferences_PreferenceCode",
                table: "CustomerDietaryPreferences",
                column: "PreferenceCode");

            migrationBuilder.CreateIndex(
                name: "IX_FoodPreferenceDefinitions_Code",
                table: "FoodPreferenceDefinitions",
                column: "Code",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_FoodPreferenceDefinitions_IsActive_SortOrder",
                table: "FoodPreferenceDefinitions",
                columns: new[] { "IsActive", "SortOrder" });

            migrationBuilder.CreateIndex(
                name: "IX_IngredientFoodPreferences_IngredientId_PreferenceCode",
                table: "IngredientFoodPreferences",
                columns: new[] { "IngredientId", "PreferenceCode" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_MealFoodPreferences_MealId_PreferenceCode",
                table: "MealFoodPreferences",
                columns: new[] { "MealId", "PreferenceCode" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_PaymentWebhookReceipts_Gateway_EventKey",
                table: "PaymentWebhookReceipts",
                columns: new[] { "Gateway", "EventKey" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_PaymentWebhookReceipts_SubscriptionId",
                table: "PaymentWebhookReceipts",
                column: "SubscriptionId");

            migrationBuilder.CreateIndex(
                name: "IX_SubscriptionDayMealSnapshots_MenuPlanVersionId",
                table: "SubscriptionDayMealSnapshots",
                column: "MenuPlanVersionId");

            migrationBuilder.CreateIndex(
                name: "IX_SubscriptionDayMealSnapshots_SubscriptionDayId",
                table: "SubscriptionDayMealSnapshots",
                column: "SubscriptionDayId",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_SubscriptionDayMealSnapshots_SubscriptionId_ServiceDate",
                table: "SubscriptionDayMealSnapshots",
                columns: new[] { "SubscriptionId", "ServiceDate" },
                unique: true);

            migrationBuilder.Sql("""
                INSERT INTO "AllergenDefinitions" ("Id", "Code", "NameAr", "NameEn", "IsActive", "SortOrder", "CreatedAtUtc", "IsDeleted") VALUES
                (gen_random_uuid(), 'GLUTEN', 'القمح والجلوتين', 'Wheat and gluten', true, 1, NOW(), false),
                (gen_random_uuid(), 'EGGS', 'البيض', 'Eggs', true, 2, NOW(), false),
                (gen_random_uuid(), 'MILK', 'الحليب', 'Milk', true, 3, NOW(), false),
                (gen_random_uuid(), 'PEANUTS', 'الفول السوداني', 'Peanuts', true, 4, NOW(), false),
                (gen_random_uuid(), 'TREE_NUTS', 'المكسرات', 'Tree nuts', true, 5, NOW(), false),
                (gen_random_uuid(), 'FISH', 'السمك', 'Fish', true, 6, NOW(), false),
                (gen_random_uuid(), 'CRUSTACEANS', 'القشريات', 'Crustaceans', true, 7, NOW(), false),
                (gen_random_uuid(), 'SOY', 'الصويا', 'Soy', true, 8, NOW(), false),
                (gen_random_uuid(), 'SESAME', 'السمسم', 'Sesame', true, 9, NOW(), false)
                ON CONFLICT ("Code") DO NOTHING;

                INSERT INTO "FoodPreferenceDefinitions" ("Id", "Code", "NameAr", "NameEn", "IsActive", "SortOrder", "CreatedAtUtc", "IsDeleted") VALUES
                (gen_random_uuid(), 'VEGETARIAN', 'نباتي', 'Vegetarian', true, 1, NOW(), false),
                (gen_random_uuid(), 'KETO', 'كيتو', 'Keto', true, 2, NOW(), false),
                (gen_random_uuid(), 'LOW_CARB', 'قليل الكربوهيدرات', 'Low carb', true, 3, NOW(), false),
                (gen_random_uuid(), 'LACTOSE_FREE', 'خالٍ من اللاكتوز', 'Lactose free', true, 4, NOW(), false),
                (gen_random_uuid(), 'GLUTEN_FREE', 'خالٍ من الجلوتين', 'Gluten free', true, 5, NOW(), false)
                ON CONFLICT ("Code") DO NOTHING;

                UPDATE "Ingredients" ingredient
                SET "AllergenCodes" = normalized.codes
                FROM (
                    SELECT source."Id", string_agg(DISTINCT upper(replace(trim(code), ' ', '_')), ',' ORDER BY upper(replace(trim(code), ' ', '_'))) AS codes
                    FROM "Ingredients" source
                    CROSS JOIN LATERAL regexp_split_to_table(source."AllergenCodes", '[,;|]+') code
                    WHERE trim(code) <> ''
                    GROUP BY source."Id"
                ) normalized
                WHERE ingredient."Id" = normalized."Id";

                UPDATE "Meals" meal
                SET "PresentAllergenCodes" = normalized.codes
                FROM (
                    SELECT source."Id", string_agg(DISTINCT upper(replace(trim(code), ' ', '_')), ',' ORDER BY upper(replace(trim(code), ' ', '_'))) AS codes
                    FROM "Meals" source
                    CROSS JOIN LATERAL regexp_split_to_table(source."PresentAllergenCodes", '[,;|]+') code
                    WHERE trim(code) <> ''
                    GROUP BY source."Id"
                ) normalized
                WHERE meal."Id" = normalized."Id";

                UPDATE "Meals" meal
                SET "MayContainAllergenCodes" = normalized.codes
                FROM (
                    SELECT source."Id", string_agg(DISTINCT upper(replace(trim(code), ' ', '_')), ',' ORDER BY upper(replace(trim(code), ' ', '_'))) AS codes
                    FROM "Meals" source
                    CROSS JOIN LATERAL regexp_split_to_table(source."MayContainAllergenCodes", '[,;|]+') code
                    WHERE trim(code) <> ''
                    GROUP BY source."Id"
                ) normalized
                WHERE meal."Id" = normalized."Id";

                INSERT INTO "CustomerDietaryPreferences" ("Id", "CustomerUserId", "PreferenceCode", "CreatedAtUtc", "IsDeleted")
                SELECT gen_random_uuid(), "UserId", upper(trim("DietaryPattern")), NOW(), false
                FROM "CustomerProfiles" cp
                WHERE cp."DietaryPattern" IS NOT NULL
                  AND upper(trim(cp."DietaryPattern")) IN ('VEGETARIAN','KETO','LOW_CARB','LACTOSE_FREE','GLUTEN_FREE')
                ON CONFLICT ("CustomerUserId", "PreferenceCode") DO NOTHING;
                """);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "AllergenDefinitions");

            migrationBuilder.DropTable(
                name: "CustomerDietaryPreferences");

            migrationBuilder.DropTable(
                name: "FoodPreferenceDefinitions");

            migrationBuilder.DropTable(
                name: "IngredientFoodPreferences");

            migrationBuilder.DropTable(
                name: "MealFoodPreferences");

            migrationBuilder.DropTable(
                name: "PaymentWebhookReceipts");

            migrationBuilder.DropTable(
                name: "SubscriptionDayMealSnapshots");

            migrationBuilder.Sql("""ALTER TABLE "CapacityHolds" DROP COLUMN IF EXISTS "MenuPlanVersionId";""");
        }
    }
}
