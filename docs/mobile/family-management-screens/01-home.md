# الرئيسية — إدارة الأسرة (08.01)

**السابق:** مدخل إدارة الأسرة · **التالي:** [الأفراد](./02-members.md)، [تفاصيل الفرد](./03-member-detail.md)، أو فلو الإضافة الموضح في [00](./00-flow-and-contract.md).

## API وحقول UI

`GET /api/v1/customer/family/home`.

| العرض | حقول response |
|---|---|
| اسم الأسرة | `groupId`, `name`, `status` |
| اسم/سعر الباقة | `packageLabelAr`, `packageLabelEn`, `quotedFinalPrice`, `currencyCode` |
| فرد في المجموعة | `members[]`: `memberId`, `displayName`, `role`, `isPaused`, `memberSubscriptionId`, `programLabelAr/En`, `todayUiStatus`, `todayFilledCount`, `todayRequiredCount`, `remainingDays`, `lifecycle`, `upcomingSubscriptionId`, `upcomingStartDate` |
| سطر جدول اليوم | `todaySchedule[]` من نفس نوع الفرد؛ قد يكون فارغاً |
| اختصار | `shortcuts[]`: `code`, `enabled`, `unavailableReason` |
| wallet | `wallet.available`, `unavailableReason`؛ لا يوجد balance |

قيمة status enum لا تفترض أنها رقم: البيئة قد تسلسل `FamilyGroupStatus` كسلسلة مثل `Active`. اقرأ `status` كما ورد. `todayUiStatus` نص حالة الخدمة/اليوم لا ترجمه كقيمة API. أسماء البرامج الثنائية قد تُرسل localized field إضافياً بدل الحقلين، حسب `Accept-Language`.

مثال response جزئي صالح شكلياً للاختصارات والمجموعة (قيم اصطناعية):

```json
{"groupId":"11111111-1111-4111-8111-111111111111","name":"مجموعة توضيحية","status":"Active","packageLabelAr":"باقة توضيحية","packageLabelEn":"Illustrative package","quotedFinalPrice":37.500,"currencyCode":"KWD","members":[],"todaySchedule":[],"shortcuts":[{"code":"calendar","enabled":true,"unavailableReason":null},{"code":"wallet","enabled":false,"unavailableReason":"wallet_not_implemented"}],"wallet":{"available":false,"unavailableReason":"wallet_not_implemented"}}
```

## الحالات والسلوك

Loading skeleton؛ المجموعة/الأعضاء الفارغة تبقى فارغة مع زر بدء التدفق؛ خطأ تحميل يظهر retry. اختصارات تعرض فقط عند enabled. اخفِ wallet؛ لا تعرض رصيداً. عضو `isPaused` يأخذ شارة من قاموس locale. إذا لديه `upcomingSubscriptionId` و`memberSubscriptionId:null` اعرض الخدمة القادمة وتاريخها من lifecycle، ولا تصفه بغير المشترك. `todaySchedule` لا يختلق بطاقات يوم للأعضاء الذين لا يملكون سجل يوم.

اختصار التقويم يختار memberId أولاً ثم effective subscription ID (current ثم upcoming)؛ لا تستخدم memberId في URL التقويم. إضافة الفرد تدخل flow مستقل. `POST /api/v1/customer/family/members` لا يضيف خدمة ويعيد `409 family.use_checkout_flow`؛ المسار الصحيح موضح في [00](./00-flow-and-contract.md).

بعد أي إضافة، تغيير وصول، checkout أو رجوع للشاشة أعد GET وأبطل cache؛ لا تعرض response قديم كحديث. خطأ `family.not_manager` 403 يمنع وصول غير المدير. `auth.forbidden` 403 للـ user type غير customer؛ Unauthorized 401 يعالج انتهاء الجلسة. أخطاء الشبكة تعرض retry ولا تمسح المحتوى السابق من دون إشارة قدم البيانات.

**قبول:** مبالغ KWD بدقة 3 decimals؛ names reactive مع اللغة؛ empty تبقى empty؛ upcoming لا يختفي ولا يصير «غير مشترك»؛ wallet لا يعرض رصيداً؛ لا next-day افتراضي.

## اللغة والتنسيق

كل copy جديد في قاموس عربي/إنجليزي reactive؛ اختبر `ar → en → ar` دون reload مع RTL/LTR. أرسل `Accept-Language`. قد يعيد BilingualJsonModifier الحقل الموافق للغة وحقل الاسم unsuffixed بدلاً من الحقلين `nameAr` و`nameEn`؛ لا تشترط وجودهما معاً. اختر اللغة النشطة ثم البديل غير الفارغ ثم label مترجم. التاريخ التشغيلي Asia/Kuwait؛ KWD بثلاث منازل عشرية.


