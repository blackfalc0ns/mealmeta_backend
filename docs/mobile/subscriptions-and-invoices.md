# ربط شاشة الاشتراكات والفواتير (موبايل)

عقد الـ API الحالي لشاشة **الاشتراكات والفواتير**. اربط كل جزء من الشاشة على endpoint منفصل. لا تعتمد على endpoint واحد مجمع.

- Auth: `Authorization: Bearer <customer_jwt>`
- Role: عميل فقط. غير كده = `403`
- بدون توكن = `401`
- JSON: `camelCase`
- Enums: string (`"Platinum"`, `"Elite"`, `"Submitted"`)
- التواريخ: `yyyy-MM-dd`
- الوقت: ISO-8601 UTC
- العملة الحقيقية: `KWD` (مش `$`)

الدفع الحقيقي **مش** موجود في أزرار الترقية / التجميد / الإلغاء. الدفع فقط من checkout.

---

## ترتيب التحميل للشاشة

عند فتح الشاشة نفّذ الأربعة مع بعض:

1. `GET /api/v1/customer/subscriptions/current` → كارت الاشتراك + الأزرار
2. `GET /api/v1/customer/invoices` → قائمة الفواتير
3. `GET /api/v1/customer/payment-methods/default` → وسيلة الدفع
4. `GET /api/v1/customer/subscriptions/freeze-policy` → الصندوق البرتقالي

لو `current.subscription` مش `null`، الأزرار تستخدم `subscription.actions` و`subscription.subscriptionId`.

لا تستخدم `GET /api/v1/customer/subscriptions/management` في الشاشة دي. موجود للتوافق القديم فقط.

---

## 1) كارت الاشتراك الحالي

`GET /api/v1/customer/subscriptions/current`

### لو مفيش اشتراك حالي

```json
{
  "subscription": null,
  "emptyStateCode": "no_current_subscription"
}
```

اعرض empty state. عطّل أزرار الترقية / التجميد / التجديد / الإلغاء.

### لو فيه اشتراك

```json
{
  "subscription": {
    "subscriptionId": "3fa85f64-5717-4562-b3fc-2c963f66afa6",
    "status": "Active",
    "statusNameAr": "نشط",
    "programId": "11111111-1111-1111-1111-111111111111",
    "programNameAr": "خسارة الوزن",
    "programNameEn": "Weight loss",
    "bundleId": "22222222-2222-2222-2222-222222222222",
    "bundleNameAr": "باقة 4 وجبات",
    "bundleNameEn": "4 meals",
    "tier": "Platinum",
    "tierNameAr": "بلاتينيوم",
    "tierNameEn": "Platinum",
    "tierLogoUrl": "/api/v1/uploads/content?key=...",
    "mealsPerDay": 4,
    "imageUrl": "https://...",
    "startDate": "2026-08-10",
    "endDate": "2026-08-15",
    "nextRenewalDate": "2026-08-16",
    "totalServiceDays": 26,
    "completedServiceDays": 22,
    "remainingServiceDays": 4,
    "progressPercentage": 84.6,
    "paidAmount": 49.99,
    "currencyCode": "KWD",
    "restaurant": {
      "restaurantId": "33333333-3333-3333-3333-333333333333",
      "nameAr": "سيلكت",
      "nameEn": "Select",
      "logoUrl": "/api/v1/uploads/content?key=..."
    },
    "pendingFreeze": null,
    "actions": {
      "canRenew": true,
      "canFreeze": true,
      "canCancel": true,
      "canUpgrade": true,
      "renewUnavailableReason": null,
      "freezeUnavailableReason": null,
      "cancelUnavailableReason": null,
      "upgradeUnavailableReason": null,
      "freezeStartDelayDays": 3,
      "maximumFreezeDays": 7,
      "freezeExtendsSubscriptionEnd": true,
      "renewalEndpoint": "/api/v1/customer/subscriptions/{id}/renewal-requests",
      "freezeEndpoint": "/api/v1/customer/subscriptions/{id}/freeze-requests",
      "cancellationEndpoint": "/api/v1/customer/subscriptions/{id}/cancellation-requests",
      "upgradeEndpoint": "/api/v1/customer/subscriptions/{id}/upgrade-requests"
    }
  },
  "emptyStateCode": null
}
```

