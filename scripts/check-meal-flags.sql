SELECT r."TradeNameEn" || ' | available=' || m."IsAvailableForNewOrders"::text || ' | allergens_reviewed=' || COALESCE(m."AllergensReviewed"::text,'null')
     || ' | data_resp=' || COALESCE(m."DataResponsibilityConfirmed"::text,'null') || ' | program=' || COALESCE(m."ProgramId"::text,'null') || ' | slot=' || m."Slot"
FROM "Meals" m JOIN "RestaurantProfiles" r ON r."Id"=m."RestaurantId" WHERE m."IsDeleted"=false ORDER BY r."TradeNameEn", m."Slot" LIMIT 8;
SELECT 'assignments: ' || a."ProgramId"::text || ' meals=' || count(*) FROM "MealProgramAssignments" a GROUP BY a."ProgramId";
