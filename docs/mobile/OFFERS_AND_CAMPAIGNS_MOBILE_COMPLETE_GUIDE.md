# الدليل الشامل للموبايل: اختيار العرض وشراؤه — عروض وكوبونات وحملات (2026-10-20)

> **الجمهور:** فريق تطبيق العميل (Flutter).
> **الحالة:** كل المسارات أدناه منشورة ومُتحقق منها حيًّا على `api.trymealmate.com` بتاريخ 2026-10-20 (سجل التحقق في القسم 10).
> **المصدر الرسمي:** استجابة السيرفر دائمًا. التطبيق لا يُعيد حساب نسبًا ولا أسعارًا ولا أهلية محليًا. نصوص الأخطاء من `code` داخل ProblemDetails أو من الرسائل الثنائية ar/en الراجعة حسب `Accept-Language`.
>
> هذا الملف ذاتي الاكتفاء: يجمع خريطة الاختيار + عقود المسارات الثلاثة كاملة + بيانات QA الحية. المراجع الأعمق عند الحاجة:
> [OFFER_DIRECT_PAYMENT_MOBILE_CONTRACT.md](OFFER_DIRECT_PAYMENT_MOBILE_CONTRACT.md) · [CAMPAIGN_MOBILE_FLOW_CONTRACT.md](CAMPAIGN_MOBILE_FLOW_CONTRACT.md) · [MOBILE_BACKEND_HANDOFF_COMPLETE.md](MOBILE_BACKEND_HANDOFF_COMPLETE.md)

---

## 1. القاعدة الذهبية: `purchaseMode` هو الحاكم

كل عنصر في قوائم العروض يحمل حقل `purchaseMode` بقيمتين فقط:

| `purchaseMode` | `sourceType` | `promoCode` | تصرّف التطبيق |
|---|---|---|---|
| `DirectPurchase` | `Offer` | دائمًا null | **المسار أ — الشراء المباشر** (القسم 4): المعرف offer حقيقي يفتح `purchase-options` |
| `CouponOnly` | `Offer` | موجود | **المسار ب — كوبون** (القسم 5): الكود يُطبق في مسار الاشتراك العادي |
| `CouponOnly` | `Campaign` | موجود | كوبون عادي في مسار الاشتراك (الكوبون ليس حملة — لا يفتح تبويب الحملات) |
| `CouponOnly` | `Campaign` | null | بطاقة **للقراءة فقط** — انقل المستخدم لتبويب الحملات (المسار جـ، القسم 6). لا يوجد شراء من هذه البطاقة |

**ممنوع منعًا باتًا:**
- الاستدلال على النمط من اسم العرض أو الصورة أو `packageTargets` أو وجود `promoCode` وحده — استخدم حقل `purchaseMode` حرفيًا.
- تمرير `campaignId` مكان `offerId` في مسارات الشراء المباشر (يرجع 404؛ معرف الحملة ليس معرف offer).
- عرض مساهمات التمويل (`platformShareRate` / `restaurantShareRate`) للمستخدم إطلاقًا.
- إعادة حساب الخصم من النسبة محليًا — اعرض الأرقام كما رجعت.

---

## 2. اكتشاف العروض (قوائم العروض)

### 2.1 المسارات

| الغرض | العقد |
|---|---|
| قائمة العروض (GET) | `GET /api/v1/customer/offers/available` |
| قائمة العروض بجسم مُعاملات | `POST /api/v1/customer/offers/available` |
| هَب العروض | `GET /api/v1/customer/offers/hub` — يرجع `{ "offers": [ ... ] }` |
| تفاصيل عرض | `GET /api/v1/customer/offers/{offerId}/details` |
| حفظ / إلغاء حفظ | `POST` / `DELETE /api/v1/customer/offers/{offerId}/save` |
| قائمة المحفوظ | `GET /api/v1/customer/offers/saved` |

