# شاشة 4 — الباقة والأيام والمدة والتصنيف (07.05)

**السابق:** [البرنامج](./03-program.md) · **التالي:** [الحساسية](./05-allergies.md)

## الهدف

لكل فرد مختار: باقة وجبات + 5 أو 6 أيام + مدة + تصنيف مطاعم. الخطة مستقلة لكل `memberId`.

## عناصر الشاشة

- شريط الأفراد (نفس شاشة البرنامج)
- بطاقات الباقات (محتوى البوكس + مطاعم)
- 5 أيام = جمعة مقفولة (`includeFriday: false`) · 6 أيام = جمعة شغالة (`true`)
- المدة: 26 / 12 / 6 / مخصص حسب إعداد الأدمن
- Select / Plus / Max → أرسل `tier`: `1` Basic / `2` Platinum / `3` Elite (أسماء الأدمن من `tierLabels` لو رجعت في الـ tiers API)
- تاريخ بداية اختياري `startDate` (`YYYY-MM-DD`) — مش في فورم الإضافة
- `التالي: الحساسية` بعد ما الفرد الحالي عنده `programId + bundleId + selectedTier + durationDays`

امشي باقي الأفراد المختارين على شاشتي 3 و 4 قبل الحساسية.

## APIs

### 1) باقات البرنامج (عائلي = فردي في المحتوى)

```http
GET /api/v1/customer/family-subscription-flow/programs/{programId}/bundles
```

قائمة `CustomerBundleCardDto`: `bundleId`, `mealsPerDay`, `breakfastCount`, `lunchCount`, `dinnerCount`, `snackCount`, `slots`, `restaurantCount`, …

برنامج مش موجود/مش نشط → `404` `{ "error": "family.catalog_not_available" }`.

### 2) المدد (مسار الفردي)

```http
GET /api/v1/customer/subscription-flow/programs/{programId}/bundles/{bundleId}/durations?includeFriday=false
```

`409 subscription_flow.friday_unavailable` أو `family.friday_unavailable` بعد الحفظ → الغي الجمعة أو غيّر التصنيف.

### 3) التصنيفات — **عنوان الفرد الحالي مطلوب**

التصنيف يتفلتر على **منطقة عنوان نفس `memberId`**. عنوان المدير لا يكفي لفرد تاني.

```http
GET /api/v1/customer/subscription-flow/programs/{programId}/bundles/{bundleId}/tiers?durationDays=26&includeFriday=false&familyMemberId={memberId}
GET /api/v1/customer/subscription-flow/programs/{programId}/bundles/{bundleId}/tiers/{tier}/restaurants?familyMemberId={memberId}
```

`familyMemberId` **إلزامي** بعد ما العيلة يبقى فيها أكتر من فرد.

شرح كامل للموبايل (Dio GET / أخطاء / أرشيف): [`../family-tier-family-member-id-guide.md`](../family-tier-family-member-id-guide.md)

| الحالة | HTTP | code |
|--------|------|------|
| نسيت `familyMemberId` والعيلة فيها فردين+ | 409 | `family.member_required` |
| الفرد الحالي من غير عنوان توصيل | 409 | `family.address_required` — `POST /api/v1/customer/family-subscription-flow/members/{memberId}/addresses` |
| المدير لوحده من غير عنوان | 409 | `checkout.address_required` (مسار الفردي) |

متعرضش كروت التصنيف لفرد من غير عنوان. بعد حفظ عنوان **هذا** الفرد، أعد طلب `tiers` بنفس `familyMemberId`.

### 4) حفظ الخطة (patch)

```http
PUT /api/v1/customer/family-subscription-flow/members/{memberId}/plan
Content-Type: application/json

{
  "bundleId": "…",
  "durationDays": 26,
  "includeFriday": false,
  "tier": 1,
  "startDate": "2026-09-20"
}
```

تقدر تستخدم `selectedBundleId` بدل `bundleId` و`selectedTier` بدل `tier`.  
حقل متعرّفش في JSON = متتغيرش. `null` الصريح يمسح (الـ optional converter).

مثال كامل بعد اختيار كل حاجة:

```json
{
  "programId": "…",
  "bundleId": "…",
  "tier": 2,
  "durationDays": 12,
  "includeFriday": true,
  "startDate": "2026-09-20"
}
```

## أخطاء

| code | HTTP | UI |
|------|------|-----|
| `family.program_required` | 400 | باقة من غير برنامج |
| `family.bundle_not_found` | 404 | باقة غلط |
| `family.catalog_not_available` | 400 | باقة/برنامج مش نشطين |
| `family.member_required` | 409 | ابعت `familyMemberId` مع طلب التصنيف |
| `family.address_required` | 409 | الفرد ده محتاج عنوانه — مش عنوان المدير |
| `family.duration_not_available` | 400 | مدة مش مسموحة (مثلاً 3 أيام) |
| `family.friday_unavailable` | 400 | مفيش مطعم جمعة في التصنيف |
| `family.catalog_unavailable` | 400 | التصنيف مش في كتالوج التسعير |
| `family.plan_locked` | 409 | فرد مدفوع |
| `conversion.manager_plan_locked` | 409 | خطة المدير المحوّل مقفولة في المرحلة دي |

## قواعد

- سعر الفرد بيتحسب في السمري من السيرفر — متثبتش سعر الكارت محلياً للدفع.
- `includeFriday` مع المدة: 5 أيام أسبوعياً ≠ مدة 5 أيام. المدة 26/12/6/مخصص.
- بعد آخر فرد مختار → [05-allergies.md](./05-allergies.md)

## التالي

[05-allergies.md](./05-allergies.md)
