BEGIN;

-- ============ Campaign 1: Keto Month 25% (26d @160, 14d @90) ============
WITH c AS (
  INSERT INTO "Campaigns"
    ("Id","NameAr","NameEn","Status","StartsAtUtc","EndsAtUtc","TotalDiscountRate","MealMateShareRate","RestaurantShareRate",
     "FundingMode","MaxSubscribers","CurrentSubscribers","CurrentSnapshotVersion","CreatedAtUtc","IsDeleted")
  SELECT gen_random_uuid(),'حملة الكيتو الشهرية','[MM-MOBILE-QA] Keto Month - 25% off',7,
    now()-interval '1 hour', now()+interval '25 days', 0.25,0.15,0.10, 1,100,0,1, now(),false
  WHERE NOT EXISTS (SELECT 1 FROM "Campaigns" WHERE "NameEn"='[MM-MOBILE-QA] Keto Month - 25% off' AND "IsDeleted"=false)
  RETURNING "Id"
), cb AS (
  INSERT INTO "CampaignBundles" ("Id","CampaignId","BundleId","CreatedAtUtc","IsDeleted")
  SELECT gen_random_uuid(), c."Id", '8227a8ea-e9b7-4a67-a2b2-3c18c4d8d735', now(), false FROM c
  RETURNING "Id"
)
INSERT INTO "CampaignBundleDurationPrices"
  ("Id","CampaignBundleId","DurationDays","ReferenceCustomerPrice_Amount","ReferenceCustomerPrice_CurrencyCode","Source","Version","CapturedAtUtc")
SELECT gen_random_uuid(), cb."Id", d.days, d.amount, 'KWD', 'MM-MOBILE-QA seed', 1, now()
FROM cb, (VALUES (26, 160.000), (14, 90.000)) AS d(days, amount);

INSERT INTO "CampaignPrograms" ("Id","CampaignId","ProgramId","CreatedAtUtc","IsDeleted")
SELECT gen_random_uuid(), c."Id", '7fe77ed4-e152-45bf-8b7a-c21a92077d49', now(), false
FROM "Campaigns" c WHERE c."NameEn"='[MM-MOBILE-QA] Keto Month - 25% off'
AND NOT EXISTS (SELECT 1 FROM "CampaignPrograms" cp WHERE cp."CampaignId"=c."Id");

INSERT INTO "CampaignRestaurantEnrollments"
  ("Id","CampaignId","RestaurantId","Status","RequestedAtUtc","ParticipationStatus","CreatedAtUtc","IsDeleted")
SELECT gen_random_uuid(), c."Id", 'f8575d1f-943e-455b-bd45-12b5af4eb097', 3, now(), 2, now(), false
FROM "Campaigns" c WHERE c."NameEn"='[MM-MOBILE-QA] Keto Month - 25% off'
AND NOT EXISTS (SELECT 1 FROM "CampaignRestaurantEnrollments" e WHERE e."CampaignId"=c."Id" AND e."RestaurantId"='f8575d1f-943e-455b-bd45-12b5af4eb097');

INSERT INTO "CampaignRestaurantCapacities"
  ("Id","CampaignId","RestaurantId","ServiceDate","MaxBoxes","ReservedBoxes","CreatedAtUtc","IsDeleted")
SELECT gen_random_uuid(), c."Id", 'f8575d1f-943e-455b-bd45-12b5af4eb097', CURRENT_DATE + g, 15, 0, now(), false
FROM "Campaigns" c, generate_series(1,45) g
WHERE c."NameEn"='[MM-MOBILE-QA] Keto Month - 25% off'
AND NOT EXISTS (SELECT 1 FROM "CampaignRestaurantCapacities" x WHERE x."CampaignId"=c."Id" AND x."ServiceDate"=CURRENT_DATE + g);

