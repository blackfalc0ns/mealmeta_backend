# الأفراد (08.02)

**السابق:** [الرئيسية](./01-home.md) · **التالي:** [تفاصيل الفرد](./03-member-detail.md) أو [تعديل الفرد](./11-member-edit.md)

## API والحقول

المصدر الحالي للإصدار `83fc8d0` لا يعيد حقول العرض الجديدة أدناه. تنفيذها مرشح موجود في checkout الإصدار المعزول، ولم يُنشر بعد. بعد تضمينه، يضيف `GET /api/v1/customer/family/members` إلى `FamilyMemberListItemDto` الحقول الاختيارية: `profileImageUrl`, `hasSeparateAccount`, `planSummary`, `todayMealSelection`. تبقى حقول العضو والخدمة الحالية/القادمة و`lifecycle` الحالية كما هي.

```json
{
  "memberId": "11111111-1111-4111-8111-111111111111",
  "displayName": "عضو توضيحي",
  "role": "Member",
  "ageYears": 12,
  "isPaused": false,
  "memberSubscriptionId": "22222222-2222-4222-8222-222222222222",
  "programNameAr": "برنامج توضيحي",
  "programNameEn": "Illustrative program",
  "remainingDays": 22,
  "setupStatus": "Ready",
  "lifecycle": { "setupStatus": "Ready", "checkoutSelected": true, "paymentStatus": "Paid", "fulfillmentStatus": "Completed", "serviceStatus": "Active", "accessStatus": "Managed", "displayStatus": "Active" },
  "upcomingSubscriptionId": null,
  "upcomingStartDate": null,
  "profileImageUrl": "/api/v1/uploads/content?key=uploads%2Fcustomers%2Fprofile%2Fprofile.png",
  "hasSeparateAccount": false,
  "planSummary": { "mealsPerDay": 3, "durationDays": 26, "startDate": "2026-08-01", "endDate": "2026-09-30", "nextRenewalDate": "2026-10-01", "totalServiceDays": 26, "usedServiceDays": 4, "usagePercentage": 15.38 },
  "todayMealSelection": { "serviceDate": "2026-08-25", "uiStatus": "CustomerSelected", "filledCount": 3, "requiredCount": 3, "isAutoSelected": false }
}
```

| العرض | الحقل والمصدر | قاعدة العرض |
|---|---|---|
| الصورة الشخصية | `profileImageUrl` من `CustomerProfiles.ProfileImageStorageKey` للمدير أو للحساب المرتبط بهذا الفرد | `null` عند غياب الصورة أو الحساب المرتبط؛ لا تستخدم صورة المدير للفرد غير المرتبط. عند null يمكن للواجهة عرض أحرف الاسم الأولى |
| البرنامج وعدد الوجبات | `programNameAr/En` و`planSummary.mealsPerDay` | عدد الوجبات هو مجموع فتحات الوجبات الفعلية للباقة (فطور + غداء + عشاء + وجبات خفيفة). أبقِ التسمية ثنائية اللغة حسب اللغة النشطة |
| مدة الخطة | `planSummary.durationDays` | المدة المحفوظة للخدمة الممولة؛ قد تختلف عن عدد صفوف التقويم الفعلية |
| التواريخ والتجديد | `startDate`, `endDate`, `nextRenewalDate` | تاريخ التجديد هو اليوم التالي لنهاية الخدمة وفق اصطلاح التطبيق، ويعني أقرب تاريخ ممكن فقط؛ لا يثبت وجود تجديد أو خصم مجدول |
| تقدم الاستخدام | `usedServiceDays`, `totalServiceDays`, `usagePercentage` | الأيام المستخدمة هي أيام `Delivered` الفعلية؛ الإجمالي صفوف أيام الخدمة الفعلية باستثناء `Cancelled`. النسبة قيمة بين 0 و100؛ اقسمها على 100 عند تمريرها إلى مكوّن Flutter الذي يتوقع 0 إلى 1. عند وجود صفوف تقويم كلها ملغاة تكون النسبة 0؛ عند عدم وجود صفوف أيام تقويم فعلية تبقى مقاييس التقدم `null` |
| شارة دورة الحياة | `lifecycle` والحقول الحالية `isPaused`, `hasSeparateAccount` | اعرض حالة الحساب المستقل منفصلة عن الإيقاف؛ الإيقاف لا يغير حالة دورة الخدمة |
| شارة وجبات اليوم | `todayMealSelection` | غياب اليوم الفعلي يعني إخفاء الشارة. كوّن النص من `filledCount/requiredCount` و`uiStatus` و`isAutoSelected`: العدد الناقص مثل `2/3` يوضح الوجبات الناقصة، والحالة الفعلية مع `isAutoSelected=true` توضح الاختيار التلقائي. لا تستنتج تأكيداً مستقلاً غير موجود في العقد |

المسارات الكاملة في JSON هي `planSummary.mealsPerDay`, `planSummary.durationDays`, `planSummary.startDate`, `planSummary.endDate`, `planSummary.nextRenewalDate`, `planSummary.usedServiceDays`, `planSummary.totalServiceDays`, `planSummary.usagePercentage` و`todayMealSelection.serviceDate`, `todayMealSelection.uiStatus`, `todayMealSelection.filledCount`, `todayMealSelection.requiredCount`, `todayMealSelection.isAutoSelected`. تاريخ `nextRenewalDate` اليوم التالي لـ`endDate` حسب اصطلاح النظام.

