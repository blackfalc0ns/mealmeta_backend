# 3 — العنوان وبوابات البروفايل

بدون هذه الخطوات الملخص/الـcheckout يُرفضان.

## الترتيب الإجباري قبل الملخص

1. عنوان توصيل افتراضي فيه `regionId` (Area من الكتالوج).
2. تأكيد الحساسية (`PUT .../food-preferences/allergies`) حتى لو «لا يوجد».
3. تأكيد التفضيلات الغذائية (`PUT .../food-preferences/dietary`) حتى لو «لا يوجد».

تحقق سريع:

```http
GET /api/v1/customer/subscription-flow/readiness
  ?programId&bundleId&tier&durationDays&includeFriday
```

| حقل | استخدام |
|-----|---------|
| `canOpenSummary` | هل تدخل الملخص؟ |
| `canCheckout` | هل الدفع متاح؟ |
| `nextAction` | الشاشة التالية (`ADDRESS` / `ALLERGIES` / `DIETARY_PREFERENCES` / `SUMMARY`) |
| `checkoutBlockReason` | سبب منع الدفع إن وُجد |

## العنوان والمنطقة — سبب فشل شائع جدًا

- اختيار منطقة **بدون مطاعم مؤهلة** → ملخص بـ `restaurantCount: 0` و`canCheckout: false` أو checkout بـ `restaurant.not_eligible`.
- مصدر المناطق:

```http
GET /api/v1/catalog/locations?countryCode=KW
```

خذ `governorates[].areas[].id` كـ `regionId`.

### ماذا يفعل الموبايل

- عند حفظ العنوان: تأكد أن `regionId` من قائمة Areas وليس اسمًا حرًا فقط.
- قبل الملخص: لو `canCheckout=false` بسبب المطاعم، اعرض «لا تتوفر مطاعم لمنطقتك» وزر تغيير العنوان.
- لا تعتمد على أن كل مناطق العاصمة مغطاة — التغطية حسب بيانات المطاعم.

## الحساسية والتفضيلات

- Hard block في التقويم لاحقًا: أي وجبة بـ `blockReason` أو أكواد حساسية لا تُختار.
- إكمال البوابة ≠ إرسال قائمة فارغة بدون تأكيد — اتبع عقد الـAPI الحالي لـ`confirmed` / `noneSelected` حسب الـendpoint المرفوع.

## اشتراك فردي قائم

- لو `checkout.existing_subscription` أو النوع `INDIVIDUAL.isAvailable=false`: لا تبدأ checkout فردي جديد.
- التحويل لعائلة له مستندات منفصلة تحت `docs/mobile/individual-to-family-conversion/`.