> ⚠️ **تنبيه مهم — استبعاد الحملات من قائمة العروض:**
> `GET /offers/available` و`GET /offers/hub` **لا يقرآن باراميتر `includeCampaigns` أصلًا** (يرجعان الحملات دائمًا). الطريقة الوحيدة لاستبعاد مشروعات الحملات هي **`POST /offers/available`** مع جسم:
> ```json
> { "includeCampaigns": false }
> ```
> **القاعدة المعتمدة لتبويب العروض:** استخدم `POST /offers/available` بـ `includeCampaigns=false`، واجعل الحملات تُجلب حصريًا من تبويب الحملات (`GET /customer/campaigns`). هكذا لا تظهر أبدًا بطاقة حملة ميتة بلا كود داخل قائمة العروض.

**معاملات التصفية الاختيارية (POST body أو GET query):** `cartAmount`, `durationDays`, `deliveryGovernorate`, `programId`, `bundleId`, `restaurantId`. تبويب العروض العام يستدعيها **بلا معاملات**؛ التصفية بالسياق تُستخدم عند وجود اختيار فعلي (مثلًا داخل شاشة باقة).

### 2.2 حقول عنصر العرض (قاموس الـ payload)

| الحقل | المعنى والاستخدام |
|---|---|
| `id` | معرف العرض — للـ `DirectPurchase` هو مفتاح كل مسارات الشراء. لمشروعات الحملات (= `campaignId`) لا يُستخدم للشراء |
| `nameAr` / `nameEn` / `descriptionAr` / `descriptionEn` | نصوص العرض كما هي — لا تكتب نصوصًا محلية بديلة |
| `bannerImageUrl` | اختياري؛ اعرض حالة صورة غير متاحة محليًا ولا تستبدلها بصورة عرض آخر |
| `status`, `startsAtUtc`, `endsAtUtc` | القائمة ترجع النشط داخل نافذته فقط؛ العدّاد التنازلي من `endsAtUtc` مقابل `serverNowUtc` (من تفاصيل العرض)، لا ساعة الجهاز |
| `discountType` | `Percentage`: النسبة في `discountRate` (0.25 = 25% حرفيًا) · `FixedAmount`: المبلغ في `discountAmountKwd` |
| `fundingMode` | تصنيف داخلي — لا يُعرض |
| `totalUsageLimit` / `currentUsageCount` / `remainingSpots` | السقف الكلي والاستهلاك والمتبقي — اعرض المتبقي كما رجع |
| `perCustomerLimit` | حد الاستخدام لكل عميل |
| `customerSegment` | `All` / `NewCustomersOnly` / `ExistingCustomersOnly` (نص، لا تفرض رقمًا) |
| `campaignId` | غير null = العرض مصدره حملة (مزامَن) أو مشروع حملة |
| `allowPromoCodeStacking` | هل يسمح العرض بدمج كوبون فوقه — قرار الدمج من السيرفر وحده |
| `promoCode` | كود الكوبون للمسار ب |
| `termsAndConditions` (وAr/En) | نص الشروط — اعرضه قبل الشراء |
| `eligibleProgramIds` / `eligibleBundleIds` / `eligibleRestaurantIds` | نطاق العرض — للمعلومات فقط؛ لا تبِنِ اختيارات منها |
| `packageTargets[]` | `{ subscriptionType, programId, bundleId, tier, allowedDurationDays[] }` — نطاق الباقات الدقيق. فراغه لا يعني شيء عن مسار الشراء |
| `isAvailable` + `availabilityReasonCode` + `ineligibilityReasonCodes` | `false` = معروض لكن غير قابل للاستخدام لهذا العميل — اعرضه مع السبب واعطل زر الشراء |
| `isSaved` | هل حفظه المستخدم — زر القلب يُدير `save`/`unsave` |
| `purchaseMode` | الحاكم — القسم 1 |

---

## 3. قرارات الشاشة عند الضغط على بطاقة عرض

1. اقرأ `purchaseMode` و`sourceType` و`promoCode` (الجدول في القسم 1).
2. `DirectPurchase` → افتح شاشة «باقات العرض» (المسار أ).
3. `CouponOnly` + كود → شاشة تفاصيل الكوبون (نسخ الكود / «استخدم في الاشتراك») والكود يُستهلك داخل مسار الاشتراك (المسار ب).
4. `CouponOnly` + `sourceType=Campaign` + بلا كود → وجّه لتبويب «حملات» (المسار جـ). الأفضل أصلًا ألا تظهر داخل قائمة العروض (استخدم `includeCampaigns=false`).
5. `isAvailable=false` → البطاقة ظاهرة لكن الزر معطل مع سبب الرجوع.

