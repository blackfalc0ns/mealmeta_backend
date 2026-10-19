# 07 — إدخال القسيمة وملخص الطلب

قائمة العروض للاستكشاف. رموز `PromoCode` التي أنشأها المسؤول يدخلها العميل يدويًا هنا. يتطلب ملخص الاشتراك رمز دخول العميل.

## طلب الملخص والتقييم

`GET /api/v1/customer/subscription-flow/summary` يربط معاملات `SubscriptionSummaryRequest` التالية:

| الحقل | النوع | مطلوب؟ | المعنى |
|---|---|---:|---|
| `programId` | UUID | نعم | البرنامج المختار |
| `bundleId` | UUID | نعم | الباقة المختارة |
| `tier` | `RestaurantTier` enum | نعم | قيمة الفئة كما يعيدها API |
| `durationDays` | عدد صحيح | نعم | المدة المختارة بالأيام |
| `startDate` | `DateOnly` بصيغة `YYYY-MM-DD` | لا | تاريخ البداية |
| `includeFriday` | boolean | لا | هل تشمل أيام الخدمة الجمعة |
| `restaurantsPage` | عدد صحيح | لا | صفحة المطاعم |
| `restaurantsPageSize` | عدد صحيح | لا | حجم الصفحة |
| `promoCode` | نص | لا | الرمز الذي أدخله العميل |

مثال بقيم رمزية؛ استبدل المعرفات والفئة والتاريخ بقيم حالة التطبيق الفعلية. يرمز الرمز داخل URL عند الإرسال:

```http
GET /api/v1/customer/subscription-flow/summary?programId={PROGRAM_ID}&bundleId={BUNDLE_ID}&tier={TIER_ENUM}&durationDays=30&startDate=2026-11-01&includeFriday=false&promoCode={URL_ENCODED_CODE}
Authorization: Bearer {CUSTOMER_ACCESS_TOKEN}
X-Bilingual: true
```

التقييم للقراءة فقط ولا ينشئ حجزًا ولا يستهلك الاستخدام. يحتوي حقل `coupon` في الملخص على `code`, `status`, `offerId` (قد يكون null), `discountAmount`, `messageAr`, `messageEn`. القيم المعروفة لـ `status` هي `valid`, `not_found`, `inactive`, `not_started`, `expired`, `usage_exhausted`, `stacking_conflict`, `ineligible`. احتفظ بهذه القيم كأكواد تقنية واربطها بنصوص التطبيق العربية والإنجليزية. اعرض الرسالة الثنائية وفق اللغة الحالية.

تتضمن `SubscriptionOrderSummaryDto` مثلًا `basePrice`, `campaignDiscount`, `promoDiscount`, `walletCredit`, `gatewayFee`, `finalPrice`, `currencyCode`, `termsAcceptanceRequired`, `checkoutEndpoint`, `canCheckout`, `checkoutBlockReason` و`coupon`. اعرض الإجماليات كما وردت. لا تضف `coupon.discountAmount` مرة أخرى إلى `campaignDiscount` أو `finalPrice`.

## التعديل والاستعادة

احتفظ بالرمز في حالة الشاشة، وأعد طلب الملخص بعد إدخاله أو حذفه أو تغيير البرنامج/الباقة/الفئة/المدة/التاريخ. صلاحية المعاينة لا تحجز الاستخدام؛ يعيد الدفع التقييم وينشئ حجزًا مؤقتًا ضمن معاملة الشراء. إذا تغيرت الصلاحية، اعرض الحالة والرسالة والإجمالي الجديدين. أبقِ الرمز غير الصحيح حتى يتمكن العميل من تصحيحه. لا تعد بتطبيق كل عرض تلقائيًا: قد يتطلب الاشتراك الفردي إدخال الرمز، بينما للحملة مسار شراء مستقل.

احترم `canCheckout`, `checkoutBlockReason`, `termsAcceptanceRequired` وحالة التوفر. وفّر حالات مترجمة للتقييم والصلاحية وعدم الصلاحية وانتهاء العرض وتحديث السعر. عند فشل الشبكة احتفظ بالمدخلات وأتح إعادة المحاولة.

**مصادر التنفيذ:** [CustomerSubscriptionFlowController.cs](../../../src/MealMate.Api/Controllers/V1/CustomerSubscriptionFlowController.cs)، [SubscriptionFlowContracts.cs](../../../src/MealMate.Application/Features/ProgramsSubscriptions/Flow/SubscriptionFlowContracts.cs)، [SubscriptionFlowCouponService.cs](../../../src/MealMate.Application/Features/ProgramsSubscriptions/Flow/SubscriptionFlowCouponService.cs)، [CustomerCheckoutService.cs](../../../src/MealMate.Application/Features/CustomerCheckout/CustomerCheckoutService.cs).
