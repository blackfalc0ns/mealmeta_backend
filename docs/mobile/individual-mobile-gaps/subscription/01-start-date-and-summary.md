# 1 — بداية الاشتراك + شاشة الملخص

> يخص شاشات ما قبل الدفع.  
> مرجع عام: [`../../../mobile-customer-subscription-flow.md`](../../../mobile-customer-subscription-flow.md)

## المشكلة الشائعة في الموبايل

- عدم إرسال `startDate`، أو اعتبار `startDate` = «اليوم» بدون اختيار المستخدم.
- عرض تاريخ واحد فقط بينما المنتج يحتاج: **يوم الاختيار** + **أول صندوق توصيل**.

## القاعدة

| حقل | من أين | المعنى للعرض |
|-----|--------|----------------|
| طلب `startDate` | اختيار العميل في الواجهة | يوم يختاره ≥ اليوم (تقويم الكويت) |
| رد `chosenStartDate` | من الباك | نفس اختيار العميل |
| رد `startDate` | من الباك | **أول يوم توصيل** = اختيار + 3 أيام (تخطي الجمعة إن `includeFriday=false`) |
| رد `serviceDates` / `endDate` | من الباك | أيام الخدمة تبدأ من أول التوصيل |

مثال: اختيار `2026-08-27` → أول وجبة `2026-08-30`.

## ماذا يصلح الموبايل

1. أضف DatePicker لبداية الاشتراك قبل/داخل الملخص.
2. نادِ الملخص:

```http
GET /api/v1/customer/subscription-flow/summary
  ?programId={uuid}
  &bundleId={uuid}
  &tier=Basic
  &durationDays=6
  &startDate=2026-08-27
  &includeFriday=false
```

3. في UI الملخص اعرض بوضوح:
   - «يبدأ اشتراكك من: `chosenStartDate`»
   - «أول توصيل: `startDate`»
4. خزّن نفس `startDate` (اختيار العميل) لـ checkout — **لا ترسل تاريخ أول التوصيل كطلب**؛ الباك يحسبه مرة أخرى.

## Checkout

```json
{
  "programId": "...",
  "bundleId": "...",
  "tier": "Basic",
  "durationDays": 6,
  "startDate": "2026-08-27",
  "callbackUrl": "mealmate://payment/success",
  "errorUrl": "mealmate://payment/error",
  "includeFriday": false,
  "paymentMethodId": 2,
  "correlationId": "mobile-..."
}
```

من الرد استخدم `chosenStartDate` + `startDate` للتأكيد قبل فتح الدفع.

## أخطاء لازم تعالجها في UI

| كود | HTTP | تصرف الموبايل |
|-----|------|----------------|
| `checkout.start_date_required` | 400 | اطلب اختيار تاريخ |
| `checkout.start_date_invalid` | 400 | التاريخ في الماضي — أعد الاختيار |
| `subscription_flow.profile_incomplete` | 409 | وجّه حسب `nextAction` |

## ملاحظة نشر

- على السيرفر الحي قد يكون الحقل مطلوبًا بعد رفع آخر باك.
- لو الـAPI القديم لا يزال يقبل بدون `startDate`، أرسله من الآن — متوافق للأمام.
