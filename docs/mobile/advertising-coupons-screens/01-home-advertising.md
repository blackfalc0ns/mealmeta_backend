# 01 — إعلانات الصفحة الرئيسية

## العقد

- `GET /api/v1/customer/advertising/placements?placementKey={placementKey}` معلّم `[AllowAnonymous]`، لكن إرجاع الإعلانات المناسبة يتطلب عميلًا مسجلًا وعنوانًا افتراضيًا له `RegionId`. عند غياب الهوية أو العنوان أو المنطقة، تكون `placements` فارغة. يعيد الاستعلام الحالي مواضع نشطة لمزاد حالته `Awarded` وخلال نافذة العرض بالتوقيت UTC والمناسبة لمنطقة العميل؛ بحد أقصى خمسة مواضع.
- الاستجابة: `{ "placements": AdPlacementDto[], "serverNowUtc": "..." }`.
- يحتوي `AdPlacementDto` على `id`, `placementKey`, `assetReadUrl`, `targetType`, و`targetId` اختياري، و`displayEndsAtUtc`, و`isSponsored`. الإعلان ممول وليس قسيمة أو خصمًا.
- رابط `assetReadUrl` قصير العمر (يُطلب حاليًا لمدة 30 دقيقة). عند انتهاء صلاحيته أو فشل تحميل الصورة، أعد جلب المواضع وحاول مرة واحدة، ثم اعرض حالة بديلة مترجمة. لا تحفظ الرابط الموقّع كرابط دائم.

## العرض والتنقل

استخدم `placementKey` الذي يحدده تصميم الصفحة الرئيسية. واجهة المطعم ترسل `targetType` الافتراضي `restaurant`. عند إرسال المادة الإعلانية، يطابق الخادم هذا النوع دون حساسية لحالة الأحرف ويخزنه بصيغة `restaurant`. إذا كان `targetId` مفقودًا أو `Guid.Empty`، يملؤه الخادم بمعرف المطعم المالك المحفوظ في الموضع؛ لذلك تكون المواد الجديدة المرسلة عبر هذا المسار قابلة للتوجيه إلى المطعم. يبقى `targetType` نصًا حرًا بلا تعداد API لأنواع أخرى. للمواد القديمة أو الأنواع الأخرى، إذا كان `targetId` null أو النوع غير مدعوم في الجوال، اعرض الإعلان دون جعله قابلًا للتنقل.

استخدم تسمية «إعلان ممول» مترجمة ووصفًا مناسبًا للصورة لقارئات الشاشة. عند فراغ القائمة أخفِ وحدة الإعلانات دون عرض خطأ. اعرض عنصر تحميل مترجم أثناء الطلب، وإعادة المحاولة عند فشل الشبكة. احسب العد التنازلي اعتمادًا على `serverNowUtc` و`displayEndsAtUtc`، وجدّد المواضع عند الانتهاء أو عند استئناف التطبيق.

## تسجيل الأحداث

- `POST /api/v1/customer/advertising/events` بجسم JSON يحتوي `placementId` (UUID)، و`eventId` (نص لا يتجاوز 128 حرفًا)، و`eventType` (`impression` أو `click` بحروف صغيرة مطابقة).
- أنشئ `eventId` ثابتًا لكل ظهور أو نقرة منطقية وأعد استخدامه عند إعادة المحاولة. يمنع الخادم تكرار الزوج `(placementId,eventId)`. سجّل الظهور عند ظهور الإعلان فعليًا والنقرة قبل التنقل. لا تمنع مشكلة تسجيل التحليلات فتح الوجهة.
- يسمح المسار بالوصول المجهول؛ تسجيل الحدث لا يثبت الشراء.

**مصادر التنفيذ:** [CustomerAdvertisingController.cs](../../../src/MealMate.Api/Controllers/Customer/CustomerAdvertisingController.cs)، [CustomerAdvertisingQueries.cs](../../../src/MealMate.Application/Features/Advertising/Customer/CustomerAdvertisingQueries.cs)، [RestaurantAdvertisingCommands.cs](../../../src/MealMate.Application/Features/Advertising/Restaurant/RestaurantAdvertisingCommands.cs)، [AdAuction.cs](../../../src/MealMate.Domain/Advertising/AdAuction.cs).
