# شاشة 2 — إضافة فرد (موبايل أولاً)

**السابق:** [إعداد العيلة](./01-family-setup.md) · **التالي:** ترجع لنفس الشاشة أو [البرنامج](./03-program.md)

## الهدف

إضافة فرد للعيلة **من غير اشتراك ومن غير باسورد**. الموبايل مفتاح البحث.

دي **إدارة عيلة** مش خطوة دفع. ينفع والشاشة 1 مفتوحة، وينفع بعد ما العيلة تبقى `Active`.

## عناصر الشاشة

1. حقل **رقم الموبايل** أولاً (بعد blur أو زر بحث)
2. لو الرقم موجود: املأ الاسم (والعمر/النوع لو رجّعوا)
3. لو مش موجود: فورم فاضي — الاسم إجباري
4. باقي الحقول: الاسم الكامل، المهنة، القرابة، العمر، النوع
5. زر `إضافة فرد` فقط — **مفيش** «مستخدم حالي» و**مفيش** باسورد

تاريخ بداية الاشتراك **مش هنا**. العنوان والحساسية في شاشات لاحقة لو هتشترك له.

## APIs

### 1) بحث

```http
POST /api/v1/customer/family-subscription-flow/members/lookup
Content-Type: application/json

{ "phone": "+96551111111" }
```

موجود:

```json
{
  "found": true,
  "displayName": "سارة أحمد",
  "ageYears": 30,
  "gender": 2
}
```

مش موجود:

```json
{ "found": false, "displayName": null, "ageYears": null, "gender": null }
```

الرقم في عيلة تانية → `409 family.already_in_family`.

موبايل فاضي → `400 family.phone_required`.

### 2) حفظ الفرد

```http
POST /api/v1/customer/family-subscription-flow/members
Content-Type: application/json

{
  "phone": "+96551111111",
  "displayName": "سارة أحمد",
  "occupation": "موظفة",
  "relationshipCode": "spouse",
  "gender": 2,
  "ageYears": 30,
  "deliveryAddressId": null
}
```

- الرقم موجود عند MealMate → الربط بحسابه (الاسم من الحساب لو بعتّ اسم فاضي).
- الرقم جديد → إنشاء عميل بباسورد عشوائي **عمرها ما تترجع للموبايل**.
- الرد: `FamilyGroupScreenDto` زي `GET current`.

### 3) تعديل فرد غير مدفوع

```http
PUT /api/v1/customer/family-subscription-flow/members/{memberId}
```

نفس جسم الإضافة. المدير `relationshipCode` يتثبت `self`.

### 4) حذف فرد غير مدير

```http
DELETE /api/v1/customer/family-subscription-flow/members/{memberId}
```

## قرابات (`relationshipCode`)

من `GET .../relationships`:

| code | عربي |
|------|------|
| `self` | أنت (المدير فقط) |
| `spouse` | زوج / زوجة |
| `father` | أب |
| `mother` | أم |
| `child` | ابن / ابنة |
| `sibling` | أخ / أخت |
| `friend` | صديق / صديقة |
| `parent` | والد / والدة |
| `other` | أخرى |

النوع: `1` ذكر · `2` أنثى. العمر 1–120.

## أخطاء

| code | HTTP | UI |
|------|------|-----|
| `family.phone_required` | 400 | اطلب الموبايل |
| `family.display_name_required` | 400 | الاسم فاضي (رقم جديد) |
| `family.already_in_family` | 409 | الرقم في العيلة دي أو عيلة تانية |
| `family.max_members` | 409 | وصلت الحد |
| `family.age_invalid` | 400 | عمر غلط |
| `family.relationship_invalid` | 400 | قرابة مش من القائمة |
| `family.not_draft` | 409 | الدفع شغّال (`CheckoutPending`) |
| `family.cannot_delete_manager` | 409 | متشيلش المدير |

## قواعد

- بعد الإضافة الفرد `setupStatus` غالباً `NotStarted` — طبيعي.
- متظهرش الفرد ده في لوحات الأدمن كمشترك لحد ما يدفع.
- كرّر الشاشة لكل فرد. `التالي: البرنامج` من شاشة 1 للأفراد اللي هتخططلهم خطة.

## التالي

ارجع لـ [01-family-setup.md](./01-family-setup.md) أو ابدأ الخطة من [03-program.md](./03-program.md).
