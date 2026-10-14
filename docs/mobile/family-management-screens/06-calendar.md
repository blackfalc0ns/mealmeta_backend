# تقويم الفرد (08.04)

**السابق:** [تفاصيل الفرد](./03-member-detail.md) · **التالي:** [تفاصيل اليوم](./07-day-details.md)

## الطلب والرد

`GET /api/v1/customer/subscriptions/{subscriptionId}/calendar`. استخدم current subscription ID إن وجد وإلا upcoming ID؛ إذا لا يوجد أيهما فلا يوجد مسار تقويم. `subscriptionId` ليس `memberId`.

رد `CustomerCalendarScreenDto`: `subscriptionId`, `programId`, `bundleId`, `requiredCount`, `slotRequirements[]`, `isFulfillmentReady`, `days[]`, `hasUnreadNotifications`, `unreadNotificationsCount`. اليوم: `id`, `serviceDate`, مطعم، `status` enum، `uiStatus`, `isAutoSelected`, `canEdit`, `selectionDeadlineUtc`, `lockAtUtc`, `lockRemainingSeconds`, `filledCount`, `requiredCount`, `slotRequirements[]`, `failureReasonCode`, `selectedMeals[]`, بيانات التسليم/الوقت والبرنامج إن توفرت. عنصر `selectedMeals[]` في ملخص التقويم فقط: `mealId`, `nameAr`, `nameEn`, `slot`, `slotNameAr`, `slotNameEn`, `sortOrder`, `imageUrl`, `isSelected`. هذا الملخص لا يحتوي macros أو allergen fields؛ اقرأ [تفاصيل اليوم](./07-day-details.md) ووجباته الكاملة لهذه البيانات.

```json
{"subscriptionId":"11111111-1111-4111-8111-111111111111","programId":null,"bundleId":null,"requiredCount":0,"slotRequirements":[],"isFulfillmentReady":true,"days":[],"hasUnreadNotifications":false,"unreadNotificationsCount":0}
```

المثال اصطناعي. `status` enum قد يصل كسلسلة، لا numeric-only. `uiStatus`: `empty`, `drafting`, `awaiting_restaurant`, `auto_selected`, `locked`, `frozen`, `cancelled`, `replacement_required`. تبقى قيم API مفاتيح حالة؛ labels من localization resources.

## حالات وتحديث

`isFulfillmentReady` موجود في DTO، لكن عندما `FulfillmentCompletedAtUtc` null فالـ GET الحالي يرفض قبل إرجاع الشاشة بـ `409 calendar.fulfillment_not_ready`; لذلك `false` دفاعي ولا يُتوقع كاستجابة نجاح من هذا handler. اظهر Preparing بحسب lifecycle، أعد GET لاحقاً ولا تعرض تقويماً تجريبياً. `409 subscription.service_unavailable` حالة منفصلة لاشتراك الأسرة الذي ليس Active/Scheduled/Frozen. الخدمة upcoming تعرض التواريخ المستقبلية؛ لا تحولها لحالة اليوم الحالي.

اليوم read-only عندما `canEdit:false`; reason من `editBlockReason`/deadline/status. لا تثبت 72 ساعة: deadline متغير من الخادم. الانتقال لليوم يستخدم تاريخ `serviceDate` بصيغة yyyy-MM-dd. Loading skeleton، `days:[]` empty، retry على فشل. عند تغيير العضو/الشهر تجاهل الردود stale. بعد restaurant/meal/confirm/complete أبطل cache يوم وتقويم وأعد GET.

أخطاء: `calendar.fulfillment_not_ready` 409 لعدم اكتمال تجهيز التقويم؛ `subscription.service_unavailable` 409 منفصل عندما تكون حالة اشتراك الخدمة غير تشغيلية؛ خطأ وصول `family.permission_required`, `family.access_paused`, `family.access_disabled`, `family.action_not_available` (403؛ `family.access_paused` و`family.access_disabled` يرفضان فعل العضو المربوط فقط؛ المدير يستطيع إدارة العضو المستهدف الموقوف) 403؛ عضو غير موجود `family.member_not_found` 404. إذا قفل اليوم/انتهت المهلة اعرض error code وGET جديد. **قبول:** UUID اشتراك صحيح، قادم لا يعرض تجديد/جدول اليوم، ماكروز null لا تعرض كصفر، response stale لا يبدل الشهر أو الفرد المختار.

## اللغة والتنسيق

كل copy جديد في قاموس عربي/إنجليزي reactive؛ اختبر `ar → en → ar` دون reload مع RTL/LTR. أرسل `Accept-Language`. قد يعيد BilingualJsonModifier الحقل الموافق للغة وحقل الاسم unsuffixed بدلاً من الحقلين `nameAr` و`nameEn`؛ لا تشترط وجودهما معاً. اختر اللغة النشطة ثم البديل غير الفارغ ثم label مترجم. التاريخ التشغيلي Asia/Kuwait؛ KWD بثلاث منازل عشرية.