صياغة الشارات من قاموس الواجهة reactive في اللغتين؛ أمثلة دلالية: `2/3` → «لم يكتمل اختيار الوجبات» / “Meal selection incomplete”، و`isAutoSelected=true` → «اختيار تلقائي» / “Automatically selected”، و`hasSeparateAccount=true` → «لديه حساب مستقل» / “Separate account”، و`isPaused=true` → «متوقف مؤقتاً» / “Paused”. عند `mealsPerDay:null` لا تعرض رقماً بديلاً من القيمة صفر. لا تخلط paused مع lifecycle status أو تعطيل الحساب.

`todayMealSelection.uiStatus` هو الاسم الخام لقيمة `SubscriptionDayStatus` مثل `CustomerSelected`, `AutoSelected`, `Open`, `ReplacementRequired`؛ ليس نصاً محلياً ولا صيغة التقويم بحروف صغيرة. حوّله في قاموس الواجهة إلى معنى عربي/إنجليزي عند الحاجة، واستخدم `isAutoSelected` كإشارة مستقلة لشارة الاختيار التلقائي.

**التحقق من مرشح التنفيذ:** اجتازت حزمة اختبارات الوحدة الأوسع 238/238 في checkout الإصدار، واختبارات `FamilyManagerHandlerTests` المركزة 35/35، واختبار PostgreSQL HTTP عبر Testcontainers 1/1 مع assertions لحمولة الحقول الجديدة. أظهر اختبار الحساسية أن حذف حقول الإسقاط يفشل الاختبار لأن صورة العضو المتوقعة تصبح `null`؛ أُعيد الإسقاط ثم اجتاز الاختبار المركّز. هذه النتائج تخص checkout المرشح، ولا تعني أن API المنشور يعيد الحقول حتى يُنشر الإصدار.

ملخص الخطة يختار الخدمة الحالية الممولة أولاً ثم القادمة الممولة. إذا لم توجد خدمة ممولة وكان للفرد اختيار خطة محفوظ، قد يعرض عدد الوجبات والمدة وتاريخ البداية المحفوظ فقط؛ تبقى النهاية والتجديد ومقاييس الاستخدام `null`. إذا لم توجد خطة أو خدمة، يكون `planSummary:null`. الخدمة القادمة تعرض إجمالي الأيام الفعلية المتاحة واستخداماً صفرياً إن كان تقويمها موجوداً، ولا تُنشئ يوماً لليوم الحالي. كل هذه الإضافات قراءات فقط ولا تغيّر سياسة الصلاحيات.

مثال مختصر اصطناعي: `[ {"memberId":"11111111-1111-4111-8111-111111111111","displayName":"عضو توضيحي","role":"Member","ageYears":12,"isPaused":false,"memberSubscriptionId":null,"programNameAr":"برنامج توضيحي","programNameEn":"Illustrative program","remainingDays":26,"setupStatus":"Ready","lifecycle":{"setupStatus":"Ready","checkoutSelected":true,"paymentStatus":"Paid","fulfillmentStatus":"Pending","serviceStatus":"Scheduled","accessStatus":"Enabled","displayStatus":"PaidPreparingCalendar"},"upcomingSubscriptionId":"22222222-2222-4222-8222-222222222222","upcomingStartDate":"2026-11-01"} ]`.

`setupStatus` و`role` enum: serializer قد يرسل الاسم النصي؛ لا تفترض التمثيل الرقمي. `Lifecycle` يفصل الإعداد والاختيار والدفع والتهيئة والخدمة والوصول والعرض. الاسم قد يعود كحقل localized مفرد؛ أرسل `Accept-Language` واسم بديل عند توفره.

## التفاعل والحالات

بحث محلي في القائمة الموجودة حسب displayName؛ لا يوجد query بحث. loading skeleton، `[]` empty حقيقي، والخطأ يتيح retry. الخادم يرتب المدير أولاً ثم الاسم؛ حافظ على role كما وصل. `memberSubscriptionId:null` مع upcoming موجود يعني خدمة قادمة؛ اعرض التاريخ والحالة ولا تكتب «غير مشترك». عند كلاهما null اعرض حالة إعداد/عدم تهيئة حسب lifecycle، لا تتنبأ من setupStatus وحده.

إضافة الفرد تبدأ `POST /api/v1/customer/family-subscription-flow/members` ثم الخطة والتفضيلات والعنوان والاختيار والملخص والدفع. `POST /api/v1/customer/family/members` لا يضيف عضواً؛ يرجع 409 `family.use_checkout_flow`. لا تعتمد مسارات UI قديمة تحت `../family-screens`.

أعد GET بعد إضافة/تعديل/إزالة أو checkout؛ أبطل cache السابق. `family.not_manager` 403؛ `family.use_checkout_flow` 409 عند المسار غير المناسب؛ 401 للجلسة غير المصادق عليها. لا تعرض المصفوفة القديمة كحديثة إذا فشل refresh بعد mutation.

**قبول:** upcoming لا يخفي العضو ولا يصنفه بلا اشتراك؛ search محلي لا يغير API؛ empty تبقى empty؛ after mutation يتم refresh؛ لا يوجد DOB.

## اللغة والتنسيق

كل copy جديد في قاموس عربي/إنجليزي reactive؛ اختبر `ar → en → ar` دون reload مع RTL/LTR. أرسل `Accept-Language`. قد يعيد BilingualJsonModifier الحقل الموافق للغة وحقل الاسم unsuffixed بدلاً من الحقلين `nameAr` و`nameEn`؛ لا تشترط وجودهما معاً. اختر اللغة النشطة ثم البديل غير الفارغ ثم label مترجم. التاريخ التشغيلي Asia/Kuwait؛ KWD بثلاث منازل عشرية.

