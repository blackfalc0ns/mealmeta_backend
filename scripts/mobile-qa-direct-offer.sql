BEGIN;

INSERT INTO "Offers"
  ("Id","NameAr","NameEn","DescriptionAr","DescriptionEn","Status","Type","DiscountType","FundingMode",
   "DiscountRate","PlatformShareRate","RestaurantShareRate","StartsAtUtc","EndsAtUtc",
   "TotalUsageLimit","CurrentUsageCount","PerCustomerLimit","CustomerSegment","CampaignId","IsDeleted",
   "AllowPromoCodeStacking","PromoCode","CreatedAtUtc")
SELECT gen_random_uuid(), 'عرض شراء مباشر للتجربة', '[MM-MOBILE-QA] Direct 15% off',
       'عرض مباشر بدون كوبون - جاهز للشراء من صفحة العرض', 'Direct offer without a coupon - purchasable from the offer page',
       3, 1, 1, 1,
       0.15, 0.05, 0.10, now() - interval '1 hour', now() + interval '30 days',
       NULL, 0, 1, 1, NULL, false,
       false, NULL, now()
WHERE NOT EXISTS (SELECT 1 FROM "Offers" WHERE "NameEn"='[MM-MOBILE-QA] Direct 15% off' AND "IsDeleted"=false);

COMMIT;

SELECT 'qa_offer=' || count(*) || ' mode_check_promo_null=' || count(*)
FROM "Offers" WHERE "NameEn"='[MM-MOBILE-QA] Direct 15% off' AND "PromoCode" IS NULL AND "Status"=3;
