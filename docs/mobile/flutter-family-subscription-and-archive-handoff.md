# تسليم تعديلات Flutter — اشتراك العائلة والأرشيف

**الجمهور:** مطور تطبيق العميل Flutter  
**الغرض:** تنفيذ تغييرات الموبايل اللازمة للتوافق مع إصلاحات اشتراك العائلة والأرشيف في الـBackend.  
**Base URL:** `https://maelmate.runasp.net/api/v1`  
**مهم:** اختبر التغييرات على بيئة Backend التي تحتوي الإصلاحات. لا تستخدم IDs ثابتة أو IDs من بيئة أخرى.

المراجع التفصيلية: [عقد فلو العائلة](./family-subscription-api.md)، [دليل الفلو الكامل](./family-subscription-mobile-flow-guide.md)، [عقد الأرشيف](./subscription-archive-kind-guide.md).

## نطاق التنفيذ

1. تصحيح اختيار باقة حجم العائلة وإرسال معرّفها من الـAPI.
2. عرض البرامج وباقات الوجبات في فلو العائلة من نفس الكتالوج الظاهر للفرد، مع الحفاظ على اختيارات كل فرد وأهليته.
3. فصل شاشة الأرشيف عن شاشة الاشتراكات غير المكتملة.
4. إظهار نوع كل اشتراك في الأرشيف: فردي أو عائلي، وعدم تكرار أعضاء دفعة العائلة كاشتراكات فردية.

## 1. باقات حجم العائلة

### المطلوب

- عند فتح إعداد العائلة، حمّل الباقات النشطة من:

```http
GET /customer/family-subscription-flow/packages
```

- استخدم `id` كما ورد من نفس استجابة البيئة في طلب حفظ/تحديث المجموعة:

```http
PUT /customer/family-subscription-flow/group
Content-Type: application/json

{
  "name": "عائلة أحمد",
  "familyPackageId": "<id من GET /packages>"
}
```

- اعرض الاسم، `minMembers`، `maxMembers`، `discountPercent`، والصورة إن توفرت.
- اعرض الاختيار المحفوظ من `selectedFamilyPackageId` عند تحميل `GET /current` أو استجابة المجموعة. لا تعرض باقة بديلة على أنها المختارة إذا كان المعرّف المحفوظ لم يعد متاحًا.
- قبل checkout، اعرض `minMembers` و`maxMembers` من الباقة المختارة، لكن اعتمد على `GET /summary` و`canCheckout` كمرجع نهائي للأهلية والسعر.

### معالجة `family.package_not_found`

إذا رجع هذا الخطأ عند حفظ المجموعة:

1. أعد طلب `GET /packages` مرة واحدة.
2. حدّث القائمة بالنتيجة الجديدة.
3. اطلب من المستخدم اختيار باقة نشطة وأرسل `familyPackageId` الجديد.
4. لا تعِد إرسال UUID ثابت أو اختيارًا قديمًا تلقائيًا.

إذا لم توجد باقات نشطة، اعرض حالة واضحة وامنع الانتقال للدفع حتى تصبح باقة متاحة.

### الدفع قيد التنفيذ

إذا رجع `family.not_draft` عند تعديل المجموعة، اعرض أن بيانات العائلة مقفولة أثناء وجود checkout معلّق، وحدّث الشاشة من `GET /current`. لا تكرر التعديل أو checkout تلقائيًا.

## 2. البرامج وباقات الوجبات

يجب أن تعرض شاشة العائلة البرامج وباقات الوجبات نفسها المتاحة للفرد، بالمعرّفات والمحتوى نفسيهما لنفس سياق العميل. حمّلها من مسارات العائلة:

```http
GET /customer/family-subscription-flow/programs
GET /customer/family-subscription-flow/programs/{programId}/bundles
```

ثم استخدم مسارات المدد والتصنيفات والمطاعم المشتركة:

```http
GET /customer/subscription-flow/programs/{programId}/bundles/{bundleId}/durations?includeFriday=false
GET /customer/subscription-flow/programs/{programId}/bundles/{bundleId}/tiers?durationDays=26&includeFriday=false
GET /customer/subscription-flow/programs/{programId}/bundles/{bundleId}/tiers/{tier}/restaurants
```

