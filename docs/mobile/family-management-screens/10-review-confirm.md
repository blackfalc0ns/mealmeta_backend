# 10 — مراجعة وتأكيد اليوم (08.10)

**السابق:** [الوجبات](./09-meals.md) · **التالي:** [التقويم](./06-calendar.md) أو [اليوم](./07-day-details.md)

## قراءة بيانات المراجعة

`GET /api/v1/customer/subscriptions/{subscriptionId}/days/{date}`. اربط `selectedMeals`, `slotRequirements`, `filledCount`, `requiredCount`, `nutritionTotals`, `canEdit`, `uiStatus`; address لا يقرأ من ملف المستخدم العام.

`GET /api/v1/customer/subscriptions/{subscriptionId}/delivery-address` يعيد `subscriptionId`, `currentVersion`, `acceptedAddress`, `eligibleAddresses[]`. `acceptedAddress` هو snapshot المقبول للخدمة، ويحوي `addressId`, `regionId`, `label`, `addressLine`, `governorate`, `area`, `block`, `blockRegionId`. لا تستخدم عنوان profile المتغير على أنه عنوان يوم خدمة مدفوع. في حالة address amendment تكون كتابة العنوان عملية منفصلة مع `addressId`, `effectiveFromDate`, `expectedVersion`؛ شاشة المراجعة تعرض snapshot المقبول ولا تعدل العنوان.

مثال response مختصر اصطناعي: `{"subscriptionId":"11111111-1111-4111-8111-111111111111","currentVersion":2,"acceptedAddress":{"addressId":"22222222-2222-4222-8222-222222222222","regionId":"33333333-3333-4333-8333-333333333333","label":"عنوان توضيحي","addressLine":"عنوان توضيحي","governorate":"محافظة","area":"منطقة","block":"1","blockRegionId":null},"eligibleAddresses":[]}`.

## تأكيد العميل والصندوق

`confirm` لا يستقبل body؛ `complete` يستقبل `CompleteDayBoxRequest?` اختيارياً، وداخله `idempotencyKey` nullable. لا تفرض المفتاح ولا تضمن ديمومته كهوية للمحرك. فلو المراجعة: بعد تحقق `filledCount >= requiredCount` أرسل `POST /api/v1/customer/subscriptions/{subscriptionId}/days/{date}/complete` بجسم `{}`؛ ثم بعد نجاحه أرسل `POST /api/v1/customer/subscriptions/{subscriptionId}/days/{date}/confirm` بلا body. confirm يسجل إقرار العميل ويرسل للمطعم حسب cutoff وسياسة الخادم. لا تدمج endpointين.

الرد في كل منهما `CustomerCalendarDayDto`؛ اعتمد آخر response ثم أبطل cache اليوم/التقويم وأعد GET لكليهما. complete قبل اكتمال الصندوق يعطي `calendar.box_incomplete` (400). وجبة فيها تعارض حساسية `calendar.allergy_conflict` (400). صندوق مطعم غير جاهز `calendar.box_not_ready` (409). عدم الجاهزية الكلية `calendar.fulfillment_not_ready` (409)، حالة اشتراك غير تشغيلي `subscription.service_unavailable` (409). أعد GET بعد 409 ولا تعرض النجاح optimistically.

`canEdit:false`, cutoff، frozen، cancelled أو locked تعني عرض فقط؛ cutoff حسب الخادم وليس 72 ساعة ثابتة. أذونات الوصول `family.permission_required`, `family.access_paused`, `family.access_disabled`, `family.action_not_available` (403؛ `family.access_paused` و`family.access_disabled` يرفضان فعل العضو المربوط فقط؛ المدير يستطيع إدارة العضو المستهدف الموقوف). راعِ `confirmMeals`؛ الخادم حاكم الصلاحية.

**قبول:** لا تأكيد لصندوق ناقص؛ العنوان من snapshot المقبول؛ complete ثم confirm؛ لا duplicate UI request؛ stale result لا يبدل اليوم؛ retry يعتمد GET حديث.

## اللغة والتنسيق

كل copy في قاموس عربي/إنجليزي reactive؛ اختبر `ar → en → ar` دون reload وRTL/LTR. أرسل `Accept-Language`; قد يعيد serializer النص المحلي فقط مع الحقل unsuffixed. لا تشترط حقلي ar/en في كل response. Asia/Kuwait وKWD ثلاث منازل عشرية.