-- ============ Campaign 2: Athletic Sprint 20% (14d @110, 7d @60) ============
WITH c AS (
  INSERT INTO "Campaigns"
    ("Id","NameAr","NameEn","Status","StartsAtUtc","EndsAtUtc","TotalDiscountRate","MealMateShareRate","RestaurantShareRate",
     "FundingMode","MaxSubscribers","CurrentSubscribers","CurrentSnapshotVersion","CreatedAtUtc","IsDeleted")
  SELECT gen_random_uuid(),'حملة الرياضيين','[MM-MOBILE-QA] Athletic Sprint - 20% off',7,
    now()-interval '1 hour', now()+interval '20 days', 0.20,0.10,0.10, 1,60,0,1, now(),false
  WHERE NOT EXISTS (SELECT 1 FROM "Campaigns" WHERE "NameEn"='[MM-MOBILE-QA] Athletic Sprint - 20% off' AND "IsDeleted"=false)
  RETURNING "Id"
), cb AS (
  INSERT INTO "CampaignBundles" ("Id","CampaignId","BundleId","CreatedAtUtc","IsDeleted")
  SELECT gen_random_uuid(), c."Id", '86903897-4676-4d73-8934-8d6d93e275a2', now(), false FROM c
  RETURNING "Id"
)
INSERT INTO "CampaignBundleDurationPrices"
  ("Id","CampaignBundleId","DurationDays","ReferenceCustomerPrice_Amount","ReferenceCustomerPrice_CurrencyCode","Source","Version","CapturedAtUtc")
SELECT gen_random_uuid(), cb."Id", d.days, d.amount, 'KWD', 'MM-MOBILE-QA seed', 1, now()
FROM cb, (VALUES (14, 110.000), (7, 60.000)) AS d(days, amount);

INSERT INTO "CampaignPrograms" ("Id","CampaignId","ProgramId","CreatedAtUtc","IsDeleted")
SELECT gen_random_uuid(), c."Id", '67ea99eb-79e3-489f-bfc9-3346e4c5dc5c', now(), false
FROM "Campaigns" c WHERE c."NameEn"='[MM-MOBILE-QA] Athletic Sprint - 20% off'
AND NOT EXISTS (SELECT 1 FROM "CampaignPrograms" cp WHERE cp."CampaignId"=c."Id");

INSERT INTO "CampaignRestaurantEnrollments"
  ("Id","CampaignId","RestaurantId","Status","RequestedAtUtc","ParticipationStatus","CreatedAtUtc","IsDeleted")
SELECT gen_random_uuid(), c."Id", 'f8575d1f-943e-455b-bd45-12b5af4eb097', 3, now(), 2, now(), false
FROM "Campaigns" c WHERE c."NameEn"='[MM-MOBILE-QA] Athletic Sprint - 20% off'
AND NOT EXISTS (SELECT 1 FROM "CampaignRestaurantEnrollments" e WHERE e."CampaignId"=c."Id" AND e."RestaurantId"='f8575d1f-943e-455b-bd45-12b5af4eb097');

INSERT INTO "CampaignRestaurantCapacities"
  ("Id","CampaignId","RestaurantId","ServiceDate","MaxBoxes","ReservedBoxes","CreatedAtUtc","IsDeleted")
SELECT gen_random_uuid(), c."Id", 'f8575d1f-943e-455b-bd45-12b5af4eb097', CURRENT_DATE + g, 15, 0, now(), false
FROM "Campaigns" c, generate_series(1,45) g
WHERE c."NameEn"='[MM-MOBILE-QA] Athletic Sprint - 20% off'
AND NOT EXISTS (SELECT 1 FROM "CampaignRestaurantCapacities" x WHERE x."CampaignId"=c."Id" AND x."ServiceDate"=CURRENT_DATE + g);

