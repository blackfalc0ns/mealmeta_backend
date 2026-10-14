# دليل دمج الإعلانات والعروض والقسائم في تطبيق الجوال

تشرح هذه الملفات شاشات العميل وعقود API الموجودة في `backend/src`. يجب أن تبقى أهلية العرض والأسعار والدفع والسعة والحالة من الخادم هي المرجع. لم تُجرَ اختبارات تشغيل على تطبيق الجوال أو جهاز.

## ترتيب الشاشات

1. [إعلانات الصفحة الرئيسية](01-home-advertising.md)
2. [قائمة العروض](02-offers-hub.md)
3. [تفاصيل العرض](03-offer-details.md)
4. [اختيار البرنامج](04-program-selection.md)
5. [الباقة والمدة والفئة](05-bundle-duration-tier.md)
6. [العنوان والتفضيلات](06-address-and-preferences.md)
7. [القسيمة وملخص الطلب](07-coupon-and-summary.md)
8. [الدفع](08-payment.md)
9. [نتيجة الدفع واستئنافه](09-payment-result-and-resume.md)
10. [الدفع لحملة](10-campaign-checkout.md)
11. [إعلانات المطعم والدفع والإيقاف](11-restaurant-advertising.md)

## قواعد مشتركة

- أرسل رمز دخول العميل إلى مسارات تدفق الاشتراك والدفع الفردي وتقييم القسائم واسترداد العرض ودفع الحملة. وجود `[AllowAnonymous]` على الإعلانات أو استكشاف العروض لا يجعل النتائج شخصية؛ عرض الإعلانات يحتاج عميلًا مسجلًا وعنوانًا افتراضيًا له منطقة.
- يختار API اللغة من `Language` أولًا، ثم `X-Language`، ثم `Accept-Language` (ما لم يُرسل محدد الثقافة في query string). استخدم `Language: ar` أو `Language: en`، أو أرسل `Accept-Language` المناسب. من دون `X-Bilingual: true` يختار الخادم اللغة المطلوبة ويعيد الحقل المترجم، غالبًا مع المفتاح غير الملحق مثل `name` وإبقاء لاحقة اللغة المختارة. عند الحاجة إلى اللغتين معًا — وهو الأنسب للتبديل الفوري دون طلب جديد — أرسل `X-Bilingual: true`؛ عندها تبقى حقول المصدر مثل `nameAr` و`nameEn`.
- اجعل اختيار `nameAr/nameEn` و`descriptionAr/descriptionEn` و`messageAr/messageEn` تفاعليًا مع اللغة النشطة، واحتفظ بالمعرّفات وقيم التعداد والأكواد كما هي. إذا استُخدمت استجابة لغة واحدة، حدّث ترويسة اللغة وأعد الجلب عند التبديل. عند غياب نص اللغة النشطة استخدم النص المتاح باللغة الأخرى.
- افصل أكواد أخطاء API عن نصوص الواجهة المترجمة. وفّر حالات تحميل وفراغ وإعادة محاولة وتحقق وانتهاء ونجاح مترجمة. لا تعرض تشخيصات الخادم الخام كنص للمستخدم.
- رابط عودة الدفع إشارة تنقل فقط. أكّد النتيجة عبر مسار حالة الدفع المصادق عليه.
- حافظ على دلالة `DateOnly` وUTC وعلى العملة والدقة القادمتين من الخادم؛ لا تعِد حساب الإجمالي على الجهاز.

يعالج API ترويسات اللغة عبر [LocalizationExtensions.cs](../../../src/MealMate.Api/Localization/LocalizationExtensions.cs)، [LanguageHeaderRequestCultureProvider.cs](../../../src/MealMate.Api/Localization/LanguageHeaderRequestCultureProvider.cs)، و[BilingualJsonMiddleware.cs](../../../src/MealMate.Api/Localization/BilingualJsonMiddleware.cs). يفعّل `[X-Bilingual: true]` إبقاء حقول اللغتين في الاستجابة كما يوضح [BilingualJsonModifier.cs](../../../src/MealMate.Api/Localization/BilingualJsonModifier.cs). عند تبديل اللغة أثناء عرض حقول bilingual أبقِ مصدر اللغتين واختر النص عبر حالة اللغة الحالية في الجوال.

## مسار العائلة منفصل

تسجيل أفراد العائلة وتخصيص القسائم لهما عقد مستقل. راجع [واجهة اشتراك العائلة](../family-subscription-api.md) و[دليل تدفق العائلة للجوال](../family-subscription-mobile-flow-guide.md). لا تفترض أن حقول القسائم أو المسارات الفردية تنطبق على تدفق العائلة.