---

## 4. المسار أ — الشراء المباشر للعرض (`DirectPurchase`)

> العقد الكامل بالتفصيل: [OFFER_DIRECT_PAYMENT_MOBILE_CONTRACT.md](OFFER_DIRECT_PAYMENT_MOBILE_CONTRACT.md). هذا ملخص تنفيذي كافٍ للتنفيذ.

### 4.1 ترتيب الاستدعاءات

```
GET /customer/offers/{offerId}/details
GET /customer/offers/{offerId}/purchase-options[?startDate=yyyy-MM-dd&includeFriday=false]
GET /customer/subscription-flow/summary?offerId=..&programId=..&bundleId=..&tier=..&durationDays=..&startDate=..
POST /customer/subscriptions/checkout          (offerId + quoteId + idempotencyKey)
   ↳ افتح paymentUrl
GET/POST /customer/subscriptions/{subscriptionId}/payment ، /resume
```

- كل الاستدعاءات بهوية عميل مصادق (`Authorization: Bearer`)؛ لا ترسل `customerId` لتحديد المشتري.
- **الكروت:** `purchase-options` يرجع `subscriptionType`, `prerequisites{isComplete, hasDeliveryAddress, hasAllergies, hasDietaryPreferences}`, `blockingReasons[]`, و`options[]` — كل كارت: `programId/NameAr/En`, `bundleId/NameAr/En`, `tier`, `tierNameAr/En`, `durationDays`, `startDate`, `endDate`, `includeFriday`, `totalServiceDays`, `mealsPerDay`, `basePrice`, `campaignDiscount`, `promoDiscount`, `walletCredit`, `gatewayFee`, `finalPrice`, `currencyCode`, `canCheckout`, `blockingReasons[]`.
  - قد ترجع `options: []` — اعرض السبب ولا تصنع اختيارات محلية.
  - لا تفرض `durationDays` من عندك؛ الكروت هي مصدر المدد المتاحة. لا تخلط `durationDays` مع `totalServiceDays`.
  - إذا `subscriptionType=Family`: `options=[]` و`familyFlowDestination` وجهة نقل — استخدم فلو العائلة وعقوده.
- **الملخص (quote):** يُطلب بنفس تركيبة الكارت + `offerId`. المضمون منه: `basePrice`, `campaignDiscount`, `promoDiscount`, `walletCredit`, `gatewayFee`, `finalPrice` + `quoteId`, `summaryExpiresAtUtc`, `canCheckout`, `checkoutBlockReason`, `blockingReasons[]`, `termsAcceptanceRequired`, `checkoutEndpoint`. أي تغيير (اختيار/عنوان/كوبون/تاريخ) = ملخص جديد. الملخص بلا `canCheckout=true` و`quoteId` لا يفتح دفعًا.
- **الإجمالي:** `finalPrice` كما رجع؛ إجمالي الخصم المعروض = `campaignDiscount + promoDiscount`؛ كائن `coupon` يكرر جزءًا من الخصم وليس مبلغًا إضافيًا. رسوم البوابة والتقريب قرار السيرفر — لا تجمع الحقول المقربة محليًا وتتوقع تطابق `finalPrice`.

### 4.2 بدء الدفع

`POST /api/v1/customer/subscriptions/checkout`:

```json
{
  "programId": "...", "bundleId": "...", "tier": "Basic",
  "durationDays": 6, "startDate": "yyyy-MM-dd", "includeFriday": false,
  "offerId": "...", "quoteId": "...",
  "idempotencyKey": "mobile-persisted-key-for-this-attempt",
  "paymentMethodId": 3,
  "callbackUrl": "https://مسموح-لك/payment/success",
  "errorUrl": "https://مسموح-لك/payment/error"
}
```

