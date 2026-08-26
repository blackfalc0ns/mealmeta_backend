# إصلاحات الموبايل — الاشتراك الفردي + التقويم

> **الجمهور:** مبرمج Flutter.  
> **الهدف:** قائمة نواقص واضحة لإغلاق الفلو الفردي حتى اختيار الوجبات.  
> **Base URL الحي:** `http://maelmate.runasp.net/api/v1`  
> **آخر تحقق حي:** 2026-08-25 (دفع MyFatoorah حقيقي + تقويم بعد fulfillment)

## اقرأ بهذا الترتيب

| # | ملف | ماذا يغطّي |
|---|-----|------------|
| 0 | [`00-priority-checklist.md`](./00-priority-checklist.md) | أولويات الإصلاح + Definition of Done |
| 1 | [`subscription/01-start-date-and-summary.md`](./subscription/01-start-date-and-summary.md) | اختيار بداية الاشتراك + الملخص |
| 2 | [`subscription/02-checkout-and-payment.md`](./subscription/02-checkout-and-payment.md) | Checkout + الدفع بدون تخطي |
| 3 | [`subscription/03-address-profile-gates.md`](./subscription/03-address-profile-gates.md) | العنوان / الحساسية / بوابات البروفايل |
| 4 | [`calendar/01-after-payment-gate.md`](./calendar/01-after-payment-gate.md) | بعد الدفع حتى فتح التقويم |
| 5 | [`calendar/02-day-selection-api.md`](./calendar/02-day-selection-api.md) | اختيار المطعم والوجبات (API الحي) |
| 6 | [`calendar/03-ui-rules-and-gaps.md`](./calendar/03-ui-rules-and-gaps.md) | قواعد الواجهة والنواقص الشائعة |
| 7 | [`calendar/04-slim-calendar-and-day-detail.md`](./calendar/04-slim-calendar-and-day-detail.md) | التقويم الخفيف + `GET /days/{date}` |

## مراجع عقد كاملة (لا تستبدل الملفات أعلاه)

- اشتراك: [`../../mobile-customer-subscription-flow.md`](../../mobile-customer-subscription-flow.md)
- دفع: [`../../mobile-checkout-flow.md`](../../mobile-checkout-flow.md)
- تقويم — سلوك شاشات: [`../individual-calendar/00-individual-calendar-flow.md`](../individual-calendar/00-individual-calendar-flow.md)
- تقويم — API حي: [`../individual-calendar/01-individual-calendar-api.md`](../individual-calendar/01-individual-calendar-api.md)
