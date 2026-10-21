SELECT p."NameEn" || ' | bundle=' || LEFT(c."BundleId"::text,8) || ' | tier=' || c."Tier"::text || ' | restaurants=' || count(*)
FROM "RestaurantClassifications" c JOIN "Programs" p ON p."Id"=c."ProgramId"
WHERE c."IsDeleted"=false GROUP BY p."NameEn", c."BundleId", c."Tier" ORDER BY p."NameEn" LIMIT 20;
