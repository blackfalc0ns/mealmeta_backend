> Payment integration updated 2026-10-06: use the [Payzah contract](<F:/MealMate/backend/docs/mobile/payzah-payments.md>). Any old-provider payment endpoints, method IDs, credential/webhook fields or refund instructions below are superseded; they are retained only as historical context for this mixed guide.

# بوابة الدفع — ربط الموبايل

ملف شاشة **الدفع عبر MyFatoorah** فقط. باقي فلو الاشتراك: [`mobile-customer-subscription-flow.md`](./mobile-customer-subscription-flow.md).
**التحديثات الجديدة (pagination + theme + كوبون الخصم):** [`mobile-subscription-flow-updates.md`](./mobile-subscription-flow-updates.md).

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

**بعد تطبيق/إزالة كود الخصم أو تغيير المدة أو الجمعة: أعد جلب هذه القائمة بالمبلغ الجديد** قبل عرض الطرق — `serviceCharge` و`totalAmount` يتغيران مع المبلغ.

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
| `startDate` | نعم — يوم اختيار العميل (تقويم الكويت ≥ اليوم) |

باقي الجسم: `programId`, `bundleId`, `tier`, `durationDays`, `includeFriday`, `correlationId`, و`promoCode` اختياري.

لا ترسل `restaurantId` ولا `regionId` ولا `deliveryAddress`. الباك يملّيهم من عنوان العميل الافتراضي وأول مطعم مؤهل. أول صندوق = `startDate + 3 أيام` (تخطي الجمعة إن لزم). الرد يعيد `paymentDate` + `startDate` (أول توصيل).

**كود الخصم في الدفع:**

- أرسل `promoCode` بنفس القيمة المعروضة في الملخص. الباك يعيد التحقق ويعيد التسعير بنفسه؛ `finalPrice` في رد الـcheckout هو المبلغ المحسوب من الخادم ولا يُقبل خصم محسوب في التطبيق.
- إذا صار الكود غير صالح (منتهي/مستنفد/غير مؤهل…) بين الملخص والدفع يُرفض الطلب **قبل إنشاء أي اشتراك أو جلسة دفع**:

```json
{
  "error": "checkout.coupon_rejected",
  "coupon": {
    "code": "WELCOME10",
    "status": "expired",
    "offerId": null,
    "discountAmount": 0,
    "messageAr": "انتهت صلاحية كود الخصم",
    "messageEn": "This coupon code has expired"
  }
}
```

هذا خطأ 400 — أعد المستخدم للملخص بنفس الرسالة واسمح له بإزالة الكود وإعادة المحاولة. حالات `status` الممكنة: `not_found` | `inactive` | `not_started` | `expired` | `usage_exhausted` | `stacking_conflict` | `ineligible` (والصحيح `valid` في الملخص).

- إذا صار السعر صفرًا بعد الخصم يُرفض الدفع بـ`400 checkout.coupon_full_discount_unsupported` — لا يوجد مسار دفع بمبلغ صفري حاليًا.
- استئناف دفع اشتراك غير مكتمل (`resume`) لا ينشئ خصمًا جديدًا: الكوبون المحجوز للاشتراك يُعاد التحقق منه ويُعاد التسعير منه. إذا لم يعد صالحًا يرجع `400 checkout.coupon_no_longer_valid` بنفس شكل `coupon` أعلاه قبل بدء الدفع.

```json
{
  "programId": "b59a2251-ba7f-4a78-a6bb-d43defa2e4d2",
  "bundleId": "a6497534-efb2-4744-b190-9ac81a5b5102",
  "tier": "Basic",
  "durationDays": 6,
  "startDate": "2026-08-27",
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
  "paymentDate": "2026-08-27",
  "startDate": "2026-08-30",
  "deliveryAddress": "Salmiya, Kuwait"
}
```

- الحجز على البوابة **15 دقيقة** (`holdExpiresAtUtc`).
- `502` = فشل إنشاء جلسة MyFatoorah؛ الحجوزات اتفكت. اعمل جلسة جديدة، لا تعيد نفس `subscriptionId`.

---

## 4. بعد الدفع — شاشة تم الدفع

لا تستدعي التقويم.

بعد الرجوع من البوابة اسأل حالة الدفع. الـGET يسأل MyFatoorah فورًا ويتحدّث الحالة — مش مستني الـwebhook:

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
- [ ] مفيش `restaurantId` / `regionId` / `deliveryAddress` في الـbody
- [ ] `startDate` = يوم اختيار العميل (إلزامي)
