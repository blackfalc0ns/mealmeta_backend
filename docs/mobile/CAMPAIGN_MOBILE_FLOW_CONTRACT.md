# عقد ربط موبايل الحملات — تبويب «حملات» المستقل (2026-10-20)

> **للفريق:** هذا الملف هو الملف الرابط الكامل لتنفيذ تبويب الحملات في تطبيق العميل. لا يلزم أي تغيير في الباك اند؛ كل المسارات أدناه جاهزة ومدعومة باختبارات. المصدر الرسمي للأخطاء هو `code` داخل ProblemDetails، والنصوص من موارد الترجمة حسب `Accept-Language`.

**القواعد الأربعة غير القابلة للتفاوض:**

1. الكوبون **ليس حملة**: يبقى في مسار العروض/الاشتراكات المؤهلة الحالي، ولا يُحوّل دخوله العميل إلى شاشة الباقات الجاهزة.
2. التطبيق **لا يعيد حساب النسب أو الأسعار محليًا**. كل رقم يعرض كما رجع من السيرفر.
3. لا يُسمح بدمج كوبون فوق سعر الحملة: السيرفر يرجع `stacking_conflict` ويُبقي الكود — لا تجرب دمجًا من التطبيق.
4. لا تعرض أبدًا مساهمات تمويل المطعم أو المنصة؛ السيرفر أصلًا لا يرسلها في عقود العميل الجديدة.

---

## 1) خريطة المسارات (مجموعة واحدة، مصادقة عميل)

المسار الأساسي: `/api/v1/customer/campaigns` — كل العمليات تتطلب `Authorization: Bearer <token>` بعميل مسجل دخول (عدا price-preview القديم).

| # | العملية | العقد |
|---|---|---|
| 1 | قائمة الحملات | `GET /api/v1/customer/campaigns?pageSize=20&cursor=...` |
| 2 | تفاصيل حملة | `GET /api/v1/customer/campaigns/{campaignId}` |
| 3 | عرض سعر محفوظ | `POST /api/v1/customer/campaigns/quote` |
| 4 | إنشاء جلسة دفع/حجز | `POST /api/v1/customer/campaigns/checkout-session` |
| 5 | بدء الدفع | `POST /api/v1/customer/campaigns/{sessionId}/payment` |
| 6 | حالة الدفع (polling) | `GET /api/v1/customer/campaigns/{sessionId}/payment` |
| 7 | إلغاء اشتراك حملة | `POST /api/v1/customer/campaigns/subscriptions/{id}/cancel` |

الـ callback البنكي (`/api/v1/webhooks/campaign-payment/callback`) **خاص بالبوابة حصريًا** — التطبيق لا يقرأ منه ولا يثق بأي نجاح منه.

---

## 2) قائمة الحملات — `GET /campaigns`

**Query:** `pageSize` (افتراضي 20، أقصى 50)، `cursor` (من `nextCursor` بالرد السابق).

**الاستجابة 200:**

```json
{
  "items": [
    {
      "campaignId": "8f1c...",
      "nameAr": "حملة كيتو الشهرية",
      "nameEn": "Monthly Keto Campaign",
      "descriptionAr": null,
      "descriptionEn": null,
      "startsAtUtc": "2026-10-18T08:00:00Z",
      "endsAtUtc": "2026-11-01T08:00:00Z",
      "minPrice": 109.200,
      "currencyCode": "KWD",
      "minPriceCampaignBundleId": "3bd2...",
      "minPriceDurationDays": 26,
      "remainingSubscriberSlots": 42,
      "fullSequenceAvailable": true
    }
  ],
  "nextCursor": "NjM4...",
  "serverNowUtc": "2026-10-20T09:00:00Z"
}
```

