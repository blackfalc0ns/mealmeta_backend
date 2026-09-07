# شاشة 3 — البرنامج (07.04)

**السابق:** [إعداد العيلة](./01-family-setup.md) · **التالي:** [الباقة والأيام](./04-bundle-days-duration-tier.md)

## الهدف

اختيار **برنامج غذائي لكل فرد هتشترك له دلوقتي**. الكتالوج **نفس الفردي**: كل برنامج نشط عليه باقة وعرض مطعم.

## عناصر الشاشة

- شريط أفراد أفقي — الفرد الحالي مظلل
- بطاقات البرامج: اسم عربي/إنجليزي، وصف، صورة، كلمات مفتاحية، عدد الباقات، السلوتات
- `التالي: الباقة والأيام` يتعطل لحد ما `programId` يتسجل للفرد الحالي
- مفيش نطاقات تغذية داخلية على الكارت

اعرض في الشريط الأفراد المختارين للدفع (خطة هتتكمل). فرد «من غير خطة» يتساب للمرة الجاية.

## APIs

### 1) الكتالوج

```http
GET /api/v1/customer/family-subscription-flow/programs
```

نفس شكل الفردي `CustomerProgramCardDto`:

```json
[
  {
    "programId": "…",
    "code": "PROG_WEIGHT_LOSS",
    "shortCode": "WL",
    "nameAr": "خسارة الوزن",
    "nameEn": "Weight Loss",
    "descriptionAr": "…",
    "descriptionEn": "…",
    "keywordsAr": ["سعرات محسوبة"],
    "keywordsEn": ["Calorie controlled"],
    "imageUrl": "/content/…",
    "bundleCount": 2,
    "supportedSlots": ["lunch"]
  }
]
```

**لا تستخدم** `GET /api/v1/customer/subscription-flow/programs` جوه شاشات العيلة — نفس البيانات، بس المسار العائلي هو المصدر هنا.

### 2) حفظ البرنامج للفرد

Patch — الحقول المش موجودة تفضل زي ما هي:

```http
PUT /api/v1/customer/family-subscription-flow/members/{memberId}/plan
Content-Type: application/json

{ "programId": "aaaaaaaa-aaaa-aaaa-aaaa-aaaaaaaaaaaa" }
```

الرد `FamilyGroupScreenDto`. `setupStatus` يبقى `InProgress` لحد باقة+تصنيف+مدة.

بدّل الفرد من الشريط وأعد الطلب على `memberId` التاني.

## أخطاء

| code | HTTP | UI |
|------|------|-----|
| `family.program_not_found` | 404 | البرنامج اتشال |
| `family.catalog_not_available` | 400 | برنامج مش نشط |
| `family.plan_empty` | 400 | جسم فاضي |
| `family.plan_locked` | 409 | فرد مدفوع على عيلة Active |
| `family.not_found` | 404 | عضو/عيلة غلط |
| `family.not_draft` | 409 | CheckoutPending |

## قواعد

- كل فرد خطة مستقلة. متستنسخش برنامج المدير تلقائي إلا لو المستخدم عمل كده.
- بعد البرنامج روح لشاشة الباقة **لنفس** `memberId`.
- لما تخلّص كل المختارين في شاشات 3–4، روح الحساسية.

## التالي

[04-bundle-days-duration-tier.md](./04-bundle-days-duration-tier.md)
