> Payment integration updated 2026-10-06: use the [Payzah contract](<F:/MealMate/backend/docs/mobile/payzah-payments.md>). Any old-provider payment endpoints, method IDs, credential/webhook fields or refund instructions below are superseded; they are retained only as historical context for this mixed guide.

# عقود API لإدارة الاشتراك

## 1. المصادقة والـHeaders

جميع المسارات تحتاج Customer access token:

```http
Authorization: Bearer <customer-access-token>
Accept: application/json
Content-Type: application/json
Accept-Language: ar
```

لا ترسل `customerId` في query أو body؛ الباك يستخرجه من الـJWT ويتأكد أن الاشتراك مملوك للعميل الحالي.

## 2. تحميل الشاشة بالكامل

```http
GET /api/v1/customer/subscriptions/management?includeHistory=true
```

Query parameters:

| الحقل | النوع | الافتراضي | الوصف |
|---|---:|---:|---|
| `includeHistory` | boolean | `true` | يعيد الاشتراكات السابقة أيضًا. استخدم `false` إذا أردت الجارية فقط. |

استجابة `200 OK` نموذجية:

```json
{
  "currentSubscription": {
    "subscriptionId": "79f895d5-62ae-4802-bfb1-ad34c4e890ee",
    "status": "Active",
    "statusNameAr": "نشط",
    "programId": "b59a2251-ba7f-4a78-a6bb-d43defa2e4d2",
    "programNameAr": "خسارة الوزن",
    "programNameEn": "Weight Loss",
    "bundleId": "a6497534-efb2-4744-b190-9ac81a5b5102",
    "bundleNameAr": "باقة 4 وجبات",
    "bundleNameEn": "4 Meals Bundle",
    "tier": "Platinum",
    "mealsPerDay": 4,
    "imageUrl": "https://cdn.example.com/subscriptions/meal-box.jpg",
    "startDate": "2026-08-01",
    "endDate": "2026-08-26",
    "suggestedRenewalStartDate": "2026-08-27",
    "totalServiceDays": 26,
    "completedServiceDays": 18,
    "remainingServiceDays": 8,
    "progressPercentage": 69.2,
    "paidAmount": 49.99,
    "currencyCode": "KWD",
    "restaurant": {
      "restaurantId": "97db2bbb-b652-4aaa-8584-3414e6966792",
      "nameAr": "المطبخ الصحي",
      "nameEn": "Healthy Kitchen"
    },
    "pendingFreeze": null,
    "actions": {
      "canRenew": true,
      "canFreeze": true,
      "canCancel": true,
      "renewUnavailableReason": null,
      "freezeUnavailableReason": null,
      "cancelUnavailableReason": null,
      "freezeStartDelayDays": 3,
      "maximumFreezeDays": 7,
      "freezeExtendsSubscriptionEnd": true,
      "renewalEndpoint": "/api/v1/customer/subscriptions/79f895d5-62ae-4802-bfb1-ad34c4e890ee/renewal-requests",
      "freezeEndpoint": "/api/v1/customer/subscriptions/79f895d5-62ae-4802-bfb1-ad34c4e890ee/freeze-requests",
      "cancellationEndpoint": "/api/v1/customer/subscriptions/79f895d5-62ae-4802-bfb1-ad34c4e890ee/cancellation-requests"
    },
    "invoices": [
      {
        "receiptId": "73b74a8c-6a52-42df-94e0-69dc3620bcd1",
        "invoiceNumber": "100622510000012842",
        "paymentId": "100622510000012842",
        "issuedAtUtc": "2026-08-01T09:30:00Z",
        "amount": 49.99,
        "currencyCode": "KWD",
        "status": "PAID",
        "gateway": "MyFatoorah",
        "maskedCardNumber": null,
        "canDownload": false,
        "downloadUnavailableReason": "invoice_document_not_available"
      }
    ]
  },
  "subscriptions": [
    {
      "subscriptionId": "79f895d5-62ae-4802-bfb1-ad34c4e890ee",
      "status": "Active",
      "statusNameAr": "نشط",
      "programId": "b59a2251-ba7f-4a78-a6bb-d43defa2e4d2",
      "programNameAr": "خسارة الوزن",
      "programNameEn": "Weight Loss",
      "bundleId": "a6497534-efb2-4744-b190-9ac81a5b5102",
      "bundleNameAr": "باقة 4 وجبات",
      "bundleNameEn": "4 Meals Bundle",
      "tier": "Platinum",
      "mealsPerDay": 4,
      "imageUrl": "https://cdn.example.com/subscriptions/meal-box.jpg",
      "startDate": "2026-08-01",
      "endDate": "2026-08-26",
      "suggestedRenewalStartDate": "2026-08-27",
      "totalServiceDays": 26,
      "completedServiceDays": 18,
      "remainingServiceDays": 8,
      "progressPercentage": 69.2,
      "paidAmount": 49.99,
      "currencyCode": "KWD",
      "restaurant": {
        "restaurantId": "97db2bbb-b652-4aaa-8584-3414e6966792",
        "nameAr": "المطبخ الصحي",
        "nameEn": "Healthy Kitchen"
      },
      "pendingFreeze": null,
      "actions": {
        "canRenew": true,
        "canFreeze": true,
        "canCancel": true,
        "renewUnavailableReason": null,
        "freezeUnavailableReason": null,
        "cancelUnavailableReason": null,
        "freezeStartDelayDays": 3,
        "maximumFreezeDays": 7,
        "freezeExtendsSubscriptionEnd": true,
        "renewalEndpoint": "/api/v1/customer/subscriptions/79f895d5-62ae-4802-bfb1-ad34c4e890ee/renewal-requests",
        "freezeEndpoint": "/api/v1/customer/subscriptions/79f895d5-62ae-4802-bfb1-ad34c4e890ee/freeze-requests",
        "cancellationEndpoint": "/api/v1/customer/subscriptions/79f895d5-62ae-4802-bfb1-ad34c4e890ee/cancellation-requests"
      },
      "invoices": []
    }
  ],
  "invoices": [],
  "emptyStateCode": null
}
```