**تغيير عن السلوك القديم:** لا تخفِ برنامجًا أو باقة وجبات من شاشة العائلة اعتمادًا على `availableForFamily` وحده. الـBackend يوحّد ظهور الكتالوج للفرد والعائلة، لكنه يظل يتحقق من صلاحية الخطة والعنوان والتغطية لكل فرد عند اختيار الخطة والحفظ. استخدم `familyMemberId` عند طلب معلومات خاصة بفرد.

احتفظ بخطة مستقلة لكل `memberId`، واحفظ الاختيارات على:

```http
PUT /customer/family-subscription-flow/members/{memberId}/plan
```

لا تعتبر ظهور البرنامج في القائمة ضمانًا بأن كل مطعم أو عنوان مناسب لكل فرد؛ اعرض خطأ الأهلية الخاص بالفرد ووجّهه لتصحيح خطته/عنوانه.

## 3. فصل الأرشيف عن الاشتراكات غير المكتملة

استدعاء كل شاشة من endpoint مستقل:

| الشاشة | endpoint | المقصود |
|---|---|---|
| الأرشيف | `GET /customer/subscriptions/archived` | اشتراكات تاريخية بحالات `Completed`, `Expired`, `Cancelled`, `Upgraded` |
| غير مكتمل / متابعة | `GET /customer/subscriptions/uncompleted` | أعمال مفتوحة قابلة للمتابعة، مثل المسودات والدفع المعلّق أو الفاشل حسب النوع |

- نتيجة `[]` من `/uncompleted` تعني عدم وجود عمل مفتوح للمتابعة فقط؛ لا تعرضها على أنها أرشيف فارغ ولا تستخدم endpoint هذا لملء شاشة الأرشيف.
- لا تخلط نتائج المسارين في قائمة واحدة قبل العرض.
- استخدم الحالات الفعلية التي يعيدها الخادم لإظهار الحالة، ولا تستنتج اكتمال الاشتراك من كون القائمة غير مكتملة فارغة.

## 4. تمييز الفردي والعائلي في الأرشيف

اقرأ `kind` من كل عنصر في `/archived`:

- `INDIVIDUAL`: بطاقة اشتراك فردي، ويكون `familyGroupId` عادةً `null`.
- `FAMILY`: بطاقة عائلية، حتى لو لم يتوفر اسم المجموعة التاريخي. استخدم `familyName` عند وجوده، وإلا اعرض تسمية عامة مثل «اشتراك عائلي».
- اعرض `memberCount` للعائلة و`paidAmount` كإجمالي الدفعة التاريخية.
- أضف فلاتر محلية: الكل، فردي، عائلي.
- لا تحوّل سجل عائلي إلى فردي عند غياب `familyName` أو بيانات المجموعة.
- قد تظهر للعائلة أكثر من بطاقة تاريخية إذا كانت هناك دفعات/إضافات ناجحة في أوقات مختلفة. اعرض كل عنصر كما أعاده endpoint؛ لا تدمج العناصر حسب `familyGroupId` أو التاريخ.
- سجلات العائلة في `/archived` مجمعة من الخادم لكل دفعة. لا تعرض اشتراكات أعضاء الدفعة نفسها كبطاقات فردية إضافية.
- لا تستخدم بيانات المجموعة الحالية لإعادة بناء تفاصيل قديمة؛ بيانات الاسم/العدد/المبلغ في عنصر الأرشيف تخص الدفعة التاريخية.

الحقول الأساسية المتوقعة للبطاقة:

```text
kind, subscriptionId, status, statusNameAr,
programNameAr/programNameEn, bundleNameAr/bundleNameEn,
startDate, endDate, paidAmount, currencyCode,
familyGroupId, familyName, memberCount
```

قد تكون بعض الحقول nullable. يجب أن يتعامل الـDTO والواجهة مع `null` من دون crash. `subscriptionId` هو معرّف سجل الاشتراك الذي يمثّل العنصر؛ استخدم أي شاشة تفاصيل فقط إذا كانت تدعم نوع `FAMILY` فعليًا.

