SELECT 'athletic_pricings_valid_now=' || count(*) FROM "RestaurantBundlePricings"
  WHERE "BundleId"='86903897-4676-4d73-8934-8d6d93e275a2' AND "ProgramId"='67ea99eb-79e3-489f-bfc9-3346e4c5dc5c'
  AND "EffectiveFromUtc" <= now() AND ("EffectiveToUtc" IS NULL OR "EffectiveToUtc" > now())
  AND "LockStatus" IN (1,2,3) AND "ReferencePrice26_Amount" > 0 AND "ContractedSupplyPrice26_Amount" > 0;
SELECT 'wl_pricings_valid_now=' || count(*) FROM "RestaurantBundlePricings"
  WHERE "BundleId"='71d3bb5d-c4cb-41fa-b18d-9b481504b963' AND "ProgramId"='dd12dfbc-3987-4e38-83af-664b6963c04c'
  AND "EffectiveFromUtc" <= now() AND ("EffectiveToUtc" IS NULL OR "EffectiveToUtc" > now())
  AND "LockStatus" IN (1,2,3) AND "ReferencePrice26_Amount" > 0 AND "ContractedSupplyPrice26_Amount" > 0;
SELECT 'keto_pricings_valid_now=' || count(*) FROM "RestaurantBundlePricings"
  WHERE "BundleId"='8227a8ea-e9b7-4a67-a2b2-3c18c4d8d735' AND "ProgramId"='7fe77ed4-e152-45bf-8b7a-c21a92077d49'
  AND "EffectiveFromUtc" <= now() AND ("EffectiveToUtc" IS NULL OR "EffectiveToUtc" > now())
  AND "LockStatus" IN (1,2,3) AND "ReferencePrice26_Amount" > 0 AND "ContractedSupplyPrice26_Amount" > 0;