**قواعد ثابتة:**
- القائمة تعرض الحملات **Active داخل نافذة البيع فقط** (`startsAt <= serverNow < endsAt`). المسودة/المجدولة/الموقوفة/المنتهية/المكتمل السقف لا تظهر أصلًا.
- `minPrice` يظهر **فقط** عند وجود اختيار مسعّر فعليًا تحت سياسة معتمدة وطاقة تكفي المدة كاملة. إذا `minPrice = null` فلا تكتب «يبدأ من» — اعرض البطاقة بلا سعر.
- لا تقارن عملات مختلفة: إذا اختلطت العملات يرجع `minPrice = null` بثلاث حقوله null.
- `nextCursor = null` تعني نهاية القائمة. أرسل `cursor` كما هو (نص Base64) دون فك تشفيره. `cursor` غير صالح = 400 بكود `campaign.invalid_cursor`.
- ترتيب ثابت: `startsAtUtc` ثم `createdAtUtc` — آمن للتمرير اللانهائي.
- `remainingSubscriberSlots = null` تعني بلا سقف معلن.

**شاشة التبويب:** تبويبان مستقلان — «باقات جاهزة» (المسار الحالي) و«حملات» (هذا العقد). لا تعرض حملة داخل قائمة العروض (`GET offers` مع `includeCampaigns=false` متاح عند الحاجة) ولا باقة جاهزة داخل تبويب الحملات. حالات: loading / empty (لا حملات) / error — كل واحدة مستقلة لكل تبويب، وتبديل التبويبات أثناء loading مسموح ولا يكسر الحالة.

---

## 3) تفاصيل حملة — `GET /campaigns/{campaignId}`

**Query اختياري:** `bundleId` + `durationDays` معًا لاختبار خيار بعينه.

**الاستجابة 200:**

```json
{
  "campaignId": "8f1c...",
  "nameAr": "حملة كيتو الشهرية",
  "nameEn": "Monthly Keto Campaign",
  "descriptionAr": null,
  "descriptionEn": null,
  "startsAtUtc": "2026-10-18T08:00:00Z",
  "endsAtUtc": "2026-11-01T08:00:00Z",
  "serverNowUtc": "2026-10-20T09:00:00Z",
  "canCheckout": true,
  "remainingSubscriberSlots": 42,
  "blockingReasons": [],
  "options": [
    {
      "campaignBundleId": "3bd2...",
      "catalogBundleId": "9a1e...",
      "programId": "77aa...",
      "programNameAr": "برنامج كيتو",
      "programNameEn": "Keto Program",
      "bundleNameAr": "باقة ٢٦ يوم",
      "bundleNameEn": "26-Day Bundle",
      "bundleDescriptionAr": "٣ وجبات يوميًا",
      "bundleDescriptionEn": "3 meals a day",
      "durationDays": 26,
      "referencePrice": 156.000,
      "finalPrice": 109.200,
      "currencyCode": "KWD",
      "customerDiscountPercent": 30.000,
      "canShowReferencePrice": true,
      "fullSequenceAvailable": true
    }
  ]
}
```

**قواعد ثابتة:**
- `campaignBundleId` هو مفتاح الاختيار في كل المسارات اللاحقة (quote/checkout). `catalogBundleId` للاستخدام في شاشة تفاصيل المحتوى فقط. **لا تخلطهما** ولا تفترض أن أحدهما يصلح مكان الآخر.
- `customerDiscountPercent = 30.000` تعني حرفيًا 30% (مقياس مئوي، 3 منازل عشرية). `referencePrice` يُعرض مشطوبًا فقط عند `canShowReferencePrice = true`؛ إذا false فالسعر المرجعي `null` — لا تعرض «وفر X دينار» ولا سعرًا مشطوبًا.
- `blockingReasons[]` عناصرها `{code, messageAr, messageEn, action}` — اعرضها من الموارد الرجعة (لا تكتب نصوصًا محلية بديلة)، و`action` يقود لشاشة الإصلاح:
  - `profile.address_missing` → شاشة العناوين (action = `addresses`)
  - `profile.allergies_missing` / `profile.dietary_missing` → شاشة الحساسية/التفضيلات (action = `food-preferences`)
  - `campaign.already_subscribed` → شاشة اشتراكاتي (`subscriptions`)
  - `campaign.capacity_full` / `campaign.no_available_capacity` → زر واحد «تصفح حملات أخرى» (`browse_campaigns`)
  - `campaign.pricing_policy_not_approved` → لا يوجد action؛ أخفِ السعر والشراء واعرض الرسالة.
