# 05 — الباقة والمدة والفئة

تتطلب هذه المسارات رمز دخول عميل صالحًا.

## العقود

- الباقات: `GET /api/v1/customer/subscription-flow/catalog/programs/{programId}/bundles`، مع `page` و`pageSize` اختياريين. يتوفر المسار القديم `GET /api/v1/customer/subscription-flow/programs/{programId}/bundles` أيضًا.
- المدد: `GET /api/v1/customer/subscription-flow/programs/{programId}/bundles/{bundleId}/durations?includeFriday={bool}`. يوجد أيضًا مسار للبرنامج دون `bundleId`.
- فئات كتالوج البيع: `GET /api/v1/customer/subscription-flow/catalog/programs/{programId}/bundles/{bundleId}/tiers?durationDays={n}&includeFriday={bool}&startDate=YYYY-MM-DD`.
- مسار الفئات القديم: `GET /api/v1/customer/subscription-flow/programs/{programId}/bundles/{bundleId}/tiers` ويقبل اختياريًا `durationDays`, `includeFriday`, `startDate`, و`familyMemberId`.

تحمل النتائج معرفات الخادم وأسماء عربية وإنجليزية. يتضمن خيار المدة `durationDays` (قد يكون null)، ودعم الجمعة وتوفرها وعدد أيام الخدمة. يتضمن خيار الفئة `tier` (قيمة enum API)، والأسماء، والمدة، والسعر الإجمالي واليومي، والعملة، والتوفر وخيارات أسعار المدد. احتفظ بقيم enum كما أعادها الخادم. لا تخترع سعرًا أو مدة عند غياب القيمة أو عدم التوفر؛ اعرض سبب التعطيل الذي أعاده الخادم بترجمة مناسبة.

## حفظ الاختيارات

احتفظ بـ `programId`, `bundleId`, `durationDays`, قيمة `tier`, اختيار الجمعة والسياق السعري المعاد في حالة التنقل. أعد تحميل التوفر عند تغير الاختيار أو تاريخ البداية. ميّز النتائج الفارغة عن التحميل والخطأ. بعد الاختيار انتقل إلى [العنوان والتفضيلات](06-address-and-preferences.md)، ثم أعد التحقق من الملخص قبل الدفع.

تسعير باقات ومدد الحملة يتبع [دفع الحملة](10-campaign-checkout.md)، ولا يخلط مع سعر الاشتراك الفردي.

## مثال طلب

قالب بمعرفات رمزية؛ أرسل القيم المختارة من استجابات الخادم:

```http
GET /api/v1/customer/subscription-flow/catalog/programs/{PROGRAM_ID}/bundles/{BUNDLE_ID}/tiers?durationDays=30&includeFriday=false&startDate=2026-11-01
Authorization: Bearer {CUSTOMER_ACCESS_TOKEN}
X-Bilingual: true
```

**مصادر التنفيذ:** [CustomerSubscriptionFlowController.cs](../../../src/MealMate.Api/Controllers/V1/CustomerSubscriptionFlowController.cs)، [SubscriptionFlowContracts.cs](../../../src/MealMate.Application/Features/ProgramsSubscriptions/Flow/SubscriptionFlowContracts.cs)، [SubscriptionCatalogAvailabilityService.cs](../../../src/MealMate.Application/Features/ProgramsSubscriptions/Flow/SubscriptionCatalogAvailabilityService.cs).