ملاحظات مهمة:

- `subscriptions` يحتوي نفس شكل `currentSubscription` لكل الاشتراكات الجارية والسابقة.
- `invoices` أعلى الاستجابة يخص `currentSubscription` لتسهيل رسم الشاشة.
- كل عنصر داخل `subscriptions` له نفس عقد `currentSubscription`، وقد يختلف محتوى `invoices` حسب الاشتراك.
- `currentSubscription` يختاره الباك بالأولوية: `Active`, ثم `Frozen`, ثم `Scheduled`, ثم `Paid`, ثم `SuspendedForReview`, ثم `PendingPayment`.
- `completedServiceDays` يحسب أيام `Delivered` فقط.
- `remainingServiceDays = totalServiceDays - completedServiceDays`.
- `progressPercentage` رقم من 0 إلى 100، وقد يحتوي منزلة عشرية.

الحالة الفارغة:

```json
{
  "currentSubscription": null,
  "subscriptions": [],
  "invoices": [],
  "emptyStateCode": "no_current_subscription"
}
```

قد تكون `subscriptions` غير فارغة وتحتوي اشتراكات منتهية، بينما `currentSubscription=null`. في هذه الحالة اعرض الحالة الفارغة للكارت الرئيسي، ويمكن عرض قسم تاريخ الاشتراكات أسفلها.

## 3. تفاصيل اشتراك واحد

```http
GET /api/v1/customer/subscriptions/{subscriptionId}/management
```

الاستجابة `200 OK` هي كائن الاشتراك نفسه الموجود داخل `currentSubscription`.

الاستخدامات:

- فتح شاشة التفاصيل من تاريخ الاشتراكات.
- Refresh لعنصر واحد بعد إرسال تجميد أو إلغاء.
- عدم إعادة تحميل كل تاريخ العميل إذا لم يكن ذلك مطلوبًا.