- حالة الحملة اختفت بعد الفتح:
  - `404` كود `Campaign.NotFound` — غير منشورة أو معرف خاطئ (ارجع لقائمة الحملات).
  - `410` كود `Campaign.NoLongerAvailable` — منتهية أو موقوفة كانت مرئية سابقًا (ارجع لقائمة الحملات برسالة).
  - `409` كود `Campaign.OptionCapacityUnavailable` — طلب خيار بعينه (`bundleId+durationDays`) وطاقته لم تعد تكفي المدة كاملة (حدّث الشاشة، عطّل الخيار).
  - `404` كود `Campaign.OptionNotFound` — الخيار غير موجود في هذه الحملة.

---

## 4) تسلسل الاختيار في شاشة التفاصيل

1. اختَر **البرنامج** (programId) → 2. ثم **الباقة** (campaignBundleId) → 3. ثم **المدة** (durationDays).
- تغيير الأب (برنامج/باقة) **يمسح الاختيار التابع وأي quote قديم محفوظ** في حالة الشاشة.
- إذا كان هناك خيار وحيد مسعّر متاح، حدّده تلقائيًا لكن أبقه ظاهرًا.
- تجاهل استجابة quote أقدم إذا تغير الاختيار أثناء الطلب (اطلب رقم تسلسل/إلغاء الطلب السابق).
- استنتج `startDate` من منتقي تاريخ (لا يقل عن اليوم التالي بتوقيت السيرفر — راجع `serverNowUtc`)، وأرسل `addressId` من العنوان النشط للمستخدم.

---

## 5) عرض السعر المحفوظ — `POST /campaigns/quote`

**الطلب:**

```json
{
  "customerId": "5f2e...",
  "campaignId": "8f1c...",
  "programId": "77aa...",
  "campaignBundleId": "3bd2...",
  "durationDays": 26,
  "startDate": "2026-10-22",
  "addressId": "c11d...",
  "promoCode": "WELCOME10"
}
```

- `customerId` **لا يُؤخذ كثقة**: السيرفر يفرض هوية التوكن؛ أرسل نفس قيمة المستخدم الحالي.
- `promoCode` اختياري. عند الإرسال يقيَّم الكوبون بتقييم الفردي الحالي وتُرجع حالته **دون خصمه من سعر الحملة**.
- `startDate` في الماضي = 400 `campaign.invalid_start_date`.

**الاستجابة 200 (جاهز للشراء):**

```json
{
  "quoteId": "e400...",
  "selection": {
    "campaignId": "8f1c...",
    "programId": "77aa...",
    "campaignBundleId": "3bd2...",
    "durationDays": 26,
    "startDate": "2026-10-22",
    "addressId": "c11d...",
    "promoCode": "WELCOME10"
  },
  "price": {
    "referencePrice": 156.000,
    "discountAmount": 46.800,
    "finalPrice": 109.200,
    "currencyCode": "KWD",
    "customerDiscountPercent": 30.000,
    "canShowReferencePrice": true
  },
  "expiresAtUtc": "2026-10-20T09:15:00Z",
  "serverNowUtc": "2026-10-20T09:00:00Z",
  "canCheckout": true,
  "blockingReasons": [],
  "coupon": {
    "code": "WELCOME10",
    "status": "stacking_conflict",
    "offerId": null,
    "discountAmount": 0,
    "messageAr": "لا يمكن دمج الكوبون مع سعر الحملة.",
    "messageEn": "The coupon cannot be combined with the campaign price."
  }
}
```

**الاستجابة 200 (غير جاهز):** نفس الشكل مع `quoteId = "00000000-0000-0000-0000-000000000000"` و `canCheckout = false` و `blockingReasons` معبأة. **لا شيء حُفظ ولا حُجز** — اعرض الموانع وأزرار `action`، وعند إصلاحها أعد `POST quote`.

**حالات الكوبون** (`coupon.status`): `valid` غير متوقع فوق الحملة (سيرجع `stacking_conflict` دائمًا مع بقاء الكود و`discountAmount = 0`)، وكذلك `not_found` / `expired` / `inactive` / `not_started` / `usage_exhausted` / `ineligible` / `self_referral` / `already_attributed`. اعرض رسالة ar/en الراجعة كما هي. الكوبون لا يمنع تكوين الـ quote بلا كوبون.

