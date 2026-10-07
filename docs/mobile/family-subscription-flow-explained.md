> Payment integration updated 2026-10-06: use the [Payzah contract](<F:/MealMate/backend/docs/mobile/payzah-payments.md>). Any old-provider payment endpoints, method IDs, credential/webhook fields or refund instructions below are superseded; they are retained only as historical context for this mixed guide.

# شرح فلو اشتراك العائلة (MealMate)

> **مرجع Flutter الكامل بأمثلة:** [`family-subscription-mobile-flow-guide.md`](./family-subscription-mobile-flow-guide.md)

دليل منتج/موبايل: إزاي فلو العيلة بيشتغل من أول ما العميل يختار «عائلة» لحد الدفع.  
العملة **KWD**. التفاصيل التقنية في [`family-subscription-api.md`](./family-subscription-api.md) وتعديلات Flutter في [`family-subscription-flutter-changes.md`](./family-subscription-flutter-changes.md).

---

## 1) الفكرة باختصار

- عميل واحد (**المدير**) يعمل مجموعة عائلية.
- يضيف أفراد (زوج/زوجة، أبناء، أب/أم، أخ/أخت، صديق، …) **من غير ما يحتاجوا حساب**.
- **كل فرد** يختار لنفسه: برنامج + باقة + مدة + تصنيف (Basic / Platinum / Elite).
- الدفع **فاتورة واحدة واحدة** بخصم عائلي (لو العدد ≥ الحد الأدنى)، وبعد الدفع يتفتح **اشتراك منفصل لكل فرد** مربوط بنفس `familyGroupId`.

الحد الأقصى لعدد الأفراد (يشمل المدير) يتحكم فيه **الأدمن**.

---

## 2) مين يظهر في الأفراد؟

| فرد | مصدر البيانات |
| --- | --- |
| المدير (أنت) | بيتضاف تلقائي من البروفايل عند `GET /current` أو `PUT /group`: الاسم، العمر من تاريخ الميلاد، العنوان الافتراضي إن وُجد، `relationshipCode = self`، `isCurrentUser = true` |
| باقي الأفراد | المدير يدخلهم يدويًا: اسم، صلة قرابة، جنس، عمر، عنوان توصيل |

**مش مطلوب** في شاشة الفرد: هدف غذائي أو مستوى نشاط (اتشالوا من العقد).

صلة القرابة من:

```http
GET /api/v1/customer/family-subscription-flow/relationships
```

في picker الإضافة استخدم العناصر اللي `forDependents: true` فقط (متظهرش `self`).

---

## 3) خطوات الشاشات

```text
[اختيار نوع الاشتراك: FAMILY]
        ↓
[0] باقة الحجم (اسم + صورة + نطاق أفراد + خصم)
        ↓
[1] العائلة والأفراد
    - اسم العائلة + familyPackageId
    - قائمة الأفراد (المدير + مضافين)
    - تعديل بيانات كل فرد + عنوان
        ↓
[2] البرنامج (لكل فرد) — كتالوج العائلة
        ↓
[3] الباقة + المدة + الجمعة + التصنيف (لكل فرد)
        ↓
[4] الملخص والدفع
    - جدول الأفراد والوجبات
    - خصم باقة الحجم إن وُجد
    - MyFatoorah
```

`currentStep` من الـ API:

| قيمة | معنى |
| --- | --- |
| 1 | لسه إعداد ملفات الأفراد |
| 2 | في خطط ناقصة |
| 3 | كل الأفراد Ready — جاهز للدفع |
| 4 | CheckoutPending أو Active |

---

## 4) إيه اللي بيتخزن لكل فرد؟

### بيانات الشخص (شاشة 1)

- `displayName`
- `relationshipCode` (من `/relationships`)
- `gender` (1 ذكر / 2 أنثى)
- `ageYears` (1–120)
- `deliveryAddressId` (من عناوين **المدير** — Guid حقيقي، مش `""`)

### الخطة (شاشة 2 + 3) — طلب واحد

```json
{
  "programId": "...",
  "bundleId": "...",
  "tier": 1,
  "durationDays": 26,
  "includeFriday": false
}
```

