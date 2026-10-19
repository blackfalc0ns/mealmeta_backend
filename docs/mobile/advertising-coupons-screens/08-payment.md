# 08 — دفع الاشتراك الفردي

تتطلب المسارات التالية رمز دخول العميل.

## طرق الدفع

`GET /api/v1/customer/subscription-flow/payment-methods?amount={FINAL_PRICE}` يعيد قائمة `CustomerPaymentMethodDto`. أرسل مبلغ الإجمالي الذي أعاده ملخص الخادم، واعرض الطرق ومعرفاتها كما أعادها API؛ لا تثبت قائمة طرق داخل التطبيق.

## بدء الدفع

`POST /api/v1/customer/subscriptions/checkout` يستقبل `StartCheckoutRequest`.

| الحقل | النوع | مطلوب؟ | القيمة |
|---|---|---:|---|
| `programId` | UUID | نعم | معرف البرنامج من الخادم |
| `bundleId` | UUID | نعم | معرف الباقة من الخادم |
| `tier` | `RestaurantTier` enum | نعم | قيمة enum التي يدعمها API |
| `durationDays` | عدد صحيح | نعم | المدة المختارة |
| `callbackUrl` | نص URL | نعم | رابط عودة التطبيق المسجل/المعتمد |
| `errorUrl` | نص URL | نعم | رابط العودة عند الخطأ المعتمد |
| `startDate` | `DateOnly` بصيغة `YYYY-MM-DD` | نعم | تاريخ البداية المختار |
| `includeFriday` | boolean | لا | الافتراضي `false` |
| `correlationId` | نص | لا | معرّف تتبع العميل إن كان مستخدمًا |
| `paymentMethodId` | عدد صحيح | لا | الافتراضي `3`؛ فضّل معرف الطريقة المعادة من الخادم |
| `promoCode` | نص أو null | لا | رمز القسيمة المدخل، إن وجد |

### قالب طلب — لا ترسله قبل استبدال القيم

هذا قالب توثيقي بمعرفات رمزية وليس حمولة صالحة للتنفيذ كما هي:

```http
POST /api/v1/customer/subscriptions/checkout
Authorization: Bearer {CUSTOMER_ACCESS_TOKEN}
Content-Type: application/json

{
  "programId": "{PROGRAM_ID}",
  "bundleId": "{BUNDLE_ID}",
  "tier": "{TIER_ENUM}",
  "durationDays": 30,
  "callbackUrl": "{APP_CALLBACK_URL}",
  "errorUrl": "{APP_ERROR_URL}",
  "startDate": "2026-11-01",
  "includeFriday": false,
  "correlationId": "{OPTIONAL_CORRELATION_ID}",
  "paymentMethodId": 3,
  "promoCode": "{OPTIONAL_COUPON_CODE}"
}
```

استجابة النجاح `CheckoutStartedDto` تشمل `subscriptionId`, `paymentUrl`, `holdExpiresAtUtc`, `menuPlanVersionId`, `finalPrice`، وحقولًا اختيارية مثل `priceSnapshotId`, `eligibilityCheckedAtUtc`, `menuPlanVersionsByDate`, `restaurantPoolSnapshotId`, `restaurantSnapshotCount`, `paymentStatus`, `fulfillmentStatus`, `includeFriday`, `fridayDays`, `totalServiceDays`, `restaurantId`, `regionId`, `startDate`, `deliveryAddress`, `chosenStartDate`. احتفظ بـ `subscriptionId` قبل فتح `paymentUrl` لاستعادة التدفق عند إغلاق التطبيق.

أرسل المعرفات المحفوظة واختيارات العميل الحالية فقط. لا ترسل سعرًا محسوبًا محليًا. عالج أخطاء التحقق والتعارض وتغير التوفر وفشل بوابة الدفع (502) برسالة مترجمة وإعادة تحميل الملخص أو إعادة المحاولة المناسبة.

**مصادر التنفيذ:** [CustomerCheckoutController.cs](../../../src/MealMate.Api/Controllers/V1/CustomerCheckoutController.cs)، [CustomerCheckoutContracts.cs](../../../src/MealMate.Application/Features/CustomerCheckout/CustomerCheckoutContracts.cs)، [SubscriptionUpgradeContracts.cs](../../../src/MealMate.Application/Features/ProgramsSubscriptions/Upgrade/SubscriptionUpgradeContracts.cs) لتعريف `CustomerPaymentMethodDto`، [CustomerSubscriptionFlowController.cs](../../../src/MealMate.Api/Controllers/V1/CustomerSubscriptionFlowController.cs).
