# فلو اشتراك العميل للموبايل

Base URL: `https://maelmate.runasp.net/api/v1`

كل المسارات التالية تحتاج Customer access token. أرسل:

```http
Authorization: Bearer <customer-access-token>
Accept-Language: ar
```

## ترتيب الشاشات والـendpoints

| الشاشة | Endpoint |
|---|---|
| نوع الاشتراك | `GET /customer/subscription-flow/types` |
| اختيار البرنامج | `GET /customer/subscription-flow/programs` |
| اختيار الباقة | `GET /customer/subscription-flow/programs/{programId}/bundles` |
| المدة والجمعة | `GET /customer/subscription-flow/programs/{programId}/bundles/{bundleId}/durations?includeFriday=` |
| التصنيف | `GET /customer/subscription-flow/programs/{programId}/bundles/{bundleId}/tiers?includeFriday=` |
| شبكة استعراض مطاعم التصنيف | `GET /customer/subscription-flow/programs/{programId}/bundles/{bundleId}/tiers/{tier}/restaurants` |
| تفاصيل مطعم من شبكة التصنيف | `GET /customer/subscription-flow/programs/{programId}/bundles/{bundleId}/restaurants/{restaurantId}/details` |
| تفاصيل وجبة من شبكة التصنيف | `GET /customer/subscription-flow/programs/{programId}/bundles/{bundleId}/restaurants/{restaurantId}/meals/{mealId}` |
| بطاقة مطعم واحد من التصنيف | `GET /customer/subscription-flow/programs/{programId}/bundles/{bundleId}/restaurants/{restaurantId}` |
| ملخص الباقة بعد اختيار المدة والتصنيف | `GET /customer/subscription-flow/summary?programId&bundleId&tier&durationDays&includeFriday` بدون `restaurantId` |
| استعراض مطاعم التصنيف المختار | `GET /customer/subscription-flow/restaurants?...context` |
| تفاصيل المطعم ووجبات المنيو | `GET /customer/subscription-flow/restaurants/{restaurantId}?...context` |
| تفاصيل الوجبة | `GET /customer/subscription-flow/restaurants/{restaurantId}/meals/{mealId}?...context` |
| طرق الدفع | `GET /customer/subscription-flow/payment-methods?amount={finalPrice}` |
| بدء الدفع | `POST /customer/subscriptions/checkout` |

ربط زر «استعراض المطاعم» وكروت الشبكة: [`mobile-tier-restaurants-browse.md`](./mobile-tier-restaurants-browse.md).

بعد المدة والتصنيف افتح شاشة الملخص مباشرة. لا ترسل `restaurantId` في `summary` في هذه الخطوة. `tiers.restaurants` معاينة لوجوهات فقط (`restaurantId` + `logoUrl`). زر «استعراض المطاعم» على كرت التصنيف يستدعي `GET .../tiers/{tier}/restaurants` ويمكن إرسال `restaurantIds` من `tiers[].restaurants`. كل كرت يرجع الاسم واللوجو والتقييم وعدد الوجبات و`bundleMealCount` وتصنيفات المطعم `{ logoUrl, tier }` فقط بدون أسماء. القائمة الكاملة للأهلية والوجبات تبقى في `GET /customer/subscription-flow/restaurants` بعد اختيار التصنيف والمنطقة والتاريخ.

`summary` لا يأخذ `startDate` ولا `regionId` ولا `restaurantId`. الباك يضع `startDate` تلقائيًا بتاريخ الإنشاء (اليوم UTC). الحقول المطلوبة:

```text
programId={uuid}
bundleId={uuid}
tier=Basic|Platinum|Elite
durationDays=6|12|26
includeFriday=false
```

مسارات المطاعم ما زالت تحتاج `context` الكامل:

```text
programId={uuid}
bundleId={uuid}
tier=Basic|Platinum|Elite
durationDays=6|12|26
startDate=YYYY-MM-DD
regionId={uuid}
includeFriday=false
```

زر «استعراض المطاعم» على كرت التصنيف يستدعي `GET .../tiers/{tier}/restaurants`. تفاصيل المطعم ووجبات المنيو بعد اختيار المنطقة والتاريخ تبقى على `GET /customer/subscription-flow/restaurants/{restaurantId}` مع الـcontext الكامل. ملخص الباقة لا يرجع `restaurantId` ولا اسم المطعم؛ `restaurantCount` عدد مطاعم التصنيف. `slotRequirements` فيها `slot` و`nameAr` و`nameEn` و`count`.

`includes` قسم «يشمل اشتراكك». اعرض `titleAr`/`titleEn` ثم `items[].textAr`/`textEn` كنقاط. النصوص محسوبة من الباقة الحقيقية: عدد الوجبات اليومية مع أسماء الـslots، عدد مطاعم التصنيف المختار، أيام الخدمة، وعدد الوجبات المعتمدة إن وُجدت. لا تثبت «أكثر من 100 مطعم» أو نصوص تسويقية.