الفرد يبقى `setupStatus = Ready` لما الاسم + العمر + العنوان + الخطة الكاملة موجودين.

---

## 5) حد عدد الأفراد (أدمن — عبر باقات الحجم)

الأدمن يعرّف **باقات حجم** مسماة (مثل صغيرة 2–3 / متوسطة 4–5). الموبايل يختار باقة؛ الحدود والخصم تيجي منها:

| حقل على الشاشة بعد الاختيار | معنى |
| --- | --- |
| `minMembers` | أقل عدد للدفع (soft أثناء المسودة) |
| `maxMembers` | أقصى عدد أفراد (يشمل المدير) |
| `discountPercent` | خصم الباقة على مجموع أسعار الأفراد |

`GET .../packages` أو `metadata.packages` للقائمة. إضافة فوق السقف → `409 family.max_members`.  
حذف المدير ممنوع → `family.cannot_delete_manager`.

التفاصيل والأمثلة: [`family-subscription-mobile-flow-guide.md`](./family-subscription-mobile-flow-guide.md).

---

## 6) كتالوج العائلة vs الفردي (الاتجاه المعتمد)

- **نفس** البرامج والباقات والوجبات والتصنيفات في النظام.
- الأدمن يفعّل لكل **برنامج** و**باقة** خيار «متاح للعائلة».
- تطبيق العائلة يشوف **بس** البرامج/الباقات المفعّلة للعائلة (مش كل كتالوج الفردي).
- المدد والتصنيفات تفضل من نفس قواعد الفردي بعد اختيار برنامج+باقة مسموحين.

> التنفيذ التفصيلي في: `docs/superpowers/specs/2026-08-20-family-catalog-eligibility-design.md`

لحد ما الـ endpoints العائلية تتنشر، الموبايل المؤقت يستخدم كتالوج الفردي — بعد الشيب لازم يتحول لمسارات `family-subscription-flow/programs...`.

> **محدّث:** استخدم الآن:
> - `GET .../family-subscription-flow/programs`
> - `GET .../family-subscription-flow/programs/{id}/bundles`
> - ثم `subscription-flow/.../durations` و `.../tiers` بنفس الـ IDs
> - رفض الخطة غير العائلية: `family.catalog_not_available`
> - `maxMembers` من الأدمن: 2–12

---

## 7) التسعير والدفع

1. النظام يحسب سعر كل فرد (نفس محرك تسعير العميل حسب برنامج/باقة/مدة/تصنيف).
2. `subtotal` = مجموع الأفراد.
3. لو العدد ≥ `minMembersForDiscount` → يطبّق `discountPercent` → `finalPrice`.
4. `GET summary` يعرض الجدول + `canCheckout`.
5. `POST checkout` → فاتورة MyFatoorah واحدة → بعد النجاح اشتراك لكل فرد جاهز.

---

## 8) ترتيب استدعاءات مقترح (Flutter)

```text
1. GET /api/v1/customer/subscription-flow/types
2. GET /api/v1/customer/family-subscription-flow/metadata
3. GET /api/v1/customer/family-subscription-flow/relationships
4. GET /api/v1/customer/family-subscription-flow/current
5. PUT /group  (اختياري — اسم العائلة)
6. GET /api/v1/customers/me/addresses
7. PUT/POST /members/...
8. كتالوج العائلة: programs → bundles → durations → tiers
9. PUT /members/{id}/plan  لكل فرد
10. GET /summary
11. GET /payment-methods?amount={finalPrice}
12. POST /checkout → WebView
```

---

## 9) حالات تمنع الدفع

- فرد ناقص بيانات أو خطة → `family.member_incomplete`
- عدد الأفراد أقل من الحد الأدنى → `family.min_members`
- فيه عائلة Active بالفعل → `family.active_exists` عند إنشاء مجموعة جديدة

---

## 10) ملخص جملة واحدة

المدير يجهّز أفراد العيلة وبياناتهم وخطة كل فرد من كتالوج يحدده الأدمن للعائلة، بعدين يدفع مرة واحدة بخصم وحدّ أفراد يتحكم فيهم الأدمن، والنظام يفتح اشتراك مستقل لكل فرد تحت نفس المجموعة.