## 5. نماذج Dart المقترحة

```dart
enum SubscriptionKind { individual, family, unknown }

SubscriptionKind parseSubscriptionKind(String? value) {
  switch (value?.toUpperCase()) {
    case 'INDIVIDUAL':
      return SubscriptionKind.individual;
    case 'FAMILY':
      return SubscriptionKind.family;
    default:
      return SubscriptionKind.unknown;
  }
}

class ArchivedSubscription {
  final String subscriptionId;
  final String? status;
  final String? statusNameAr;
  final String? programNameAr;
  final String? bundleNameAr;
  final num? paidAmount;
  final String? currencyCode;
  final String? startDate;
  final String? endDate;
  final SubscriptionKind kind;
  final String? familyGroupId;
  final String? familyName;
  final int? memberCount;

  const ArchivedSubscription({
    required this.subscriptionId,
    required this.kind,
    this.status,
    this.statusNameAr,
    this.programNameAr,
    this.bundleNameAr,
    this.paidAmount,
    this.currencyCode,
    this.startDate,
    this.endDate,
    this.familyGroupId,
    this.familyName,
    this.memberCount,
  });
}
```

اعتبر `unknown` قيمة احتياطية للتوافق مع استجابة غير متوقعة، وسجّلها للمراجعة. لا تفسّرها تلقائيًا على أنها عائلية أو فردية.

## 6. حالات الأخطاء التي يجب تغطيتها

| `code` | سلوك Flutter |
|---|---|
| `family.package_not_found` | تحديث `/packages` وطلب اختيار باقة نشطة مجددًا |
| `family.package_required` | لا توجد باقة متاحة؛ إبقاء checkout مغلقًا وإظهار رسالة مناسبة |
| `family.min_members` | توضيح الحد الأدنى المطلوب حسب الباقة المختارة |
| `family.member_incomplete` | تحديد الفرد غير الجاهز وتوجيه المستخدم لإكمال عنوانه/خطته |
| `family.not_draft` | تحديث المجموعة وإظهار أن التعديل غير متاح أثناء حالة checkout الحالية |
| أي `code` آخر | اعرض رسالة مفهومة، وسجّل `code` للتشخيص؛ لا تعتمد على نص `detail` وحده لاتخاذ قرار برمجي |

## 7. معايير القبول والاختبار

- [ ] الباقة المعروضة والمختارة مصدرها `/packages`، و`familyPackageId` المرسل يطابق `id` من الاستجابة.
- [ ] إذا أصبحت الباقة المختارة غير متاحة، لا يظهر اختيار وهمي؛ يحدث التطبيق القائمة ويطلب اختيار المستخدم.
- [ ] قائمة البرامج وباقات الوجبات للعائلة تطابق قائمة الفرد لنفس الحساب/السياق في IDs والمحتوى.
- [ ] يمكن اختيار خطة مختلفة لكل فرد، وتظل قيود العنوان والتغطية خاصة بكل فرد.
- [ ] الأرشيف يستدعي `/archived` ويعرض سجلات الفرد والعائلة مع بادج/فلتر النوع.
- [ ] دفعة العائلة الواحدة تظهر كبطاقة عائلية واحدة، ولا يظهر أعضاء الدفعة كبطاقات فردية مكررة.
- [ ] دفعتان تاريخيتان للعائلة نفسها تظهران كبطاقتين منفصلتين.
- [ ] غياب `familyName` لا يغيّر `kind: FAMILY` ولا يسبب crash.
- [ ] `/uncompleted` الفارغ يعرض حالة «لا توجد اشتراكات غير مكتملة» فقط، ويبقى الأرشيف مستقلًا.
- [ ] اختبر التحميل، الخطأ، إعادة المحاولة، الاستجابة الفارغة، الحقول nullable، واللغة العربية والإنجليزية.

## ملاحظة الإطلاق

تعديلات Flutter مطلوبة حتى يستخدم التطبيق معنى `/archived` الجديد ويعرض نوع الاشتراك. يجب تنسيق إصدار التطبيق مع نشر الـBackend، واختبار endpoints على البيئة المنشورة قبل تفعيل الفلو للمستخدمين.