### ربط الكارت

| UI | Field |
|---|---|
| اسم الباقة | `bundleNameAr` / `bundleNameEn` |
| الحالة الخضراء | `statusNameAr` — الكود من `status` |
| المطعم + اللوجو | `restaurant.nameAr`, `restaurant.logoUrl` |
| التجديد القادم | `nextRenewalDate` |
| متبقي ن يوم | `remainingServiceDays` |
| شريط الاستخدام | `completedServiceDays` من `totalServiceDays` و `progressPercentage` |
| المبلغ المدفوع سابقاً | `paidAmount` + `currencyCode` |

`status` قيمه string من السيرفر: `PendingPayment`, `Paid`, `Scheduled`, `Active`, `Frozen`, `Completed`, `Cancelled`, `Expired`, `PaymentFailed`, `SuspendedForReview`.

`tier` قيمه: `"Basic"` | `"Platinum"` | `"Elite"`.

لو الزر `canX = false` عطّله واعرض `xUnavailableReason` لو حابب.

---

## 2) وسيلة الدفع الافتراضية

`GET /api/v1/customer/payment-methods/default`

```json
{
  "hasSavedMethod": false,
  "brand": null,
  "last4": null,
  "expiry": null,
  "gateway": "MyFatoorah",
  "emptyStateCode": "saved_card_not_available"
}
```

**مهم:** النظام لا يحفظ رقم كارت ولا تاريخ انتهاء. لا تخترع `4582` أو `06/27`.

- لو `hasSavedMethod == false`: اخفي الكارت أو اعرض "لا توجد بطاقة محفوظة".
- `gateway` ممكن يكون اسم آخر دفعة ناجحة، وممكن `null` لو مفيش إيصالات.

ده **مش** endpoint طرق دفع checkout. طرق الدفع وقت الشراء:

`GET /api/v1/customer/subscription-flow/payment-methods?amount={finalPrice}`

---

## 3) الفواتير

`GET /api/v1/customer/invoices`

يرجع array، الأحدث أولاً:

```json
[
  {
    "receiptId": "44444444-4444-4444-4444-444444444444",
    "subscriptionId": "3fa85f64-5717-4562-b3fc-2c963f66afa6",
    "invoiceNumber": "2025-000123",
    "paymentId": "pay-1",
    "issuedAtUtc": "2026-08-09T10:00:00Z",
    "amount": 49.99,
    "currencyCode": "KWD",
    "status": "PAID",
    "statusNameAr": "مدفوع",
    "gateway": "MyFatoorah",
    "maskedCardNumber": null,
    "canDownload": false,
    "downloadUnavailableReason": "invoice_document_not_available"
  }
]
```

| UI | Field |
|---|---|
| التاريخ | `issuedAtUtc` |
| رقم الفاتورة | `invoiceNumber` — ممكن `null`، ساعتها اعرض `paymentId` أو `receiptId` |
| الحالة | `statusNameAr` |
| المبلغ | `amount` + `currencyCode` |

`status`:

| status | statusNameAr |
|---|---|
| `PAID` | مدفوع |
| `PAID_PENDING_FULFILLMENT` | مدفوع (قيد التفعيل) |
| `REVIEW_REQUIRED` | قيد المراجعة |

تفاصيل صف واحد:

`GET /api/v1/customer/invoices/{receiptId}`

- `404` `{ "error": "invoice.not_found" }` لو الفاتورة مش بتاعته
- `canDownload` حالياً دائماً `false`. لا تفتح PDF.

فواتير اشتراك واحد فقط (اختياري، مش مطلوب للشاشة):

`GET /api/v1/customer/subscriptions/{id}/invoices`

---

## 4) صندوق سياسة التجميد

`GET /api/v1/customer/subscriptions/freeze-policy`

