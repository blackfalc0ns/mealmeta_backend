BEGIN;

INSERT INTO "CampaignPricingPolicies"
  ("Id","Version","Model","CurrencyCode","RoundingRule","ContributionRulesJson","StackingRulesJson","RefundRulesJson","ApprovedByUserId","ApprovedAtUtc","CreatedAtUtc","IsDeleted")
SELECT gen_random_uuid(), 1, 'ReferenceCustomerDiscount', 'KWD', 'AwayFromZero',
  '{"restaurantShare":0.33}', '{"stacking":"exclusive"}', '{"basis":"undeliveredDays"}',
  COALESCE((SELECT "CreatedByUserId" FROM "Campaigns" WHERE "Id"='23b8e0a4-bd10-48b7-81bd-63024a429f71'), gen_random_uuid()),
  now(), now(), false
WHERE NOT EXISTS (SELECT 1 FROM "CampaignPricingPolicies" WHERE "CurrencyCode"='KWD' AND "ApprovedByUserId" IS NOT NULL AND "IsDeleted"=false);

INSERT INTO "CampaignBundleDurationPrices"
  ("Id","CampaignBundleId","DurationDays","ReferenceCustomerPrice_Amount","ReferenceCustomerPrice_CurrencyCode","Source","Version","CapturedAtUtc")
SELECT gen_random_uuid(), '00eff55a-62fd-4b56-a0a6-2737a2f813b6', d.days, d.amount, 'KWD', 'MM-MOBILE-QA seed', 1, now()
FROM (VALUES (7, 45.000), (26, 156.000)) AS d(days, amount)
WHERE NOT EXISTS (SELECT 1 FROM "CampaignBundleDurationPrices" WHERE "CampaignBundleId"='00eff55a-62fd-4b56-a0a6-2737a2f813b6' AND "DurationDays"=d.days);

INSERT INTO "CampaignRestaurantCapacities"
  ("Id","CampaignId","RestaurantId","ServiceDate","MaxBoxes","ReservedBoxes","CreatedAtUtc","IsDeleted")
SELECT gen_random_uuid(), '23b8e0a4-bd10-48b7-81bd-63024a429f71', 'f8575d1f-943e-455b-bd45-12b5af4eb097', CURRENT_DATE + g, 20, 0, now(), false
FROM generate_series(1, 45) AS g
WHERE NOT EXISTS (SELECT 1 FROM "CampaignRestaurantCapacities" WHERE "CampaignId"='23b8e0a4-bd10-48b7-81bd-63024a429f71' AND "RestaurantId"='f8575d1f-943e-455b-bd45-12b5af4eb097' AND "ServiceDate"=CURRENT_DATE + g);

COMMIT;

SELECT 'policy_approved=' || count(*) FROM "CampaignPricingPolicies" WHERE "ApprovedByUserId" IS NOT NULL AND "CurrencyCode"='KWD';
SELECT 'prices=' || count(*) FROM "CampaignBundleDurationPrices";
SELECT 'future_capacities=' || count(*) FROM "CampaignRestaurantCapacities" WHERE "ServiceDate" >= CURRENT_DATE;
