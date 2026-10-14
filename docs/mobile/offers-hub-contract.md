# عقد مركز العروض للعميل (Offers Hub) — handoff موبايل 2026-10-13

> هذا عقد API فقط. تنفيذ الشاشات يتحدد بعد وصول مستودع الموبايل الفعلي.
> كل الأوقات UTC مخزنة، والـ server يرجع `serverNowUtc` لكل envelope يعرض عدّادًا.

## 1) قائمة العروض (hub)

`GET /api/v1/customer/offers/hub?cartAmount=&durationDays=&deliveryGovernorate=&programId=&bundleId=&restaurantId=`
(AllowAnonymous، إضافة لا تكسر `/available` القديم الذي يبقى كما هو)

```json
{
  "offers": [ /* نفس OfferDto المعروف من /available */ ],
  "serverNowUtc": "2026-10-13T12:00:00Z"
}
```

- `serverNowUtc` هو **المصدر الوحيد** لأي عدّاد تنازلي. الموبايل يحسب الفارق من
  round-trip time عند التحميل ويعيد المزامنة عند resume/focus. **لا يستخدم وقت
  الجهاز لقرار شراء** — القرار النهائي دائمًا من الخادم في quote وcheckout.
- الفلترة والترتيب والأهلية من الخادم. لا يُحذف عرض اعتمادًا على لغة الواجهة؛
  `nameAr/nameEn` بيانات ثنائية تعرض حسب locale الحالي مع fallback
  للغة الأخرى غير الفارغة عند نقص القيمة.
- empty list = لا عروض مؤهلة فعلًا (حالة فارغة حقيقية، ليست بيانات مصطنعة).

## 2) quote وسعر الشراء

- `POST /api/v1/customer/campaigns/price-preview` → معاينة سعر مرجعي/نهائي/توفير.
- `POST /api/v1/customer/campaigns/checkout-session` (Authorize، عميل) →
  يرجع `finalCustomerPrice` و`currency` و`expiresAtUtc` — **السعر من الخادم
  دائمًا**؛ لا يقبل الخادم مبلغًا من العميل.
- رموز الأخطاء المستقرة للشاشة (تُترجم محليًا في الموبايل):
  `campaign.review_window_*` (admin فقط)، `campaign.pricing_policy_not_approved`
  (الشراء غير متاح — أظهر رسالة لا شراء)، `Campaign.CapacityUnavailable` (نفدت
  السعة — SoldOut)، `Idempotency.Conflict`، `campaign.concurrent_modification`
  (أعد التحميل وأعد التأكيد).

## 3) الإعلان (Sponsored)

- `GET /api/v1/customer/advertising/placements?placementKey=offers.hero`
  → `{placements:[{id, placementKey, assetReadUrl(مؤقت 30د), targetType, targetId,
  displayEndsAtUtc, isSponsored:true}], serverNowUtc}`.
- الإعلان **دائمًا** `isSponsored: true` ولا يمنح أي خصم. لا يظهر إلا إذا كان
  مدفوعًا ومعتمدًا داخل نافذة عرضه.
- Analytics: `POST /api/v1/customer/advertising/events`
  `{placementId, eventId (uuid يولده التطبيق مرة لكل حدث), eventType: "impression"|"click"}`
  — نفس `eventId` يُحسب مرة واحدة (idempotent).

## 4) حالات الشاشة المطلوبة

loading / empty / error(retry) / نفاد السعة / تغير السعر قبل الدفع (أعد quote
واطلب تأكيدًا جديدًا) / انتهاء العرض (عطّل الشراء وأعد الجلب) / كوبون مرفوض.
عند فشل التحميل: احتفظ بآخر محتوى مخزّن قابلًا للعرض مع زر إعادة محلي.

## 5) نصوص catalog مقترحة (مثال توضيحي وليست مفروضة)

```json
{
  "ar": {"sponsored":"إعلان","couponRejected":"لا يمكن تطبيق هذا الكوبون","rewardPending":"المكافأة قيد الاستحقاق","soldOut":"نفدت السعة","priceChanged":"تغير السعر، أعد المراجعة"},
  "en": {"sponsored":"Sponsored","couponRejected":"This coupon cannot be applied","rewardPending":"Reward pending eligibility","soldOut":"Sold out","priceChanged":"Price changed, please review again"}
}
```
المصدر النهائي للنصوص يحدد في مستودع الموبايل عند التنفيذ (ابدأ من ملف
localization الحالي هناك).
