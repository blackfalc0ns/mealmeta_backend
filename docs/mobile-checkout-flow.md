# بوابة الدفع — ربط الموبايل

ملف شاشة **الدفع عبر MyFatoorah** فقط. باقي فلو الاشتراك: [`mobile-customer-subscription-flow.md`](./mobile-customer-subscription-flow.md).

## لينك جلسة الفيزا / الدفع

مفيش لينك ثابت للفيزا. كل ضغطة شراء بتنشئ جلسة جديدة من MyFatoorah، واللينك بيرجع في `paymentUrl`.

1. زر الشراء:

```http
POST https://maelmate.runasp.net/api/v1/customer/subscriptions/checkout
Authorization: Bearer <customer-access-token>
Content-Type: application/json
```

2. من `200` خد الحقل ده **فقط** وافتحه في WebView أو المتصفح — دي شاشة إدخال الفيزا وزر الدفع:

```json
{
  "paymentUrl": "https://demo.myfatoorah.com/ie/Master?paymentId=0707..."
}
```

`paymentUrl` بيتغيّر كل مرة (sandbox أو live حسب إعداد البوابة). لا تبني اللينك يدويًا ولا تفتح webhook.

3. بعد ما العميل يدوس دفع على صفحة MyFatoorah، البوابة وحدها تنادي:

```text
POST https://maelmate.runasp.net/api/v1/webhooks/myfatoorah
```

Base URL:

```text
https://maelmate.runasp.net/api/v1
```

```http
Authorization: Bearer <customer-access-token>
Accept: application/json
Accept-Language: ar
```

لا تجمع بيانات البطاقة داخل MealMate. لا تستدعِ `confirm-payment`. التأكيد من الباك عبر webhook MyFatoorah.

---

## 1. طرق الدفع

`amount` = `finalPrice` من الملخص. أكبر من صفر.

```http
GET /customer/subscription-flow/payment-methods?amount={finalPrice}
```

مثال:

```json
{
  "paymentMethodId": 1,
  "code": "KN",
  "nameAr": "كي نت",
  "nameEn": "KNET",
  "integrationType": "MYFATOORAH_HOSTED",
  "serviceCharge": 0,
  "totalAmount": 41.6,
  "currencyCode": "KWD",
  "isAvailable": true,
  "unavailableReason": null
}
```

- اعرض فقط `isAvailable === true`.
- خزّن `paymentMethodId` وأرسله في بدء الدفع.
- القائمة تتفلتر في الباك (KNET / Visa-Master / Apple Pay حسب إعداد الأدمن). لا تثبت طرق محليًا.
- المحفظة ترجع معطّلة: `code=MEALMATE_WALLET` و `unavailableReason=wallet_not_implemented`. لا تفعّلها من التطبيق.

أخطاء:

| كود | HTTP |
|---|---|
| `subscription_flow.invalid_amount` | 400 |
| `payment.gateway_not_configured` | 409 |
| Payment methods unavailable | 502 |

---

## 2. بدء جلسة البوابة

```http
POST /customer/subscriptions/checkout
Content-Type: application/json
```

الحقول الخاصة بالبوابة:

| حقل | إلزامي |
|---|---|
| `paymentMethodId` | نعم — من الخطوة 1 |
| `callbackUrl` | نعم |
| `errorUrl` | نعم |

باقي الجسم: `programId`, `bundleId`, `tier`, `durationDays`, `includeFriday`, `correlationId`.

لا ترسل `restaurantId` ولا `regionId` ولا `startDate` ولا `deliveryAddress`. الباك يملّيهم من عنوان العميل الافتراضي وأول مطعم مؤهل، و`startDate` = تاريخ إنشاء الاشتراك (اليوم UTC).

```json
{
  "programId": "b59a2251-ba7f-4a78-a6bb-d43defa2e4d2",
  "bundleId": "a6497534-efb2-4744-b190-9ac81a5b5102",
  "tier": "Basic",
  "durationDays": 6,
  "callbackUrl": "mealmate://payment/success",
  "errorUrl": "mealmate://payment/error",
  "includeFriday": false,
  "paymentMethodId": 1,
  "correlationId": "mobile-generated-id"
}
```

### `callbackUrl` / `errorUrl`

مقبول: scheme `mealmate` أو `https`.  
لو `https`: الـhost `maelmate.runasp.net` أو `localhost`.  
غير كده: `checkout.redirect_url_not_allowed`. ممنوع `http` أو رابط عشوائي.

---

## 3. رد البوابة

`200` — خذ `paymentUrl` وافتحه فورًا في WebView أو المتصفح:

```json
{
  "subscriptionId": "3fa85f64-5717-4562-b3fc-2c963f66afa6",
  "paymentUrl": "https://...",
  "holdExpiresAtUtc": "2026-08-15T00:35:00Z",
  "finalPrice": 41.6,
  "paymentStatus": "PendingPayment",
  "restaurantId": "97db2bbb-b652-4aaa-8584-3414e6966792",
  "regionId": "6590e7e9-5951-4977-895c-fc586b6a2952",
  "startDate": "2026-08-17",
  "deliveryAddress": "Salmiya, Kuwait"
}
```

- الحجز على البوابة **15 دقيقة** (`holdExpiresAtUtc`).
- `502` = فشل إنشاء جلسة MyFatoorah؛ الحجوزات اتفكت. اعمل جلسة جديدة، لا تعيد نفس `subscriptionId`.

---

## 4. بعد الدفع — شاشة تم الدفع

لا تستدعي التقويم.

بعد `checkout` خزّن `subscriptionId` وافتح `paymentUrl`. بعد الرجوع من البوابة (أو أثناء الانتظار) اسأل حالة الدفع فقط:

```http
GET /customer/subscriptions/{subscriptionId}/payment
```

الرد دائمًا `200` (مش `409`):

```json
{
  "subscriptionId": "4a4e48c4-d42c-4e01-bc3e-552c0fc40a0a",
  "paymentStatus": "Paid",
  "isPaid": true,
  "isFailed": false,
  "amount": 41.6,
  "currencyCode": "KWD"
}
```

| `isPaid` | `isFailed` | الشاشة |
|---|---|---|
| `false` | `false` | انتظار قصير، كرّر الـGET (2s → 4s → 8s) |
| `true` | `false` | **تم الدفع** |
| `false` | `true` | الدفع فشل |

`mealmate://payment/success` لوحده مش كفاية. اعرض «تم الدفع» لما `isPaid === true`.

ممنوع: `GET .../calendar` في الخطوة دي.

---

## Checklist

- [ ] `amount` = `finalPrice` من الملخص
- [ ] `paymentMethodId` من الـAPI
- [ ] مفيش إدخال بطاقة
- [ ] فتح `paymentUrl` فقط
- [ ] مفيش `confirm-payment` من الموبايل
- [ ] بعد الدفع استعلم `GET .../payment` واعرض تم الدفع عند `isPaid: true` — من غير التقويم
- [ ] مفيش `restaurantId` / `regionId` / `startDate` / `deliveryAddress` في الـbody
