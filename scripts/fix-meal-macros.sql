BEGIN;
UPDATE "Meals" SET
  "Calories" = CASE WHEN "Slot"='snack' THEN 200 ELSE 600 END,
  "ProteinG" = CASE WHEN "Slot"='snack' THEN 12 ELSE 35 END,
  "NetCarbsG" = CASE WHEN "Slot"='snack' THEN 2 ELSE 4 END,
  "CarbsG" = CASE WHEN "Slot"='snack' THEN 5 ELSE 8 END,
  "FatG" = CASE WHEN "Slot"='snack' THEN 16 ELSE 45 END,
  "FiberG" = CASE WHEN "Slot"='snack' THEN 3 ELSE 6 END,
  "ModifiedAtUtc" = now()
WHERE "IsDeleted"=false AND "ApprovalStatus"=4
  AND "RestaurantId" IN (SELECT "Id" FROM "RestaurantProfiles" WHERE "TradeNameEn" LIKE '[TEST]%');
COMMIT;
SELECT 'updated=' || count(*) FROM "Meals" WHERE "Calories" IN (200,600)
  AND "RestaurantId" IN (SELECT "Id" FROM "RestaurantProfiles" WHERE "TradeNameEn" LIKE '[TEST]%');
