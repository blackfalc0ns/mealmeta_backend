# 08 — مطاعم اليوم (08.07)

**السابق:** [تفاصيل اليوم](./07-day-details.md) · **التالي:** [الوجبات](./09-meals.md)

## APIs والربط

`GET /api/v1/customer/subscriptions/{subscriptionId}/days/{date}/restaurants?query={query}&page=1&pageSize=20`. `query`, `page`, `pageSize` اختيارية في controller؛ response `CustomerDayRestaurantsDto`: `serviceDate`, `limit`, `adminCeiling`, `restaurants[]`, `page`, `pageSize`, `totalCount`, `totalPages`, `hasNextPage`, `hasPreviousPage`. التثبيت: `POST /api/v1/customer/subscriptions/{subscriptionId}/days/{date}/restaurant` بجسم `{"restaurantId":"11111111-1111-4111-8111-111111111111"}`. الرد تفاصيل اليوم المحدّثة.

مثال response مختصر اصطناعي: `{"serviceDate":"2026-10-14","limit":3,"adminCeiling":8,"restaurants":[{"restaurantId":"11111111-1111-4111-8111-111111111111","nameAr":"مطعم توضيحي","nameEn":"Illustrative restaurant","logoUrl":null,"tier":"Basic","usedCount":1,"limit":3,"remainingCount":2,"isBusy":false,"isQuotaExhausted":false,"isEnabled":true,"disabledReason":null,"safeMealCount":5,"rating":null,"ratingCount":0}],"page":1,"pageSize":20,"totalCount":1,"totalPages":1,"hasNextPage":false,"hasPreviousPage":false}`. `adminCeiling` من response ويعرض السقف الإداري كما أرسله الخادم.

اربط البطاقة بـ `restaurantId`, `nameAr/nameEn` أو الحقل النصي localized إن أعاده serializer، `logoUrl`, `tier`, `usedCount`, `limit`, `remainingCount`, `isBusy`, `isQuotaExhausted`, `isEnabled`, `disabledReason`, `safeMealCount`, `rating`, `ratingCount` عند وجودها. `isEnabled:false` أو quota exhausted يمنع الاختيار.

## الحالات والتزامن

Loading ثم قائمة؛ `restaurants:[]` empty state حقيقية مع مسح البحث/retry. Debounce البحث وألغِ أو تجاهل رد query أقدم. قبل الاختيار تحقق `canEdit` وقرار `selectRestaurant`. عطّل النقر المتكرر أثناء POST. بعد النجاح اعتمد رد اليوم ثم أعد GET اليوم/المطاعم عند الحاجة. إذا تغيرت الحصة/الإتاحة أو القفل، لا تثبت اختياراً متفائلاً: ترجم `ProblemDetails.code` وأعد GET القائمة واليوم.

أخطاء وصول: `family.permission_required`, `family.access_paused`, `family.access_disabled`, `family.action_not_available` (403؛ `family.access_paused` و`family.access_disabled` يرفضان فعل العضو المربوط فقط؛ المدير يستطيع إدارة العضو المستهدف الموقوف) (403). `calendar.fulfillment_not_ready` 409 يعني لم تكتمل تهيئة التقويم؛ أعد GET لاحقاً. `subscription.service_unavailable` 409 حالة منفصلة لاشتراك غير تشغيلي. سياسة cutoff ديناميكية من الخدمة، ليست قاعدة 72 ساعة على العميل. الأخطاء الموثقة للاختيار: `calendar.restaurant_not_in_pool` 400، `calendar.quota_exhausted` 409، `calendar.restaurant_busy` 409، `calendar.box_not_ready` 409، و`calendar.allergy_conflict` 400 عند تعارض الحساسية. عرض الحصة لا يضمن نجاح الاختيار إذا تغيرت الحالة بين GET وPOST؛ حدّث القائمة بعد كل conflict.

**قبول:** لا اختيار لمطعم محجوب؛ stale search response لا يستبدل نتيجة أحدث؛ نجاح الاختيار يحدث بيانات اليوم؛ empty لا تُملأ بنتائج sample.

## اللغة والتنسيق

كل copy جديد في قاموس عربي/إنجليزي reactive؛ اختبر `ar → en → ar` دون reload مع RTL/LTR. أرسل `Accept-Language`. قد يعيد BilingualJsonModifier الحقل الموافق للغة وحقل الاسم unsuffixed بدلاً من الحقلين `nameAr` و`nameEn`؛ لا تشترط وجودهما معاً. اختر اللغة النشطة ثم البديل غير الفارغ ثم label مترجم. التاريخ التشغيلي Asia/Kuwait؛ KWD بثلاث منازل عشرية.




