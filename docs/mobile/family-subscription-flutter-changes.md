# تعديلات الموبايل (Flutter) — فلو اشتراك العائلة

> **الدليل الكامل بأمثلة (باقات الحجم أولاً):**  
> [`family-subscription-mobile-flow-guide.md`](./family-subscription-mobile-flow-guide.md)  
> **تصنيف + `familyMemberId` (إصلاح 409 `family.member_required`):**  
> [`family-tier-family-member-id-guide.md`](./family-tier-family-member-id-guide.md)

**التاريخ:** 2026-08-20  
**الجمهور:** فريق Flutter / Customer App  
**الهدف:** تعديلات لازمة عشان التطبيق يشتغل مع الـ API الحالي بدون `400` وبدون حقول اتشالت.

العقد الكامل: [`family-subscription-api.md`](./family-subscription-api.md)

Base URL:

```text
/api/v1/customer/family-subscription-flow
```

---

## 1) ليه الـ 400 اللي ظهر عند Yahya؟

الطلب كان:

```json
{
  "displayName": "Yahya",
  "relationshipCode": "self",
  "gender": 1,
  "ageYears": 43,
  "nutritionalGoalCode": "balanced",
  "activityLevel": 2,
  "deliveryAddressId": ""
}
```

| مشكلة | السبب | الحل |
| --- | --- | --- |
| `deliveryAddressId: ""` | فاضي مش Guid | ابعت Guid من عناوين العميل، أو `null`، أو احذف الحقل |
| `nutritionalGoalCode` | اتشال من العقد | احذف الحقل من الـ model والـ UI |
| `activityLevel` | اتشال من العقد | احذف الحقل من الـ model والـ UI |

بعد نشر الـ backend الحالي: `""` بيتحول `null` ومش هيكسر الـ binding — بس **الفرد مش هيبقى Ready** من غير عنوان صحيح.

---

## 2) Checklist — عدّل دول في الموبايل

### لازم يتشال من الشاشة والـ DTO

- [ ] ويدجت **الهدف الغذائي** (dropdown)
- [ ] ويدجت **مستوى النشاط** (قليل / متوسط / نشط)
- [ ] حقول `nutritionalGoalCode` و `activityLevel` من `FamilyMemberWriteRequest` / أي mapper لـ Dio

### لازم يتظبط

- [ ] `deliveryAddressId`: عمرك ما تبعت `""` — استخدم `String?` وابعت بس لو فيه قيمة
- [ ] قبل حفظ الفرد: لو مفيش عنوان → `GET /api/v1/customers/me/addresses` أو إنشاء عنوان ثم حط الـ `id`
- [ ] المدير يظهر من `GET /current` مع `isCurrentUser: true` — متضيفوش تاني يدويًا كـ member جديد
- [ ] صلة القرابة من `GET /relationships` (مش hardcoded) — في picker الأفراد استخدم `forDependents == true` فقط
- [ ] المدير: `relationshipCode` يبقى `self` (السيرفر بيفرضها)
- [ ] **شريط أفراد ثابت** على شاشات البرنامج + الباقة/المدة/التصنيف + الملخص — كل `members[]` ظاهرين وقابلين للاختيار (مش المدير لوحده)
- [ ] state: `selectedMemberId`؛ تبديل الـ chip يحمّل خطة الفرد من `programId` / `bundleId` / `selectedTier` / …
- [ ] طلبات التصنيف والمطاعم: `GET .../subscription-flow/.../tiers?familyMemberId={selectedMemberId}` — عنوان المدير **لا** يفتح التصنيف لفرد تاني. `409 family.address_required` → شاشة عنوان **هذا** الفرد. `409 family.member_required` → نسيت تبعت `familyMemberId`
- [ ] بعد `POST /members` حدّث القائمة من الـ response أو `GET /current` فورًا
- [ ] لكل فرد: عند اختيار برنامج/باقة/مدة/تصنيف ابعت `PUT .../plan` فورًا بالحقول المتغيرة فقط (partial merge)
- [ ] قبل الدفع: كل الأفراد `setupStatus === 3` (خطة كاملة)

### اختياري / تحسين UX

- [ ] لو `GET /current` رجّع المدير بـ `deliveryAddressId` من البروفايل → اعرضه prefilled
- [ ] لو `ageYears` موجود من `dateOfBirth` → اعرضه prefilled وخلّي التعديل متاح

---

## 3) Model الكتابة الصحيح (Dart)

```dart
class FamilyMemberWriteRequest {
  final String displayName;
  final String relationshipCode; // spouse | father | mother | child | sibling | friend | parent | other | self
  final int gender;              // 1 Male, 2 Female
  final int ageYears;            // 1..120
  final String? deliveryAddressId; // Guid أو null — ممنوع ""
  final String? phone;           // اختياري للأطفال والتابعين — لو فارغ ينشئ الباكيند Shadow Account تلقائياً

  FamilyMemberWriteRequest({
    required this.displayName,
    required this.relationshipCode,
    required this.gender,
    required this.ageYears,
    this.deliveryAddressId,
    this.phone,
  });

  Map<String, dynamic> toJson() {
    final map = <String, dynamic>{
      'displayName': displayName,
      'relationshipCode': relationshipCode,
      'gender': gender,
      'ageYears': ageYears,
    };
    // مهم: متضيفش المفتاح لو فاضي
    if (deliveryAddressId != null && deliveryAddressId!.trim().isNotEmpty) {
      map['deliveryAddressId'] = deliveryAddressId;
    }
    if (phone != null && phone!.trim().isNotEmpty) {
      map['phone'] = phone!.trim();
    }
    return map;
  }
}
```

مثال Dio بعد التعديل:

