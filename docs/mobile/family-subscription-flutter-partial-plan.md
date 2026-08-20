# اللي هيتعدّل على الموبايل — فلو اشتراك العائلة

**للموبايل فقط** · 2026-08-20  
مرجع API: [`family-subscription-api.md`](./family-subscription-api.md)  
دليل كامل: [`family-subscription-mobile-flow-guide.md`](./family-subscription-mobile-flow-guide.md)  
**شاشة الدفع فقط:** [`family-subscription-flutter-payment-summary.md`](./family-subscription-flutter-payment-summary.md)

Base:

```text
/api/v1/customer/family-subscription-flow
```

---

## 1) شاشات لازم تتغيّر

| شاشة | التعديل |
| --- | --- |
| **البرنامج** | شريط كل الأفراد أعلى الشاشة + اختيار برنامج للفرد المحدد فقط |
| **الباقة والأيام والتصنيف** | نفس الشريط + اختيار باقة/مدة/جمعة/تصنيف للفرد المحدد فقط |
| **الملخص / الدفع** | اعرض كل الأفراد وحالة كل واحد (مكتمل / ناقص) |
| **إضافة فرد** | شيل هدف غذائي + مستوى النشاط من الـ UI والـ request |

---

## 2) State جديد (إلزامي)

```dart
String? selectedMemberId; // الفرد اللي بتعدّل عليه دلوقتي
List<FamilyMember> members; // من GET /current أو رد أي mutation
```

- مصدر الأفراد = `members[]` من السيرفر — **مش** المدير لوحده، ومش cache قديم.
- بعد `POST /members` أو أي `PUT` → حدّث `members` من الـ response.
- الضغط على chip في الشريط = غيّر `selectedMemberId` واعرض حقوله:
  `programId` · `bundleId` · `selectedTier` · `durationDays` · `includeFriday` · `setupStatus`

---

## 3) الحفظ — يتغيّر من «طلب واحد في الآخر» إلى «حفظ فور كل ضغطة»

نفس الـ endpoint:

```text
PUT /members/{memberId}/plan
```

`memberId` = **`selectedMemberId`** (مش دائمًا المدير).

ابعت **الحقل المتغيّر فقط** (Map من غير nulls زيادة):

| لما المستخدم يختار | ابعت |
| --- | --- |
| برنامج | `{ "programId": "..." }` |
| باقة وجبات | `{ "bundleId": "..." }` |
| مدة + جمعة | `{ "durationDays": 26, "includeFriday": false }` |
| تصنيف | `{ "tier": 1 }` |

قيم التصنيف: `1` Basic · `2` Platinum · `3` Elite  
مقبول كمان: `selectedTier` بدل `tier`، و`selectedBundleId` بدل `bundleId`.

### ترتيب الحفظ لكل فرد

```text
1) programId
2) bundleId
3) durationDays (+ includeFriday)
4) tier
```

لو غيّر البرنامج بعد ما اختار باقة → السيرفر **يمسح** `bundleId` و`selectedTier` ويفضل `durationDays`.  
الموبايل لازم يفضي اختيار الباقة/التصنيف في الـ UI ويعيد اختيارهم.

---

## 4) أمثلة Dart (النمط المطلوب)

```dart
Future<void> patchMemberPlan(String memberId, Map<String, dynamic> fields) async {
  final res = await dio.put(
    '/api/v1/customer/family-subscription-flow/members/$memberId/plan',
    data: fields, // بس المفاتيح المتغيرة
  );
  // حدّث members من res.data['members']
}

// شاشة البرنامج
onProgramTap(programId) =>
  patchMemberPlan(selectedMemberId!, {'programId': programId});

// شاشة الباقة
onBundleTap(bundleId) =>
  patchMemberPlan(selectedMemberId!, {'bundleId': bundleId});

onDurationTap(days, friday) =>
  patchMemberPlan(selectedMemberId!, {
    'durationDays': days,
    'includeFriday': friday,
  });

onTierTap(tier) =>
  patchMemberPlan(selectedMemberId!, {'tier': tier});
```

**ممنوع:**
- model فيه كل الحقول `null` ويتبعت كامل (الـ `null` الصريح = مسح على السيرفر)
- حفظ خطة الفرد التاني على `memberId` المدير
- الاعتماد على state محلي من غير ما تتأكد من رد الـ API

---

## 5) كتالوج العائلة (مش الفردي)

```text
GET .../family-subscription-flow/programs
GET .../family-subscription-flow/programs/{programId}/bundles
```

بعد البرنامج+الباقة:

```text
GET .../subscription-flow/programs/{programId}/bundles/{bundleId}/durations
GET .../subscription-flow/programs/{programId}/bundles/{bundleId}/tiers
```

---

## 6) أزرار التنقّل

| زر | شرط |
| --- | --- |
| التالي من شاشة البرنامج | كل فرد عنده `programId != null` |
| التالي من شاشة الباقة/التصنيف | كل فرد `setupStatus == 3` (Ready) — أو اعتمد على `summary.canCheckout` |
| الدفع | `canCheckout == true` من `GET /summary` |

`setupStatus`: `1` NotStarted · `2` InProgress · `3` Ready  
Ready محتاج كمان: عنوان توصيل + اسم + عمر صالح (مش `ageYears: 0`).

---

## 7) إضافة / تعديل فرد — اتشال من العقد

امسح من الشاشة والـ DTO:

- `nutritionalGoalCode`
- `activityLevel`

و`deliveryAddressId`: ابعت Guid أو متبعتش المفتاح — **ممنوع** `""`.

---

## 8) شاشة الدفع — نفس `GET /summary`

```http
GET .../family-subscription-flow/summary
GET .../family-subscription-flow/summary?memberId={selectedMemberId}
```

| UI | API |
| --- | --- |
| شريط الأفراد + جاهز | `members[].setupStatus == 3` أو `mealOverview[].isReady` |
| ملخص الفرد عند التبديل | `selectedMember` (برنامج/باقة/مدة/تصنيف/سعر) |
| إجمالي الكل | `subtotal` · خصم العائلة · `finalPrice` |
| جدول الوجبات | `mealOverview[]` → `breakfast`/`lunch`/`snack`/`dinner` + `mealsPerDay` |
| طرق الدفع | `GET .../payment-methods?amount={finalPrice}` |

مفيش ضريبة في الرد. بعد تغيير `memberId` الإجمالي والجدول ما بيتغيروش.

---

## 9) Checklist تنفيذ Flutter

- [ ] شريط أفراد على شاشات البرنامج + الباقة/التصنيف + الملخص
- [ ] `selectedMemberId` + تحديث من `members[]` بعد كل رد
- [ ] `PUT plan` فور اختيار برنامج (للـ selected فقط)
- [ ] `PUT plan` فور اختيار باقة (`bundleId`)
- [ ] `PUT plan` فور اختيار مدة/جمعة
- [ ] `PUT plan` فور اختيار تصنيف (`tier`)
- [ ] لو اتغيّر البرنامج → امسح باقة/تصنيف محليًا وأعد الاختيار
- [ ] متبعتش حقول فاضي/`null` زيادة في الـ plan body
- [ ] كتالوج من `family-subscription-flow/programs` مش الفردي
- [ ] شيل هدف غذائي + مستوى نشاط
- [ ] قبل الدفع: كل الأفراد Ready + عنوان لكل فرد
- [ ] شاشة الدفع من `GET /summary?memberId=` عند تبديل الفرد
- [ ] جدول الوجبات من `mealOverview`