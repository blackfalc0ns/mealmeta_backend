# ترقية تصنيف المطاعم (موبايل)

عقد الـ API لشاشات **ترقية التصنيف** (Select → Plus → Max / Basic → Platinum → Elite).

- Auth: `Authorization: Bearer <customer_jwt>`
- Role: عميل فقط → غير كده `403`
- JSON: `camelCase`
- Enums: string (`"Basic"`, `"Platinum"`, `"Elite"`, `"Submitted"`, `"Approved"`, `"Rejected"`)
- العملة: `KWD`
- الدفع: **MyFatoorah فقط** (نفس مسار checkout الاشتراك). مفيش محفظة ومفيش كارت محفوظ.

---

## سلوك المنتج (مهم)

1. الترقية على اشتراك **Active** أو **Scheduled** (مدفوع وجاهز قبل/أثناء الخدمة).
2. السعر = فرق الأيام المتبقية (prorata) عبر `PricingEngine` — **مش** مضاعفات ثابتة.
3. بعد دفع ناجح → التصنيف يتغير **فوراً وتلقائياً**.
4. **مفيش أي تدخل من الأدمن** في مسار ترقية العميل: لا موافقة، لا مراجعة، لا انتظار.
5. أثناء انتظار الدفع الاشتراك يفضل حالته الحالية (`Active` / `Scheduled`). فشل دفع الترقية **ما يحوّلش** الاشتراك إلى `PaymentFailed`.
6. `PaidAmount` الأصلي للاشتراك **ما بيتغيّرش** رجعياً.
7. أسماء الشاشة (Select / Plus / Max) = labels إعدادات التسعير لـ Basic / Platinum / Elite.

> ملاحظة enums: `status: "Approved"` معناها **الدفع اتحقق والترقية اتطبّقت** — مش موافقة أدمن.

> ملاحظة حالة الاشتراك: `Scheduled` = الاشتراك مدفوع ومتفعّل تقنياً، لكن `startDate` لسه في المستقبل (بتوقيت الكويت). ده طبيعي ومش خطأ. الترقية مسموحة عليه.

---

## فلو الشاشات (Figma)

```
Subscriptions screen
  → Upgrade tier (لو actions.canUpgrade)
    → GET upgrade-options
    → اختيار طبقة أعلى (selectable=true)
      → شاشة التأكيد (breakdown من نفس الـ option)
        → POST upgrade-requests
          → لو paymentUrl موجود: افتح MyFatoorah
          → بعد الرجوع: GET upgrade-requests/{upgradeId}
          → لو isPaid: حدّث UI / أعد تحميل current
```

استخدم `subscriptionId` من:

`GET /api/v1/customer/subscriptions/current` → `subscription.subscriptionId`

وتحقق من:

`subscription.actions.canUpgrade == true`

قبل ما تفتح فلو الترقية. لو `false` اعرض `actions.upgradeUnavailableReason`.

---

## 1) خيارات الترقية

`GET /api/v1/customer/subscriptions/{id}/upgrade-options`