```dart
await dio.put(
  '/api/v1/customer/family-subscription-flow/members/$memberId',
  data: FamilyMemberWriteRequest(
    displayName: 'Yahya',
    relationshipCode: 'self',
    gender: 1,
    ageYears: 43,
    deliveryAddressId: selectedAddressId, // من addresses API
  ).toJson(),
);
```

---

## 4) صلة القرابة — endpoint جديد

```http
GET /api/v1/customer/family-subscription-flow/relationships
```

مثال response:

```json
[
  { "code": "self", "nameAr": "أنت", "nameEn": "You", "forDependents": false },
  { "code": "spouse", "nameAr": "زوج / زوجة", "nameEn": "Spouse", "forDependents": true },
  { "code": "father", "nameAr": "أب", "nameEn": "Father", "forDependents": true },
  { "code": "mother", "nameAr": "أم", "nameEn": "Mother", "forDependents": true },
  { "code": "child", "nameAr": "ابن / ابنة", "nameEn": "Child", "forDependents": true },
  { "code": "sibling", "nameAr": "أخ / أخت", "nameEn": "Sibling", "forDependents": true },
  { "code": "friend", "nameAr": "صديق / صديقة", "nameEn": "Friend", "forDependents": true },
  { "code": "parent", "nameAr": "والد / والدة", "nameEn": "Parent", "forDependents": true },
  { "code": "other", "nameAr": "أخرى", "nameEn": "Other", "forDependents": true }
]
```

في شاشة إضافة فرد:

```dart
final options = relationships.where((r) => r.forDependents).toList();
```

`self` للمدير الحالي فقط — متظهرهوش في قائمة الإضافة.

نفس القائمة موجودة كمان داخل `GET /metadata` تحت `relationships`.

---

## 5) ترتيب الاستدعاء المعدّل

```text
1. GET /api/v1/customer/subscription-flow/types
   → اختار FAMILY

2. GET /api/v1/customer/family-subscription-flow/current
   → المستخدم ظاهر في members (isCurrentUser: true)
   → 404 فقط لو فيه عائلة Active

3. (اختياري) PUT /group { "name": "عائلة يحيى" }
   → تعديل اسم العائلة

4. GET /relationships
   → picker صلة القرابة

5. GET /api/v1/customers/me/addresses
   → picker الموقع — لازم id قبل Ready

6. PUT /members/{managerMemberId}
   body بدون nutritionalGoal / activityLevel
   ومع deliveryAddressId صحيح

7. POST /members
   لكل فرد إضافي (spouse/child/…)

8. كتالوج العائلة: family-subscription-flow/programs → bundles
   ثم subscription-flow/.../durations → tiers
9. PUT /members/{id}/plan لكل فرد
10. GET /summary حتى canCheckout == true
11. GET /payment-methods?amount={finalPrice}
12. POST /checkout → WebView
```

---

## 6) حقول الاستجابة اللي تستخدمها في الـ UI

من `GET /current` / بعد أي mutation:

| حقل | استخدام |
| --- | --- |
| `members[].isCurrentUser` | علّم كارت المستخدم الحالي |
| `members[].role` | `1` = Manager (متتحذفش) |
| `members[].setupStatus` | `1` NotStarted · `2` InProgress · `3` Ready |
| `members[].deliveryAddressId` | prefilling العنوان |
| `currentStep` | تقدّم الشاشات |
| `maxMembers` | حد الإضافة (يشمل المدير؛ أدمن 2–12) |

**مش هترجع تاني في الـ card:** `nutritionalGoalCode` · `activityLevel`

---

## 7) Ready / Checkout — شروط الموبايل

الفرد `Ready` (`setupStatus === 3`) لما:

1. اسم + عمر 1–120
2. `deliveryAddressId` صالح
3. خطة كاملة: `programId` + `bundleId` + `selectedTier` + `durationDays`

`GET /summary` → `canCheckout: true` لما كل الأفراد Ready وعدد الأفراد ≥ `minMembersForDiscount` (عادة 2).

---

## 8) أخطاء شائعة بعد التعديل

| Symptom | السبب المحتمل |
| --- | --- |
| `400` على `$.deliveryAddressId` | لسه بتبعت `""` على سيرفر قديم — حدّث السيف أو ابعت Guid/null |
| `family.address_invalid` | الـ id مش من عناوين نفس العميل |
| `family.relationship_invalid` | code مش من `/relationships` |
| `family.cannot_delete_manager` | محاولة حذف `role: 1` |
| `family.member_incomplete` | فرد ناقص عنوان أو خطة |
| `family.catalog_not_available` | برنامج/باقة مش مفعّلين للعائلة — استخدم كتالوج `family-subscription-flow` |
| `family.not_found` على current | فيه عائلة Active — مش مسودة |

---

## 9) ملخص سطر واحد للـ PR الموبايل

> Remove nutritional goal & activity level from family member form; load kinship from `GET .../relationships`; send only name/relationship/gender/age + real `deliveryAddressId` (never empty string); seed manager from `GET .../current` (`isCurrentUser`); load family programs/bundles from `family-subscription-flow` (not individual catalog).

---

## 10) ملفات/طبقات متوقع تتلمس في Flutter

عدّل حسب هيكل مشروعكم — عادة:

- `family_member_form` / شاشة إضافة فرد → شيل goal + activity
- `family_member_write_dto` / request model → 5 حقول فقط
- كتالوج العائلة → base path `family-subscription-flow` للبرامج/الباقات
- `family_api_service` → أضف `getRelationships()`
- `family_members_controller` / cubit → prefilling من `current` + address picker
- أي hardcoded list لصلات القرابة → استبدلها بـ API

بعد التعديل: جرّب PUT للمدير بنفس payload Yahya **بعد** اختيار عنوان — المفروض `200` + `FamilyGroupScreenDto`.