-- ============ Campaign 3: Weight Loss Kickoff 30% (26d @150, 7d @42) ============
WITH c AS (
  INSERT INTO "Campaigns"
    ("Id","NameAr","NameEn","Status","StartsAtUtc","EndsAtUtc","TotalDiscountRate","MealMateShareRate","RestaurantShareRate",
     "FundingMode","MaxSubscribers","CurrentSubscribers","CurrentSnapshotVersion","CreatedAtUtc","IsDeleted")
  SELECT gen_random_uuid(),'حملة خسارة الوزن','[MM-MOBILE-QA] Weight Loss Kickoff - 30% off',7,
    now()-interval '1 hour', now()+interval '18 days', 0.30,0.15,0.15, 1,80,0,1, now(),false
  WHERE NOT EXISTS (SELECT 1 FROM "Campaigns" WHERE "NameEn"='[MM-MOBILE-QA] Weight Loss Kickoff - 30% off' AND "IsDeleted"=false)
  RETURNING "Id"
), cb AS (
  INSERT INTO "CampaignBundles" ("Id","CampaignId","BundleId","CreatedAtUtc","IsDeleted")
  SELECT gen_random_uuid(), c."Id", '71d3bb5d-c4cb-41fa-b18d-9b481504b963', now(), false FROM c
  RETURNING "Id"
)
INSERT INTO "CampaignBundleDurationPrices"
  ("Id","CampaignBundleId","DurationDays","ReferenceCustomerPrice_Amount","ReferenceCustomerPrice_CurrencyCode","Source","Version","CapturedAtUtc")
SELECT gen_random_uuid(), cb."Id", d.days, d.amount, 'KWD', 'MM-MOBILE-QA seed', 1, now()
FROM cb, (VALUES (26, 150.000), (7, 42.000)) AS d(days, amount);

INSERT INTO "CampaignPrograms" ("Id","CampaignId","ProgramId","CreatedAtUtc","IsDeleted")
SELECT gen_random_uuid(), c."Id", 'dd12dfbc-3987-4e38-83af-664b6963c04c', now(), false
FROM "Campaigns" c WHERE c."NameEn"='[MM-MOBILE-QA] Weight Loss Kickoff - 30% off'
AND NOT EXISTS (SELECT 1 FROM "CampaignPrograms" cp WHERE cp."CampaignId"=c."Id");

INSERT INTO "CampaignRestaurantEnrollments"
  ("Id","CampaignId","RestaurantId","Status","RequestedAtUtc","ParticipationStatus","CreatedAtUtc","IsDeleted")
SELECT gen_random_uuid(), c."Id", 'f8575d1f-943e-455b-bd45-12b5af4eb097', 3, now(), 2, now(), false
FROM "Campaigns" c WHERE c."NameEn"='[MM-MOBILE-QA] Weight Loss Kickoff - 30% off'
AND NOT EXISTS (SELECT 1 FROM "CampaignRestaurantEnrollments" e WHERE e."CampaignId"=c."Id" AND e."RestaurantId"='f8575d1f-943e-455b-bd45-12b5af4eb097');

INSERT INTO "CampaignRestaurantCapacities"
  ("Id","CampaignId","RestaurantId","ServiceDate","MaxBoxes","ReservedBoxes","CreatedAtUtc","IsDeleted")
SELECT gen_random_uuid(), c."Id", 'f8575d1f-943e-455b-bd45-12b5af4eb097', CURRENT_DATE + g, 15, 0, now(), false
FROM "Campaigns" c, generate_series(1,45) g
WHERE c."NameEn"='[MM-MOBILE-QA] Weight Loss Kickoff - 30% off'
AND NOT EXISTS (SELECT 1 FROM "CampaignRestaurantCapacities" x WHERE x."CampaignId"=c."Id" AND x."ServiceDate"=CURRENT_DATE + g);

-- ============ Ready offer: Direct Keto 20% with package target ============
WITH o AS (
  INSERT INTO "Offers"
    ("Id","NameAr","NameEn","DescriptionAr","DescriptionEn","Status","Type","DiscountType","FundingMode",
     "DiscountRate","PlatformShareRate","RestaurantShareRate","StartsAtUtc","EndsAtUtc",
     "TotalUsageLimit","CurrentUsageCount","PerCustomerLimit","CustomerSegment","CampaignId","IsDeleted",
     "AllowPromoCodeStacking","PromoCode","CreatedAtUtc")
  SELECT gen_random_uuid(),'عرض كيتو مباشر 20%','[MM-MOBILE-QA] Direct Keto 20% off',
    'اشترك في باقة الكيتو الكاملة بخصم مباشر','Subscribe to Full Keto Bundle with a direct discount',
    3,1,1,1, 0.20,0.05,0.15, now()-interval '1 hour', now()+interval '30 days',
    NULL,0,1,0,NULL,false, false,NULL, now()
  WHERE NOT EXISTS (SELECT 1 FROM "Offers" WHERE "NameEn"='[MM-MOBILE-QA] Direct Keto 20% off' AND "IsDeleted"=false)
  RETURNING "Id"
)
INSERT INTO "OfferPackageTargets"
  ("Id","OfferId","SubscriptionType","ProgramId","BundleId","Tier","AllowedDurationDays")
