BEGIN;
INSERT INTO "Meals"
  ("Id","MenuId","RestaurantId","ProgramId","Code","NameAr","NameEn","DescriptionAr","DescriptionEn","Slot","Calories","ProteinG","CarbsG","NetCarbsG","FatG","FiberG",
   "ApprovalStatus","IsAvailableForNewOrders","AllergensReviewed","DataResponsibilityConfirmed","PresentAllergenCodes","MayContainAllergenCodes","NutritionTagCodes","CreatedAtUtc","IsDeleted")
SELECT gen_random_uuid(), m."MenuId", m."RestaurantId", m."ProgramId", m."Code" || '-II', m."NameAr" || ' ٢', m."NameEn" || ' II',
  m."DescriptionAr", m."DescriptionEn", 'snack',
  m."Calories", m."ProteinG", m."CarbsG", m."NetCarbsG", m."FatG", m."FiberG",
  m."ApprovalStatus", m."IsAvailableForNewOrders", m."AllergensReviewed", m."DataResponsibilityConfirmed",
  m."PresentAllergenCodes", m."MayContainAllergenCodes", m."NutritionTagCodes", now(), false
FROM "Meals" m JOIN "RestaurantProfiles" r ON r."Id"=m."RestaurantId"
WHERE m."IsDeleted"=false AND m."Slot"='snack' AND m."ApprovalStatus"=4 AND r."TradeNameEn" LIKE '[TEST]%'
  AND (SELECT count(*) FROM "Meals" x WHERE x."RestaurantId"=m."RestaurantId" AND x."Slot"='snack' AND x."IsDeleted"=false) < 2;

INSERT INTO "MealImages" ("Id","MealId","ImageUrl","IsPrimary","CreatedAtUtc","IsDeleted")
SELECT gen_random_uuid(), m."Id", i."ImageUrl", true, now(), false
FROM "Meals" m
JOIN "RestaurantProfiles" r ON r."Id"=m."RestaurantId"
JOIN "MealImages" i ON i."MealId"=m."Id" AND i."IsDeleted"=false
WHERE m."IsDeleted"=false AND m."Slot"='snack' AND m."NameEn" LIKE '% II' AND r."TradeNameEn" LIKE '[TEST]%'
  AND NOT EXISTS (SELECT 1 FROM "MealImages" x WHERE x."MealId"=m."Id" AND x."IsDeleted"=false);

INSERT INTO "MealProgramAssignments" ("Id","MealId","ProgramId","CreatedAtUtc","IsDeleted")
SELECT gen_random_uuid(), m."Id", p."ProgramId", now(), false
FROM "Meals" m
CROSS JOIN (VALUES ('dd12dfbc-3987-4e38-83af-664b6963c04c'::uuid), ('7fe77ed4-e152-45bf-8b7a-c21a92077d49'::uuid), ('67ea99eb-79e3-489f-bfc9-3346e4c5dc5c'::uuid)) AS p("ProgramId")
JOIN "RestaurantProfiles" r ON r."Id"=m."RestaurantId"
WHERE m."IsDeleted"=false AND m."Slot"='snack' AND m."NameEn" LIKE '% II' AND r."TradeNameEn" LIKE '[TEST]%'
  AND NOT EXISTS (SELECT 1 FROM "MealProgramAssignments" x WHERE x."MealId"=m."Id" AND x."ProgramId"=p."ProgramId" AND x."IsDeleted"=false);
COMMIT;
SELECT 'snack_max_per_restaurant=' || max(c) FROM (SELECT count(*) c FROM "Meals" WHERE "Slot"='snack' AND "IsDeleted"=false GROUP BY "RestaurantId") s;