- `idempotencyKey`: **مطلوب** مع `quoteId`، ≤200 حرف، يُحفظ محليًا قبل الإرسال ويصمد لإعادة تشغيل التطبيق. نفس المفتاح + نفس الطلب = نفس النتيجة (آمن للـ retry). نفس المفتاح بجسم مختلف = conflict. لا تُبدّل المفتاح تلقائيًا عند timeout.
- `startDate` هو **أول يوم توصيل مختار** — لا تضف أيام تجهيز في التطبيق.
- النجاح 200 = `CheckoutStartedDto`: `subscriptionId`, `paymentUrl`, `holdExpiresAtUtc`, `menuPlanVersionId`, `finalPrice`, `paymentStatus`, `fulfillmentStatus`, `startDate`, `paymentDate`.
  - `paymentUrl` ليست دليل نجاح. استخدم `subscriptionId` في كل ما بعد — لا تعِد استخدام offerId/quoteId.
  - بعد `holdExpiresAtUtc` لا تفتح الـ URL مجددًا — اقرأ الحالة/الاستئناف.

### 4.3 أخطاء checkout وسلوك التطبيق

| HTTP | الكود | الإجراء |
|---|---|---|
| 400 | `checkout.idempotency_key_required` / `checkout.idempotency_key_invalid` | صحح المفتاح |
| 400 | `checkout.redirect_url_not_allowed` / `checkout.start_date_required` / `checkout.start_date_invalid` | صحح الروابط/التاريخ |
| 400 | `checkout.coupon_rejected` / `checkout.coupon_full_discount_unsupported` | اعرض السبب؛ صحح/أزل الكود وأعد الملخص |
| 404 | `offers.customer.unavailable` | العرض/الquote غير متاح أو ليس لك — لا تخمّن معرفًا بديلًا |
| 409 | `checkout.quote_changed` | اطلب ملخصًا جديدًا بعد التحقق من المحاولة السابقة |
| 409 | `checkout.idempotency_conflict` | استرجع المحاولة الأصلية أولًا |
| 409 | `checkout.in_progress` (+`subscriptionId`,`paymentStatusUrl`) | تابع نفس الاشتراك — لا تنشئ محاولة ثانية |
| 409 | `checkout.address_required` / `checkout.allergies_required` / `checkout.dietary_preferences_required` / `checkout.existing_subscription` | استكمل البيانات أو اعرض الاشتراك الموجود |
| 410 | `checkout.quote_expired` | أعد الملخص والتوفر |
| 422 | `checkout.selection_not_eligible` | أعد الكروت والملخص |
| 400 | `checkout.capacity_unavailable` | أعد التوفر والملخص |
| 502 | `checkout.payment_init_failed` (المسار القديم) | لا تفترض أمان retry — تحقق من الحالة |

شكل الخطأ قد يكون `{ "error": "code" }` أو ProblemDetails بـ `code` — اقرأ الاثنين ولا تفترض envelope موحدة.

### 4.4 حالة الدفع والاستئناف

- `GET /customer/subscriptions/{subscriptionId}/payment` → `{ subscriptionId, paymentStatus, isPaid, isFailed, amount, currencyCode }`. اعتمد `isPaid`/`isFailed` فقط؛ حالة غير معروفة = قيد التحقق (ليست نجاحًا ولا فشلًا).
- `GET /resume` = preview (`canPayDirectly`, `reason`, `holdExpiresAtUtc`, `suggestedStartDate`, `checkoutStartDate`) · `POST /resume` يقبل اختياريًا `startDate`, `callbackUrl`, `errorUrl`, `paymentMethodId`.
- Polling متباعد بتراجع بعد العودة من الويب فيو/deep link، ويتوقف عند حسم أو مغادرة الشاشة. انقطاع الشبكة ليس نتيجة دفع. لا تبدأ محاولة جديدة قبل التأكد من نتيجة السابقة.

---

## 5. المسار ب — الكوبون (`CouponOnly` + `promoCode`)

