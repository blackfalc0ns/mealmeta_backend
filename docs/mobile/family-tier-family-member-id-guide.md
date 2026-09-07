# دليل الموبايل — تصنيف العيلة و `familyMemberId`

**الجمهور:** Flutter / Customer App  
**Auth:** `Authorization: Bearer <customer JWT>`  
**JSON:** camelCase  
**التاريخ:** 2026-09-08

الملف ده مخصوص لخطأ Dio اللي بيظهر على:

```text
GET /api/v1/customer/subscription-flow/programs/{programId}/bundles/{bundleId}/tiers?includeFriday=false
→ 409 { "error": "family.member_required" }
```

عقد الشاشات: [`family-screens/README.md`](./family-screens/README.md)  
شاشة الباقة/التصنيف: [`family-screens/04-bundle-days-duration-tier.md`](./family-screens/04-bundle-days-duration-tier.md)

---

## 1) الترتيب المعتمد للفلو

```text
Create Family → Program → Location → Allergies → Package / Days / Tier → Payment
```

| مرحلة | شرط الانتقال للتالية | من `GET .../family-subscription-flow/current` |
|--------|------------------------|-----------------------------------------------|
| إنشاء العيلة / إضافة فرد | الفرد موجود في `members[]` | `memberId` |
| البرنامج | `programId != null` | `PUT .../members/{memberId}/plan` بجسم `{ "programId": "…" }` |
| الموقع | `deliveryAddressId != null` **لنفس** `memberId` | `POST .../members/{memberId}/addresses` |
| الحساسية | بعد `PUT .../members/{memberId}/allergies` (محلياً أو من الملخص) | مش ظاهر في `current` |
| باقة / أيام / مدة / تصنيف | `bundleId` + `selectedTier` + `durationDays` | نفس `PUT .../plan` |

**ممنوع** تستخدموا `isInCheckoutBasket` أو `setupStatus === 3` عشان تفتحوا الموقع أو الحساسية أو التصنيف.  
`isInCheckoutBasket` **مش موجود في الـ API** — لو الفرونت بيحسبه من اكتمال الباقة+التصنيف+المدة، هيتخطّى Location و Allergies.

`setupStatus`: `1` NotStarted · `2` InProgress (كفاية `programId`) · `3` Ready (خطة كاملة **و** عنوان **و** اسم **و** سن).

---

## 2) ليه التصنيف بيكسر من غير `familyMemberId`

مسار التصنيف هو مسار **الفردي**:

```text
/api/v1/customer/subscription-flow/programs/{programId}/bundles/{bundleId}/tiers
```

JWT = المدير. من غير query `familyMemberId` السيرفر كان بيستخدم **عنوان المدير** لكل الشريط → فرد من غير عنوان كان بيشوف التصنيف.

بعد ما العيلة فيها **أكتر من فرد**، الطلب من غير `familyMemberId` يرجع:

```json
{
  "error": "family.member_required",
  "missingStep": "FAMILY_MEMBER",
  "actionEndpoint": "Pass familyMemberId for the family member whose classification is being requested"
}
```

التصنيف بيتفلتر على **منطقة عنوان نفس الفرد**. عنوان المدير لا يكفي لفرد تاني.

---

## 3) الطلب الصحيح (GET — مفيش body)

Dio `data:` / Map زي `{durationDays: 6, includeFriday: false}` **ما بيتبعتش** مع GET. كله query.

`selectedMemberId` من شريط الأفراد = `familyMemberId`.

### 3.1 كروت التصنيف

قبل اختيار المدة (من غير أسعار):

```http
GET /api/v1/customer/subscription-flow/programs/{programId}/bundles/{bundleId}/tiers?includeFriday=false&familyMemberId={memberId}
```

بعد اختيار المدة (أسعار على الكرت):

```http
GET /api/v1/customer/subscription-flow/programs/{programId}/bundles/{bundleId}/tiers?includeFriday=false&durationDays=6&familyMemberId={memberId}
```

### 3.2 مطاعم التصنيف

```http
GET /api/v1/customer/subscription-flow/programs/{programId}/bundles/{bundleId}/tiers/{tier}/restaurants?familyMemberId={memberId}
```

`tier`: `1` Basic · `2` Platinum · `3` Elite.

### 3.3 المدد (من غير familyMemberId)

```http
GET /api/v1/customer/subscription-flow/programs/{programId}/bundles/{bundleId}/durations?includeFriday=false
```

### 3.4 مثال Dio

```dart
final memberId = selectedMemberId; // من شريط الأفراد — عمره ما ينفع يبقى null هنا

final response = await dio.get(
  '/api/v1/customer/subscription-flow/programs/$programId/bundles/$bundleId/tiers',
  queryParameters: {
    'includeFriday': includeFriday, // false = 5 أيام، true = 6 أيام
    if (durationDays != null) 'durationDays': durationDays,
    'familyMemberId': memberId,
  },
);
```

تبديل الـ chip → أعد نفس الـ GET بـ `familyMemberId` الجديد. متكاشوش رد المدير لفرد تاني.

