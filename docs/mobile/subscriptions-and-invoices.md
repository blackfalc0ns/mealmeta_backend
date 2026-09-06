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

الدفع الحقيقي موجود في: **checkout الاشتراك** و**ترقية التصنيف** (MyFatoorah). التجميد / الإلغاء / التجديد (كطلب) بدون خصم مباشر.

---

## ترتيب التحميل للشاشة

عند فتح الشاشة نفّذ الخمسة بالتوازي:

1. `GET /api/v1/customer/subscriptions/current` → كارت الاشتراك الحالي النشط/المدفوع فقط
2. `GET /api/v1/customer/subscriptions/uncompleted` (أو `/archived`) → كروت الاشتراكات غير المكتملة / المسودات (إمكانية الحذف أو إكمال الدفع)
3. `GET /api/v1/customer/invoices` → قائمة الفواتير
4. `GET /api/v1/customer/payment-methods/default` → وسيلة الدفع
5. `GET /api/v1/customer/subscriptions/freeze-policy` → الصندوق البرتقالي

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

`status` قيمه للموبايل: `PendingPayment`, `Paid`, `Scheduled`, `Active`, `Frozen`, `Completed`, `Cancelled`, `Expired`, `PaymentFailed`.

لو الدفع اتحقق والحالة الداخلية عند الأدمن `SuspendedForReview`، الموبايل بياخد `status: "Paid"` و `statusNameAr: "تم الدفع"`. لا تعرض «معلق للمراجعة» للعميل.

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
    "statusNameAr": "تم الدفع",
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
| `PAID` | تم الدفع |
| `PAID_PENDING_FULFILLMENT` | تم الدفع (قيد التفعيل) |

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

كل زر POST. **ترقية التصنيف** بتفتح MyFatoorah لو فيه مبلغ مستحق. التجديد بيرجعك لـ checkout. التجميد والإلغاء طلبات مراجعة بدون دفع.

استخدم `subscriptionId` من `current`. لو `canX == false` لا تبعت الطلب.

### ترقية التصنيف — دفع MyFatoorah + تفعيل فوري

التفاصيل الكاملة للموبايل: [`tier-upgrade.md`](./tier-upgrade.md)

1. `GET /api/v1/customer/subscriptions/{id}/upgrade-options`

```json
{
  "canUpgrade": true,
  "unavailableReason": null,
  "currentTier": "Basic",
  "remainingServiceDays": 18,
  "totalServiceDays": 26,
  "upgradeEndpoint": "/api/v1/customer/subscriptions/{id}/upgrade-requests",
  "options": [
    {
      "targetTier": "Platinum",
      "tierNameAr": "بلس",
      "tierNameEn": "Plus",
      "logoUrl": null,
      "isCurrent": false,
      "selectable": true,
      "accessibleRestaurantCount": 28,
      "currentRemainingCredit": 55.385,
      "targetRemainingPrice": 72.0,
      "gatewayRecovery": 1.2,
      "priceDifference": 16.615,
      "currencyCode": "KWD"
    }
  ]
}
```

اختَر فقط `selectable == true`. `priceDifference` = المبلغ المستحق الآن.

`unavailableReason`: `upgrade_not_available_for_status` | `already_elite` | `upgrade_request_pending` | `upgrade_no_remaining_days` | `pricing.unavailable`

`Scheduled` = مدفوع و`startDate` لسه مستقبلية — الترقية مسموحة عليه زي `Active`.

2. بعد اختيار التصنيف وشاشة التأكيد:

`POST /api/v1/customer/subscriptions/{id}/upgrade-requests`

```json
{
  "targetTier": "Platinum",
  "callbackUrl": "https://your-app/payment/callback",
  "errorUrl": "https://your-app/payment/error",
  "paymentMethodId": 1,
  "reason": null
}
```

- `201` + `paymentUrl` → افتح MyFatoorah ثم استعلم:
  `GET /api/v1/customer/subscriptions/{id}/upgrade-requests/{upgradeId}`
- `201` + `isPaid: true` و`paymentUrl: null` → ترقية بصفر مستحق؛ التصنيف اتطبّق فوراً
- بعد الدفع الناجح التصنيف يتغير **فوراً وتلقائياً** — **مفيش أي تدخل أو موافقة من الأدمن**
- `status: "Approved"` = دفع ناجح + تفعيل، مش مراجعة أدمن
- `409` `{ "error": "subscription.upgrade_not_available" }`
- `409` `{ "error": "subscription.upgrade_request_already_pending" }`
- `404` `{ "error": "subscription.not_found" }`
- `502` فشل تهيئة البوابة

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
| `POST upgrade-requests` | **نعم** لو `priceDifference > 0` (MyFatoorah). تفعيل فوري بعد الدفع |
| `GET upgrade-requests/{upgradeId}` | حالة دفع/تفعيل الترقية بعد الرجوع من البوابة |
| `POST renewal-requests` | لا يخصم. بيرجعك لـ checkout |
| `POST /api/v1/customer/subscriptions/checkout` | **نعم. دفع اشتراك جديد** |
| `GET /api/v1/customer/subscriptions/{id}/payment` | حالة دفع الاشتراك بعد MyFatoorah |
| `GET /api/v1/customer/subscriptions/uncompleted` | قائمة الاشتراكات غير المكتملة (`Draft`, `PendingPayment`, `PaymentFailed`) |
| `DELETE /api/v1/customer/subscriptions/{id}` | حذف المسودة وفك حجز المطعم فوراً وفك الحظر عن العميل |
| `GET /api/v1/customer/subscriptions/{id}/resume` | معاينة بيانات الاستئناف وصلاحية مهلة الـ 15 دقيقة |
| `POST /api/v1/customer/subscriptions/{id}/resume` | **نعم** تجديد حجز المطعم وتوليد رابط دفع MyFatoorah |

