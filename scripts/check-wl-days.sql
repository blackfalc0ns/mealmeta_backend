SELECT v."Id"::text || ' | day=' || w."DayOfWeek"::text || ' | slot=' || w."Slot" || ' | meals=' || count(*)
FROM "WeekdayMealAssignments" w
JOIN "MenuPlanVersions" v ON v."Id"=w."MenuPlanVersionId"
JOIN "MenuPlans" mp ON mp."Id"=v."MenuPlanId"
JOIN "RestaurantProfiles" r ON r."Id"=mp."RestaurantId"
WHERE w."ProgramId"='dd12dfbc-3987-4e38-83af-664b6963c04c' AND w."IsDeleted"=false
  AND r."TradeNameEn" LIKE '[TEST]%'
GROUP BY v."Id", w."DayOfWeek", w."Slot" ORDER BY v."Id", w."DayOfWeek", w."Slot" LIMIT 40;
