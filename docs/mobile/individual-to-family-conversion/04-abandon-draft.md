# 04 — إلغاء مسودة العائلة — `abandon-draft`

## ما الذي يتغير في الموبايل؟

- زر/إجراء **جديد**: «إلغاء التحويل» أو «حذف المسودة» أثناء Draft بعد التحويل (ومفيد لأي Draft قابل للتحرير).
- بعد النجاح: العميل يرجع لاشتراكه الفردي؛ `types` هترجع `requiresConversion` تاني لو لسه فيه فردي.

## API

```http
POST /api/v1/customer/family-subscription-flow/abandon-draft
Authorization: Bearer <token>
```

لا يوجد body.

### نجاح

`204 No Content`

### ماذا يفعل الباك؟

- يحذف مجموعة الـDraft وأعضاءها.
- لو المسودة ناتجة عن تحويل (`convertedFromSubscriptionId`): يفك ربط الاشتراك الفردي (`familyGroupId` / `familyMemberId` → null) ويُبقي حالة الاشتراك كما هي (Active/…).

### أخطاء

| HTTP | كود | متى |
|------|-----|-----|
| 404 | `family.not_found` | مفيش مسودة |
| 409 | `family.not_draft` | المجموعة مش Draft (مثل CheckoutPending) |

## سلوك UI

- أكّد بـ dialog قبل الاستدعاء.
- بعد `204`: امسح حالة العائلة المحلية → ارجع لشاشة الاشتراكات / الأنواع.
- لا تستدعِ `abandon-draft` بعد ما الدفع بدأ (`CheckoutPending`) — مش مدعوم هنا.

## ملفات Flutter المتوقعة

- زر في شاشة العائلة Draft / settings المسودة
- `family_flow_repository.abandonDraft()`