إذا كان الاشتراك غير موجود أو مملوكًا لعميل آخر:

```http
404 Not Found
```

```json
{
  "error": "subscription.not_found"
}
```

## 4. الفواتير فقط

```http
GET /api/v1/customer/subscriptions/{subscriptionId}/invoices
```

استجابة `200 OK`:

```json
[
  {
    "receiptId": "73b74a8c-6a52-42df-94e0-69dc3620bcd1",
    "invoiceNumber": "100622510000012842",
    "paymentId": "100622510000012842",
    "issuedAtUtc": "2026-08-01T09:30:00Z",
    "amount": 49.99,
    "currencyCode": "KWD",
    "status": "PAID",
    "gateway": "MyFatoorah",
    "maskedCardNumber": null,
    "canDownload": false,
    "downloadUnavailableReason": "invoice_document_not_available"
  }
]
```

حالات الفاتورة:

| القيمة | التصرف في الموبايل |
|---|---|
| `PAID` | اعرض «مدفوع» باللون الأخضر. |
| `PAID_PENDING_FULFILLMENT` | اعرض «تم الدفع وجارٍ تجهيز الاشتراك». |
| `REVIEW_REQUIRED` | اعرض «قيد المراجعة» ولا تعِد المستخدم بأن الاشتراك تفعل. |

قواعد الفواتير:

- محاولات الدفع الفاشلة وWebhooks المرفوضة لا تظهر كفواتير.
- `invoiceNumber` أو `paymentId` قد يكون `null` في بيانات تاريخية قديمة؛ اجعل الحقول Nullable.
- `maskedCardNumber=null` لأن MealMate لا يخزن البطاقة.
- اخفِ زر التحميل عندما `canDownload=false`.
- لا تنشئ PDF أو رابطًا وهميًا من جهة الموبايل.

## 5. طلب تجميد الاشتراك

```http
POST /api/v1/customer/subscriptions/{subscriptionId}/freeze-requests
```

Body:

```json
{
  "startDate": "2026-08-20",
  "endDate": "2026-08-26",
  "reason": "سفر"
}
```

الحقول:

| الحقل | مطلوب | القاعدة |
|---|---:|---|
| `startDate` | نعم | لا يقل عن تاريخ اليوم + `freezeStartDelayDays`. |
| `endDate` | نعم | لا يسبق `startDate`. |
| `reason` | لا | نص اختياري. |

عدد الأيام شامل تاريخ البداية والنهاية، ولا يتجاوز `maximumFreezeDays`.

استجابة `200 OK`:

```json
{
  "id": "865039ea-13d0-4381-ab29-1672a9ff0470",
  "subscriptionId": "79f895d5-62ae-4802-bfb1-ad34c4e890ee",
  "freezeStartDate": "2026-08-20",
  "freezeEndDate": "2026-08-26",
  "freezeDays": 7,
  "reason": "سفر",
  "status": "Submitted",
  "approvedByUserId": null,
  "processedAtUtc": null,
  "createdAtUtc": "2026-08-13T15:00:00Z"
}
```

بعد النجاح:

1. أغلق Bottom Sheet.
2. اعرض رسالة «تم إرسال طلب التجميد».
3. أعد `GET /{id}/management`.
4. سيظهر `pendingFreeze` ويصبح `actions.canFreeze=false`.

## 6. طلب تجديد الاشتراك

```http
POST /api/v1/customer/subscriptions/{subscriptionId}/renewal-requests
```

لا يوجد Body.

استجابة `202 Accepted`:

```json
{
  "requestId": "2812aa75-ff8b-48aa-9866-b7d8ef4da4f6",
  "previousSubscriptionId": "79f895d5-62ae-4802-bfb1-ad34c4e890ee",
  "status": "Requested",
  "programId": "b59a2251-ba7f-4a78-a6bb-d43defa2e4d2",
  "bundleId": "a6497534-efb2-4744-b190-9ac81a5b5102",
  "tier": "Platinum",
  "durationDays": 26,
  "includeFriday": false,
  "regionId": "6590e7e9-5951-4977-895c-fc586b6a2952",
  "suggestedStartDate": "2026-08-27",
  "configurationEndpoint": "/api/v1/customer/subscription-flow/configuration",
  "checkoutEndpoint": "/api/v1/customer/subscriptions/checkout"
}
```

