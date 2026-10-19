# تسليم الإعلانات والكوبونات للموبايل — 2026-10-14

## المنفذ

ملفات ربط بالعربية لكل شاشة في [فهرس الشاشات](../../mobile/advertising-coupons-screens/README.md): الرئيسية والإعلانات، العروض، التفاصيل، البرنامج، الباقة/المدة/الفئة، العنوان والتفضيلات، الكوبون والملخص، الدفع، النتيجة والاستئناف، ومرجع مستقل للحملات. تتضمن العقود الفعلية، ترويسات اللغة و`X-Bilingual: true`، التنقل، الأخطاء، وقوالب طلبات بمعرفات رمزية.

إصلاحات المصدر:

- ربط رفع المادة الإعلانية بمعرف المزاد الموجود في URL بدل الوثوق بمعرف الجسم، واشتقاق وجهة `restaurant` المفقودة من المطعم المالك المحفوظ.
- استبعاد مساحة الإعلان غير النشطة، وتوحيد قص `eventId` قبل فحص التكرار والحفظ.
- تثبيت معرف العرض المشتق من حملة غير متزامنة مع سجل Offer ليكون معرف الحملة؛ حفظ معرف العرض الحقيقي عند وجود سجل متزامن. إزالة أوصاف المنتج المركبة خارج موارد الترجمة.
- إضافة `eligibleProgramIds`, `eligibleBundleIds`, `eligibleRestaurantIds`, `sourceType` إلى `OfferDto` وتحميل العلاقات في قارئات العرض.
- إضافة `GET /api/v1/customer/offers/{offerId}/details` الذي يعيد `{offer, serverNowUtc}` ويستخدم أهلية قائمة العروض. إبقاء استجابة `GET /offers/{id}` القديمة مسطحة مع إخضاعها لنفس أهلية العميل، لمنع إظهار عروض Draft أو غير المؤهلة عبر مسار التفاصيل العام السابق.
- جعل تطبيع كود الكوبون المدخل مستقلًا عن ثقافة عملية الخادم (`ToUpperInvariant`)؛ أثبت الاختبار أن `admin10` يفشل سابقًا تحت `tr-TR` ثم ينجح بعد الإصلاح دون استهلاك استخدام أثناء المعاينة.
- ربط اسم شاشة الإعلانات في قائمة المطعم بالقاموس العربي/الإنجليزي الموجود. تصنيف `restaurant` و`Active` كمعرفات API عبر استثناءات محددة في ملفات checker، دون تعديل baseline.

## ملفات المصدر المتأثرة

جميع المسارات التالية نسبية إلى جذر المشروع؛ توجد تعديلات أخرى للمستخدم في الشجرة ولم تُضمّن في هذا التسليم أو تُحذف:

```text
backend/src/MealMate.Api/Controllers/Customer/CustomerOffersController.cs
backend/src/MealMate.Api/Controllers/Restaurant/RestaurantAdvertisingController.cs
backend/src/MealMate.Api/Resources/Resources.Messages.resx
backend/src/MealMate.Api/Resources/Resources.Messages.ar.resx
backend/src/MealMate.Application/Features/Advertising/Customer/CustomerAdvertisingQueries.cs
backend/src/MealMate.Application/Features/Advertising/Restaurant/RestaurantAdvertisingCommands.cs
backend/src/MealMate.Application/Features/Offers/Commands/CreateOfferDraftCommand.cs
backend/src/MealMate.Application/Features/Offers/DTOs/OfferDtos.cs
backend/src/MealMate.Application/Features/Offers/Queries/GetCustomerAvailableOffersQuery.cs
backend/src/MealMate.Application/Features/Offers/Queries/GetCustomerOfferDetailsQuery.cs
backend/src/MealMate.Application/Features/Offers/Queries/GetOfferByIdQuery.cs
backend/src/MealMate.Application/Features/Offers/Queries/GetOffersListQuery.cs
backend/src/MealMate.Application/Features/ProgramsSubscriptions/Flow/SubscriptionFlowCouponService.cs
backend/tests/MealMate.UnitTests/Advertising/AdvertisingLifecycleTests.cs
backend/tests/MealMate.UnitTests/Offers/OfferQueriesTests.cs
backend/tests/MealMate.UnitTests/Offers/CampaignOfferSyncTests.cs
backend/tests/MealMate.UnitTests/Offers/SubscriptionFlowCouponServiceTests.cs
backend/tests/MealMate.ApiTests/Controllers/AdvertisingMobileBoundaryTests.cs
frontend/restaurant-dashboard/src/app/core/navigation/restaurant-nav.config.ts
frontend/restaurant-dashboard/scripts/i18n-exceptions.json
scripts/i18n-exceptions.json
```

## التحقق الفعلي

