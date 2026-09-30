# دليل فلو اشتراك العائلة للموبايل (Flutter) — شرح كامل بأمثلة

**التاريخ:** 2026-08-20  
**الجمهور:** فريق Customer App / Flutter  
**العملة:** KWD · JSON: **camelCase** · Auth: `Authorization: Bearer <customer JWT>`

هذا الملف هو **المرجع التشغيلي الكامل** للفلو بعد باقات حجم العائلة والصور.  
العقد التقني المختصر: [`family-subscription-api.md`](./family-subscription-api.md)  
**تعديل الموبايل الصغير (شريط أفراد + حفظ جزئي):** [`family-subscription-flutter-partial-plan.md`](./family-subscription-flutter-partial-plan.md)  
Checklist إصلاحات قديمة (goal/activity): [`family-subscription-flutter-changes.md`](./family-subscription-flutter-changes.md)  
**تصنيف العيلة و `familyMemberId` (409 `family.member_required`):** [`family-tier-family-member-id-guide.md`](./family-tier-family-member-id-guide.md)

---

## 0) الفكرة في دقيقة

عميل واحد (**المدير**) يدفع مرة واحدة لعائلة:

1. يختار **باقة حجم** (مثلاً «عائلة صغيرة 2–3» بخصم 8%).
2. يسمّي العائلة ويضيف الأفراد (بدون حساب لكل فرد).
3. **كل فرد** يختار برنامج + باقة وجبات + مدة + تصنيف (Basic / Platinum / Elite) من **كتالوج العائلة** فقط — والموبايل يعرض **شريط كل الأفراد** على شاشات البرنامج والتصنيف والدفع عشان المستخدم يبدّل بينهم.
4. يدفع فاتورة واحدة بخصم الباقة المختارة → النظام يفتح **اشتراك منفصل لكل فرد** تحت نفس `familyGroupId`.

```text
[نوع الاشتراك: FAMILY]
        ↓
[0] اختيار باقة الحجم (صورة + اسم + نطاق أفراد + خصم)
        ↓
[1] اسم العائلة + الأفراد (بيانات + عنوان)
        ↓
[2] البرنامج لكل فرد
        ↓
[3] باقة الوجبات + المدة + الجمعة + التصنيف لكل فرد
        ↓
[4] الملخص والدفع (MyFatoorah)
```

> **مهم:** «باقة الحجم» ≠ «باقة الوجبات».  
> باقة الحجم = عدد الأفراد والخصم.  
> باقة الوجبات = breakfast/lunch/… داخل خطة كل فرد.

---

## 1) Base URLs

```text
# فلو العائلة
/api/v1/customer/family-subscription-flow

# كتالوج البرامج وباقات الوجبات (موحد بين الفردي والعائلي بنفس المعرفات والمحتوى)
/api/v1/customer/family-subscription-flow/programs
/api/v1/customer/family-subscription-flow/programs/{programId}/bundles

# مدد وتصنيفات ومطاعم = نفس مسارات الفردي
# عند الاستعلام عن التصنيف لفرد في العائلة، أرسل familyMemberId لفحص تغطية عنوان الفرد المستقل
/api/v1/customer/subscription-flow/programs/{programId}/bundles/{bundleId}/durations
/api/v1/customer/subscription-flow/programs/{programId}/bundles/{bundleId}/tiers?familyMemberId={memberId}
```

> **ملاحظة:** كتالوج البرامج وباقات الوجبات موحد بالكامل بين الفردي والعائلي؛ نفس الـ IDs والترتيب والمحتوى يُعرض في الحالتين، مع الحفاظ على فحص تغطية التوصيل لكل فرد حسب عنوانه.

---

## 2) باقة الحجم — إيه هي؟

الأدمن يعرّف باقات مسماة من: **الاشتراكات والبرامج → باقات العائلة**.

مثال واقعي بعد الـ seed:

| باقة | أفراد (شامل المدير) | خصم |
| --- | --- | --- |
| عائلة صغيرة / Small family | 2–3 | 8% (`0.08`) |
| عائلة متوسطة / Medium family | 4–5 | 10% (`0.10`) |
| عائلة كبيرة / Large family | 6–8 | 12% (`0.12`) |

### قواعد المنتج