الطلب Idempotent: تكراره أثناء وجود Renewal بحالة `Requested` يعيد نفس الطلب.

هذا الطلب لا يجدد ولا يسحب مبلغًا. استخدم القيم المرجعة لفتح فلو الاشتراك الحالي بهذا الترتيب:

1. Configuration والمدة والتصنيف.
2. اختيار/مراجعة المنطقة والعنوان.
3. جلب المطاعم المؤهلة الحالية لا المطاعم القديمة تلقائيًا.
4. عرض الملخص والسعر الجديد.
5. إنشاء Checkout جديد والدفع عبر MyFatoorah.

## 7. طلب إلغاء الاشتراك

```http
POST /api/v1/customer/subscriptions/{subscriptionId}/cancellation-requests
```

Body:

```json
{
  "reason": "لن أكون متواجدًا خلال الفترة القادمة",
  "requestedEffectiveDate": "2026-08-20"
}
```

كلا الحقلين Nullable من ناحية العقد، لكن يفضل أن يطلب الموبايل سببًا واضحًا قبل الإرسال.

استجابة `202 Accepted`:

```json
{
  "requestId": "33270e2f-f905-43b8-8589-53bc38b841d4",
  "subscriptionId": "79f895d5-62ae-4802-bfb1-ad34c4e890ee",
  "status": "Submitted",
  "reason": "لن أكون متواجدًا خلال الفترة القادمة",
  "requestedAtUtc": "2026-08-13T15:10:00Z"
}
```

قواعد مهمة:

- الطلب لا يغير حالة الاشتراك فورًا.
- لا توقف Calendar أو الوجبات من الموبايل.
- لا تعرض «تم الإلغاء»؛ اعرض «تم إرسال طلب الإلغاء للمراجعة».
- الطلب Idempotent ويعيد نفس `requestId` عند التكرار.
- بعد الإرسال أعد تحميل تفاصيل الاشتراك؛ يصبح `canCancel=false` و`cancelUnavailableReason=cancellation_request_pending`.

## 8. شكل الأخطاء

أخطاء قواعد التطبيق قد تأتي بهذا الشكل البسيط:

```json
{
  "error": "subscription.freeze_request_already_pending"
}
```

وأخطاء الـvalidation العامة قد تأتي كـProblem Details:

```json
{
  "type": "https://tools.ietf.org/html/rfc9110#section-15.5.1",
  "title": "بيانات غير صالحة",
  "status": 400,
  "detail": "Freeze must start on or after 2026-08-16.",
  "instance": "/api/v1/customer/subscriptions/.../freeze-requests",
  "code": "errors.validation.inclusive_between",
  "traceId": "...",
  "correlationId": "..."
}
```

اقرأ كود الخطأ بهذه الأولوية:

```text
body.error ?? body.code ?? HTTP status
```

جدول أهم الأخطاء:

| HTTP | الكود | المعنى |
|---:|---|---|
| 401 | — | التوكن منتهي؛ نفذ Refresh Token ثم أعد الطلب مرة واحدة. |
| 403 | — | التوكن ليس Customer. |
| 404 | `subscription.not_found` | غير موجود أو لا يخص المستخدم الحالي. |
| 409 | `subscription.freeze_request_already_pending` | يوجد طلب تجميد قيد المراجعة. |
| 409 | `subscription.renewal_not_available` | حالة الاشتراك لا تسمح بالتجديد. |
| 409 | `subscription.cancellation_not_available` | الحالة لا تسمح بطلب الإلغاء. |
| 400 | `errors.validation.*` | تاريخ أو مدة غير مطابقين للسياسة. |
| 500 | `errors.common.server_error` | اعرض Retry واحتفظ بـ`traceId` للتتبع. |