```json
{
  "defaultDurationDays": 7,
  "startDelayDays": 3,
  "extendSubscriptionEnd": true,
  "messageAr": "يمكن تجميد الاشتراك لمدة حتى 7 أيام، ويبدأ التجميد بعد 3 أيام على الأقل.",
  "messageEn": "You can freeze your subscription for up to 7 days. Freezing can start after at least 3 days."
}
```

اعرض `messageAr` / `messageEn` كما هي. الأرقام تجي من إعداد الأدمن، مش ثابتة 30 يوم / 6 شهور.

نفس الأرقام موجودة في `subscription.actions.maximumFreezeDays` و `freezeStartDelayDays`.

---

## 5) الأزرار

كل زر POST. لا تفتح MyFatoorah إلا في **تجديد** بعد ما ترجّع على checkout.

استخدم `subscriptionId` من `current`. لو `canX == false` لا تبعت الطلب.

### ترقية التصنيف — لا يخصم فلوس

1. `GET /api/v1/customer/subscriptions/{id}/upgrade-options`

```json
{
  "subscriptionId": "3fa85f64-5717-4562-b3fc-2c963f66afa6",
  "currentTier": "Platinum",
  "canUpgrade": true,
  "unavailableReason": null,
  "upgradeEndpoint": "/api/v1/customer/subscriptions/{id}/upgrade-requests",
  "options": [
    {
      "tier": "Elite",
      "nameAr": "إيليت",
      "nameEn": "Elite",
      "priceDifference": 14.997,
      "currencyCode": "KWD"
    }
  ]
}
```

`unavailableReason`: `upgrade_not_available_for_status` | `already_elite` | `upgrade_request_pending`

2. بعد اختيار التصنيف:

`POST /api/v1/customer/subscriptions/{id}/upgrade-requests`

```json
{ "targetTier": "Elite", "reason": null }
```

- `200` الطلب اتسجل للمراجعة. التصنيف **لا يتغير** غير بعد موافقة الأدمن.
- `409` `{ "error": "subscription.upgrade_not_available" }`
- `409` `{ "error": "subscription.upgrade_request_already_pending" }`
- `404` `{ "error": "subscription.not_found" }`

لا تفتح بوابة دفع هنا.

### تجميد — لا يخصم فلوس

`POST /api/v1/customer/subscriptions/{id}/freeze-requests`

```json
{
  "startDate": "2026-08-20",
  "endDate": "2026-08-26",
  "reason": null
}
```

قواعد لازم تطبقها في الفورم قبل الإرسال:

- `startDate >= today + freezeStartDelayDays`
- عدد الأيام شامل الطرفين `<= maximumFreezeDays`
- `endDate >= startDate`

نجاح = `200` وطلب بحالة `Submitted`. الاشتراك يتجمد بعد موافقة الأدمن.

- `409` `{ "error": "subscription.freeze_request_already_pending" }`
- لو التاريخ خارج السياسة: `400` ProblemDetails

### تجديد — ده اللي بيرجع للدفع

`POST /api/v1/customer/subscriptions/{id}/renewal-requests`

Body فاضي.

`202 Accepted`:

```json
{
  "requestId": "55555555-5555-5555-5555-555555555555",
  "previousSubscriptionId": "3fa85f64-5717-4562-b3fc-2c963f66afa6",
  "status": "Requested",
  "programId": "11111111-1111-1111-1111-111111111111",
  "bundleId": "22222222-2222-2222-2222-222222222222",
  "tier": "Platinum",
  "durationDays": 26,
  "includeFriday": false,
  "regionId": "66666666-6666-6666-6666-666666666666",
  "suggestedStartDate": "2026-08-16",
  "configurationEndpoint": "/api/v1/customer/subscription-flow/configuration",
  "checkoutEndpoint": "/api/v1/customer/subscriptions/checkout"
}
```

بعد كده افتح فلو الاشتراك بالقيم دي، وبعد التأكيد ادفع من:

`POST /api/v1/customer/subscriptions/checkout`

```json
{
  "programId": "11111111-1111-1111-1111-111111111111",
  "bundleId": "22222222-2222-2222-2222-222222222222",
  "tier": "Platinum",
  "durationDays": 26,
  "includeFriday": false,
  "callbackUrl": "https://your-app/payment/callback",
  "errorUrl": "https://your-app/payment/error",
  "paymentMethodId": 1
}
```

