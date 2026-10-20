BEGIN;
INSERT INTO "MealProgramAssignments" ("Id","MealId","ProgramId","CreatedAtUtc","CreatedByUserId","IsDeleted")
SELECT gen_random_uuid(), m."Id", p."ProgramId", now(), NULL, false
FROM "Meals" m
CROSS JOIN (VALUES ('7fe77ed4-e152-45bf-8b7a-c21a92077d49'::uuid), ('67ea99eb-79e3-489f-bfc9-3346e4c5dc5c'::uuid)) AS p("ProgramId")
JOIN "RestaurantProfiles" r ON r."Id"=m."RestaurantId"
WHERE m."IsDeleted"=false AND m."ApprovalStatus"=4 AND m."IsAvailableForNewOrders"=true
  AND m."Slot" IN ('breakfast','lunch','dinner','snack')
  AND r."TradeNameEn" LIKE '[TEST]%'
  AND NOT EXISTS (SELECT 1 FROM "MealProgramAssignments" x WHERE x."MealId"=m."Id" AND x."ProgramId"=p."ProgramId" AND x."IsDeleted"=false);
COMMIT;
SELECT 'keto_assigned=' || count(*) FROM "MealProgramAssignments" WHERE "ProgramId"='7fe77ed4-e152-45bf-8b7a-c21a92077d49';
SELECT 'athletic_assigned=' || count(*) FROM "MealProgramAssignments" WHERE "ProgramId"='67ea99eb-79e3-489f-bfc9-3346e4c5dc5c';