| الأمر / الفحص | النتيجة |
|---|---|
| اختبارات Unit بفلاتر AdvertisingLifecycleTests / OfferQueriesTests / CampaignOfferSyncTests / SubscriptionFlowCouponServiceTests | 52 ناجحًا، صفر فشل |
| اختبارات API بفلاتر AdvertisingMobileBoundaryTests / CheckoutCouponFlowTests / OffersControllerTests | 14 ناجحًا، صفر فشل |
| `dotnet build MealMate.sln --no-restore --verbosity quiet /p:WarningLevel=0` من backend | نجاح، صفر أخطاء؛ تحذيرات البناء مخفية بهذا الخيار، وليس هذا إثبات غياب التحذيرات المعتادة |
| `npm run typecheck` و`npm run build` من restaurant-dashboard | نجاح؛ البناء يعرض تحذيرات CSS/حجم معروفة |
| `node scripts/i18n-audit.cjs --self-test` | نجاح |
| `node scripts/i18n-audit.cjs` | نجاح الفحص التشخيصي، صفر اختلافات parity المكتشفة فيه؛ ليس بوابة منع ولا إثبات خلو المشروع من النصوص القديمة |
| `node scripts/i18n-check.cjs` من الجذر | نجاح، صفر مخالفات جديدة؛ توجد ثلاث عيوب parity قديمة مقبولة بالخط الأساس الحالي |
| `npm run i18n:check` من restaurant-dashboard | نجاح، صفر مخالفات جديدة |
| `npm run i18n:test` من الجذر | 36 ناجحًا |
| `npm run i18n:test` من restaurant-dashboard | 36 ناجحًا |
| رسالة `offers.customer.unavailable` | تحقق من النصين العربي والإنجليزي والعودة إلى العربي باستخدام ResourceManager؛ ليس اختبار تبديل واجهة موبايل |
| روابط Markdown المحلية، مراجعة مستقلة للتغيير، و`git diff --check` | نجاح |

أُعيد إنتاج أخطاء معرف الحملة، مساحة الإعلان المتوقفة، غياب معرّفات نطاق التنقل، تطبيع الكوبون، معرّف المزاد في الجسم، وجهة المطعم الفارغة، وإعادة إرسال eventId المقصوص قبل إصلاحها.

## إصلاحات التحقق قبل رفع Git

أعيدت اختبارات المهمة في فرعين نظيفين من `origin/dev` للباك إند والمطعم، بدل رفع تغييرات الشجرة المشتركة كلها. اختير من ملفات موارد الترجمة المشتركة سطر رسالة العرض فقط، ولم تُضمّن تعديلات العائلة أو أداة Seed.

مراجعة اعتماديات شاشة المطعم كشفت ثلاثة عيوب في الواجهة التي لم تكن محفوظة في Git: عدم بدء جلسة الدفع، إرسال `bidId` بدل `placementId` عند الإيقاف، وتخزين الرسائل بعد ترجمتها بدل اشتقاقها من اللغة. أصلحت الواجهة لتبدأ API جلسة دفع حقيقية، وتحفظ مرجع المزود والمبلغ والعملة للمزاد، ثم تتحقق عبر الخادم بعد العودة. يضيف الباك إند `placementId` لبيانات مزايدات المطعم مع حصره في المواضع التي يملكها المطعم. تستخدم الرسائل رموزًا دلالية وتبديلًا تفاعليًا، والتواريخ لغة العرض مع توقيت الكويت.

التحقق الإضافي: 53 اختبار Unit ناجحًا و14 اختبار API ناجحًا، وبناء الباك إند ناجح في النسخة المعزولة. سكربت المطعم `node scripts/advertising-flow.test.cjs` يجرب الكومبوننت الحقيقي داخل Angular injection context مع بدائل عند حدود HTTP والتنقل؛ نجحت مجموعات الدفع والاستئناف، معرف الإيقاف، والتبديل `ar → en → ar` للخطأ والنجاح المفتوحين. فشل السكربت على snapshot الكود السابق بسبب غياب `startPayment` ثم نجح على الإصلاح. هذا تحقق من منطق الكومبوننت، وليس تجربة متصفح أو عملية دفع فعلية. فحص TypeScript وبناء Angular ناجحان أيضًا.

أضيف [ملف شاشة إعلانات المطعم](../../mobile/advertising-coupons-screens/11-restaurant-advertising.md) إلى التسليم. يوجد الـZIP المحدث مع هذه الملفات في مستودع الباك إند.

## حدود التسليم

تطبيق الموبايل غير موجود في هذا المستودع. لم يُختبر على جهاز أو محاكي، ولم تُجرَ تجربة واجهة `ar → en → ar` أو RTL/LTR أو تحديث الصفحة، ولم تُجرَ عملية دفع حقيقية أو اختبارات تزامن PostgreSQL في هذه المهمة. لا نشر، ولا migrations، ولا بيانات Seed، ولا كتابة على قاعدة الإنتاج.

دفع الحملات التشاركية مسار مستقل ما زال يحتاج عقد بدء الدفع وحالة الجلسة للعميل: API الحالي ينشئ حجزًا ويعيد السعر والتخصيصات، ولا يعيد `paymentUrl`. ملف الشاشة العاشرة يسجل هذه الفجوة؛ لا يُستدعى webhook المزود من الموبايل، ولا يُدّعى أن ذلك الفلو اكتمل.

Mock Data Check:
- No mock business data introduced.
- No fake fallback responses introduced.
- No hardcoded production business values introduced.
- All runtime data comes from real application sources.
