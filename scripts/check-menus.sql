SELECT mp."Id", mp."NameEn",
  (SELECT string_agg(DISTINCT mpp."ProgramId"::text, ',') FROM "MenuPlanPrograms" mpp WHERE mpp."MenuPlanId"=mp."Id") AS program_ids
FROM "MenuPlans" mp WHERE mp."IsDeleted"=false LIMIT 8;