1. الكود من `promoCode` — انسخه كما هو (غير حساس لحالة الأحرف عند الإرسال، لكن اعرضه كما رجع).
2. (اختياري) تحقق سريع قبل الاشتراك: `POST /api/v1/customer/offers/validate-discount`.
3. داخل مسار الاشتراك الفردي: أرسل `promoCode` (URL-encoded) في **الملخص** ثم **نفس الكود في checkout** — بدون `offerId` (المسار القديم يظل مدعومًا كما هو).
4. كائن `coupon` في الملخص/الquote:

```json
{ "code": "SAVE20", "status": "valid", "offerId": "...", "discountAmount": 20.000, "messageAr": "...", "messageEn": "..." }
```

حالات الكوبون المغلقة: `valid`, `not_found`, `inactive`, `not_started`, `expired`, `usage_exhausted`, `stacking_conflict`, `ineligible`, `self_referral`, `already_attributed`, `reward_policy_unset` — وأي حالة غير معروفة تُعامل كـ«تحديث مطلوب» لا كنجاح. عند الرفض: احتفظ بإدخال المستخدم واعرض `messageAr/En`.

**قواعد ثابتة:**
- الكوبون يبقى في مسار الاشتراك المؤهل — **لا يفتح تبويب الحملات ولا يُحوَّل إليه**.
- لا يُدمج كوبون فوق سعر حملة: السيرفر يرجع `stacking_conflict` (يرجع بالكود مع `discountAmount=0`).
- قرار الدمج (عرض + كوبون) من السيرفر وحده وفق `allowPromoCodeStacking` وسياساته.

---

## 6. المسار جـ — تبويب الحملات (`sourceType=Campaign`)

> العقد الكامل: [CAMPAIGN_MOBILE_FLOW_CONTRACT.md](CAMPAIGN_MOBILE_FLOW_CONTRACT.md). تبويب مستقل عن «باقات جاهزة» — لكل تبويب loading/empty/error مستقلة.

### 6.1 ترتيب الاستدعاءات

```
GET  /api/v1/customer/campaigns?pageSize=20&cursor=...
GET  /api/v1/customer/campaigns/{campaignId}[?bundleId=..&durationDays=..]
POST /api/v1/customer/campaigns/quote
POST /api/v1/customer/campaigns/checkout-session
POST /api/v1/customer/campaigns/{sessionId}/payment
GET  /api/v1/customer/campaigns/{sessionId}/payment
POST /api/v1/customer/campaigns/subscriptions/{id}/cancel
```

### 6.2 القائمة والتفاصيل

- **القائمة** تعرض الحملات Active داخل نافذة البيع فقط. العنصر: `campaignId`, `nameAr/En`, `descriptionAr/En`, `startsAtUtc/endsAtUtc`, `minPrice` (+`minPriceCampaignBundleId`, `minPriceDurationDays`, `currencyCode`), `remainingSubscriberSlots`, `fullSequenceAvailable`. الترقيم بالـ cursor: أرسل `nextCursor` كما هو (نص Base64)؛ `null` = نهاية؛ cursor غير صالح = 400 `campaign.invalid_cursor`.
  - `minPrice=null` فلا تكتب «يبدأ من» — بطاقة بلا سعر.
- **التفاصيل** ترجع `canCheckout`, `remainingSubscriberSlots`, `blockingReasons[]`, و`options[]` — كل خيار: `campaignBundleId` (**مفتاح الاختيار في quote/checkout**), `catalogBundleId` (لعرض المحتوى فقط — لا يُستخدم في الشراء), `programId/NameAr/En`, `bundleNameAr/En`, `bundleDescriptionAr/En`, `durationDays`, `referencePrice`, `finalPrice`, `currencyCode`, `customerDiscountPercent` (مئوية: 30.000 = 30%), `canShowReferencePrice`, `fullSequenceAvailable`.
  - اختيار الشاشة: برنامج → باقة (`campaignBundleId`) → مدة. تغيير الأب يمسح الاختيار التابع وأي quote محفوظ. خيار وحيد متاح = تحديد تلقائي ظاهر.
  - `blockingReasons[]` عناصرها `{code, messageAr, messageEn, action}` و`action` يقود: `addresses` / `food-preferences` / `subscriptions` / `browse_campaigns` / (بلا action لإخفاء السعر عند `campaign.pricing_policy_not_approved`).