| قاعدة | سلوك الموبايل |
| --- | --- |
| اختيار الباقة أولاً | اعرض بطاقات الباقات (`imageUrl` + أسماء + نطاق + خصم) قبل أو مع إنشاء المسودة |
| Soft min أثناء المسودة | المسودة ممكن تبدأ بالمدير وحده (1 فرد) |
| Hard max دائمًا | ممنوع إضافة فوق `maxMembers` للباقة → `family.max_members` |
| خصم عند الدفع | الخصم يطبّق لو العدد ∈ `[minMembers, maxMembers]` |
| قفل الدفع تحت الحد | لو العدد `< minMembers` → `canCheckout: false` و `family.min_members` |
| تغيير الباقة | مسموح والمجوعة `Draft` فقط؛ لو الأفراد أكتر من max الجديد ارفض أو اطلب حذف أفراد قبل التغيير |

---

## 3) مثال رحلة كاملة — «عائلة أحمد»

### المشهد

- أحمد (المدير) يريد اشتراك لنفسه ولزوجته سارة.
- يختار باقة **عائلة صغيرة** (2–3 أفراد، خصم 8%).
- كل واحد يختار برنامج/باقة/مدة/تصنيف خاص به.
- يدفع مرة واحدة.

### 3.1 دخول الفلو

```http
GET /api/v1/customer/subscription-flow/types
```

اختَر العنصر:

```json
{ "code": "FAMILY", "membersConfiguredAfterPayment": false }
```

### 3.2 تحميل باقات الحجم

يفضّل واحدة من الاثنين (نفس البيانات تقريبًا):

```http
GET /api/v1/customer/family-subscription-flow/packages
```

أو ضمن:

```http
GET /api/v1/customer/family-subscription-flow/metadata
```

مثال response لـ `/packages`:

```json
[
  {
    "id": "11111111-1111-1111-1111-111111111111",
    "nameAr": "عائلة صغيرة",
    "nameEn": "Small family",
    "minMembers": 2,
    "maxMembers": 3,
    "discountPercent": 0.08,
    "sortOrder": 0,
    "imageStorageKey": "uploads/admin/family/abc.webp",
    "imageUrl": "/api/v1/uploads/content?key=uploads%2Fadmin%2Ffamily%2Fabc.webp"
  },
  {
    "id": "22222222-2222-2222-2222-222222222222",
    "nameAr": "عائلة متوسطة",
    "nameEn": "Medium family",
    "minMembers": 4,
    "maxMembers": 5,
    "discountPercent": 0.1,
    "sortOrder": 1,
    "imageStorageKey": null,
    "imageUrl": null
  }
]
```

**UI مقترح لكل كارت:**

- صورة (`imageUrl`) أو placeholder لو `null`
- الاسم حسب لغة التطبيق
- نص النطاق: «من 2 إلى 3 أفراد»
- شارة الخصم: «خصم 8%»
- CTA: اختيار

```dart
// مثال عرض الخصم للمستخدم
String discountLabel(double fraction) =>
    '${(fraction * 100).toStringAsFixed(fraction * 100 % 1 == 0 ? 0 : 1)}%';
```

### 3.3 إنشاء / تحديث المجموعة مع الباقة

```http
PUT /api/v1/customer/family-subscription-flow/group
Content-Type: application/json

{
  "name": "عائلة أحمد",
  "familyPackageId": "11111111-1111-1111-1111-111111111111"
}
```

مثال screen DTO بعد النجاح:

```json
{
  "familyGroupId": "a1000000-0000-0000-0000-000000000001",
  "name": "عائلة أحمد",
  "status": 1,
  "selectedFamilyPackageId": "11111111-1111-1111-1111-111111111111",
  "packageNameAr": "عائلة صغيرة",
  "packageNameEn": "Small family",
  "minMembers": 2,
  "maxMembers": 3,
  "discountPercent": 0.08,
  "currentStep": 1,
  "members": [
    {
      "memberId": "m1000000-0000-0000-0000-000000000001",
      "role": 1,
      "displayName": "أحمد",
      "relationshipCode": "self",
      "gender": 1,
      "ageYears": 32,
      "deliveryAddressId": "d1000000-0000-0000-0000-000000000001",
      "setupStatus": 2,
      "programId": null,
      "bundleId": null,
      "selectedTier": null,
      "durationDays": null,
      "includeFriday": false,
      "isCurrentUser": true
    }
  ]
}
```

ملاحظات:

- `GET /current` لو مفيش مسودة: السيرفر ينشئ Draft ويملأ المدير من البروفايل؛ ابعت `familyPackageId` بـ `PUT /group` فورًا لو المستخدم اختار باقة.
- لو الباقة مش موجودة/غير نشطة → `family.package_not_found`
- لو مفيش أي باقة نشطة أصلًا → `family.package_required`

### 3.4 صلة القرابة + العناوين

```http
GET /api/v1/customer/family-subscription-flow/relationships
GET /api/v1/customers/me/addresses
```

في picker إضافة فرد استخدم فقط `forDependents: true` (استبعد `self`).

### 3.5 تحديث المدير + إضافة سارة

**ممنوع** إرسال `nutritionalGoalCode` أو `activityLevel`.  
**ممنوع** `deliveryAddressId: ""` — استخدم Guid أو احذف الحقل / `null`.

```http
PUT /api/v1/customer/family-subscription-flow/members/m1000000-0000-0000-0000-000000000001

{
  "displayName": "أحمد",
  "relationshipCode": "self",
  "gender": 1,
  "ageYears": 32,
  "deliveryAddressId": "d1000000-0000-0000-0000-000000000001"
}
```

```http
POST /api/v1/customer/family-subscription-flow/members

{
  "displayName": "سارة",
  "relationshipCode": "spouse",
  "gender": 2,
  "ageYears": 28,
  "deliveryAddressId": "d1000000-0000-0000-0000-000000000001"
}
```

الآن العدد = 2 → داخل نطاق الباقة الصغيرة (2–3) → الخصم هيطبّق عند الملخص.

محاولة إضافة فرد رابع على باقة max=3 → `409` + `code: "family.max_members"`.

اعرض في الـ UI: «2 / 3 أفراد» من `members.length` و `maxMembers`.

### 3.5.1 شريط الأفراد على كل الشاشات (إلزامي)

**كل فرد لازم يظهر ويُختار في كل شاشة بعد ما تتوجد المسودة** — خصوصًا شاشة البرنامج وشاشة الباقة/الأيام/التصنيف — لأن كل فرد له برنامج وتصنيف (Basic / Platinum / Elite) مستقل.

| قاعدة | سلوك الموبايل |
| --- | --- |
| مصدر الأفراد | `members[]` من `GET /current` ومن **كل** رد mutation (`PUT /group` · CRUD members · `PUT .../plan`) — ممنوع تعتمد على المدير بس أو cache قديم |
| أين يظهر الشريط | شاشات **[1] الأفراد** · **[2] البرنامج** · **[3] الباقة + المدة + التصنيف** · **[4] الملخص/الدفع** |
| الاختيار | احتفظ بـ `selectedMemberId` محليًا؛ الضغط على chip يبدّل الفرد المعروض ويحمّل حقوله: `programId` · `bundleId` · `selectedTier` · `durationDays` · `includeFriday` · `setupStatus` |
| البرنامج والتصنيف | اختيار كتالوج الشاشة [2]/[3] يخص **الفرد المحدد فقط** — متخفّيش باقي الأفراد وأنت بتعدّل واحد |
| الحفظ | كل ضغطة → `PUT .../members/{memberId}/plan` بالحقول المتغيرة فقط (partial merge)؛ حدّث الـ state من رد الـ `members[]` |
| اكتمال الفرد | علامة ✓ لما `setupStatus === 3` (Ready) عشان المستخدم يشوف مين لسه ناقص برنامج/تصنيف |
| العداد | `members.length / maxMembers` ثابت فوق أو جنب الشريط |

```text
┌──────────┐ ┌──────────┐
│ أحمد ✓   │ │ سارة     │   ← selectedMemberId = سارة
└──────────┘ └──────────┘
        ↓ شاشة البرنامج / التصنيف
   كتالوج يخص سارة فقط → بعد الحفظ PUT plan لـ سارة
```

بعد `POST /members` لازم تعيد رسم الشريط من الـ response (أو `GET /current`). لو الفرد التاني اختفى بعد الرجوع للشاشة، ده bug تخزين/تزامن — الـ backend بيحفظ الأعضاء بـ `SaveChanges`؛ لو السيرفر قديم بدون الـ fix هيرجع المدير بس.

### 3.6 كتالوج العائلة ثم الخطة

على شاشتي **البرنامج** و**الباقة والأيام** اعرض شريط الأفراد أولًا، وبعدين الكتالوج للفرد المحدد.

