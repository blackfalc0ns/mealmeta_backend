SELECT 'meals_total=' || count(*) FROM "Meals" WHERE "IsDeleted"=false;
SELECT 'with_images=' || count(*) FROM (SELECT DISTINCT m."Id" FROM "Meals" m JOIN "MealImages" i ON i."MealId"=m."Id" WHERE m."IsDeleted"=false) x;
SELECT 'with_ingredients=' || count(*) FROM (SELECT DISTINCT m."Id" FROM "Meals" m JOIN "MealIngredients" mi ON mi."MealId"=m."Id" JOIN "Ingredients" ing ON ing."Id"=mi."IngredientId" WHERE m."IsDeleted"=false AND ing."IsActive"=true AND COALESCE(ing."OriginCountryCode",'')<>'' ) x;
SELECT r."TradeNameEn" || ' meals=' || count(*) || ' approved=' || count(*) FILTER (WHERE m."ApprovalStatus"=4) || ' imgs=' || count(*) FILTER (WHERE EXISTS (SELECT 1 FROM "MealImages" i WHERE i."MealId"=m."Id")) || ' slots=' || string_agg(DISTINCT COALESCE(m."Slot",'?'), ',')
FROM "Meals" m JOIN "RestaurantProfiles" r ON r."Id"=m."RestaurantId" WHERE m."IsDeleted"=false GROUP BY r."TradeNameEn";
SELECT 'assigned=' || count(*) FROM "MealProgramAssignments";