**الأسعار:** KWD بثلاث منازل عشرية، لا تقريب من التطبيق. **quote لا يحجز أي طاقة ولا يخصم أي مبلغ**؛ `expiresAtUtc` مدة صلاحية السعر فقط (15 دقيقة) وهي مستقلة عن مدة الحجز بعد الجلسة.

**أخطاء quote:** 400 (`Campaign.SelectionMismatch` — programId لا يطابق، `campaign.invalid_start_date`، `Campaign.ScheduleUnavailable`)، 403 (`Campaign.AddressNotOwned`)، 404 (`Campaign.PricingNotFound` — مدة غير مسعرة، `Campaign.BundleNotInCampaign`)، 401/403 للمصادقة.

---

## 6) إنشاء الجلسة — `POST /campaigns/checkout-session`

**الشكل الجديد (الموصى به لتبويب الحملات) — quoteId هو مصدر الحقيقة:**

```json
{
  "idempotencyKey": "ck-9f2e-26-20261022-1",
  "campaignId": "8f1c...",
  "bundleId": "3bd2...",
  "customerId": "5f2e...",
  "durationDays": 26,
  "requestedServiceDates": ["2026-10-22"],
  "quoteId": "e400..."
}
```

- عند إرسال `quoteId`: التواريخ والمبلغ والشروط **تُشتق من الـ quote المحفوظ**؛ أي قيم متناقضة من التطبيق تُرفض — لا تحاول تمرير تواريخ أو أسعار من التطبيق.
- `requestedServiceDates` يُتجاهل في المسار الجديد (أرسل أي قيمة صيغتها صحيحة). في المسار القديم (بلا `quoteId`) يبقى عقد 2026-10 القديم كما هو ولم يتغير.
- `idempotencyKey`: مفتاح ثابت تولّده الشاشة عند تأكيد الاختيار (scope **لكل عميل** — نفس المفتاح من عميلين لا يتصادمان). نفس المفتاح+نفس الحمولة يعيد **نفس الجلسة** (آمن لإعادة المحاولة بعد انقطاع الشبكة). نفس المفتاح بحمولة مختلفة = 409 `Idempotency.Conflict`.

**الاستجابة 200:**

```json
{
  "sessionId": "aa90...",
  "campaignId": "8f1c...",
  "bundleId": "3bd2...",
  "customerId": "5f2e...",
  "durationDays": 26,
  "finalCustomerPrice": 109.200,
  "currency": "KWD",
  "expiresAtUtc": "2026-10-20T09:20:00Z",
  "status": "Pending",
  "allocations": [ { "serviceDate": "2026-10-22", "restaurantId": "12bb...", "units": 1 } ]
}
```

**تحقق التطبيق:** `finalCustomerPrice` **يساوي** `price.finalPrice` في الـ quote. إذا اختلف (لن يحدث بعقد quoteId) لا تبدأ دفعًا — اطلب quote جديدًا.

**أخطاء الجلسة:**
- `410` `Campaign.QuoteExpired` + extensions `refreshSummary: true, refreshAvailability: true` → **اطلب quote جديدًا بنفس الاختيار ثم جلسة جديدة ومفتاح idempotency جديد.** لا تُعد إرسال المفتاح القديم.
- `409` `Campaign.QuoteStale` — تغيرت الشروط/السعر/إصدار الحملة بعد الـ quote → نفس سيناريو 410.
- `409` `Campaign.CapacityUnavailable` — نفدت الطاقة أثناء الحجز الذري (حدث واقعي بعد فتح التفاصيل) → زر واحد يعيد المستخدم لقائمة الحملات (`browse_campaigns`). **لا تعيد المحاولة تلقائيًا** ولا بسعر مختلف.
- `409` `Campaign.QuoteNotFound` / `400` `Idempotency.Conflict` / `400` `campaign.pricing_policy_not_approved` / `400` `Campaign.Ineligible` (موانع الملف الشخصي — اعرضها كأزرار action كالتفاصيل).
- الحجز الملزم يُعاد تقييمه داخل transaction عند هذه الخطوة فقط. القائمة والتفاصيل ليستا حجزًا.

---

## 7) الدفع — `POST /{sessionId}/payment`