---

## 4) جدول أخطاء التصنيف / الاشتراك الفردي

كلها `409` جسم `{ "error", "missingStep", "actionEndpoint" }`.

| `error` | امتى | UI |
|---------|------|-----|
| `family.member_required` | `tiers` أو `.../tiers/{tier}/restaurants` والعيلة فيها فردين+ **من غير** `familyMemberId` | ابعت `familyMemberId={selectedMemberId}` على **نفس** الـ URL |
| `family.address_required` | `familyMemberId` موجود والفرد ده من غير عنوان | شاشة عنوان **هذا** الفرد: `POST /api/v1/customer/family-subscription-flow/members/{memberId}/addresses` — مش `customers/me/addresses` |
| `family.incomplete` | `GET /subscription-flow/prerequisites` وفي مسودة عيلة (`Draft` / `CheckoutPending`) | رسالة: عندك اشتراك عائلي لسه مكتملش. افتح أرشيف الاشتراكات |
| `checkout.address_required` | مسار **فردي** (مفيش عيلة متعددة) ومن غير عنوان المدير | عنوان الحساب الحالي |
| `subscription_flow.no_restaurants_for_address` | عنوان الفرد برة تغطية المطاعم | غيّر عنوان **هذا** الفرد |

`family.member_required` **مش** معناه «كمّل الأرشيف». معناه نسيت query على شاشة التصنيف.

---

## 5) أرشيف الاشتراكات — مسودة العيلة

`GET /api/v1/customer/subscriptions/uncompleted`  
(نفس القائمة: `GET /api/v1/customer/subscriptions/archived`)

مسودة العيلة تظهر بنفس المصفوفة مع:

| حقل | قيمة |
|------|------|
| `kind` | `"FAMILY"` (الفردي `"INDIVIDUAL"` أو غائب) |
| `familyGroupId` و `subscriptionId` | معرّف العيلة |
| `statusNameAr` | `اشتراك عائلي غير مكتمل` أو `اشتراك عائلي بانتظار الدفع` |
| `actions.nextScreen` | `"family_setup"` — **لا** تفتح Checkout الفردي |
| `actions.resumeEndpoint` | `GET /api/v1/customer/family-subscription-flow/current` |
| `actions.deleteEndpoint` | `POST /api/v1/customer/family-subscription-flow/abandon-draft` (المسودة فقط؛ `canDelete: false` أثناء `CheckoutPending`) |

دليل الأرشيف الكامل: [`uncompleted-and-archived-subscriptions-guide.md`](./uncompleted-and-archived-subscriptions-guide.md)

لو المستخدم فتح اشتراك **فردي** وهو عنده مسودة عيلة:

```http
GET /api/v1/customer/subscription-flow/prerequisites
→ 409 { "error": "family.incomplete", "missingStep": "FAMILY", "actionEndpoint": "GET /api/v1/customer/subscriptions/uncompleted" }
```

اعرض الرسالة وودّيه لقسم غير المكتمل. **متفتحش** شاشة التصنيف الفردي.

---

## 6) عنوان الفرد قبل التصنيف

```http
POST /api/v1/customer/family-subscription-flow/members/{memberId}/addresses
```

خريطة أو يدوي — نفس عقد الفردي. التفاصيل: [`family-screens/06-address.md`](./family-screens/06-address.md)

بعد `200` حدّث `current` وتأكد `deliveryAddressId` للفرد ده، وبعدين أعد `GET .../tiers?...&familyMemberId={memberId}`.

---

## 7) Checklist Flutter

- [ ] state: `selectedMemberId` على شريط الأفراد من أول شاشة البرنامج
- [ ] كل `GET .../tiers` و `GET .../tiers/{tier}/restaurants` فيها `familyMemberId`
- [ ] `durationDays` و `includeFriday` query على GET — مش body
- [ ] تبديل الفرد يعيد طلب التصنيف — متستخدمش كاش رد فرد تاني
- [ ] `409 family.address_required` → عنوان `memberId` الحالي
- [ ] `409 family.member_required` → نقص query؛ أصلحه مش شاشة تانية
- [ ] `409 family.incomplete` على `prerequisites` → أرشيف / كمّل العيلة
- [ ] متستخدمش `isInCheckoutBasket` كشرط لفتح الموقع أو الحساسية أو التصنيف
- [ ] عنوان المدير لا يُعتبر عنوان الزوج/الابن

---

## 8) مثال الغلط vs الصح

**غلط (اللي في اللوج):**

```http
GET /api/v1/customer/subscription-flow/programs/0fe90c83-.../bundles/293b4b8b-.../tiers?includeFriday=false
```

**صح (مدة 6 أيام، جمعة مقفولة، الفرد الحالي):**

```http
GET /api/v1/customer/subscription-flow/programs/0fe90c83-.../bundles/293b4b8b-.../tiers?includeFriday=false&durationDays=6&familyMemberId=11111111-1111-1111-1111-111111111111
```

`familyMemberId` = `members[].memberId` من `GET /api/v1/customer/family-subscription-flow/current`.
