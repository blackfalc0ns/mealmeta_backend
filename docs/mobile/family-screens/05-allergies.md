# شاشة 5 — الحساسية (07.02)

**السابق:** [الباقة والأيام](./04-bundle-days-duration-tier.md) · **التالي:** [العنوان](./06-address.md)

## الهدف

حفظ حساسية **كل فرد في سلة الدفع** قبل الدفع. مفيش تخطي. `{ "code": "NONE" }` مسموح ومطلوب لو مفيش حساسية.

المكوّنات المرفوضة تنفع على نفس الشاشة **ومش شرط للدفع**.  
**مفيش** شاشة تفضيل غذائي في فلو العيلة.

## عناصر الشاشة

- شريط الأفراد المختارين
- شيبس الحساسية من كتالوج الأدمن/المطاعم
- خيار **بدون حساسية**
- بحث مرفوضات (اختياري، مش gate)
- `التالي: الموقع` بعد نجاح الحفظ للفرد الحالي
- ممنوع زر Skip

كرر لكل فرد `memberSubscriptionId == null` وهيكون في السلة (عنده خطة كاملة).

## APIs

### 1) كتالوج الشيبس (نفس الفردي — للقائمة فقط)

```http
GET /api/v1/customers/me/food-preferences/allergies?page=1&pageSize=50&search=
```

الكتالوج عام. الحفظ لازم يبقى **على حساب الفرد** من مسار العيلة.

### 2) حفظ حساسية الفرد (إجباري)

```http
PUT /api/v1/customer/family-subscription-flow/members/{memberId}/allergies
Content-Type: application/json
```

بدون حساسية:

```json
{ "allergies": [ { "code": "NONE" } ] }
```

بحساسية:

```json
{
  "allergies": [
    { "code": "MILK", "severity": "High" }
  ]
}
```

نجاح: `200` جسم `true`.  
كود `NONE` يتحسب زي الفردي: بيعلّم اكتمال الحساسية من غير صفوف ألرجين.

المدير من غير `FamilyMemberAccess` ينحفظ على حساب المدير. باقي الأفراد على `LinkedCustomerUserId`.

### 3) مرفوضات (اختياري — مش موجود على family-subscription-flow)

لو هتعرضها: استخدم مسارات الفردي للحساب المرتبط لاحقاً، أو سيّبها لبعد الدفع. **متقفلش التالي** بسببها.

## أخطاء

| code | HTTP | UI |
|------|------|-----|
| `family.not_found` | 404 | عضو/عيلة |
| `family.member_account_required` | 409 | الفرد مش مربوط بحساب — المفروض الإضافة بالهاتف عملت الربط |
| `food_preferences.invalid_allergen` | 400 | كود مش في الكتالوج |

لو دفعت من غير الحفظ: السمري `canCheckout: false` و`checkoutBlockReason: "family.allergies_required"` والدفع `409 family.allergies_required`.

## قواعد

- افتح الشاشة لكل فرد في السلة حتى لو اختار NONE.
- فرد في العيلة مش هتشترك له دلوقتي: **متطلبش** منه حساسية.
- بعد آخر فرد → [06-address.md](./06-address.md)

## التالي

[06-address.md](./06-address.md)