```http
GET /api/v1/customer/family-subscription-flow/programs
GET /api/v1/customer/family-subscription-flow/programs/{programId}/bundles
GET /api/v1/customer/subscription-flow/programs/{programId}/bundles/{bundleId}/durations?includeFriday=false
GET /api/v1/customer/subscription-flow/programs/{programId}/bundles/{bundleId}/tiers?durationDays=26&includeFriday=false&familyMemberId={memberId}
```

احفظ الخطة جزئيًا — كل ضغطة اختيار ترسل الحقول المتغيرة فقط، والسيرفر يدمج:

```http
PUT /api/v1/customer/family-subscription-flow/members/{memberId}/plan

{ "programId": "..." }
```

ثم في شاشة الباقة/التصنيف مثلًا:

```http
PUT /api/v1/customer/family-subscription-flow/members/{memberId}/plan

{
  "bundleId": "...",
  "tier": 2,
  "durationDays": 26,
  "includeFriday": false
}
```

أو الخطة كاملة في طلب واحد (متوافق مع القديم القديم):

```http
PUT /api/v1/customer/family-subscription-flow/members/{memberId}/plan

{
  "programId": "...",
  "bundleId": "...",
  "tier": 2,
  "durationDays": 26,
  "includeFriday": false
}
```

**قواعد الدمج:** الحقل المتساب = يفضل الحالي · `null` صريح = مسح · تغيير برنامج يمسّح الباقة/التصنيف لو مش متوافقين.

`tier`: `1` Basic · `2` Platinum · `3` Elite.

برنامج/باقة غير مفعّلين للعائلة → `family.catalog_not_available`.

الفرد يصبح `setupStatus: 3` (Ready) لما:

1. اسم + عمر 1–120  
2. `deliveryAddressId` صالح من عناوين المدير  
3. خطة كاملة: program + bundle + tier + durationDays  

### 3.7 الملخص والدفع

```http
GET /api/v1/customer/family-subscription-flow/summary
```

مثال أرقام (توضيحي):

| بند | قيمة |
| --- | --- |
| سعر أحمد | 150.000 |
| سعر سارة | 150.000 |
| `subtotal` | 300.000 |
| `familyDiscountPercent` | 0.08 |
| `familyDiscountAmount` | 24.000 |
| `finalPrice` | 276.000 |
| `canCheckout` | true |

شروط `canCheckout == true`:

- المجموعة `Draft`
- كل الأفراد Ready
- `minMembers ≤ count ≤ maxMembers` للباقة المختارة
- `finalPrice > 0`

```http
GET /api/v1/customer/family-subscription-flow/payment-methods?amount=276.000
POST /api/v1/customer/family-subscription-flow/checkout

{
  "callbackUrl": "mealmate://checkout/success",
  "errorUrl": "mealmate://checkout/error",
  "paymentMethodId": 1
}
```

افتح `paymentUrl` في WebView. بعد الرجوع:

```http
GET /api/v1/customer/subscriptions
```

المتوقع: اشتراكان (أو أكثر) لنفس المدير، نفس `familyGroupId`.

---

## 4) `currentStep` — ربط الشاشات

من `GET /current` / بعد أي mutation:

| قيمة | معنى منتج | شاشة مقترحة |
| --- | --- | --- |
| `1` | ملفات الأفراد ناقصة | شاشة الأفراد / الباقة لو لسه مش مختارة |
| `2` | في خطط ناقصة | شاشات البرنامج/الباقة لكل فرد غير Ready |
| `3` | الكل Ready — جاهز للدفع | الملخص |
| `4` | CheckoutPending أو Active | حالة الدفع / اشتراكات نشطة |

اقتراح UX: لو `selectedFamilyPackageId == null` اعرض شاشة الباقات حتى لو `currentStep == 1`.

---

## 5) Models Dart مقترحة

