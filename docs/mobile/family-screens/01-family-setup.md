# شاشة 1 — إعداد العيلة (07.01)

**الملف السابق:** [README](./README.md) · **التالي:** [إضافة فرد](./02-add-member.md) أو [البرنامج](./03-program.md)

## الهدف

حفظ **اسم العيلة** وقائمة الأفراد. الدفع مش هنا. الفرد يتضاف للعيلة حتى لو مش هيشترك دلوقتي.

## متى تفتح

بعد اختيار نوع الاشتراك **عائلي** من `GET /api/v1/customer/subscription-flow/types`.

## عناصر الشاشة

- اسم العيلة
- شريط أفاتار الأفراد + زر `+` (يفتح [إضافة فرد](./02-add-member.md))
- كارت لكل فرد: الاسم، القرابة، شيبس «من غير خطة» لو `memberSubscriptionId` فاضي (الموبايل يشوف `setupStatus` / مفيش برنامج)
- المدير ظاهر كعضو `role = 1` و`relationshipCode = "self"`
- لو المدير عنده اشتراك فردي: بانر **حوّل للعيلة** أو **كمّل باشتراكك الفردي**
- أزرار: `إضافة فرد` · `التالي: البرنامج`

**مش في الشاشة:** باسورد، تاريخ بداية الاشتراك، باقة حجم إجبارية، تفضيل غذائي.

## APIs

### 1) اقرأ الحالة (أول طلب)

```http
GET /api/v1/customer/family-subscription-flow/current
```

- لو مفيش عيلة: السيرفر يعمل مسودة بالمدير.
- لو العيلة `Active`: ترجع العيلة الحالية (تقدر تضيف أفراد لاحقاً).
- `409 conversion.convert_required` → لازم التحويل قبل إنشاء عيلة جديدة.

### 2) علاقات + باقات حجم (اختياري للعرض)

```http
GET /api/v1/customer/family-subscription-flow/metadata
GET /api/v1/customer/family-subscription-flow/relationships
GET /api/v1/customer/family-subscription-flow/packages
```

خصم باقة الحجم **بيتحسب من عدد اللي هتدفع لهم**، مش لازم المستخدم يختار باقة من الشاشة دي.

### 3) حفظ الاسم

```http
PUT /api/v1/customer/family-subscription-flow/group
Content-Type: application/json

{ "name": "عائلة أحمد" }
```

`familyPackageId` اختياري. لو العيلة Active → `409 family.active_exists` (غيّر الاسم من المسودة فقط؛ العيلة النشطة تُدار بالإضافة مش بإعادة إنشاء).

### 4) تحويل من اشتراك فردي (لو البانر ظاهر)

```http
POST /api/v1/customer/family-subscription-flow/convert-from-individual
Content-Type: application/json

{ "name": "عائلة أحمد" }
```

المدير يفضل على خطته الحالية (`managerPrepaid` لاحقاً في السمري). أفراد جدد هم اللي هيتدفع لهم.

### 5) إلغاء المسودة فقط

```http
POST /api/v1/customer/family-subscription-flow/abandon-draft
```

`204` — للعيلة `Draft` فقط.

## مثال `GET current`

```json
{
  "familyGroupId": "…",
  "name": "عائلة أحمد",
  "status": 1,
  "minMembers": 2,
  "maxMembers": 4,
  "discountPercent": 0.10,
  "members": [
    {
      "memberId": "…",
      "role": 1,
      "displayName": "أحمد",
      "relationshipCode": "self",
      "gender": 1,
      "ageYears": 36,
      "setupStatus": 1,
      "isCurrentUser": true,
      "phoneNumber": "+96550000001",
      "includeFriday": false
    }
  ],
  "currentStep": 1
}
```

`status`: `1` Draft · `2` CheckoutPending · `3` Active.

## أخطاء

| code | HTTP | UI |
|------|------|-----|
| `family.name_required` | 400 | الاسم فاضي |
| `family.active_exists` | 409 | فيه عيلة نشطة — استخدم `GET current` |
| `conversion.convert_required` | 409 | اعرض اختيار التحويل |
| `family.not_found` | 404 | أعد `GET current` |

## قواعد

- `التالي: البرنامج` يكمّل للأفراد اللي هتشترك لهم **دلوقتي** (هتختارهم في الخطوات الجاية بخطة كاملة). الباقي يفضل في الشريط من غير خطة.
- أثناء `CheckoutPending` متعدلش الأعضاء.

## التالي

- `+` / إضافة فرد → [02-add-member.md](./02-add-member.md)
- `التالي: البرنامج` → [03-program.md](./03-program.md)