SELECT gen_random_uuid(), o."Id", 'Individual', '7fe77ed4-e152-45bf-8b7a-c21a92077d49',
  '8227a8ea-e9b7-4a67-a2b2-3c18c4d8d735', 3, ARRAY[26,14]
FROM o;

-- ============ Ready offer: Direct Athlete 10% with package target ============
WITH o AS (
  INSERT INTO "Offers"
    ("Id","NameAr","NameEn","DescriptionAr","DescriptionEn","Status","Type","DiscountType","FundingMode",
     "DiscountRate","PlatformShareRate","RestaurantShareRate","StartsAtUtc","EndsAtUtc",
     "TotalUsageLimit","CurrentUsageCount","PerCustomerLimit","CustomerSegment","CampaignId","IsDeleted",
     "AllowPromoCodeStacking","PromoCode","CreatedAtUtc")
  SELECT gen_random_uuid(),'عرض الرياضيين المباشر 10%','[MM-MOBILE-QA] Direct Athlete 10% off',
    'اشترك في باقة Elite Athlete بخصم مباشر','Subscribe to Elite Athlete Bundle with a direct discount',
    3,1,1,1, 0.10,0.05,0.05, now()-interval '1 hour', now()+interval '30 days',
    NULL,0,1,0,NULL,false, false,NULL, now()
  WHERE NOT EXISTS (SELECT 1 FROM "Offers" WHERE "NameEn"='[MM-MOBILE-QA] Direct Athlete 10% off' AND "IsDeleted"=false)
  RETURNING "Id"
)
INSERT INTO "OfferPackageTargets"
  ("Id","OfferId","SubscriptionType","ProgramId","BundleId","Tier","AllowedDurationDays")
SELECT gen_random_uuid(), o."Id", 'Individual', '67ea99eb-79e3-489f-bfc9-3346e4c5dc5c',
  '86903897-4676-4d73-8934-8d6d93e275a2', 2, ARRAY[14,7]
FROM o;

-- ============ Ready offer: Coupon Family 25% ============
INSERT INTO "Offers"
  ("Id","NameAr","NameEn","DescriptionAr","DescriptionEn","Status","Type","DiscountType","FundingMode",
   "DiscountRate","PlatformShareRate","RestaurantShareRate","StartsAtUtc","EndsAtUtc",
   "TotalUsageLimit","CurrentUsageCount","PerCustomerLimit","CustomerSegment","CampaignId","IsDeleted",
   "AllowPromoCodeStacking","PromoCode","CreatedAtUtc")
SELECT gen_random_uuid(),'كوبون العائلة 25%','[MM-MOBILE-QA] Coupon Family 25% off',
  'استخدم الكود في الاشتراك العائلي','Apply the code in the family subscription flow',
  3,1,1,1, 0.25,0.10,0.15, now()-interval '1 hour', now()+interval '30 days',
  NULL,0,1,0,NULL,false, false,'MMQA25', now()
WHERE NOT EXISTS (SELECT 1 FROM "Offers" WHERE "NameEn"='[MM-MOBILE-QA] Coupon Family 25% off' AND "IsDeleted"=false);

COMMIT;

SELECT 'campaigns=' || count(*) FROM "Campaigns" WHERE "NameEn" LIKE '[MM-MOBILE-QA]%' AND "Status"=7;
SELECT 'qa_prices=' || count(*) FROM "CampaignBundleDurationPrices" WHERE "Source"='MM-MOBILE-QA seed';
SELECT 'direct_offers=' || count(*) FROM "Offers" o WHERE o."NameEn" LIKE '[MM-MOBILE-QA]%Direct%' AND o."PromoCode" IS NULL;
