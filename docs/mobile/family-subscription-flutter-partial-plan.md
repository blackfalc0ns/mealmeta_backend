# Flutter — تعديلات صغيرة: شريط الأفراد + حفظ جزئي للخطة

**للموبايل فقط** · 2026-08-20  
الدليل الكامل: [`family-subscription-mobile-flow-guide.md`](./family-subscription-mobile-flow-guide.md)

Base:

```text
PUT /api/v1/customer/family-subscription-flow/members/{memberId}/plan
```

---

## المطلوب في الـ UI

1. **شريط أفراد** على شاشة البرنامج وشاشة الباقة/المدة/التصنيف (والدفع).
2. `selectedMemberId` محلي — الضغط على فرد يبدّل مين بتعدّل خطته.
3. كل ضغطة اختيار **تحفظ فورًا** على السيرفر للفرد المحدد فقط.
4. بعد كل `PUT` / `POST members` حدّث الشريط من `members[]` في الرد (أو `GET /current`).

```text
[ أحمد ✓ ] [ سارة ]   ← selected = سارة
        ↓
  اختيار برنامج / باقة / مدة / تصنيف
        ↓
  PUT plan لـ سارة فقط → حدّث members[]
```

---

## ليه `bundleId` / `selectedTier` يرجعوا `null`؟

هذا **مش bug في القراءة** — القيم دي فعلًا فاضية في الداتابيز للفرد.

أشهر سببين:

1. **اتغيّر `programId` بعد ما اختار باقة**  
   السيرفر بيمسح `bundleId` + `selectedTier` لو مش تابعين للبرنامج الجديد، و**يسيب** `durationDays`.  
   شكلها في الرد: `programId` موجود + `durationDays: 26` + `bundleId: null` + `selectedTier: null` ← زي حالة `bona`.

2. **الباقة/التصنيف عمرهم ما اتحفظوا لـ `memberId` ده**  
   لازم بعد اختيار الباقة:
   ```json
   PUT .../members/{memberId}/plan
   { "bundleId": "..." }
   ```
   وقبل التصنيف لازم الباقة تكون اتحفظت. ابعت `bundleId` (أو `selectedBundleId`)، و`tier` / `selectedTier`.

### الترتيب الصحيح لكل فرد

```text
1) { "programId": "..." }
2) { "bundleId": "..." }          ← من family .../programs/{id}/bundles
3) { "durationDays": 26, "includeFriday": false }
4) { "tier": 1 }                  ← أو selectedTier
```

بعد كل `PUT` اقرأ نفس الفرد من `response.members` — لو `bundleId` لسه `null` فالحفظ فشل أو المبعتش.

Low Carb وغيره عندهم باقات عائلة في الكتالوج؛ لو الـ UI بيوري باقة ومش بتتحفظ، غالبًا الـ `memberId` غلط (مدير بدل الفرد) أو المفتاح مش `bundleId`.

---

## طلبات الحفظ (partial)

ابعت **الحقول المتغيرة بس** — متبعتش الباقي:

```http
PUT .../members/{memberId}/plan
{ "programId": "..." }
```

```http
PUT .../members/{memberId}/plan
{ "bundleId": "..." }
```

```http
PUT .../members/{memberId}/plan
{ "durationDays": 26, "includeFriday": false }
```

```http
PUT .../members/{memberId}/plan
{ "tier": 2 }
```

أو نفس القيمة باسم الرد (الاتنين مقبولين):

```http
PUT .../members/{memberId}/plan
{ "selectedTier": 2 }
```

`tier` / `selectedTier`: `1` Basic · `2` Platinum · `3` Elite  

**لازم** يكون `programId` و `bundleId` محفوظين قبل التصنيف — وإلا `family.plan_incomplete`.

**Dart:** ابني `Map` فيه المفاتيح المتغيرة فقط — متستخدمش model بـ nulls على كل الحقول (الـ null الصريح = مسح على السيرفر).

```dart
Future<void> saveMemberProgram(String memberId, String programId) async {
  await dio.put(
    '/api/v1/customer/family-subscription-flow/members/$memberId/plan',
    data: {'programId': programId},
  );
  // حدّث state من response.members
}

Future<void> saveMemberTier(String memberId, int tier) async {
  await dio.put(
    '/api/v1/customer/family-subscription-flow/members/$memberId/plan',
    data: {'tier': tier}, // أو selectedTier
  );
}
```

---

## قواعد سريعة

| قاعدة | سلوك |
| --- | --- |
| شاشة البرنامج | لكل فرد `programId` قبل «التالي» |
| شاشة الباقة/التصنيف | نفس الشريط؛ احفظ كل حقل فور الاختيار |
| تبديل فرد | اعرض `programId` / `bundleId` / `selectedTier` / `durationDays` من آخر `members[]` |
| Ready للدفع | كل فرد `setupStatus == 3` |
| جسم فاضي | `400` · `family.plan_empty` |
| برنامج مش للعائلة | `family.catalog_not_available` |

كتالوج البرامج/الباقات من:

```text
GET .../family-subscription-flow/programs
GET .../family-subscription-flow/programs/{programId}/bundles
```

المدد/التصنيفات من مسارات الفردي بعد اختيار برنامج+باقة.

---

## Checklist

- [ ] شريط `members[]` على شاشات 2 و 3 (و 4)
- [ ] `selectedMemberId`
- [ ] `PUT plan` فور اختيار برنامج
- [ ] `PUT plan` فور اختيار باقة / مدة / جمعة / تصنيف
- [ ] متبعتش حقول مش متغيرة
- [ ] حدّث UI من رد الـ API بعد كل حفظ
- [ ] شيل هدف غذائي / مستوى نشاط لو لسه ظاهرين
