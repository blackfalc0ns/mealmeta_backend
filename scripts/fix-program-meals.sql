UPDATE "Meals" SET "ProgramId"='7fe77ed4-e152-45bf-8b7a-c21a92077d49', "ModifiedAtUtc"=now()
WHERE "IsDeleted"=false AND "ApprovalStatus"=4
  AND "RestaurantId" IN (SELECT "Id" FROM "RestaurantProfiles" WHERE "TradeNameEn" LIKE '[TEST]%')
  AND "NameEn" NOT LIKE '% II';

UPDATE "Meals" SET
  "ProgramId"='dd12dfbc-3987-4e38-83af-664b6963c04c',
  "Calories"=180, "ProteinG"=15, "CarbsG"=20, "NetCarbsG"=15, "FatG"=6, "FiberG"=3, "ModifiedAtUtc"=now()
WHERE "IsDeleted"=false AND "Slot"='snack' AND "NameEn" LIKE '% II';

INSERT INTO "Meals"
  ("Id","MenuId","RestaurantId","ProgramId","Code","NameAr","NameEn","DescriptionAr","DescriptionEn","Slot","Calories","ProteinG","CarbsG","NetCarbsG","FatG","FiberG",
   "ApprovalStatus","IsAvailableForNewOrders","AllergensReviewed","DataResponsibilityConfirmed","PresentAllergenCodes","MayContainAllergenCodes","NutritionTagCodes","CreatedAtUtc","IsDeleted")
SELECT gen_random_uuid(), m."MenuId", m."RestaurantId", 'dd12dfbc-3987-4e38-83af-664b6963c04c', m."Code" || '-WL2', m."NameAr", m."NameEn" || ' WL',
  m."DescriptionAr", m."DescriptionEn", m."Slot",
  CASE WHEN m."Slot"='snack' THEN 180 ELSE 420 END,
  CASE WHEN m."Slot"='snack' THEN 15 ELSE 35 END,
  CASE WHEN m."Slot"='snack' THEN 20 ELSE 40 END,
  CASE WHEN m."Slot"='snack' THEN 15 ELSE 32 END,
  CASE WHEN m."Slot"='snack' THEN 6 ELSE 14 END,
  CASE WHEN m."Slot"='snack' THEN 3 ELSE 6 END,
  m."ApprovalStatus", m."IsAvailableForNewOrders", m."AllergensReviewed", m."DataResponsibilityConfirmed",
  m."PresentAllergenCodes", m."MayContainAllergenCodes", m."NutritionTagCodes", now(), false
FROM "Meals" m JOIN "RestaurantProfiles" r ON r."Id"=m."RestaurantId"
WHERE m."IsDeleted"=false AND m."ApprovalStatus"=4 AND r."TradeNameEn" LIKE '[TEST]%'
  AND m."NameEn" NOT LIKE '% II' AND m."NameEn" NOT LIKE '% WL' AND m."NameEn" NOT LIKE '% ATH'
  AND m."Slot" IN ('breakfast','lunch','dinner','snack')
  AND NOT EXISTS (SELECT 1 FROM "Meals" x WHERE x."RestaurantId"=m."RestaurantId" AND x."Code"=m."Code" || '-WL2' AND x."IsDeleted"=false);

INSERT INTO "Meals"
  ("Id","MenuId","RestaurantId","ProgramId","Code","NameAr","NameEn","DescriptionAr","DescriptionEn","Slot","Calories","ProteinG","CarbsG","NetCarbsG","FatG","FiberG",
   "ApprovalStatus","IsAvailableForNewOrders","AllergensReviewed","DataResponsibilityConfirmed","PresentAllergenCodes","MayContainAllergenCodes","NutritionTagCodes","CreatedAtUtc","IsDeleted")
SELECT gen_random_uuid(), m."MenuId", m."RestaurantId", '67ea99eb-79e3-489f-bfc9-3346e4c5dc5c', m."Code" || '-ATH', m."NameAr", m."NameEn" || ' ATH',
  m."DescriptionAr", m."DescriptionEn", m."Slot",
  CASE WHEN m."Slot"='snack' THEN 280 ELSE 650 END,
  CASE WHEN m."Slot"='snack' THEN 20 ELSE 42 END,
  CASE WHEN m."Slot"='snack' THEN 35 ELSE 80 END,
  CASE WHEN m."Slot"='snack' THEN 30 ELSE 70 END,
  CASE WHEN m."Slot"='snack' THEN 9 ELSE 18 END,
  CASE WHEN m."Slot"='snack' THEN 4 ELSE 6 END,
  m."ApprovalStatus", m."IsAvailableForNewOrders", m."AllergensReviewed", m."DataResponsibilityConfirmed",
  m."PresentAllergenCodes", m."MayContainAllergenCodes", m."NutritionTagCodes", now(), false
FROM "Meals" m JOIN "RestaurantProfiles" r ON r."Id"=m."RestaurantId"
WHERE m."IsDeleted"=false AND m."ApprovalStatus"=4 AND r."TradeNameEn" LIKE '[TEST]%'
  AND m."NameEn" NOT LIKE '% II' AND m."NameEn" NOT LIKE '% WL' AND m."NameEn" NOT LIKE '% ATH'
  AND m."Slot" IN ('breakfast','lunch','dinner','snack')
  AND NOT EXISTS (SELECT 1 FROM "Meals" x WHERE x."RestaurantId"=m."RestaurantId" AND x."Code"=m."Code" || '-ATH' AND x."IsDeleted"=false);
