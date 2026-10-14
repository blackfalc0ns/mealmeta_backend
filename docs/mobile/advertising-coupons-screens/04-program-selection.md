# 04 — اختيار البرنامج

تتطلب هذه المسارات رمز دخول عميل صالحًا.

## عقد API

- المسار المفضل لتوفر البرامج بحسب عنوان العميل: `GET /api/v1/customer/subscription-flow/catalog/programs`، مع `page` و`pageSize` الاختياريين. يعيد خيارات البرامج النشطة، وتوفر كل خيار وسبب تعطيله، وتغطية العنوان ومعلومات الصفحة عند طلبها.
- المسار الأقدم `GET /api/v1/customer/subscription-flow/programs` يعيد قائمة مباشرة أو غلافًا مرقمًا عند تمرير `page` أو `pageSize`.
- `GET /api/v1/customer/subscription-flow/types` يعيد أنواع الاشتراكات.

تحتوي بطاقات البرامج على معرفات الخادم والأسماء الثنائية. اعرض `nameAr/nameEn` وفق اللغة النشطة، واحتفظ بالمعرفات في حالة التنقل. لا ترسل منطقة يختارها العميل لتكون بديلًا عن عنوانه المسجل؛ الخادم يستخرج العنوان والمنطقة من حساب العميل.

ميّز بين التحميل، والقائمة الفارغة، وعدم وجود تغطية للعنوان، والخيار المعطّل، وانتهاء الجلسة، وخطأ الشبكة. أبقِ الخيارات المعطلة ظاهرة مع سبب الخادم المترجم إذا أعاده.

## الانتقال

عند اختيار برنامج متاح، مرّر `programId` الذي أعاده الخادم إلى [اختيار الباقة والمدة والفئة](05-bundle-duration-tier.md). قد تساعد معرّفات أهلية العرض على تضييق الخيارات، لكنها لا تفرض اختيارًا ولا تستبدل إعادة التحقق عند الملخص والدفع.

## مثال طلب

```http
GET /api/v1/customer/subscription-flow/catalog/programs?page=1&pageSize=20
Authorization: Bearer {CUSTOMER_ACCESS_TOKEN}
X-Bilingual: true
```

**مصادر التنفيذ:** [CustomerSubscriptionFlowController.cs](../../../src/MealMate.Api/Controllers/V1/CustomerSubscriptionFlowController.cs)، [SubscriptionFlowContracts.cs](../../../src/MealMate.Application/Features/ProgramsSubscriptions/Flow/SubscriptionFlowContracts.cs)، [SubscriptionCatalogAvailabilityService.cs](../../../src/MealMate.Application/Features/ProgramsSubscriptions/Flow/SubscriptionCatalogAvailabilityService.cs).
