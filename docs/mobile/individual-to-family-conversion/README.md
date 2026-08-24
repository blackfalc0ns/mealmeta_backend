# تحويل الاشتراك الفردي → عائلة — دليل الموبايل (Phase 1)

> تاريخ: 2026-08-24  
> الجمهور: Flutter / Customer App  
> Auth: `Authorization: Bearer <customer JWT>` · JSON camelCase · عملة KWD

هذا المجلد يفصل **كل تغيير** لازم يتنفّذ في الموبايل كملف مستقل. اقرأ الملفات بالترتيب أو حسب الشاشة اللي بتشتغل عليها.

| # | الملف | شاشة / جزء التطبيق | ماذا تغيّر؟ |
|---|--------|---------------------|-------------|
| 1 | [`01-subscription-types.md`](./01-subscription-types.md) | اختيار نوع الاشتراك | حقول جديدة على `types` + مسار التحويل بدل checkout فردي |
| 2 | [`02-conversion-options.md`](./02-conversion-options.md) | شاشة الاختيار (Chooser) | API جديد بالكامل |
| 3 | [`03-convert-from-individual.md`](./03-convert-from-individual.md) | بدء العائلة من فردي | API جديد — **بدل** `PUT group` عند وجود فردي |
| 4 | [`04-abandon-draft.md`](./04-abandon-draft.md) | إلغاء مسودة العائلة | API جديد — يرجع الاشتراك الفردي |
| 5 | [`05-summary-and-checkout.md`](./05-summary-and-checkout.md) | ملخص الدفع العائلي | حقول prepaid + المدير لا يُدفع عنه |
| 6 | [`06-manager-plan-locked.md`](./06-manager-plan-locked.md) | شاشة خطة العضو (المدير) | قفل تعديل خطة المدير المحوّل |
| 7 | [`07-models-errors-checklist.md`](./07-models-errors-checklist.md) | Models / Repository / QA | DTOs + أكواد أخطاء + checklist |

## فلو مبسّط

```text
عند العميل اشتراك فردي حالي؟
  ├─ لا  → فلو عائلة العادي (PUT group → members → summary → checkout)
  └─ نعم →
        GET types  (INDIVIDUAL معطل، FAMILY.requiresConversion=true)
        → GET conversion-options
        → POST convert-from-individual
        → إضافة أعضاء + خطط (المدير مقفول الخطة)
        → GET summary (managerPrepaid)
        → POST checkout (يدفع الأعضاء الجدد فقط)
        → اختياري: POST abandon-draft لإلغاء المسودة
```

## خارج النطاق (Phase 2)

- `CHANGE_INDIVIDUAL_PLAN` ظاهر لكن `available: false`
- لا تبنِ شاشة تغيير البرنامج/الباقة/التصنيف للاشتراك الفردي الحالي في Phase 1

## مراجع مرتبطة

- الدليل العائلي الكامل: [`../family-subscription-api.md`](../family-subscription-api.md)
- فلو الاشتراك الفردي: [`../../mobile-customer-subscription-flow.md`](../../mobile-customer-subscription-flow.md)
