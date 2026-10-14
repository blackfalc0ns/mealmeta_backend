# وجبات الفرد (08.09)

**السابق:** [المطاعم](./08-restaurants.md) · **التالي:** [مراجعة وتأكيد](./10-review-confirm.md)

## APIs

`GET /api/v1/customer/subscriptions/{subscriptionId}/days/{date}/restaurants/{restaurantId}/meals?slot={slot}&page=1&pageSize=20`. `slot` اختياري، paging يبدأ 1 وحجمه الافتراضي 20. مسار `GET .../days/{date}/meals/available` موجود كمسار Flutter قديم؛ فضّل المسار المحدد بالمطعم.

اختيار: `POST /api/v1/customer/subscriptions/{subscriptionId}/days/{date}/meals/slot`، body `{"mealId":"11111111-1111-4111-8111-111111111111","slot":"lunch"}`؛ response اليوم الكامل المحدّث. لا يوجد endpoint إزالة وجبة في هذا controller.

مثال response مختصر اصطناعي: `{"restaurantId":"11111111-1111-4111-8111-111111111111","nameAr":"مطعم توضيحي","nameEn":"Illustrative restaurant","logoUrl":null,"serviceDate":"2026-10-14","requiredCount":2,"filledCount":1,"slots":[{"slot":"lunch","slotNameAr":"الغداء","slotNameEn":"Lunch","count":1,"page":1,"pageSize":20,"totalCount":1,"totalPages":1,"hasNextPage":false,"hasPreviousPage":false,"meals":[{"mealId":"22222222-2222-4222-8222-222222222222","nameAr":"وجبة توضيحية","nameEn":"Illustrative meal","slot":"lunch","slotNameAr":"الغداء","slotNameEn":"Lunch","sortOrder":1,"imageUrl":null,"calories":null,"proteinG":null,"carbsG":null,"fatG":null,"descriptionAr":null,"descriptionEn":null,"presentAllergenCodes":[],"mayContainAllergenCodes":[],"dietaryPreferenceCodes":[],"ingredients":[],"blockReason":null,"isSelected":false}]}],"coreIngredients":[]}`.

Meals collection DTO يعرض صفحات/slots بحسب المطعم، ومع كل meal `mealId`, `nameAr/En` أو localized `name`, `slot`, `imageUrl`, calories/protein/carbs/fat nullable/قيم، allergen/dietary/ingredients، `blockReason`, `isSelected` بحسب `CustomerCalendarMealDto`. اعرض قيمة الماكرو فقط إذا موجودة، وترجم سبب معروف من API code أو label bilingual، لا تعرض diagnostics الخام.

## الحدود والحالات

قارن الاختيارات بمتطلبات `slotRequirements[].count`. إن كان عدد slot ممتلئاً فالاختيار قد يستبدل العنصر وفق سلوك الخادم؛ لا تضف فوق الحد ولا تخترع إزالة مستقلة. خادم الوجبات يرد اليوم بعد الاختيار؛ اعتمد الرد وأبطل caches للصفحة، اليوم والتقويم. إن تغيّر السياق أثناء طلب الصفحة تجاهل نتيجته.

Loading لكل صفحة؛ نتائج فارغة هي empty state؛ فشل الشبكة يحفظ الصفحة/الاختيارات الموجودة ويتيح retry. `canEdit:false` يمنع زر الإضافة. عند `409 calendar.fulfillment_not_ready` أعد GET لاحقاً؛ `subscription.service_unavailable` 409 حالة منفصلة لاشتراك غير تشغيلي. أخطاء الوصول `family.permission_required`, `family.access_paused`, `family.access_disabled`, `family.action_not_available` (403؛ `family.access_paused` و`family.access_disabled` يرفضان فعل العضو المربوط فقط؛ المدير يستطيع إدارة العضو المستهدف الموقوف) 403. نقص الصندوق عند complete هو `calendar.box_incomplete` 400؛ `calendar.allergy_conflict` 400 عند وجبة ممنوعة؛ `calendar.box_not_ready` 409 إذا snapshot الوجبات غير جاهز؛ `calendar.fulfillment_not_ready` 409 قبل تهيئة التقويم. لا تكمل من هذه الشاشة.

**قبول:** pagination لا تخلط slot/query القديم، blockReason يظهر بلغتين، لا زر remove ولا endpoint مختلق، واحترام عدد الوجبات والرد المحدث.

## اللغة والتنسيق

كل copy جديد في قاموس عربي/إنجليزي reactive؛ اختبر `ar → en → ar` دون reload مع RTL/LTR. أرسل `Accept-Language`. قد يعيد BilingualJsonModifier الحقل الموافق للغة وحقل الاسم unsuffixed بدلاً من الحقلين `nameAr` و`nameEn`؛ لا تشترط وجودهما معاً. اختر اللغة النشطة ثم البديل غير الفارغ ثم label مترجم. التاريخ التشغيلي Asia/Kuwait؛ KWD بثلاث منازل عشرية.





