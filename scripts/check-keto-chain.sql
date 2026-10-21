SELECT 'offers_for_keto_bundle=' || count(*) FROM "RestaurantProgramBundleOffers"
  WHERE "BundleId"='8227a8ea-e9b7-4a67-a2b2-3c18c4d8d735' AND "ProgramId"='7fe77ed4-e152-45bf-8b7a-c21a92077d49' AND "IsActive"=true;
SELECT 'pricings_for_keto_bundle=' || count(*) FROM "RestaurantBundlePricings"
  WHERE "BundleId"='8227a8ea-e9b7-4a67-a2b2-3c18c4d8d735' AND "ProgramId"='7fe77ed4-e152-45bf-8b7a-c21a92077d49'
  AND "LockStatus" IN (1,2,3) AND "ReferencePrice26_Amount" > 0;
SELECT r."TradeNameEn" || ' eff_from=' || COALESCE(p."EffectiveFromUtc"::text,'NULL') || ' eff_to=' || COALESCE(p."EffectiveToUtc"::text,'NULL') || ' lock=' || p."LockStatus"
FROM "RestaurantBundlePricings" p JOIN "RestaurantProfiles" r ON r."Id"=p."RestaurantId"
WHERE p."BundleId"='8227a8ea-e9b7-4a67-a2b2-3c18c4d8d735' AND p."ProgramId"='7fe77ed4-e152-45bf-8b7a-c21a92077d49';
SELECT 'nutrition_standards_keto=' || count(*) FROM "ProgramNutritionStandards" WHERE "ProgramId"='7fe77ed4-e152-45bf-8b7a-c21a92077d49' AND "IsActive"=true;
SELECT 'meals_with_keto_programid=' || count(*) FROM "Meals" WHERE "ProgramId"='7fe77ed4-e152-45bf-8b7a-c21a92077d49' AND "IsDeleted"=false;
