# 05 — ملخص الدفع والعائلة — `summary` + `checkout`

## ما الذي يتغير في الموبايل؟

- حدّث موديل `FamilyCheckoutSummary` بحقول prepaid.
- شاشة الدفع تعرض أن المدير **مدفوع مسبقًا** والسعر المستحق للأعضاء الجدد فقط.
- `finalPrice` هو مبلغ MyFatoorah؛ لو المدير فقط بدون أعضاء جدد بالكفاية حسب الباقة → `canCheckout` غالبًا false (min members).

## API

```http
GET /api/v1/customer/family-subscription-flow/summary?memberId={optional}
Authorization: Bearer <token>
```

Checkout كما هو:

```http
POST /api/v1/customer/family-subscription-flow/checkout
```

## حقول جديدة على الـsummary

| حقل | نوع | معنى |
|-----|-----|------|
| `managerPrepaid` | `bool` | `true` بعد التحويل من فردي |
| `managerSubscriptionId` | `guid?` | نفس اشتراك المدير الأصلي |
| `newMembersCount` | `int` | عدد الأفراد اللي هيتدفع عنهم (بدون المدير prepaid) |
| `newMembersSubtotal` | `decimal` | مجموع أسعار الأعضاء الجدد قبل خصم العائلة (يساوي `subtotal` في مسار التحويل) |

الحقول القديمة لسه موجودة: `subtotal`, `familyDiscountPercent`, `familyDiscountAmount`, `finalPrice`, `canCheckout`, `members[]`, `selectedMember.memberPrice`, …

### ملاحظات عرض

- سطر المدير: `memberPrice == 0` و/أو شارة «مدفوع» عند `managerPrepaid`.
- لا تضيف سعر المدير لملخص «المبلغ المطلوب».
- `payment-methods?amount={finalPrice}` استخدم `finalPrice` من الـsummary.

## مثال مقتطف

```json
{
  "familyGroupId": "...",
  "familyName": "عائلتي",
  "subtotal": 100.0,
  "familyDiscountPercent": 0.10,
  "familyDiscountAmount": 10.0,
  "finalPrice": 90.0,
  "currencyCode": "KWD",
  "canCheckout": true,
  "managerPrepaid": true,
  "managerSubscriptionId": "aaaaaaaa-bbbb-cccc-dddd-eeeeeeeeeeee",
  "newMembersCount": 1,
  "newMembersSubtotal": 100.0
}
```

## Checkout

- لا تغيير في شكل طلب الـcheckout.
- بعد الدفع: اشتراك المدير **نفس الـid**؛ أعضاء جدد فقط بتبقى PendingPayment → Paid.
- لا تفترض أن `paymentAnchorSubscriptionId` يعني «لازم تدفع عن المدير».

## ملفات Flutter المتوقعة

- `family_checkout_summary_model.dart`
- UI شاشة الدفع / جدول الأسعار
- ربط `finalPrice` ببوابة الدفع كما هو