```dart
class FamilySizePackage {
  final String id;
  final String nameAr;
  final String nameEn;
  final int minMembers;
  final int maxMembers;
  final double discountPercent; // 0.08 = 8%
  final int sortOrder;
  final String? imageStorageKey;
  final String? imageUrl;

  FamilySizePackage({
    required this.id,
    required this.nameAr,
    required this.nameEn,
    required this.minMembers,
    required this.maxMembers,
    required this.discountPercent,
    required this.sortOrder,
    this.imageStorageKey,
    this.imageUrl,
  });

  factory FamilySizePackage.fromJson(Map<String, dynamic> j) => FamilySizePackage(
        id: j['id'] as String,
        nameAr: j['nameAr'] as String? ?? '',
        nameEn: j['nameEn'] as String? ?? '',
        minMembers: j['minMembers'] as int,
        maxMembers: j['maxMembers'] as int,
        discountPercent: (j['discountPercent'] as num).toDouble(),
        sortOrder: j['sortOrder'] as int? ?? 0,
        imageStorageKey: j['imageStorageKey'] as String?,
        imageUrl: j['imageUrl'] as String?,
      );
}

class FamilyGroupWriteRequest {
  final String name;
  final String? familyPackageId;

  FamilyGroupWriteRequest({required this.name, this.familyPackageId});

  Map<String, dynamic> toJson() => {
        'name': name,
        if (familyPackageId != null && familyPackageId!.isNotEmpty)
          'familyPackageId': familyPackageId,
      };
}

class FamilyMemberWriteRequest {
  final String displayName;
  final String relationshipCode;
  final int gender; // 1 Male, 2 Female
  final int ageYears;
  final String? deliveryAddressId; // Guid أو null — ممنوع ""

  Map<String, dynamic> toJson() {
    final map = <String, dynamic>{
      'displayName': displayName,
      'relationshipCode': relationshipCode,
      'gender': gender,
      'ageYears': ageYears,
    };
    if (deliveryAddressId != null && deliveryAddressId!.trim().isNotEmpty) {
      map['deliveryAddressId'] = deliveryAddressId;
    }
    return map;
  }
}
```

---

## 6) ترتيب الاستدعاءات الموصى به (انسخه في الـ Cubit)

```text
1.  GET /api/v1/customer/subscription-flow/types
2.  GET .../family-subscription-flow/packages   (أو /metadata)
3.  UI: المستخدم يختار باقة حجم
4.  GET .../family-subscription-flow/current
5.  PUT .../group  { name, familyPackageId }
6.  GET .../relationships
7.  GET /api/v1/customers/me/addresses
8.  PUT/POST .../members/...
9.  GET .../family-subscription-flow/programs → bundles
10. GET .../subscription-flow/.../durations → tiers
11. PUT .../members/{id}/plan  (لكل فرد)
12. GET .../summary  حتى canCheckout == true
13. GET .../payment-methods?amount={finalPrice}
14. POST .../checkout → WebView(paymentUrl)
15. GET /api/v1/customer/subscriptions  (نفس familyGroupId)
```

---

## 7) أمثلة حدود الباقة (اختبرها يدويًا)

### مثال A — تحت الحد الأدنى

- باقة: min=2, max=3  
- أفراد: المدير فقط  
- النتيجة: `canCheckout: false`, `checkoutBlockReason: "family.min_members"`  
- الخصم لا يُحتسب كـ «مطبّق للدفع» قبل الوصول للحد.

### مثال B — داخل النطاق

- باقة: min=2, max=3 · discount=0.08  
- أفراد: 2 Ready  
- النتيجة: خصم 8% على `subtotal`، `canCheckout: true` لو الأسعار > 0.

### مثال C — وصل السقف

- باقة max=3 وعدد الأفراد = 3  
- زر «إضافة فرد» معطل في الـ UI  
- لو POST رغم ذلك → `family.max_members`.

### مثال D — تغيير الباقة لضيق

- مسودة بـ 4 أفراد على باقة متوسطة (4–5)  
- المستخدم يحاول التحويل لباقة صغيرة (2–3)  
- المتوقع: رفض أو إجبار تقليل الأفراد قبل الحفظ (حسب رسالة السيرفر / تحقق الواجهة مسبقًا).

---

## 8) جدول أخطاء مهمة للموبايل