---

## 6) الاشتراكات غير المكتملة / المؤرشفة (Uncompleted / Archived Subscriptions)

الاشتراكات التي بدأها العميل ولم يكتمل دفعها تظهر في هذا القسم (`Draft` مسودة، `PendingPayment` بانتظار الدفع، `PaymentFailed` فشل الدفع).  
يمكن للعميل **مسحها** (لإلغائها وفك حجز المقاعد واستعادة إمكانية الاشتراك مجدداً)، أو **إكمالها** (لتجديد الحجز والدفع عبر MyFatoorah).

### 1. جلب قائمة الاشتراكات غير المكتملة
- **Endpoint:** `GET /api/v1/customer/subscriptions/uncompleted` (أو `GET /api/v1/customer/subscriptions/archived`)
- **Headers:** `Authorization: Bearer <Customer_JWT>`

**Response `200 OK`:**
```json
[
  {
    "subscriptionId": "9b1deb4d-3b7d-4bad-9bdd-2b0d7b3dcb6d",
    "status": "PendingPayment",
    "statusNameAr": "بانتظار الدفع",
    "programId": "d3b07384-d113-4672-8888-000000000001",
    "programNameAr": "برنامج الكيتو",
    "programNameEn": "Keto Program",
    "bundleId": "b2b07384-d113-4672-8888-000000000002",
    "bundleNameAr": "باقة كيتو خفيفة (وجبة رئيسية + سناك)",
    "bundleNameEn": "Light Keto (Main Meal + Snack)",
    "tier": "Basic",
    "tierNameAr": "أساسي",
    "tierNameEn": "Basic",
    "tierLogoUrl": "/api/v1/uploads/content?key=tiers/basic.png",
    "mealsPerDay": 2,
    "imageUrl": "/api/v1/uploads/content?key=programs/keto.png",
    "startDate": "2026-09-10",
    "endDate": "2026-09-22",
    "durationDays": 12,
    "includeFriday": false,
    "amount": 70.50,
    "currencyCode": "KWD",
    "deliveryAddress": "حولي، قطعة 3، شارع 12",
    "createdAtUtc": "2026-09-06T19:30:00Z",
    "isHoldActive": false,
    "holdRemainingSeconds": null,
    "holdExpiresAtUtc": "2026-09-06T19:45:00Z",
    "actions": {
      "canDelete": true,
      "canResume": true,
      "deleteEndpoint": "/api/v1/customer/subscriptions/9b1deb4d-3b7d-4bad-9bdd-2b0d7b3dcb6d",
      "resumeEndpoint": "/api/v1/customer/subscriptions/9b1deb4d-3b7d-4bad-9bdd-2b0d7b3dcb6d/resume"
    }
  }
]
```

### 2. مسح الاشتراك غير المكتمل (Delete)
- **Endpoint:** `DELETE /api/v1/customer/subscriptions/{id}`
- **Headers:** `Authorization: Bearer <Customer_JWT>`
- **Response:** `204 No Content` (أو `409 Conflict` إذا كان الاشتراك مدفوعاً أو نشطاً بالفعل).
- **النتيجة:** يتم فك حجز المقاعد في المطبخ فوراً (`Released`)، ويتحول الاشتراك إلى `Cancelled`، ويختفي من القائمة ويزول أي حظر `checkout.existing_subscription` عن العميل.

### 3. فحص ومعاينة الاستئناف (Resume Preview)
- **Endpoint:** `GET /api/v1/customer/subscriptions/{id}/resume`
- **Headers:** `Authorization: Bearer <Customer_JWT>`

**Response `200 OK` (المهلة سارية):**
```json
{
  "subscriptionId": "9b1deb4d-3b7d-4bad-9bdd-2b0d7b3dcb6d",
  "status": "PendingPayment",
  "statusNameAr": "بانتظار الدفع",
  "programId": "d3b07384-d113-4672-8888-000000000001",
  "programNameAr": "برنامج الكيتو",
  "bundleNameAr": "باقة كيتو خفيفة (وجبة رئيسية + سناك)",
  "tier": "Basic",
  "tierNameAr": "أساسي",
  "durationDays": 12,
  "amount": 70.50,
  "currencyCode": "KWD",
  "canPayDirectly": true,
  "reason": null,
  "holdRemainingSeconds": 540,
  "holdExpiresAtUtc": "2026-09-06T20:00:00Z",
  "suggestedStartDate": "2026-09-10",
  "resumeEndpoint": "/api/v1/customer/subscriptions/9b1deb4d-3b7d-4bad-9bdd-2b0d7b3dcb6d/resume"
}
```

