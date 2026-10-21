SELECT m."NameEn" || ' | prog=' || LEFT(m."ProgramId"::text,8) || ' | approved=' || m."ApprovalStatus" || ' | avail=' || m."IsAvailableForNewOrders"::text
  || ' | allergens_rev=' || COALESCE(m."AllergensReviewed"::text,'null') || ' | data_resp=' || COALESCE(m."DataResponsibilityConfirmed"::text,'null')
  || ' | imgs=' || (SELECT count(*) FROM "MealImages" i WHERE i."MealId"=m."Id" AND i."IsDeleted"=false)
  || ' | ingredients=' || (SELECT count(*) FROM "MealIngredients" mi WHERE mi."MealId"=m."Id" AND mi."IsDeleted"=false)
  || ' | bad_ing=' || (SELECT count(*) FROM "MealIngredients" mi JOIN "Ingredients" ing ON ing."Id"=mi."IngredientId" WHERE mi."MealId"=m."Id" AND mi."IsDeleted"=false AND (ing."IsActive"=false OR COALESCE(ing."OriginCountryCode",'')=''))
FROM "Meals" m JOIN "RestaurantProfiles" r ON r."Id"=m."RestaurantId"
WHERE m."IsDeleted"=false AND m."Slot"='snack' AND r."Id"='f8575d1f-943e-455b-bd45-12b5af4eb097';
