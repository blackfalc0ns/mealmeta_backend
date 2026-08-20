# Flutter — شاشة الدفع (ملخص العائلة) فقط

**للموبايل فقط** · 2026-08-20  
مسار واحد يغطي: شريط الأفراد + ملخص الفرد + الإجمالي + جدول الوجبات.

```text
GET /api/v1/customer/family-subscription-flow/summary
GET /api/v1/customer/family-subscription-flow/summary?memberId={guid}
```

Auth: `Authorization: Bearer <customer JWT>`

---

## خطأ شائع: `payment-methods?amount=0.0` → `subscription_flow.invalid_amount`

الـ API يرفض `amount <= 0`.

**الدفع مفتوح** لما كل فرد عنده خطة كاملة قابلة للتسعير:
`programId` + `bundleId` + `tier` + `durationDays`  
(حتى لو العنوان/العمر ناقصين و`setupStatus` مش Ready).

```dart
if (summary.finalPrice <= 0 || summary.canCheckout != true) {
  // متستدعیش payment-methods — اعرض checkoutBlockReason
  return;
}
await dio.get('.../payment-methods', queryParameters: {
  'amount': summary.finalPrice, // مش 0.0
});
```

لو مفيش عنوان محفوظ للمدير: السيرفر ينشئ **عنوان مؤقت** تلقائي وقت `POST /checkout` ويكمل الدفع. يُفضّل بعدين تحديث العنوان من `POST /customers/me/addresses`.

---

## الفكرة

- طلب واحد يعبّي شاشة الدفع كلها.
- تبديل فرد في الشريط = نفس الـ GET مع `memberId` الجديد.
- يتغيّر **ملخص الفرد** فقط؛ **الإجمالي** و**الجدول** ثابتين لنفس العائلة.

```text
بدون memberId  → يختار المدير تلقائيًا
مع memberId    → selectedMember = الفرد ده
```

---

## ربط التصميم ↔ الـ API

| جزء الشاشة | منين تاخد الداتا |
| --- | --- |
| شريط الأفراد (أسماء + جاهز) | `members[]` — جاهز لو `setupStatus == 3` |
| ملخص الاشتراك تحت الشريط | `selectedMember` |
| تفاصيل الأسعار (للكل) | `subtotal` · `familyDiscountPercent` · `familyDiscountAmount` · `finalPrice` · `currencyCode` |
| جدول «نظرة عامة على العائلة» | `mealOverview[]` |
| طرق الدفع | مش من الملخص → `GET .../payment-methods?amount={finalPrice}` |
| تأكيد الدفع | `POST .../checkout` (لما `canCheckout == true` — خطة كاملة لكل فرد، حتى لو مش Ready) |

**مفيش سطر ضريبة** في الـ API. الموك اللي فيه 15% تجاهله.

---

## عند تبديل الفرد (مهم)

```dart
Future<void> onMemberChipTap(String memberId) async {
  final res = await dio.get(
    '/api/v1/customer/family-subscription-flow/summary',
    queryParameters: {'memberId': memberId},
  );
  // حدّث selectedMember من res.data['selectedMember']
  // subtotal / finalPrice / mealOverview زي ما هما
}
```

متحسبش ملخص الفرد من cache محلي لوحده — خده من الرد.

---

## `selectedMember` — ملخص الفرد المعروض

| حقل | استخدام في الـ UI |
| --- | --- |
| `displayName` | اسم الفرد |
| `isManager` / `roleAr` | «عضو رئيسي» لو المدير (`roleAr` جاهز) |
| `isReady` / `setupStatus` | شارة جاهز |
| `programNameAr` / `programNameEn` | البرنامج |
| `bundleNameAr` / `bundleNameEn` | باقة الوجبات |
| `durationDays` / `durationLabelAr` | المدة (مثل `26 يوم`) |
| `selectedTier` / `tierNameAr` | التصنيف (1 أساسي · 2 بلاتينيوم · 3 إيليت) |
| `includeFriday` | لو حابب تظهر الجمعة |
| `breakfast`…`dinner` / `mealsPerDay` | اختياري في كارت الملخص |
| `memberPrice` | سعر خط الفرد قبل خصم العائلة |

---

## الأسعار (للكل — ثابتة مع تبديل الفرد)

| حقل | المعنى |
| --- | --- |
| `subtotal` | مجموع أسعار الأفراد |
| `familyDiscountPercent` | نسبة الخصم (مثل `0.08` = 8%) |
| `familyDiscountAmount` | قيمة الخصم |
| `finalPrice` | المطلوب دفعه |
| `currencyCode` | `KWD` |

اعرض الخصم كنسبة مئوية: `(familyDiscountPercent * 100).round()` → «خصم العائلة (8%)».

---

## `mealOverview[]` — الجدول بالظبط

كل صف = فرد:

| حقل | عمود الجدول |
| --- | --- |
| `displayName` | الفرد |
| `breakfast` | إفطار ✓ / — |
| `lunch` | غداء |
| `snack` | سناك |
| `dinner` | عشاء |
| `mealsPerDay` | عدد الوجبات (مثل «+4 وجبات») |
| `isReady` | اختياري جنب الاسم |
| `isManager` | تمييز المدير لو حابب |

`true` = علامة صح، `false` = شرطة/فاضي.

---

## الدفع

1. لو `canCheckout != true` → عطّل الزر واعرض `checkoutBlockReason` إن وجد.
2. `GET /payment-methods?amount={finalPrice}` (نفس `finalPrice` من الملخص).
3. `POST /checkout` مع `paymentMethodId` + callback/error URLs.
4. افتح `paymentUrl` في WebView.

---

## Checklist الموبايل (الجزء ده بس)

- [ ] شاشة الدفع تعتمد على `GET /summary` فقط للملخص/الإجمالي/الجدول
- [ ] تبديل chip → `summary?memberId=`
- [ ] شريط الأفراد من `members` + جاهز من `setupStatus == 3`
- [ ] كارت الملخص من `selectedMember` (أسماء جاهزة عربي)
- [ ] الأسعار من حقول الإجمالي — بدون ضريبة وهمية
- [ ] الجدول من `mealOverview` (4 أعلام + `mealsPerDay`)
- [ ] طرق الدفع من endpoint منفصل بـ `finalPrice`
- [ ] زر التأكيد مربوط بـ `canCheckout`
