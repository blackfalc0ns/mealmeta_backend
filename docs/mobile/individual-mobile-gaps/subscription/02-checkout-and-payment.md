# 2 — Checkout والدفع (بدون تخطي)

> مرجع بوابة الدفع: [`../../../mobile-checkout-flow.md`](../../../mobile-checkout-flow.md)

## المطلوب من الموبايل

1. `GET .../payment-methods?amount={finalPrice}` — اعرض المتاح فقط.
2. `POST /api/v1/customer/subscriptions/checkout` مع `paymentMethodId` + `startDate` + روابط الرجوع.
3. من `200` خذ **`paymentUrl` فقط** وافتحه (WebView أو متصفح خارجي).
4. بعد رجوع التطبيق: لا تعتبر الدفع ناجحًا من الـdeep link وحده.
5. استعلم:

```http
GET /api/v1/customer/subscriptions/{subscriptionId}/payment
```

| حقل | المعنى |
|-----|--------|
| `isPaid: true` | الدفع مؤكد — انتقل لانتظار التقويم |
| `isFailed: true` | فشل — اسمح بجلسة checkout جديدة |
| `paymentStatus` | للعرض (`PendingPayment` / `Paid` / `Active` / …) |

6. بعدها polling على التقويم (انظر [`../calendar/01-after-payment-gate.md`](../calendar/01-after-payment-gate.md)).

## ممنوع

- تأكيد دفع يدوي / استدعاء webhook من التطبيق.
- اعتبار وجود `paymentId` في callback دليل نجاح نهائي.
- إعادة استخدام نفس `subscriptionId` بعد فشل إنشاء جلسة (`502`) — اعمل checkout جديد.
- إرسال `restaurantId` أو `regionId` أو `deliveryAddress` في body الـcheckout.

## أخطاء checkout الشائعة

| كود | المعنى | إصلاح الموبايل |
|-----|--------|----------------|
| `checkout.address_required` | لا عنوان | شاشة العنوان |
| `checkout.allergies_required` | لم تُؤكَّد الحساسية | شاشة الحساسية |
| `checkout.dietary_preferences_required` | لم تُؤكَّد التفضيلات | شاشة التفضيلات |
| `checkout.existing_subscription` | اشتراك فردي قائم | امنع شراء فردي جديد |
| `restaurant.not_eligible` / لا مطاعم للعنوان | المنطقة غير مغطاة | غيّر العنوان/المنطقة |
| `checkout.redirect_url_not_allowed` | callback غير مسموح | استخدم `mealmate://...` أو host مسموح |
| `502` بوابة | فشل MyFatoorah | جلسة جديدة |

## روابط الرجوع

- مقبول: `mealmate://payment/success` و`mealmate://payment/error`
- أو `https` على host مسموح في الإعدادات
- ممنوع: `http` عشوائي

## حالة الشاشة المقترحة

```text
ملخص → «ادفع الآن»
  → WebView(paymentUrl)
  → شاشة انتظار الدفع (polling payment)
     → isPaid → شاشة «جاري تفعيل اشتراكك» (polling calendar)
     → isFailed → إعادة المحاولة / دعم
```
