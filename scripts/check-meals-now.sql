SELECT 'total_meals=' || count(*) FROM "Meals" WHERE "IsDeleted"=false;
SELECT 'wl_named=' || count(*) FROM "Meals" WHERE "IsDeleted"=false AND "NameEn" LIKE '% WL';
SELECT 'ath_named=' || count(*) FROM "Meals" WHERE "IsDeleted"=false AND "NameEn" LIKE '% ATH';
SELECT 'snack2_named=' || count(*) FROM "Meals" WHERE "IsDeleted"=false AND "NameEn" LIKE '% II';
SELECT m."NameEn" || ' | prog=' || LEFT(m."ProgramId"::text,8) || ' | slot=' || m."Slot" || ' | imgs=' || (SELECT count(*) FROM "MealImages" i WHERE i."MealId"=m."Id" AND i."IsDeleted"=false)
FROM "Meals" m WHERE m."IsDeleted"=false AND m."RestaurantId"='f8575d1f-943e-455b-bd45-12b5af4eb097' ORDER BY m."ProgramId", m."Slot";
