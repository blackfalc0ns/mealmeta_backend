INSERT INTO "WeekdayMealAssignments"
  ("Id","MenuPlanVersionId","DayOfWeek","ProgramId","MealId","Slot","SortOrder","IsPrimary","CreatedAtUtc","IsDeleted")
SELECT gen_random_uuid(), w."MenuPlanVersionId", w."DayOfWeek", 'dd12dfbc-3987-4e38-83af-664b6963c04c', alt."Id", 'snack', 2, false, now(), false
FROM "WeekdayMealAssignments" w
JOIN "Meals" cur ON cur."Id"=w."MealId"
JOIN "RestaurantProfiles" r ON r."Id" = (
  SELECT mp."RestaurantId" FROM "MenuPlanVersions" v JOIN "MenuPlans" mp ON mp."Id"=v."MenuPlanId" WHERE v."Id"=w."MenuPlanVersionId")
JOIN "Meals" alt ON alt."RestaurantId"=r."Id" AND alt."Slot"='snack' AND alt."IsDeleted"=false AND alt."ApprovalStatus"=4
  AND alt."Id" <> cur."Id"
  AND ((cur."NameEn" LIKE '% WL' AND alt."NameEn" LIKE '% II') OR (NOT cur."NameEn" LIKE '% WL' AND NOT alt."NameEn" LIKE '% II'))
WHERE w."ProgramId"='dd12dfbc-3987-4e38-83af-664b6963c04c' AND w."Slot"='snack' AND w."IsDeleted"=false
  AND r."TradeNameEn" LIKE '[TEST]%'
  AND NOT EXISTS (SELECT 1 FROM "WeekdayMealAssignments" x WHERE x."MenuPlanVersionId"=w."MenuPlanVersionId"
    AND x."DayOfWeek"=w."DayOfWeek" AND x."ProgramId"=w."ProgramId" AND x."MealId"=alt."Id" AND x."IsDeleted"=false);
