# 06 — قفل خطة المدير المحوّل

## ما الذي يتغير في الموبايل؟

- في المسودة الناتجة عن `convert-from-individual`: **ممنوع** تغيير برنامج/باقة/تصنيف/مدة المدير.
- عدّل UI شريط الأعضاء: كرت المدير → عرض فقط (read-only) للخطة، أو أخفِ أزرار التعديل.
- يظل مسموحًا تعديل بيانات الملف الشخصي للمدير عبر `PUT members/{id}` (اسم، عمر، عنوان، …) إن كان الفلو الحالي يدعمها.

## API المتأثر

```http
PUT /api/v1/customer/family-subscription-flow/members/{memberId}/plan
```

عند مدير محوّل:

```http
409 Conflict
```

كود الخطأ: `conversion.manager_plan_locked`

## كيف تعرف أنه محوّل؟

إشارات عملية للموبايل:

1. دخلت الشاشة عبر `convert-from-individual` / `managerPrepaid == true` لاحقًا في summary.
2. أو احتفظ بـ flag محلي `isConvertedFamilyDraft = true` بعد التحويل حتى `abandon` أو `Active`.
3. العضو `role == Manager` (قيمة `1`) + المسار أعلاه.

لا تعتمد على تعديل الخطة ثم انتظار الـ409 كتجربة مستخدم أساسية — اقفل الـUI من الأول.

## Phase 2

تغيير خطة الفردي = خيار `CHANGE_INDIVIDUAL_PLAN` (حاليًا غير متاح). لا تربطه بـ `PUT .../plan` على المدير داخل العائلة.

## ملفات Flutter المتوقعة

- شاشة/ويدجت تعديل خطة العضو
- شرط `if (member.isManager && isConvertedDraft) readOnly`
