INSERT INTO "MealImages" ("Id","MealId","ImageUrl","DisplayOrder","IsPrimary","CreatedAtUtc","IsDeleted")
SELECT gen_random_uuid(), n."Id", i."ImageUrl", COALESCE(i."DisplayOrder",0), true, now(), false
FROM "Meals" n
JOIN "Meals" m ON m."RestaurantId" = n."RestaurantId" AND m."Slot" = n."Slot"
  AND m."IsDeleted"=false AND m."NameEn" NOT LIKE '% WL' AND m."NameEn" NOT LIKE '% ATH' AND m."NameEn" NOT LIKE '% II'
JOIN "MealImages" i ON i."MealId"=m."Id" AND i."IsDeleted"=false
WHERE n."IsDeleted"=false AND (n."NameEn" LIKE '% WL' OR n."NameEn" LIKE '% ATH')
  AND NOT EXISTS (SELECT 1 FROM "MealImages" x WHERE x."MealId"=n."Id" AND x."IsDeleted"=false);
SELECT 'imgs_for_new_meals=' || count(*) FROM "MealImages" i JOIN "Meals" m ON m."Id"=i."MealId" WHERE m."NameEn" LIKE '% WL' OR m."NameEn" LIKE '% ATH';
