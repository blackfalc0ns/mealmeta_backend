> Payment integration updated 2026-10-06: use the [Payzah contract](<F:/MealMate/backend/docs/mobile/payzah-payments.md>). Any old-provider payment endpoints, method IDs, credential/webhook fields or refund instructions below are superseded; they are retained only as historical context for this mixed guide.

# 0 — Checklist أولويات الإصلاح (فردي)

نفّذ بالترتيب. كل بند = تغيير واضح في الموبايل.

## P0 — لازم قبل أي اختبار دفع/تقويم

- [ ] شاشة اختيار **يوم بداية الاشتراك** (`startDate` = اختيار العميل، تقويم الكويت ≥ اليوم).
- [ ] إرسال `startDate` في `GET .../summary` و`POST .../checkout`.
- [ ] عرض تاريخين من الرد: `chosenStartDate` (اختيار) و`startDate` (أول توصيل = اختيار + 3 أيام).
- [ ] العنوان الافتراضي بمنطقة مغطاة (`regionId` من Areas) قبل الملخص.
- [ ] إكمال بوابات: عنوان + حساسية + تفضيلات غذائية (حتى لو NONE).
- [ ] فتح `paymentUrl` من checkout في WebView/Browser — **ممنوع تخطي الدفع**.
- [ ] بعد الرجوع: `GET .../payment` حتى `isPaid: true` ثم polling على `GET .../calendar`.

## P1 — تقويم الاختيار

- [ ] لا تفتح اختيار مطعم/وجبة قبل `calendar` = `200`.
- [ ] مطاعم اليوم من `GET .../subscriptions/{id}/restaurants` فقط (pool مجمّد).
- [ ] حفظ الوجبات بـ `mealIds` الكاملة حسب `slotRequirements`.
- [ ] عطّل أي وجبة فيها `blockReason`.
- [ ] تغيير المطعم يصفّر الوجبات ويعيد `meals/available`.

## P2 — جودة UX

- [ ] رسائل أخطاء من كود السيرفر (`checkout.*` / `calendar.*` / `subscription_flow.*`) لا نصوص ثابتة عشوائية.
- [ ] حالة انتظار الدفع / انتظار التفعيل منفصلة عن فشل الدفع.
- [ ] حد تكرار المطعم: اعرضه عند توفره من الملخص/التقويم؛ لا تخترع رقمًا محليًا.

---

## Definition of Done (فلو سعيد)

1. تسجيل/دخول بالهاتف.
2. عنوان في منطقة عليها مطاعم.
3. اختيار برنامج → باقة → مدة → تصنيف → **تاريخ بداية**.
4. ملخص يعرض أول توصيل بعد +3 أيام.
5. دفع MyFatoorah حقيقي ينجح.
6. شاشة «تم الدفع» ثم التقويم يظهر أيام الاشتراك.
7. اختيار مطعم + وجبات ليوم مفتوح والحفظ ينجح.

---

## ما لا تفعله

| خطأ شائع | الصحيح |
|----------|--------|
| تخطي الدفع / تعليم الاشتراك Paid من الموبايل | فقط MyFatoorah + استعلام `payment` |
| فتح التقويم فور `paymentUrl` | انتظر `isPaid` ثم `calendar` 200 |
| استخدام مطاعم شاشة التصنيف داخل التقويم | pool الاشتراك المجمّد فقط |
| إرسال `restaurantId` / `regionId` في checkout | الباك يأخذهما من العنوان |
| الاعتماد على مسارات `/complete` أو `/days/{date}/restaurants` | غير مرفوعة على الحي حاليًا |
