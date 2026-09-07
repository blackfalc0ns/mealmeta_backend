# شاشة 7 — الدفع (07.06)

**السابق:** [العنوان](./06-address.md) · **بعد النجاح:** تقاويم الأفراد اللي اتدفع لهم

## الهدف

عرض سلة الأفراد **المختارين للدفع**، الخصم العائلي، وسيلة الدفع، ثم دفعة واحدة. الأفراد اللي من غير خطة **مش في الإجمالي ومش مشتركين**.

## عناصر الشاشة

- كارت لكل فرد في السلة: صورة/اسم، برنامج، باقة، مدة، سعر السطر بعد خصم العيلة
- جدول مقارنة السلوتات (`mealOverview`)
- الإجمالي: `subtotal` − `familyDiscountAmount` = `finalPrice`
- وسائل: كي نت / فيزا / أبل باي حسب تفعيل البوابة (محفظة MealMate ظاهرة ومعطّلة غالباً)
- أزرار: `تأكيد الدفع` · `السابق: الموقع`
- لو `managerPrepaid: true`: المدير مش هيتدفع له تاني — السعر 0 / مش في الفاتورة الجديدة

بعد عيلة Active: نفس الشاشة لدفع **الإضافة فقط** (`newMembersSubtotal`).

## APIs

### 1) السمري (مصدر الأرقام)

```http
GET /api/v1/customer/family-subscription-flow/summary
GET /api/v1/customer/family-subscription-flow/summary?memberId={guid}
```

`memberId` يغيّر الكارت المظلل فقط. `finalPrice` واحد للسلة.

```json
{
  "familyGroupId": "…",
  "familyName": "عائلة أحمد",
  "selectedMemberId": "…",
  "selectedMember": {
    "memberId": "…",
    "displayName": "أحمد",
    "isManager": true,
    "isReady": true,
    "programNameAr": "خسارة الوزن",
    "bundleNameAr": "باقة",
    "selectedTier": 1,
    "tierNameEn": "Basic",
    "durationDays": 6,
    "durationLabelAr": "6 أيام",
    "includeFriday": false,
    "memberPrice": 45.000
  },
  "members": [],
  "subtotal": 90.000,
  "familyDiscountPercent": 0.10,
  "familyDiscountAmount": 9.000,
  "finalPrice": 81.000,
  "currencyCode": "KWD",
  "mealOverview": [
    {
      "memberId": "…",
      "displayName": "أحمد",
      "isReady": true,
      "breakfast": false,
      "lunch": true,
      "snack": false,
      "dinner": false,
      "mealsPerDay": 1
    }
  ],
  "canCheckout": true,
  "checkoutBlockReason": null,
  "checkoutEndpoint": "POST /api/v1/customer/family-subscription-flow/checkout",
  "managerPrepaid": false,
  "managerSubscriptionId": null,
  "newMembersCount": 2,
  "newMembersSubtotal": 90.000
}
```

**اعرض `finalPrice` من الرد.** لو العميل لعب في الرقم، السيرفر يتجاهله.

### 2) لو `canCheckout == false`

اقرأ `checkoutBlockReason`:

| السبب | رجّع المستخدم |
|--------|----------------|
| `family.allergies_required` | [الحساسية](./05-allergies.md) لكل فرد في السلة |
| `family.member_incomplete` | كمّل خطة فرد |
| `family.not_draft` | فيه دفع شغّال |
| `family.invalid_amount` | السلة فاضية/سعر 0 |

### 3) وسائل الدفع

```http
GET /api/v1/customer/family-subscription-flow/payment-methods?amount=81.000
```

`amount` = `finalPrice` من السمري. `400 subscription_flow.invalid_amount` لو ≤ 0.  
`409 payment.gateway_not_configured` لو البوابة مش متظبطة.

### 4) ابدأ الدفع

```http
POST /api/v1/customer/family-subscription-flow/checkout
Content-Type: application/json

{
  "callbackUrl": "https://localhost/callback",
  "errorUrl": "https://localhost/error",
  "correlationId": "family-pay-2026-09-07-1",
  "paymentMethodId": 2
}
```

`callbackUrl` / `errorUrl`: `https` (هوست مسموح) أو scheme زي `mealmate`. غير كده `400 checkout.redirect_url_not_allowed`.

نجاح:

```json
{
  "familyGroupId": "…",
  "paymentAnchorSubscriptionId": "…",
  "paymentUrl": "https://…?paymentId=…",
  "holdExpiresAtUtc": "2026-09-07T12:15:00Z",
  "finalPrice": 81.000
}
```

افتح `paymentUrl` في WebView. بعد الرجوع نفس تأكيد دفع الاشتراك الفردي (webhook/verify).

`502 payment.initialization_failed` → رسالة وإعادة السمري. العيلة Active **متتحولش** لمسودة.

`409 family.allergies_required` / `family.not_draft` / `family.member_incomplete` → ProblemDetails `code`.

## خصم العيلة (للعرض فقط)

- أول دفعة: باقة الحجم حسب **عدد اللي هيتدفع لهم** في السلة دي.
- إضافة لاحقة: **المشتركين النشطين + الجدد** — الخصم على الاشتراكات الجديدة فقط. أسعار القديم متتغيرش.

## قواعد

- `correlationId` ثابت لنفس المحاولة (idempotency).
- أثناء `CheckoutPending` متعملش checkout تاني.
- بعد النجاح: أفراد السلة ليهم تقويم. الباقي في [إعداد العيلة](./01-family-setup.md) «من غير خطة» لحد دفعة جاية.

## السابق

[06-address.md](./06-address.md)