### 6.3 quote → جلسة → دفع

- **`POST /campaigns/quote`**: جسمه `{ customerId, campaignId, programId, campaignBundleId, durationDays, startDate, addressId, promoCode? }` — `customerId` بنفس قيمة التوكن. الرد: `quoteId` (أو `00000000-...` مع `canCheckout=false` وblockingReasons عند عدم الجاهزية — لا شيء حُفظ)، `price{referencePrice, discountAmount, finalPrice, currencyCode, customerDiscountPercent, canShowReferencePrice}`, `expiresAtUtc` (15 دقيقة)، `coupon.status` (`stacking_conflict` دائمًا فوق الحملة مع بقاء الكود و`discountAmount=0`). quote لا يحجز طاقة ولا يخصم مبلغًا.
- **`POST /campaigns/checkout-session`**: الجسم الموصى به `{ idempotencyKey, campaignId, bundleId (= campaignBundleId), customerId, durationDays, requestedServiceDates, quoteId }` — مع `quoteId` تُشتق التواريخ والمبلغ من الـ quote ولا تُرسل قيم متناقضة. الرد: `sessionId`, `finalCustomerPrice`, `currency`, `expiresAtUtc`, `status`, `allocations[]` (مرجع تقويم أول توصيل). تحقق: `finalCustomerPrice == price.finalPrice` من الـ quote.
- **`POST /campaigns/{sessionId}/payment`**: `{ idempotencyKey, callbackUrl?, errorUrl?, paymentMethodId }` → `paymentId`, `paymentUrl` (افتحها WebView/متصفح خارجي؛ الرجوع لا يعني نجاحًا), `amount`, `status`. انقطاع بعد فتح الرابط = **نفس الطلب بنفس المفتاح** يعيد نفس العملية.
- **`GET /campaigns/{sessionId}/payment`** (polling): `{ status, isPaid, isFailed, subscriptionId, ... }` — enum مغلق: `Pending, PaymentPending, Paid, Committing, Completed, PaymentFailed, Expired, RefundPending, Refunded`. `isPaid=true` → شاشة الاشتراك بـ `subscriptionId`. حالة غير محسومة → قيد التحقق (reconciliation السيرفر).
- **احتفظ دائمًا:** `sessionId` + مفتاح الجلسة + مفتاح الدفع — استعادة الطلب بعد قتل التطبيق = إعادة نفس الطلبات بنفس المفاتيح.

### 6.4 أخطاء الحملات

| HTTP | code | التصرف |
|---|---|---|
| 400 | `Campaign.SelectionMismatch` / `campaign.invalid_start_date` / `Campaign.ScheduleUnavailable` | صحح الحالة وأعد quote |
| 400 | `Idempotency.Conflict` | مفتاح جديد لاختيار جديد |
| 400 | `campaign.pricing_policy_not_approved` | أخفِ السعر والزر |
| 400 | `Campaign.Ineligible` | اعرض الموانع كأزرار action |
| 404 | `Campaign.NotFound` / `Campaign.BundleNotInCampaign` / `Campaign.OptionNotFound` / `Campaign.QuoteNotFound` / `campaign.payment.session_not_found` | ارجع للقائمة |
| 409 | `Campaign.CapacityUnavailable` | «تصفح حملات أخرى» — لا retry تلقائي |
| 409 | `Campaign.QuoteStale` / `campaign.payment.idempotency_conflict` / `campaign.payment.already_started` / `campaign.payment.session_not_payable` | حدّث الشاشة قبل أي محاولة |
| 410 | `Campaign.NoLongerAvailable` / `Campaign.QuoteExpired` (+`refreshSummary`,`refreshAvailability`) / `campaign.payment.session_expired` | quote جديد → جلسة جديدة → مفتاح دفع جديد |
| 503 | `campaign.payment.initialization_unknown` / `campaign.payment.status_unavailable` | استعلم الحالة ولا تفترض شيئًا |