السيرفر يرجع `paymentUrl`. افتحه. بعد الرجوع اسأل:

`GET /api/v1/customer/subscriptions/{newSubscriptionId}/payment`

```json
{
  "subscriptionId": "...",
  "paymentStatus": "Paid",
  "isPaid": true,
  "isFailed": false,
  "amount": 49.99,
  "currencyCode": "KWD"
}
```

`callbackUrl` / `errorUrl` لازم scheme/host مسموح في إعدادات الـ API.

`409` `{ "error": "subscription.renewal_not_available" }` لو الاشتراك مسودة / فشل دفع / ملغي.

### إلغاء — لا يعمل refund تلقائي

`POST /api/v1/customer/subscriptions/{id}/cancellation-requests`

```json
{
  "reason": "travel",
  "requestedEffectiveDate": null
}
```

`202 Accepted`. الحالة تفضل زي ما هي. مفيش حساب استرجاع ومفيش إيقاف خدمة من الطلب ده.

الطلب idempotent: تكرار نفس الإلغاء يرجع نفس `requestId`.

- `409` `{ "error": "subscription.cancellation_not_available" }`

---

## 6) زر إدارة الاشتراك

للتفاصيل الكاملة لاشتراك واحد:

`GET /api/v1/customer/subscriptions/{id}/management`

`404` `{ "error": "subscription.not_found" }` لو مش بتاع العميل.

قائمة اشتراكات العميل (اختياري):

`GET /api/v1/customer/subscriptions?includeHistory=true`

---

## أخطاء عامة

| الحالة | المعنى |
|---|---|
| `401` | مفيش JWT أو منتهي |
| `403` | مش عميل |
| `404` `{ "error": "subscription.not_found" }` أو `"invoice.not_found"` | مش موجود أو مش ملكه |
| `409` `{ "error": "..." }` | العملية مش متاحة أو في طلب معلّق |
| `400` ProblemDetails | validation (تجميد/ترقية عبر MediatR) |

لا تعتمد على رسالة الـ validation كنص ثابت للترجمة. استخدم `error` code أو ProblemDetails `code` لو موجود.

---

## اللي مربوط بدفع واللي لأ

| Endpoint | دفع؟ |
|---|---|
| `GET current` / `GET invoices` | قراءة فقط. الفواتير من إيصالات MyFatoorah المؤكدة |
| `GET payment-methods/default` | لا. مفيش كارت محفوظ |
| `GET freeze-policy` | لا |
| `POST freeze-requests` | لا. طلب مراجعة |
| `POST cancellation-requests` | لا. طلب مراجعة، بدون refund |
| `POST upgrade-requests` | لا. طلب مراجعة، بدون خصم |
| `POST renewal-requests` | لا يخصم. بيرجعك لـ checkout |
| `POST /api/v1/customer/subscriptions/checkout` | **نعم. ده الدفع** |
| `GET /api/v1/customer/subscriptions/{id}/payment` | حالة الدفع بعد MyFatoorah |

---

## Checklist للموبايل

- [ ] الشاشة تتحمّل بـ 4 GETs منفصلة مش `management`
- [ ] لو `subscription == null` اعرض empty state
- [ ] الأزرار تتبع `actions.can*`
- [ ] الترقية: options ثم POST، من غير بوابة دفع
- [ ] التجميد: احترم delay وأقصى أيام من `actions` أو freeze-policy
- [ ] التجديد: renewal-requests ثم checkout ثم payment status
- [ ] الإلغاء: POST فقط، الاشتراك يفضل ظاهر لحد ما الأدمن يتصرف
- [ ] الفواتير: `statusNameAr` + `KWD`، ومن غير download
- [ ] وسيلة الدفع: لو `hasSavedMethod == false` لا ترسم كارت وهمي
- [ ] صور اللوجو: `logoUrl` / `tierLogoUrl` / `imageUrl` ممكن تكون relative تحت `/api/v1/uploads/content?key=`