مثال:

```json
"includes": {
  "titleAr": "يشمل اشتراكك",
  "titleEn": "Your subscription includes",
  "items": [
    {
      "code": "DAILY_MEALS",
      "textAr": "وجبتان يوميًا (الغداء - سناك)",
      "textEn": "2 meals daily (Lunch · Snack)"
    },
    {
      "code": "RESTAURANTS",
      "textAr": "مطعم واحد في هذا التصنيف",
      "textEn": "1 restaurant in this classification"
    },
    {
      "code": "SERVICE_DAYS",
      "textAr": "15 يوم خدمة",
      "textEn": "15 service days"
    },
    {
      "code": "MEAL_VARIETY",
      "textAr": "42 وجبة معتمدة للاختيار",
      "textEn": "42 approved meals to choose from"
    }
  ]
}
```

تفاصيل شاشتي المدة والتصنيف: [mobile-duration-tier-flow.md](./mobile-duration-tier-flow.md). لا تستخدم `GET /customer/subscription-flow/configuration`.

## قواعد مهمة للموبايل

- استخدم IDs والقيم المرجعة من الخطوة السابقة؛ لا تثبت برنامجًا أو باقة أو سعرًا داخل التطبيق.
- تعرض قائمة المطاعم المطاعم المؤهلة فقط حسب المنطقة والمنيو والسعة والتصنيف والسعر والحساسية والتفضيلات.
- تفاصيل المطعم والوجبات تُحل من Menu Plan المعتمدة لتواريخ الاشتراك، وليست من قائمة المطعم الحية العامة.
- `isSelectable=false` مع `blockReason` يعني أن الوجبة تتعارض مع حساسية أو تفضيل العميل ولا يجوز اختيارها.
- `presentAllergenCodes` و`mayContainAllergenCodes` كلاهما Hard Block.
- `ingredients` تحتوي بلد المنشأ والمورد وحالة المحلي والحلال عندما يسمح المطعم بإظهار المنشأ.
- استخدم `finalPrice` المرجع من `summary` لاستدعاء `payment-methods` ولعرض المبلغ النهائي.
- لا تجمع بيانات البطاقة داخل MealMate. بعد `checkout` افتح `paymentUrl` في WebView/External Browser ثم انتظر نجاح webhook وتحقق من Calendar.
- اختيار `FAMILY` يستخدم اشتراكًا أساسيًا، ثم تضاف أفراد العائلة بعد تفعيل الدفع، بحد أقصى 4 أفراد.

ربط بوابة الدفع فقط: [`mobile-checkout-flow.md`](./mobile-checkout-flow.md).

## بدء الـCheckout

```json
{
  "programId": "b59a2251-ba7f-4a78-a6bb-d43defa2e4d2",
  "bundleId": "a6497534-efb2-4744-b190-9ac81a5b5102",
  "tier": "Basic",
  "durationDays": 6,
  "callbackUrl": "mealmate://payment/success",
  "errorUrl": "mealmate://payment/error",
  "includeFriday": false,
  "paymentMethodId": 1,
  "correlationId": "mobile-generated-id"
}
```

لا ترسل `restaurantId` ولا `regionId` ولا `startDate` ولا `deliveryAddress`. الباك يملّيهم من عنوان العميل وأول مطعم مؤهل، و`startDate` = تاريخ إنشاء الاشتراك (اليوم UTC). الرد فيه القيم المحلولة مع `paymentUrl`.

استجابة النجاح تحتوي `subscriptionId`, `paymentUrl`, `holdExpiresAtUtc`, `finalPrice`, `priceSnapshotId`, `restaurantPoolSnapshotId` ونسخة المنيو لكل تاريخ.

## ما بعد الرجوع من الدفع

الـcallback لا يكفي لإثبات الدفع. الموبايل يعرض حالة انتظار قصيرة ويستعلم عن:

```http
GET /customer/subscriptions/{subscriptionId}/calendar
```

- `200`: الدفع والـFulfillment والـSnapshots اكتملت.
- `409 calendar.fulfillment_not_ready`: استمر في الانتظار مع backoff؛ لا تنشئ Checkout جديدًا مباشرة.
- لا تعتبر وجود `paymentId` في callback دليلاً نهائيًا؛ تأكيد MyFatoorah يتم من الباك والـwebhook.

## إضافات العائلة بعد التفعيل

```http
GET    /family_subscription/{subscriptionId}/members
POST   /family_subscription/members
DELETE /family_subscription/members/{memberId}
```

واجهة إدارة أفراد العائلة تأتي بعد نجاح الدفع، ولا تغيّر Restaurant Pool أو Meal Snapshots المثبتة وقت الاشتراك.
