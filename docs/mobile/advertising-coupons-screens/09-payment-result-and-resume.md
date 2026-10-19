# 09 — نتيجة الدفع واستئناف المحاولة

## تأكيد نتيجة الدفع

رابط العودة من Payzah للانتقال بين الشاشات فقط، وليس دليلًا على نجاح الدفع. مسار عودة المزود `GET|POST /api/v1/payments/payzah/return` يعيد HTML ولا يمثل عقد حالة الدفع لتطبيق الجوال.

بعد العودة، اطلب عبر جلسة العميل:

```http
GET /api/v1/customer/subscriptions/{SUBSCRIPTION_ID}/payment
Authorization: Bearer {CUSTOMER_ACCESS_TOKEN}
```

يعيد `SubscriptionPaymentStatusDto`: `subscriptionId`, `paymentStatus`, `isPaid`, `isFailed`, `amount`, `currencyCode`. انتقل إلى حالة النجاح فقط عندما يؤكد الخادم الدفع. إذا بقيت الحالة قيد المعالجة، أعد الاستعلام بفواصل متزايدة ومحدودة. اعرض حالات الفشل والانتهاء بحسب الحالة القادمة من الخادم، وأتح إعادة المحاولة عند أخطاء الاتصال.

## معاينة واستئناف الدفع

- `GET /api/v1/customer/subscriptions/{id}/resume` يعيد `CustomerSubscriptionResumePreviewDto`: `subscriptionId`, `status`, `statusNameAr`, `programId`, `programNameAr`, `programNameEn`, `bundleId`, `bundleNameAr`, `bundleNameEn`, `tier`, `tierNameAr`, `tierNameEn`, `tierLogoUrl`, `mealsPerDay`, `durationDays`, `includeFriday`, `startDate`, `endDate`, `amount`, `currencyCode`, `deliveryAddress`, `canPayDirectly`, `reason`, `holdRemainingSeconds`, `holdExpiresAtUtc`, `suggestedStartDate`, `resumeEndpoint`, `nextScreen`, `payEndpoint`, `checkoutStartDate`.
- `POST /api/v1/customer/subscriptions/{id}/resume` يقبل جسمًا اختياريًا `ResumeCheckoutRequest`: `startDate` (nullable `DateOnly`)، `callbackUrl`، `errorUrl`، و`paymentMethodId` (افتراضي 3). جسم الطلب كله يمكن حذفه. الاستجابة `CheckoutStartedDto` هي ذات شكل بدء الدفع الموضح في [الشاشة السابقة](08-payment.md). هوية العميل من رمز الدخول ويجب أن يملك الاشتراك.
- `GET /api/v1/customer/subscriptions/{id}/restaurants` يقبل `query`, `page`, `pageSize` لجلب خيارات المطاعم بعد إنشاء الاشتراك.

### قالب استئناف اختياري

```http
POST /api/v1/customer/subscriptions/{SUBSCRIPTION_ID}/resume
Authorization: Bearer {CUSTOMER_ACCESS_TOKEN}
Content-Type: application/json

{
  "startDate": "2026-11-02",
  "callbackUrl": "{APP_CALLBACK_URL}",
  "errorUrl": "{APP_ERROR_URL}",
  "paymentMethodId": 3
}
```

القيم أعلاه رمزية وتحتاج إلى استبدالها. إذا انتهى الحجز أو أعاد الاستئناف 404/409، حدّث الملخص وابدأ من جديد وفق التوفر الحالي. بعد فشل الدفع، اعرض الاستئناف فقط إذا سمحت `canPayDirectly` والمعاينة. احتفظ بمعرف الاشتراك والاختيارات بعد إغلاق التطبيق. اعرض الحقول الثنائية بحسب اللغة النشطة مع حفظ معنى التواريخ القادم من الخادم.

**مصادر التنفيذ:** [CustomerCheckoutController.cs](../../../src/MealMate.Api/Controllers/V1/CustomerCheckoutController.cs)، [CustomerCheckoutContracts.cs](../../../src/MealMate.Application/Features/CustomerCheckout/CustomerCheckoutContracts.cs)، [PayzahPaymentReturnController.cs](../../../src/MealMate.Api/Controllers/V1/PayzahPaymentReturnController.cs).