---

## 7. قواعد البيانات والعرض (كل المسارات)

- **النسب:** مئوية بمقياس واضح — `discountRate=0.25` و`customerDiscountPercent=30.000` كلاهما يعني خصمًا حقيقيًا 25% و30%. لا تشتق نسبة من قسمة مبالغ.
- **المبالغ:** KWD بثلاث خانات عشرية كما رجعت — بلا تقريب أو تحويل أو جمع محلي يُعرض بديلًا عن `finalPrice`.
- **التواريخ:** `*Utc` بصيغة ISO-8601 UTC؛ `startDate`/`serviceDate` بصيغة `yyyy-MM-dd`. المرجع الزمني للسيرفر `serverNowUtc` — لا ساعة الجهاز.
- **العملة والرسوم:** اعرض `currencyCode` كما رجع؛ رسوم البوابة والتقريب النهائي من السيرفر.
- **الأسماء والمعرفات:** لا تطابق برامج/باقات/tiers/عروضًا بالاسم أو الصورة — بالمعرفات الثابتة فقط. أسماء tiers من `tierNameAr/En`.
- **RTL وEN:** كل النصوص من الموارد الراجعة (`nameAr/En`, `messageAr/En`) — لا نصوص محلية بديلة.

---

## 8. بيانات QA الحية على api.trymealmate.com (بتاريخ 2026-10-20)

### 8.1 عروض جاهزة للشراء المباشر (`purchaseMode=DirectPurchase`)

| العرض | offerId | ملاحظات |
|---|---|---|
| `[MM-MOBILE-QA] Direct 15% off` | `4e667881-9219-448b-8d4e-08287dd042fd` | بلا package targets — يفتح كل الكتالوج (كان 126 كارتًا وقت التحقق) |
| `[MM-MOBILE-QA] Direct Keto 20% off` | `126350ba-c7d0-463b-93bd-43109e64e316` | عليه package target واحد |
| `[MM-MOBILE-QA] Direct Athlete 10% off` | `892b09c1-edc5-4486-8c44-f77790572f31` | عليه package target واحد |

### 8.2 كوبونات جاهزة (`CouponOnly` + كود)

| الكود | العرض | الخصم |
|---|---|---|
| `MMQA25` | `[MM-MOBILE-QA] Coupon Family 25% off` | 25% |
| `MMQA10` | كوبون اختبار عام | 10% |
| `KETO5KWD` | خصم باقات الكيتو | 5 د.ك ثابت |
| `WEEKLY25` / `FAMILY30` / `ATHLETE20` | عروض قياسية | 25% / 30% / 20% |

### 8.3 حملات جاهزة (تبويب حملات)

| الحملة | campaignId |
|---|---|
| حملة الرياضيين (20%) | `3efb7d6a-1faf-4e88-91c5-921208f55e5a` |
| حملة الكيتو الشهرية (25%) | `44c65383-7e99-4e07-91ce-49ab7fe580b1` |
| حملة خسارة الوزن (30%) | `550a7ca0-92fb-4806-b808-5ed01f96a3b2` |
| حملة معاينة (كوبون `MM-MOBILE-AD-PREVIEW-CAMPAIGN-15`) | `23b8e0a4-bd10-48b7-81bd-63024a429f71` |

> **تنبيه أهلية (اتصلح 2026-10-20):** العروض الأربعة أعلاه كانت مُبذَّرة بـ `CustomerSegment=NewCustomersOnly` فتختفي من أي عميل لديه اشتراكات سابقة (يظهر له 7 عناصر بدل 11). اتصححت على السيرفر والسكربتات إلى `All` — بعد التثبيت يفترض أن `POST /available` بـ `includeCampaigns=false` يرجع **11 عنصرًا منهم 3 DirectPurchase لأي عميل**. لو رجع 7 من جديد، افحص `CustomerSegment` في جدول `Offers` أولًا.

### 8.4 حساب اختبار