### Response `200`

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
      "targetTier": "Basic",
      "tierNameAr": "سيلكت",
      "tierNameEn": "Select",
      "logoUrl": "/api/v1/uploads/content?key=...",
      "isCurrent": true,
      "selectable": false,
      "accessibleRestaurantCount": 12,
      "currentRemainingCredit": 0,
      "targetRemainingPrice": 0,
      "gatewayRecovery": 0,
      "priceDifference": 0,
      "currencyCode": "KWD"
    },
    {
      "targetTier": "Platinum",
      "tierNameAr": "بلس",
      "tierNameEn": "Plus",
      "logoUrl": "/api/v1/uploads/content?key=...",
      "isCurrent": false,
      "selectable": true,
      "accessibleRestaurantCount": 28,
      "currentRemainingCredit": 55.385,
      "targetRemainingPrice": 72.0,
      "gatewayRecovery": 1.2,
      "priceDifference": 16.615,
      "currencyCode": "KWD"
    },
    {
      "targetTier": "Elite",
      "tierNameAr": "ماكس",
      "tierNameEn": "Max",
      "logoUrl": null,
      "isCurrent": false,
      "selectable": true,
      "accessibleRestaurantCount": 41,
      "currentRemainingCredit": 55.385,
      "targetRemainingPrice": 88.5,
      "gatewayRecovery": 1.5,
      "priceDifference": 33.115,
      "currencyCode": "KWD"
    }
  ]
}
```

### قواعد العرض

| حقل | استخدامه في UI |
|---|---|
| `isCurrent` / `selectable` | الطبقة الحالية للعرض فقط. اختَر فقط `selectable == true` |
| `accessibleRestaurantCount` | مقارنة عدد المطاعم (~25 vs ~50) بين الحالي والمختار |
| `currentRemainingCredit` | «رصيد الباقة الحالية للأيام المتبقية» |
| `targetRemainingPrice` | «سعر الباقة الجديدة للأيام المتبقية» |
| `gatewayRecovery` | سطر رسوم/استرداد بوابة (معلوماتي؛ مش يتضاف فوق الإجمالي) |
| `priceDifference` | **المبلغ المستحق الآن** = الإجمالي المطلوب دفعه |

`priceDifference` هو `totalDueNow`. متضيفش `gatewayRecovery` عليه تاني.

### `unavailableReason`

| Code | معنى |
|---|---|
| `upgrade_not_available_for_status` | الاشتراك مش Active ولا Scheduled |
| `already_elite` | بالفعل أعلى طبقة |
| `upgrade_request_pending` | في طلب ترقية `Submitted` معلّق |
| `upgrade_no_remaining_days` | مفيش أيام خدمة متبقية |
| `pricing.unavailable` | التسعير مش جاهز (بيانات مطاعم/قواعد) |

### أخطاء

- `404` `{ "error": "subscription.not_found" }`
- `403` مش عميل

---

## 2) بدء الترقية + الدفع

`POST /api/v1/customer/subscriptions/{id}/upgrade-requests`

### Body

```json
{
  "targetTier": "Platinum",
  "callbackUrl": "https://your-app/payment/callback",
  "errorUrl": "https://your-app/payment/error",
  "paymentMethodId": 1,
  "reason": null
}
```

| حقل | مطلوب؟ | ملاحظات |
|---|---|---|
| `targetTier` | نعم | أعلى من الطبقة الحالية (`Platinum` أو `Elite` حسب الحالة) |
| `callbackUrl` | نعم لو فيه مبلغ مستحق | نفس قواعد checkout (scheme/host مسموح) |
| `errorUrl` | نعم لو فيه مبلغ مستحق | نفس القواعد |
| `paymentMethodId` | اختياري | افتراضي `1` |
| `reason` | اختياري | |

### Success `201` — فيه دفع

```json
{
  "upgradeId": "aaaaaaaa-aaaa-aaaa-aaaa-aaaaaaaaaaaa",
  "subscriptionId": "3fa85f64-5717-4562-b3fc-2c963f66afa6",
  "fromTier": "Basic",
  "toTier": "Platinum",
  "amount": 16.615,
  "currencyCode": "KWD",
  "paymentUrl": "https://myfatoorah.../Invoice?paymentId=...",
  "isPaid": false,
  "status": "Submitted",
  "remainingServiceDays": 18
}
```

1. احفظ `upgradeId`.
2. افتح `paymentUrl` (WebView / browser — نفس checkout).
3. بعد الرجوع من البوابة استعلم عن الحالة (القسم 3).

### Success `201` — مستحق صفر (نادر)

```json
{
  "upgradeId": "...",
  "subscriptionId": "...",
  "fromTier": "Basic",
  "toTier": "Platinum",
  "amount": 0,
  "currencyCode": "KWD",
  "paymentUrl": null,
  "isPaid": true,
  "status": "Approved",
  "remainingServiceDays": 18
}
```

التصنيف اتطبّق فوراً. متفتحش بوابة. أعد تحميل `current`.

### أخطاء

| Status | Body | معنى |
|---|---|---|
| `400` | `{ "error": "subscription.upgrade_target_invalid" }` | طبقة مش أعلى |
| `400` | `{ "error": "upgrade.redirect_urls_required" }` | ناقص callback/error والمبلغ > 0 |
| `400` | `{ "error": "upgrade.customer_not_found" }` | بيانات العميل ناقصة للدفع |
| `409` | `{ "error": "subscription.upgrade_not_available" }` | مش Active أو التسعير فاشل للهدف |
| `409` | `{ "error": "subscription.upgrade_request_already_pending" }` | في طلب معلّق |
| `404` | `{ "error": "subscription.not_found" }` | |
| `502` | ProblemDetails | فشل تهيئة MyFatoorah (الطلب يتسجل `Rejected`) |

---

## 3) حالة طلب الترقية بعد الدفع

`GET /api/v1/customer/subscriptions/{id}/upgrade-requests/{upgradeId}`

استدعيه بعد الرجوع من MyFatoorah (وممكن polling قصير لو الـ webhook لسه مش واصل).

### Response `200`

```json
{
  "upgradeId": "aaaaaaaa-aaaa-aaaa-aaaa-aaaaaaaaaaaa",
  "subscriptionId": "3fa85f64-5717-4562-b3fc-2c963f66afa6",
  "fromTier": "Basic",
  "toTier": "Platinum",
  "amount": 16.615,
  "currencyCode": "KWD",
  "status": "Approved",
  "isPaid": true,
  "isFailed": false,
  "effectiveAtUtc": "2026-08-27T16:00:00Z",
  "paymentUrl": null
}
```

| حقل | معنى |
|---|---|
| `isPaid` | `status == Approved` → الدفع اتحقق والترقية مطبّقة تلقائياً (مش موافقة أدمن) |
| `isFailed` | `Rejected` أو `Expired` |
| `status == Submitted` | لسه بانتظار الدفع/التحقق من البوابة |

بعد `isPaid == true`:

1. أعد `GET /api/v1/customer/subscriptions/current`
2. تأكد إن `tier` بقى الطبقة الجديدة
3. اعرض نجاح وارجع لشاشة الاشتراكات

بعد `isFailed == true`: الاشتراك لسه Active على الطبقة القديمة. اسمح بإعادة المحاولة (POST جديد بعد ما الطلب المعلّق يخلص / يترفض).

`404` `{ "error": "upgrade.not_found" }` أو `subscription.not_found`.

---

## 4) Webhook / مرجع الدفع (للمعرفة فقط)

- `CustomerReference` في MyFatoorah = **Guid الاشتراك** (مش `upgrade:{id}`).
- السيرفر يطابق `PaymentId` مع صف الترقية المعلّق.
- تطبيق الموبايل **ما يستقبلش webhook**؛ يعتمد على شاشة الرجوع + `GET upgrade-requests/{upgradeId}`.

---

## 5) Checklist Flutter

- [ ] زر الترقية يظهر فقط لو `actions.canUpgrade`
- [ ] شاشة الخيارات من `GET upgrade-options` وتعرض كل الـ options بما فيها الحالية
- [ ] الاختيار فقط لـ `selectable == true`
- [ ] شاشة التأكيد تستخدم `currentRemainingCredit` / `targetRemainingPrice` / `gatewayRecovery` / `priceDifference`
- [ ] `POST` يبعت `callbackUrl` + `errorUrl` (+ `paymentMethodId` لو موجود)
- [ ] فتح `paymentUrl` بنفس آلية checkout
- [ ] بعد الرجوع: poll `GET .../upgrade-requests/{upgradeId}` لحد `isPaid` أو `isFailed`
- [ ] بعد `isPaid`: أعد تحميل `current` — التصنيف اتغيّر بدون أي خطوة أدمن
- [ ] لو `isPaid` من أول `POST` (`paymentUrl == null`) متفتحش بوابة
- [ ] فشل الدفع: متغيّرش حالة الاشتراك في الـ UI لـ PaymentFailed
- [ ] العملة `KWD` ونص عربي من `tierNameAr` / رسائل الأخطاء بالـ codes
- [ ] متستناش موافقة أدمن وماتعرضش «قيد المراجعة» لترقية التصنيف

---

## 6) اللي اتغيّر عن العقد القديم

| قبل | بعد |
|---|---|
| `POST upgrade-requests` طلب مراجعة بدون دفع | دفع MyFatoorah + تفعيل فوري تلقائي |
| سعر ≈ مضاعف 1.15 / 1.30 | Prorata من `PricingEngine` |
| موافقة / تدخل أدمن | **ملغي تماماً** لمسار الموبايل — الدفع وحده يكفي |
| Options بسيطة (`priceDifference` فقط) | Breakdown + عدد مطاعم + `selectable` |

مرجع الشاشة الأم: `docs/mobile/subscriptions-and-invoices.md` (قسم الأزرار يشير لهذا الملف).
