# المدة المخصصة — ربط الموبايل

ملف مختصر لشاشة المدة فقط. الدليل الكامل: [`mobile-duration-tier-flow.md`](./mobile-duration-tier-flow.md).

Base:

```http
GET /api/v1/customer/subscription-flow/programs/{programId}/bundles/{bundleId}/durations?includeFriday=false
Authorization: Bearer <customer-access-token>
```

---

## إزاي تعرف الكرت

من نفس قائمة المدد، ابحث عن عنصر:

| شرط | معنى |
|---|---|
| `isCustom === true` | كرت مدة مخصصة (picker) |
| `durationDays === null` | مفيش عدد أيام ثابت على الكرت |
| `minDays` / `maxDays` | حدود الإدخال من الأدمن (شاملة) |

لو الكرت مش راجع → الأدمن عطّل المدة المخصصة. متظهرش حاجة ثابتة من عندك.

مثال الكرت:

```json
{
  "durationDays": null,
  "nameAr": "مدة مخصصة",
  "nameEn": "Custom duration",
  "isCustom": true,
  "minDays": 7,
  "maxDays": 25,
  "supportsFriday": true,
  "fridayOption": { "isEnabled": true, "defaultSelected": false },
  "fridayDays": null,
  "totalServiceDays": null
}
```

---

## سلوك الشاشة

1. اعرض كروت الـpreset كالمعتاد (`durationDays` = 6 / 12 / 26 إن رجعت).
2. لو فيه كرت `isCustom`: عند اختياره افتح إدخال أرقام.
3. اقبل فقط أعداد صحيحة حيث: `minDays <= value <= maxDays`.
4. ممنوع إدخال قيمة تساوي `durationDays` لأي preset **نشط ظاهر** في نفس القائمة — وجّه المستخدم لكرت الـpreset.
5. زر المتابعة inactive لحد ما الرقم صالح.
6. الجمعة: نفس زر الـpresets — لو `fridayOption.isEnabled === false` خليه disabled (مش warning).
7. بعد الاختيار خزّن:
   - `durationDays` = الرقم اللي كتبه المستخدم
   - `includeFriday` = حالة الزر
8. مرّر القيم دي لكل اللي بعده: `tiers` (عند التسعير) / المطاعم / الملخص / checkout.

لا تحسب سعر ولا أيام جمعة على شاشة المدة للمخصص. السعر يظهر بعد اختيار التصنيف مع `durationDays`.

---

## أخطاء شائعة من الباك

| كود | متى |
|---|---|
| `subscription_flow.duration_not_available` | خارج `minDays`–`maxDays` أو المدة مش متاحة |
| `subscription_flow.friday_unavailable` | جمعة ممنوعة لهذه المدة من الأدمن |

---

## Checklist سريع

- [ ] مفيش hardcoded لـ7–25 أو أي نطاق
- [ ] الكرت يظهر فقط من الـAPI
- [ ] الـpicker يحترم `minDays` / `maxDays`
- [ ] قيمة preset نشطة متتنكتبش في الـpicker
- [ ] `durationDays` المختار بيتبعت لباقي الفلو
- [ ] زر الجمعة disabled عند `isEnabled: false`