**الطلب:** `{ "idempotencyKey": "pay-9f2e-26-20261022-1", "callbackUrl": "https://app.mealmate/campaign/pay/return", "errorUrl": "https://app.mealmate/campaign/pay/error", "paymentMethodId": 3 }`

- `callbackUrl`/`errorUrl` اختيارية (افتراضي مهيأ بالسيرفر؛ https ضمن النطاقات المسموحة فقط).
- **احتفظ بالتطبيق** بـ: `sessionId`، مفتاح إنشاء الجلسة، ومفتاح الدفع — كلها ضرورية لاستعادة الطلب بعد انقطاع/قتل التطبيق (استعادة الطلب = إعادة نفس الطلبات بنفس المفاتيح، لا جلسات جديدة).

**الاستجابة 200:**

```json
{
  "sessionId": "aa90...",
  "paymentId": "pz-track-123",
  "paymentUrl": "https://payzah.example/checkout/...",
  "amount": 109.200,
  "currency": "KWD",
  "status": "Pending",
  "expiresAtUtc": "2026-10-20T09:20:00Z"
}
```

افتح `paymentUrl` (WebView/external browser). **لا تعرض نجاحًا بناءً على رجوع `callbackUrl` وحدها** — الرجوع يعني فقط «ارجع للتطبيق».

**أخطاء الدفع:** 409 `campaign.payment.idempotency_conflict` (نفس المفتاح بجسم مختلف)، 404 `campaign.payment.session_not_found` (جلسة عميل آخر أو معرف خطأ)، **410** `campaign.payment.session_expired` (+ `refreshSummary/refreshAvailability`) — الحجز انتهى: quote جديد → جلسة جديدة → مفتاح دفع جديد، 409 `campaign.payment.already_started`، 400 `campaign.payment.customer_contact_unverified`، 503 `campaign.payment.initialization_unknown` (فشل غامض بعد بلوغ البوابة — **لا تعيد بنفس المفتاح متوقعًا نجاحًا؛ استعلم الحالة** ثم انتظر reconciliation).

**قاعدة الانقطاع:** تعطل الشبكة بعد فتح `paymentUrl`؟ أعد **نفس الطلب بنفس مفتاح الدفع** — السيرفر يعيد نفس العملية المحفوظة (نفس paymentId/paymentUrl) دون إنشاء دفعة ثانية.

---

## 8) حالة الدفع — `GET /{sessionId}/payment` (polling)

**الاستجابة 200:**

```json
{
  "sessionId": "aa90...",
  "paymentId": "pz-track-123",
  "status": "Completed",
  "isPaid": true,
  "isFailed": false,
  "amount": 109.200,
  "currency": "KWD",
  "expiresAtUtc": "2026-10-20T09:20:00Z",
  "subscriptionId": "77fe..."
}
```

- **عند العودة للتطبيق** (deeplink أو رجوع يدوي): ابدأ polling محدود مع backoff (مثلًا كل 3 ثوانٍ حتى 10 محاولات، ثم كل 10 ثوانٍ حتى سقف زمني)، وأوقفه في الخلفية أو عند حالة نهائية.
- `isPaid = true` → وجّه لشاشة الاشتراك بـ `subscriptionId`. `isFailed = true` → شاشة فشل مع زر «إعادة محاولة الدفع» (نفس الجلسة الحية) أو «قائمة الحملات» (بعد انتهاء الحجز).
- `status` غير معروف/معلّق (مثل `RefundPending` أو استجابة 503 `campaign.payment.status_unavailable`): تبقى الحالة **قيد التحقق** — لا تعرض نجاحًا ولا فشلًا. الحالات غير المحسومة يدخلها reconciliation السيرفر ولا يُعلن عنها تخمينًا.
- قائمة `status` الرسمية من enum الجلسة: `Pending, PaymentPending, Paid, Committing, Completed, PaymentFailed, Expired, RefundPending, Refunded` — تعامل معها كمصفوفة مغلقة.

---

## 9) جدول أكواد الأخطاء للعميل

