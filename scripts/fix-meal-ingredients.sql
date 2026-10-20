INSERT INTO "MealIngredients" ("Id","MealId","IngredientId","PortionGrams","IsOptional","SortOrder","CreatedAtUtc","IsDeleted")
SELECT gen_random_uuid(), n."Id", i."IngredientId", COALESCE(i."PortionGrams", 100), COALESCE(i."IsOptional", false), COALESCE(i."SortOrder", 0), now(), false
FROM "Meals" n
JOIN "Meals" m ON m."RestaurantId" = n."RestaurantId" AND m."Slot" = n."Slot"
  AND m."IsDeleted"=false AND m."NameEn" NOT LIKE '% WL' AND m."NameEn" NOT LIKE '% ATH' AND m."NameEn" NOT LIKE '% II'
JOIN "MealIngredients" i ON i."MealId"=m."Id" AND i."IsDeleted"=false
WHERE n."IsDeleted"=false AND (n."NameEn" LIKE '% WL' OR n."NameEn" LIKE '% ATH' OR n."NameEn" LIKE '% II')
  AND NOT EXISTS (SELECT 1 FROM "MealIngredients" x WHERE x."MealId"=n."Id" AND x."IngredientId"=i."IngredientId" AND x."IsDeleted"=false);
SELECT 'clones_with_ingredients=' || count(DISTINCT "MealId") FROM "MealIngredients"
  WHERE "MealId" IN (SELECT "Id" FROM "Meals" WHERE "NameEn" LIKE '% WL' OR "NameEn" LIKE '% ATH' OR "NameEn" LIKE '% II');