| `code` | متى يظهر | رسالة UX مقترحة |
| --- | --- | --- |
| `family.package_required` | مفيش باقات نشطة من الأدمن | «باقات العائلة غير متاحة حاليًا» |
| `family.package_not_found` | id غلط أو باقة موقوفة | «الباقة غير متاحة، اختر باقة أخرى» |
| `family.max_members` | إضافة فوق سقف الباقة | «وصلت الحد الأقصى لهذه الباقة» |
| `family.min_members` | دفع وعدد الأفراد أقل من min | «أضف فردًا آخر لإكمال الباقة» |
| `family.member_incomplete` | فرد ناقص عنوان أو خطة | «أكمل بيانات وخطط كل الأفراد» |
| `family.catalog_not_available` | برنامج/باقة مش للعائلة | رجّع لكتالوج `family-subscription-flow` |
| `family.active_exists` | محاولة مسودة جديدة وفيه Active | وجّه لشاشة الاشتراك الحالي |
| `family.cannot_delete_manager` | حذف المدير | امنع الزر أصلًا |
| `family.not_draft` | تعديل بعد بدء الدفع | امنع التعديل؛ اعرض حالة الدفع |
| `family.address_invalid` | عنوان مش بتاع المدير | أعد اختيار عنوان من `/customers/me/addresses` |

معظم الأخطاء ProblemDetails — اقرأ **`code`** مش بس HTTP status.

---

## 9) Enums سريعة

| حقل | قيم |
| --- | --- |
| `status` مجموعة | 1 Draft · 2 CheckoutPending · 3 Active · 4 Frozen · 5 Dispute · 6 PendingDetach · 7 Cancelled |
| `role` | 1 Manager · 2 Member |
| `setupStatus` | 1 NotStarted · 2 InProgress · 3 Ready |
| `gender` | 1 Male · 2 Female |
| `tier` / `selectedTier` | 1 Basic · 2 Platinum · 3 Elite |

اقبل رقمًا؛ النص اسم fallback إن ظهر.

---

## 10) Checklist تنفيذ Flutter

- [ ] شاشة/ستيب **باقات الحجم** قبل أو مع اسم العائلة  
- [ ] عرض `imageUrl` + اسم + نطاق + خصم  
- [ ] `PUT /group` يرسل `familyPackageId`  
- [ ] عدّاد أفراد `current / maxMembers` وتعطيل الإضافة عند السقف  
- [ ] **شريط أفراد** على شاشات البرنامج + الباقة/التصنيف + الملخص (`selectedMemberId` + كل `members[]`)  
- [ ] رسالة soft min قبل الدفع لو `count < minMembers`  
- [ ] شيل nutritional goal + activity level  
- [ ] `deliveryAddressId` كـ `String?` بدون `""`  
- [ ] المدير من `GET /current` (`isCurrentUser`) — متضيفهوش تاني  
- [ ] علاقات من `/relationships` مع `forDependents`  
- [ ] برامج/باقات من `family-subscription-flow` فقط  
- [ ] خطة بـ `PUT .../plan` واحد لكل فرد (برنامج + تصنيف مستقلين)  
- [ ] ملخص يعتمد `canCheckout` و`checkoutBlockReason`  
- [ ] WebView لـ `paymentUrl` ثم refresh للاشتراكات  

---

## 11) الفرق عن الفلو القديم (قبل باقات الحجم)

| قبل | بعد |
| --- | --- |
| `maxMembers` / `minMembersForDiscount` / `discountPercent` عامة من metadata فقط | نفس الحقول **فعّالة من الباقة المختارة** + قائمة باقات |
| شاشة أفراد مباشرة | **اختيار باقة حجم أولًا** |
| خصم واحد لكل العائلات | خصم مختلف حسب الباقة |
| بدون صورة للباقة | `imageUrl` اختياري من الأدمن |

`metadata` ما زال يرجع `packages` + scalars للتوافق؛ اعتمد على **باقة المجموعة** (`selectedFamilyPackageId` + `minMembers`/`maxMembers`/`discountPercent` في screen DTO) بعد الاختيار.

---

## 12) جملة واحدة للـ PR الموبايل

> Add family size-package picker (image, range, discount), send `familyPackageId` on `PUT /group`, enforce package min/max in member UI, keep per-member plans on family catalog, then single MyFatoorah checkout.

---

## 13) مراجع

- API مرجع: [`family-subscription-api.md`](./family-subscription-api.md)  
- إصلاحات form قديمة: [`family-subscription-flutter-changes.md`](./family-subscription-flutter-changes.md)  
- شرح منتج مختصر: [`family-subscription-flow-explained.md`](./family-subscription-flow-explained.md)  
- تصميم الباقات: `docs/superpowers/specs/2026-08-20-family-size-packages-design.md`  
- أهلية الكتالوج: `docs/superpowers/specs/2026-08-20-family-catalog-eligibility-design.md`