| HTTP | code | معنى التصرف في التطبيق |
|---|---|---|
| 400 | `Campaign.SelectionMismatch` / `campaign.invalid_start_date` / `Campaign.ScheduleUnavailable` | صحّح الطلب (عادة خطأ حالة شاشة) — أعد quote |
| 400 | `Idempotency.Conflict` | مفتاح مستعمل بحمولة أخرى — ولّد مفتاحًا جديدًا لاختيار جديد |
| 400 | `campaign.pricing_policy_not_approved` | الشراء غير متاح حاليًا — أخفِ السعر والزر |
| 400 | `Campaign.Ineligible` | موانع ملف/أهلية — اعرض الموانع كأزرار action |
| 401/403 | — | جلسة منتهية أو محاولة وصول غير مصرح — login |
| 404 | `Campaign.NotFound` / `Campaign.BundleNotInCampaign` / `Campaign.OptionNotFound` / `Campaign.QuoteNotFound` / `campaign.payment.session_not_found` | العنصر غير موجود أو ليس لك — ارجع للقائمة |
| 409 | `Campaign.CapacityUnavailable` | نفدت الطاقة — زر «تصفح حملات أخرى» |
| 409 | `Campaign.QuoteStale` / `campaign.payment.idempotency_conflict` / `campaign.payment.already_started` / `campaign.payment.session_not_payable` | تعارض حالة — حدّث الشاشة قبل أي محاولة |
| 410 | `Campaign.NoLongerAvailable` / `Campaign.QuoteExpired` / `campaign.payment.session_expired` | انتهى العرض/الquote/الحجز — quote جديد وجلسة جديدة؛ اتبع refresh hints |
| 422 | — (Failure عام) | عدم أهلية business عند محاولة شراء فعلية |
| 503 | `campaign.payment.initialization_unknown` / `campaign.payment.status_unavailable` | فشل/حالة غامضة — استعلم الحالة ولا تفترض شيئًا |

كل الاستجابات ProblemDetails موحّدة: `{ "code", "detail", "status", "title", ...extensions }` مع رسائل ar/en من الترجمة.

---

## 10) مقاييس البيانات

- **النسب:** `customerDiscountPercent` مئوية (30.000 = 30%). لا تُشتق من الخصم والمبلغ.
- **المبالغ:** 3 منازل عشرية KWD كما رجعت — بدون تقريب أو تحويل.
- **التواريخ:** `...Utc` بصيغة ISO-8601 UTC؛ `startDate`/`serviceDate` بصيغة `yyyy-MM-dd`. استخدم `serverNowUtc` كمرجع زمني للسيرفر وليس ساعة الجهاز.
- **الجدول:** تواريخ الخدمة تُشتق من `startDate` + المدة + جدول الخدمة الحقيقي (بدل الجمعة افتراضيًا) — `allocations[]` في استجابة الجلسة هي المرجع لعرض تقويم أول توصيل.

---

## 11) سلوكيات يجب الالتزام بها (اختبارها في QA)

1. تبويب الحملات يعرض الصالحة فقط؛ الاختفاء بعد الفتح يتصرف وفق 404/410/409 أعلاه.
2. سعر التفاصيل = مبلغ الدفع المطلوب فعلًا = مبلغ الـ quote (تطابق ثلاثي).
3. سباق آخر مكان: خسران المنافسة يرى 409 ولا ينشأ له جلسة.
4. إعادة نفس الطلب (جلسة أو دفع) بنفس المفتاح تعيد نفس العملية — لا مزدوج.
5. الاشتراك يثبت مرة واحدة بشروطه الأصلية (snapshot)؛ **إيقاف الحملة بعد شراء مكتمل لا يلغي الاشتراك**.
6. انتهاء الحجز بعد فتح شاشة الدفع → 410 → تدفق quote جديد كاملًا؛ لا retry صامت بسعر مختلف.
7. عرض 320/360 + textScale 2 + RTL و EN دون overflow؛ الأسماء الطويلة تلتف؛ زر الاشتراك والسعر ظاهران دائمًا.
8. الكوبون يبقى في مسار الاشتراك المؤهل؛ لا يفتح تبويب الحملات ولا يحوّل إليه.

**جاهزية الإطلاق التجاري:** تجربة دفع sandbox كاملة + التحقق من ظهور الاشتراك والجدول بعد النجاح مطلوبة قبل الإطلاق؛ هذه الوثيقة عقد ربط وليست إثبات تجربة دفع حية.