- حساب العميل للاختبار: `+96551004522` (كلمة مرور الـ probe المعروفة للفريق) — ملفه مكتمل (عنوان + حساسيات + تفضيلات) فيمرر `prerequisites`.
- كلمات المرور والأرقام أعلاه لأغراض QA الداخلي فقط ولا تُستخدم تجاريًا.

---

## 9. أخطاء شائعة يجب ألا تتكرر (من مراجعة 2026-10-20)

1. استدعاء `GET /offers/available?includeCampaigns=true` ثم محاولة شراء بطاقة حملة منها — الباراميتر غير مربوط في GET والبطاقة بلا كود: **هذا بالضبط سبب «مش عارف يختار العروض»**. استخدم `POST /available` بـ `includeCampaigns=false` + تبويب حملات مستقل.
2. انتظار `promoCode` لكل عرض — عروض `DirectPurchase` مقصود ألا يكون لها كود؛ الشراء من شاشة العرض نفسها.
3. تمرير `campaignId` إلى `GET /offers/{offerId}/purchase-options` — 404.
4. افتراض أن غياب الحملة من قائمة العروض يعني اختفاءها — تحقق من تبويب الحملات (`GET /customer/campaigns`) قبل الحكم.
5. عرض «يبدأ من X د.ك» و`minPrice=null`، أو إظهار سعر مشطوب و`canShowReferencePrice=false`.
6. تغيير `idempotencyKey` عند timeout بدل إعادة نفس المفتاح.
7. اعتبار رجوع `callbackUrl` نجاحًا للدفع — النجاح من حالة السيرفر فقط.
8. بناء باقات/مدد من اسم العرض أو `packageTargets` — التركيبات من `purchase-options`/`options` حصريًا.
9. مقارنة نتيجة القائمة بين حسابين مختلفين وافتراض خللًا — `CustomerSegment=NewCustomersOnly/ExistingCustomersOnly` تجعل العرض مرئيًا لحساب لا للآخر. بيانات QA يجب أن تكون `All` (فعلًا كذلك منذ إصلاح 2026-10-20).

---

## 10. سجل التحقق الحي (2026-10-20)

نفّذ فريق الباك اند الاستدعاءات التالية مباشرة على `api.trymealmate.com` بحساب الـ probe ونجحت جميعًا:

| الاستدعاء | النتيجة |
|---|---|
| `POST /api/v1/auth/customers/login` | 200 + توكن |
| `GET /api/v1/customer/offers/available?includeCampaigns=true` | 15 عنصرًا: 3 عروض `DirectPurchase` + كوبونات + 3 حملات + معاينة |
| `GET /api/v1/customer/offers/4e667881-.../purchase-options` | 126 كارتًا مسعّرًا، `canCheckout=true`، المتطلبات مكتملة |
| `GET /api/v1/customer/campaigns?pageSize=20` | 4 حملات بـ `minPrice` وسعات و`fullSequenceAvailable=true` |
| `GET /api/v1/customer/campaigns/3efb7d6a-...` | `canCheckout=true` وخياران مسعّران (60→48.000 و110→88.000 بخصم 20%) |
| `GET /api/v1/customer/offers/hub` | 200 `{ "offers": [15] }` |
| `POST /api/v1/customer/offers/available` بجسم `{ "includeCampaigns": false }` | 11 عنصرًا: 3 `DirectPurchase` + 8 `CouponOnly` — الحملات مستبعدة والعروض المباشرة ظاهرة |

> **ملاحظة على السجل:** أول إصدار من هذا القسم تحقق عبر GET فقط. أثناء مراجعة لاحقة ظهر أن عروض الـ QA كانت `NewCustomersOnly` فتختفي من العملاء ذوي الاشتراكات السابقة (يرجع POST 7 عناصر) — صُححت الأهلية إلى `All` على السيرفر والسكربتات، وأُعيد التحقق من POST نفسه (الصف المضاف أعلاه).

> **المتبقي قبل الإطلاق التجاري** (من عقدي المسارين): تجربة دفع sandbox كاملة من إنشاء checkout حتى تأكيد السيرفر، والتحقق من ظهور الاشتراك والجدول بعد النجاح. اختبارات Git والنشر وحدهما لا تثبت بوابة الدفع.
