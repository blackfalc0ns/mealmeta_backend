-- ATH clones take their base meal's weekday slots under the Athletic program.
INSERT INTO "WeekdayMealAssignments"
  ("Id","MenuPlanVersionId","DayOfWeek","ProgramId","MealId","Slot","SortOrder","IsPrimary","CreatedAtUtc","IsDeleted")
SELECT gen_random_uuid(), w."MenuPlanVersionId", w."DayOfWeek", '67ea99eb-79e3-489f-bfc9-3346e4c5dc5c', n."Id", w."Slot", w."SortOrder", w."IsPrimary", now(), false
FROM "WeekdayMealAssignments" w
JOIN "Meals" m ON m."Id"=w."MealId" AND m."IsDeleted"=false
JOIN "RestaurantProfiles" r ON r."Id"=m."RestaurantId"
JOIN "Meals" n ON n."RestaurantId"=m."RestaurantId" AND n."Slot"=m."Slot"
  AND n."IsDeleted"=false AND n."ApprovalStatus"=4 AND n."IsAvailableForNewOrders"=true AND n."NameEn" LIKE '% ATH'
WHERE r."TradeNameEn" LIKE '[TEST]%' AND w."IsDeleted"=false
  AND NOT EXISTS (SELECT 1 FROM "WeekdayMealAssignments" x WHERE x."MenuPlanVersionId"=w."MenuPlanVersionId"
    AND x."DayOfWeek"=w."DayOfWeek" AND x."ProgramId"='67ea99eb-79e3-489f-bfc9-3346e4c5dc5c' AND x."MealId"=n."Id" AND x."IsDeleted"=false);

-- WL clones (b/l/d named WL + both WL snacks) take the base slots under the Weight Loss program.
INSERT INTO "WeekdayMealAssignments"
  ("Id","MenuPlanVersionId","DayOfWeek","ProgramId","MealId","Slot","SortOrder","IsPrimary","CreatedAtUtc","IsDeleted")
SELECT gen_random_uuid(), w."MenuPlanVersionId", w."DayOfWeek", 'dd12dfbc-3987-4e38-83af-664b6963c04c', n."Id", w."Slot", w."SortOrder", w."IsPrimary", now(), false
FROM "WeekdayMealAssignments" w
JOIN "Meals" m ON m."Id"=w."MealId" AND m."IsDeleted"=false
JOIN "RestaurantProfiles" r ON r."Id"=m."RestaurantId"
JOIN "Meals" n ON n."RestaurantId"=m."RestaurantId" AND n."Slot"=m."Slot"
  AND n."IsDeleted"=false AND n."ApprovalStatus"=4 AND n."IsAvailableForNewOrders"=true
  AND (n."NameEn" LIKE '% WL' OR n."NameEn" LIKE '% II')
WHERE r."TradeNameEn" LIKE '[TEST]%' AND w."IsDeleted"=false
  AND NOT EXISTS (SELECT 1 FROM "WeekdayMealAssignments" x WHERE x."MenuPlanVersionId"=w."MenuPlanVersionId"
    AND x."DayOfWeek"=w."DayOfWeek" AND x."ProgramId"='dd12dfbc-3987-4e38-83af-664b6963c04c' AND x."MealId"=n."Id" AND x."IsDeleted"=false);
