UPDATE "WeekdayMealAssignments" w
SET "MealId" = ath."Id", "ModifiedAtUtc" = now()
FROM "MenuPlanVersions" v
JOIN "MenuPlans" mp ON mp."Id" = v."MenuPlanId"
JOIN "RestaurantProfiles" r ON r."Id" = mp."RestaurantId"
JOIN "Meals" ath ON ath."RestaurantId" = r."Id"
  AND ath."NameEn" LIKE '% ATH' AND ath."ApprovalStatus" = 4 AND ath."IsAvailableForNewOrders" = true AND ath."IsDeleted" = false
WHERE w."MenuPlanVersionId" = v."Id"
  AND ath."Slot" = w."Slot"
  AND w."ProgramId" = '67ea99eb-79e3-489f-bfc9-3346e4c5dc5c'
  AND w."IsDeleted" = false
  AND r."TradeNameEn" LIKE '[TEST]%';

UPDATE "WeekdayMealAssignments" w
SET "MealId" = wl."Id", "ModifiedAtUtc" = now()
FROM "MenuPlanVersions" v
JOIN "MenuPlans" mp ON mp."Id" = v."MenuPlanId"
JOIN "RestaurantProfiles" r ON r."Id" = mp."RestaurantId"
JOIN "Meals" wl ON wl."RestaurantId" = r."Id"
  AND wl."NameEn" LIKE '% WL' AND wl."ApprovalStatus" = 4 AND wl."IsAvailableForNewOrders" = true AND wl."IsDeleted" = false
WHERE w."MenuPlanVersionId" = v."Id"
  AND wl."Slot" = w."Slot"
  AND w."ProgramId" = 'dd12dfbc-3987-4e38-83af-664b6963c04c'
  AND w."Slot" IN ('breakfast','lunch','dinner')
  AND w."IsDeleted" = false
  AND r."TradeNameEn" LIKE '[TEST]%';
