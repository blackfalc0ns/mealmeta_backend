# تفاصيل اليوم (08.06)

**السابق:** [التقويم](./06-calendar.md) · **التالي:** [المطاعم](./08-restaurants.md)، [الوجبات](./09-meals.md)، [المراجعة](./10-review-confirm.md)

## API والرد

`GET /api/v1/customer/subscriptions/{subscriptionId}/days/{date}`، حيث التاريخ `yyyy-MM-dd`. الرد `CustomerCalendarDayDto` يحوي: `id`, `serviceDate`, `restaurantId`, `restaurantNameAr/En`, `restaurantLogoUrl`, `status`, `uiStatus`, `isAutoSelected`, `canEdit`, `selectionDeadlineUtc`, `lockAtUtc`, `lockRemainingSeconds`, `filledCount`, `requiredCount`, `slotRequirements[]`, `failureReasonCode`, `nutritionTotals`, `selectedMeals[]`, restaurant rating/delivery window/program/tier/status إن وجدت.

كل slot (`slot`, `slotNameAr/En`, `count`) يظل كما هو في API. كل وجبة بها `mealId`, `nameAr/En`, `slot`, `slotNameAr/En`, `imageUrl`, nullable calories/protein/carbs/fat، ingredients/allergens و`isSelected`. لا تعرض null كصفر. مثال response اصطناعي مختصر: `{"id":"11111111-1111-4111-8111-111111111111","serviceDate":"2026-10-14","restaurantId":null,"restaurantNameAr":null,"restaurantNameEn":null,"restaurantLogoUrl":null,"status":"Open","uiStatus":"empty","isAutoSelected":false,"canEdit":true,"selectionDeadlineUtc":null,"lockAtUtc":null,"lockRemainingSeconds":null,"filledCount":0,"requiredCount":1,"slotRequirements":[{"slot":"lunch","slotNameAr":"الغداء","slotNameEn":"Lunch","count":1}],"failureReasonCode":null,"nutritionTotals":{"calories":0,"proteinG":0,"carbsG":0,"fatG":0,"proteinPercentage":0,"carbsPercentage":0,"fatPercentage":0,"caloriesPercentage":0},"selectedMeals":[],"restaurantRating":null,"restaurantRatingCount":0,"deliveryWindowStart":null,"deliveryWindowEnd":null,"deliveryTimeText":null,"tier":null,"tierNameAr":null,"tierNameEn":null,"programNameAr":null,"programNameEn":null,"editBlockReason":null,"deliveryStatus":null,"isDeliveryTrackable":false}`. هذا نموذج شكلي، لا بيانات حقيقية؛ status enum قد يتسلسل كسلسلة.

## التنقل والقيود

- `restaurantId:null` مع `canEdit:true`: [اختيار مطعم](./08-restaurants.md).
- مطعم موجود واختيارات ناقصة: [الوجبات](./09-meals.md).
- `filledCount >= requiredCount`: [مراجعة](./10-review-confirm.md).
- `canEdit:false` أو `editBlockReason`: عرض فقط. الأسباب المعروفة `selection_deadline_passed`, `day_locked`, `day_frozen`, `day_cancelled`. cutoff dynamic من الخادم وليس 72h ثابتة.
- `isFulfillmentReady` في calendar DTO؛ عند عدم اكتمال `FulfillmentCompletedAtUtc` يرفض GET بـ `409 calendar.fulfillment_not_ready` ولا يعيد DTO نجاحاً؛ قيمة false defensive فقط. `409 subscription.service_unavailable` حالة منفصلة عندما subscription ليس Active/Scheduled/Frozen. لا تنشئ أيام placeholder.

بعد mutation اعتمد رد API ثم أبطل cache اليوم والتقويم. `calendar.box_incomplete` 400 عند complete ناقص؛ `calendar.allergy_conflict` 400 عند اختيار وجبة متعارضة؛ `calendar.box_not_ready` 409 عند snapshot مطعم غير جاهز؛ `calendar.restaurant_not_in_pool` 400؛ `calendar.quota_exhausted` و`calendar.restaurant_busy` 409. access errors: `family.permission_required`, `family.access_paused`, `family.access_disabled`, `family.action_not_available` (403؛ `family.access_paused` و`family.access_disabled` يرفضان فعل العضو المربوط فقط؛ المدير يستطيع إدارة العضو المستهدف الموقوف) 403. أعد GET بعد تعارض state. Loading/empty/error/retry حالات منفصلة.

**قبول:** slot counts من الخادم؛ canEdit يمنع التنقل للكتابة؛ stale response لا يبدل العضو/اليوم؛ أسماء تتفاعل مع اللغة.

## اللغة والتنسيق

كل copy في قاموس عربي/إنجليزي reactive؛ `ar → en → ar` دون reload وRTL/LTR. أرسل `Accept-Language`. API قد يرسل الاسم المحلي فقط مع الاسم unsuffixed، لا تشترط حقلي ar/en. Asia/Kuwait للتاريخ وKWD بثلاث منازل عشرية.