**Response `200 OK` (المهلة انتهت أو التاريخ فات):**
```json
{
  "subscriptionId": "9b1deb4d-3b7d-4bad-9bdd-2b0d7b3dcb6d",
  "status": "PendingPayment",
  "statusNameAr": "بانتظار الدفع",
  "canPayDirectly": false,
  "reason": "capacity_hold_expired",
  "holdRemainingSeconds": null,
  "holdExpiresAtUtc": null,
  "suggestedStartDate": "2026-09-10",
  "resumeEndpoint": "/api/v1/customer/subscriptions/9b1deb4d-3b7d-4bad-9bdd-2b0d7b3dcb6d/resume"
}
```

### 4. تنفيذ إكمال الاشتراك والدفع (Resume Execution)
- **Endpoint:** `POST /api/v1/customer/subscriptions/{id}/resume`
- **Headers:** `Authorization: Bearer <Customer_JWT>`
- **Body (اختياري، لتحديث موعد البدء وبوابة الدفع):**
```json
{
  "startDate": "2026-09-10",
  "callbackUrl": "mealmate://checkout/callback",
  "errorUrl": "mealmate://checkout/error",
  "paymentMethodId": 1
}
```
- **Response `200 OK`:**
```json
{
  "subscriptionId": "9b1deb4d-3b7d-4bad-9bdd-2b0d7b3dcb6d",
  "paymentUrl": "https://portal.myfatoorah.com/KWT/PayInvoice/Checkout?invoiceKey=...",
  "holdExpiresAtUtc": "2026-09-06T20:15:00Z",
  "finalPrice": 70.50,
  "paymentStatus": "PendingPayment"
}
```
يفتح تطبيق الموبايل الـ `paymentUrl` فوراً للمستخدم لسداد الاشتراك عبر MyFatoorah.

---

## لو جالك 404 من Dio

الـ 404 ده **مش** معناه مفيش اشتراك. لو مفيش اشتراك السيرفر بيرجع `200` و `subscription: null`.

| Response | المعنى |
|---|---|
| `404` + `"title": "Not Found"` + `rfc9110#section-15.5.5` | الـ route مش متعرّف على البيلد اللي اتنفّذ عليه الطلب (أو URL غلط) |
| `401` + `"title": "Unauthorized"` | الـ route موجود، بس التوكن ناقص/غلط |
| `403` | التوكن مش عميل |
| `200` + `subscription: null` | العميل صح، ومفيش اشتراك حالي |

الـ URLs الصحيحة على نفس الهوست:

```
GET http://maelmate.runasp.net/api/v1/customer/subscriptions/current
GET http://maelmate.runasp.net/api/v1/customer/invoices
GET http://maelmate.runasp.net/api/v1/customer/payment-methods/default
GET http://maelmate.runasp.net/api/v1/customer/subscriptions/freeze-policy
```

Header لازم:

```
Authorization: Bearer <customer_access_token>
```

اتأكد إن Dio `baseUrl` مش بيتكرر. غلط شائع:

- `baseUrl = http://maelmate.runasp.net/api/v1` + path `/api/v1/customer/...` → `/api/v1/api/v1/...` → 404
- الصح: `baseUrl = http://maelmate.runasp.net` + path `/api/v1/customer/subscriptions/current`

بعد أي نشر جديد: لو لسه 404، أعد تشغيل الـ API على RunASP لأن السيرفر ممكن يفضل على البيلد القديم لحد الـ recycle.

---

## Checklist للموبايل

- [ ] الشاشة تتحمّل بـ 5 GETs بالتوازي (`current`, `uncompleted`, `invoices`, `payment-methods/default`, `freeze-policy`)
- [ ] كارت الاشتراك الحالي يقتصر على الاشتراكات المدفوعة/النشطة (`Paid`, `Active`, `Scheduled`, `Frozen`)
- [ ] قسم الاشتراكات غير المكتملة يعرض المسودات مع زري: مسح (`DELETE`) وإكمال (`POST resume`)
- [ ] الأزرار تتبع `actions.can*`
- [ ] الترقية: options ثم POST، من غير بوابة دفع
- [ ] التجميد: احترم delay وأقصى أيام من `actions` أو freeze-policy
- [ ] التجديد: renewal-requests ثم checkout ثم payment status
- [ ] الإلغاء: POST فقط، الاشتراك يفضل ظاهر لحد ما الأدمن يتصرف
- [ ] الفواتير: `statusNameAr` + `KWD`، ومن غير download
- [ ] وسيلة الدفع: لو `hasSavedMethod == false` لا ترسم كارت وهمي
- [ ] صور اللوجو: `logoUrl` / `tierLogoUrl` / `imageUrl` ممكن تكون relative تحت `/api/v1/uploads/content?key=`
