# 03 — تحويل الفردي لعائلة — `convert-from-individual`

## ما الذي يتغير في الموبايل؟

- استدعاء **جديد** بدل إنشاء عائلة بـ `PUT .../group` عندما العميل عنده اشتراك فردي.
- بعد النجاح: ادخل فلو العائلة العادي من `FamilyGroupScreenDto` (أعضاء، خطط، ملخص).

## API

```http
POST /api/v1/customer/family-subscription-flow/convert-from-individual
Authorization: Bearer <token>
Content-Type: application/json
```

### Body (اختياري)

```json
{
  "name": "عائلة أحمد",
  "familyPackageId": "11111111-1111-1111-1111-111111111111"
}
```

- لو `name` فاضي → الباك يضع اسمًا افتراضيًا (مثل «عائلتي» / «عائلة {الاسم}»).
- لو `familyPackageId` فاضي → يختار باقة افتراضية نشطة.

### نجاح `200`

نفس `FamilyGroupScreenDto` المستخدم في `GET current` / `PUT group`:

- `status` = Draft (`1`)
- عضو Manager بخطة منسوخة من الاشتراك الفردي (program/bundle/tier/duration/includeFriday)
- **نفس** `subscriptionId` للمدير يبقى على السيرفر (مش اشتراك جديد)

### Idempotent

استدعاء ثاني والمسودة المحوّلة موجودة → يرجع نفس المجموعة بدون تكرار.

## أخطاء

| HTTP | كود | متى |
|------|-----|-----|
| 404 | `conversion.no_individual_subscription` | مفيش فردي قابل للتحويل |
| 409 | `conversion.already_in_family` | عائلة Active، أو CheckoutPending غير محوّل (دفع جارٍ). مسودة Draft المتبقية تُعتمد ولا تُرفض |
| 409 | `conversion.individual_not_convertible` | الاشتراك `PendingPayment` فقط |
| 400 | `family.package_not_found` | باقة غير صالحة |

## سلوك بعد النجاح

1. خزّن `familyGroupId` محليًا إن لزم.
2. انتقل لشاشة أفراد العائلة / الخطوة حسب `currentStep`.
3. **لا** تعدّل خطة المدير عبر `PUT .../plan` (انظر ملف 06).
4. أضف أعضاء جدد كالعادة.

## ملفات Flutter المتوقعة

- `family_convert_repository.dart` / method في family flow repo
- ربط زر Chooser → هذا الـPOST ثم navigation لـ family draft UI الموجود
