# 06 — العنوان والتفضيلات

تتطلب هذه المسارات رمز دخول عميل صالحًا.

## العقد

`GET /api/v1/customer/subscription-flow/prerequisites` يعيد `CustomerSubscriptionPrerequisitesDto` بالحقول `isComplete`, `hasDeliveryAddress`, `hasAllergies`, `hasDietaryPreferences`, `deliveryRegionId`. يستخدم تدفق الاشتراك العنوان المحفوظ الافتراضي للعميل؛ المنطقة يحددها الخادم. تعديل العنوان يتم عبر عقد عناوين العميل، ولا يجوز اعتبار منطقة الإعلان أو العرض بديلًا عن عنوان الحساب.

لقياس التغطية حسب اختيارات الاشتراك، استخدم `GET /api/v1/customer/subscription-flow/restaurant-availability` بمعاملات `SubscriptionSummaryRequest`: `programId`, `bundleId`, `tier`, `durationDays` مطلوبة، ويمكن إضافة `startDate`, `includeFriday`, معاملات صفحات المطاعم و`promoCode`.

## حالات الاستكمال والاستعادة

إذا كانت المتطلبات غير مكتملة، افتح إعداد العنوان أو الحساسية أو التفضيلات الغذائية الموجود، واحتفظ بمعرفات البرنامج والباقة، ثم أعد تحميل المتطلبات. إذا لم توجد تغطية أو تغيرت، اشرح السبب وأتح اختيار فئة أو تاريخ آخر ثم حدّث بيانات الخادم. عند فشل الشبكة احتفظ بالاختيارات وأتح إعادة المحاولة؛ البيانات المخزنة مؤقتًا لا تجيز الدفع.

**مصادر التنفيذ:** [CustomerSubscriptionFlowController.cs](../../../src/MealMate.Api/Controllers/V1/CustomerSubscriptionFlowController.cs)، [SubscriptionFlowContracts.cs](../../../src/MealMate.Application/Features/ProgramsSubscriptions/Flow/SubscriptionFlowContracts.cs)، [SubscriptionFlowQueryService.cs](../../../src/MealMate.Application/Features/ProgramsSubscriptions/Flow/SubscriptionFlowQueryService.cs).
