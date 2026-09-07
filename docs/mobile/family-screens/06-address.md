# شاشة 6 — عنوان التوصيل (لكل فرد)

**السابق:** [الحساسية](./05-allergies.md) · **التالي:** [الدفع](./07-payment.md)

## الهدف

عنوان **مستقل لكل فرد في السلة**. مش عنوان واحد للعيلة.

طريقة الإدخال **نفس الفردي بالظبط** (`POST /api/v1/customers/me/addresses`):

1. **من الخريطة:** بن داخل حدود الكويت (`latitude` + `longitude`).
2. **يدوي:** محافظة / منطقة / قطعة من كتالوج المناطق (`regionId` أو `blockRegionId` أو اسم `area`)، من غير إحداثيات — السيرفر يحل المنطقة ومركزها.

نفس جسم `CustomerAddressRequest` ونفس `CustomerAddressDto` (تغطية + مطاعم الخدمة). الفرق الوحيد: المالك = حساب الفرد المرتبط، والمسار تحت family-subscription-flow.

## عناصر الشاشة

- خريطة + بن (مسار الفردي)
- أو إدخال يدوي: محافظة، منطقة، قطعة، شارع، جادة، مبنى، دور، شقة
- تأكيد الموقع بعد preview-coverage
- بعد فرد: الفرد اللي بعده أو `التالي: الدفع`

لو العنوان برة تغطية المطاعم اقفل الدفع للسلة كلها لحد ما العناوين المختارة تتصلح (كل المختارين لازم يكونوا جاهزين).

## APIs

نفس عمليات الفردي، على حساب الفرد:

```http
GET    /api/v1/customer/family-subscription-flow/members/{memberId}/addresses
POST   /api/v1/customer/family-subscription-flow/members/{memberId}/addresses
PUT    /api/v1/customer/family-subscription-flow/members/{memberId}/addresses/{addressId}
POST   /api/v1/customer/family-subscription-flow/members/{memberId}/addresses/preview-coverage
GET    /api/v1/customer/family-subscription-flow/members/{memberId}/addresses/{addressId}/preview-coverage
POST   /api/v1/customer/family-subscription-flow/members/{memberId}/addresses/{addressId}/set-default
```

### إنشاء من الخريطة

```http
POST /api/v1/customer/family-subscription-flow/members/{memberId}/addresses
Content-Type: application/json

{
  "label": "المنزل",
  "addressLine": "قطعة 5، شارع 12، الكويت",
  "latitude": 29.3759,
  "longitude": 47.9774,
  "isDefault": true,
  "notifyOnDelivery": true
}
```

الإحداثيات لازم جوه الكويت. من غير `regionId` مسموح على مسار الخريطة (زي الفردي).

### إنشاء يدوي (كتالوج المناطق)

```http
POST /api/v1/customer/family-subscription-flow/members/{memberId}/addresses
Content-Type: application/json

{
  "label": "المنزل",
  "addressLine": "قطعة 5، شارع 12، الكويت",
  "regionId": "…",
  "blockRegionId": null,
  "governorate": "العاصمة",
  "area": "الشامية",
  "block": "5",
  "street": "12",
  "avenue": null,
  "building": "10",
  "unit": "2",
  "floor": "1",
  "isDefault": true,
  "notifyOnDelivery": true
}
```

من غير lat/lng: لازم `regionId` أو `blockRegionId` أو اسم `area` صحيح. السيرفر يملأ المحافظة/المنطقة ومركز الخريطة من الكتالوج.

الرد `CustomerAddressDto`. السيرفر يكتب `deliveryAddressId` على `FamilyMember`.

لا تستخدم `POST /api/v1/customers/me/addresses` لفرد تاني — ده عنوان المدير فقط.

### تغطية قبل التأكيد

نفس جسم `PreviewAddressCoverageRequest` الفردي:

```http
POST /api/v1/customer/family-subscription-flow/members/{memberId}/addresses/preview-coverage
```

لو المنطقة مش متغطية وضّح للمستخدم (`conflictMessage` / قائمة `servingRestaurants`).

## أخطاء

| code | HTTP | UI |
|------|------|-----|
| `validation.invalid_format` | 400 | بن برة الكويت، أو مفيش إحداثيات ولا منطقة صالحة |
| `address.area_invalid` | 400 | `regionId` مش منطقة توصيل |
| `address.area_required` | 400 | preview من غير منطقة |
| `family.member_account_required` | 409 | مفيش حساب مربوط |
| `family.not_found` | 404 | عضو |
| `family.not_draft` | 409 | تعديل/إنشاء أثناء `CheckoutPending` |

الدفع من غير عنوان للفرد: السيرفر يحاول عنوان الفرد ثم عنوان المدير للمدير فقط؛ غير المدير → فشل حجز `family.address_required`.

## قواعد

- كرّر الشاشة لكل فرد في السلة — نفس UI الفردي (خريطة **أو** يدوي).
- فرد مش في السلة: عنوان مش إجباري.
- `startDate` يتحدد في شاشة الخطة مش هنا.

## التالي

[07-payment.md](./07-payment.md)
