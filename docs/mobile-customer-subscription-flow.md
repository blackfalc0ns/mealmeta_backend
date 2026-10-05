# فلو اشتراك العميل للموبايل

> **نواقص وإصلاحات مرتبة لمبرمج Flutter:** [`mobile/individual-mobile-gaps/README.md`](./mobile/individual-mobile-gaps/README.md)

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
| ملخص الباقة بعد اختيار المدة والتصنيف | `GET /customer/subscription-flow/summary?programId&bundleId&tier&durationDays&startDate&includeFriday` بدون `restaurantId` |
| استعراض مطاعم التصنيف المختار | `GET /customer/subscription-flow/restaurants?...context` |
| تفاصيل المطعم ووجبات المنيو | `GET /customer/subscription-flow/restaurants/{restaurantId}?...context` |
| تفاصيل الوجبة | `GET /customer/subscription-flow/restaurants/{restaurantId}/meals/{mealId}?...context` |
| طرق الدفع | `GET /customer/subscription-flow/payment-methods?amount={finalPrice}` |
| بدء الدفع | `POST /customer/subscriptions/checkout` |

ربط زر «استعراض المطاعم» وكروت الشبكة: [`mobile-tier-restaurants-browse.md`](./mobile-tier-restaurants-browse.md).

بعد المدة والتصنيف افتح شاشة الملخص مباشرة. لا ترسل `restaurantId` في `summary` في هذه الخطوة. `tiers.restaurants` معاينة لوجوهات فقط (`restaurantId` + `logoUrl`). زر «استعراض المطاعم» على كرت التصنيف يستدعي `GET .../tiers/{tier}/restaurants` ويمكن إرسال `restaurantIds` من `tiers[].restaurants`. كل كرت يرجع الاسم واللوجو والتقييم وعدد الوجبات و`bundleMealCount` وتصنيفات المطعم `{ logoUrl, tier }` فقط بدون أسماء. القائمة الكاملة للأهلية والوجبات تبقى في `GET /customer/subscription-flow/restaurants` بعد اختيار التصنيف والمنطقة والتاريخ.

`summary` و`checkout` يأخذان `startDate` = يوم اختيار العميل / يوم الدفع (تقويم الكويت، ≥ اليوم). الباك يحسب **أول يوم توصيل** = `chosenStartDate + 3 أيام` (تخطي الجمعة إذا `includeFriday=false`). مثال: دفع/اختيار يوم 27 → أول وجبة 30.

الرد يعيد:
- `chosenStartDate` — يوم الاختيار/الدفع
- `startDate` — أول توصيل
- `serviceDates` / `endDate` — من أول التوصيل
- `restaurantRepetition` — حد تكرار نفس المطعم خلال المدة:

```json
"restaurantRepetition": {
  "limit": 6,
  "restaurantCount": 5,
  "durationDays": 26
}
```

`limit` = أقصى عدد أيام يُسمح فيها بتكرار نفس المطعم (من `ceil(durationDays / restaurantCount)` مع حدود داخلية في الباك؛ ولو مطعم واحد فالحد = مدة الاشتراك). لا تعرض حدود داخلية للمستخدم — فقط `limit`.

الحقول المطلوبة للملخص:

```text
programId={uuid}
bundleId={uuid}
tier=Basic|Platinum|Elite
durationDays=6|12|26
startDate=YYYY-MM-DD
includeFriday=false
```

أخطاء التاريخ: `checkout.start_date_required` | `checkout.start_date_invalid`.

مسارات المطاعم ما زالت تحتاج `context` الكامل — استخدم `startDate` = أول يوم توصيل من رد الملخص (`summary.startDate`):

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

## Pagination للقوائم (اختيارية مع عقد قديم محفوظ)

كل قوائم الفلو تدعم pagination. **العقد القديم محفوظ**: ما لم ترسل معاملات pagination، يبقى الرد بنفس الشكل السابق تمامًا. أرسل `page` أو `pageSize` (واحد منهما يكفي) لتفعيل الرد الجديد:

| القائمة | المعاملات |
|---|---|
| `GET .../programs` | `page`, `pageSize` |
| `GET .../programs/{programId}/bundles` | `page`, `pageSize` |
| `GET .../programs/{programId}/tiers/{tier}/restaurants` | `page`, `pageSize` |
| `GET .../programs/{programId}/bundles/{bundleId}/tiers/{tier}/restaurants` | `page`, `pageSize` |
| `GET .../summary` | `restaurantsPage`, `restaurantsPageSize` (تخص المطاعم فقط) |

الرد الجديد للقوائم المستقلة (programs وtier restaurants) هو مغلف Paged:

```json
{
  "items": [],
  "totalCount": 0,
  "page": 1,
  "pageSize": 20,
  "totalPages": 0,
  "hasNextPage": false,
  "hasPreviousPage": false
}
```

القواعد:
- `pageSize` الافتراضي 20 والحد الأقصى 100؛ `page` أقل من 1 تُعامل كـ1. قيمة غير رقمية في الـquery ترجع 400.
- صفحة بعد النهاية ترجع `200` مع `items` فارغة و`totalCount` الحقيقي — ليست خطأ.
- `totalCount` محسوب بعد الفلترة (البرامج المرئية فقط، مطاعم الأهلية الحقيقية) وليس حجم الصفحة.
- اتحاد الصفحات = المجموعة الكاملة بلا تكرار ما دامت البيانات ثابتة. استخدم `restaurantId`/`programId`/`bundleId` كمفتاح مع منع التكرار عند الدمج.
- اطلب كل قائمة بصفحاتها المستقلة؛ لا تعيد تحميل كل القوائم عند تغيير صفحة واحدة. أعد التحميل من الصفحة 1 عند تغيير program/bundle/tier/عنوان التوصيل أو أي سياق مؤثر، وتجاهل رد طلب قديم إذا تغير الاختيار أثناء تحميله (race guard).
- في `bundles`: يبقى الرد wrapper نفسه (`hasDeliveryAddress`, `hasAllergies`, `hasDietaryPreferences`, `isProfileComplete`, `bundles`) ويُضاف حقل `pagination` (بنفس حقول المغلف أعلاه بدون `items`) عند إرسال `page`/`pageSize`. حساب `isMostSelected` و`restaurantCount` وقوائم شعارات المعاينة يتم على المجموعة الكاملة دائمًا ولا يتأثر بالصفحات.
- في `summary`: يُضاف `restaurantsPagination` بجوار `restaurants` عند إرسال `restaurantsPage`/`restaurantsPageSize`. `restaurantCount` والأسعار و`canCheckout` و`restaurantRepetition` و`includes` تُحسب على كل المطاعم المؤهلة، والصفحة تقلص كروت `restaurants` المعروضة فقط.
- `payment-methods` بدون pagination — القائمة صغيرة.

## تصميم شاشة التصنيف (theme)

الباك يرجع اختيار المسؤول لتصميم شاشة اختيار التصنيف داخل `tiers[].theme` (نفس القيمة في كل عناصر tiers):

```json
"theme": { "designId": 2, "designName": "Aurora" }
```

- `designId` رقم التصميم وهو المعرّف المعلق مع فريق الموبايل — طابقه مع widget factory داخل التطبيق. `designName` للفهم والتشخيص فقط.
- `theme: null` يعني لم يُختر تصميم — استخدم التصميم الحالي. تطبيق قديم أو `designId` غير مدعوم يعرض تصميمه المتوافق ولا ينهار؛ ليس خطأ بيانات.
- التصميم نفسه ينفذه Flutter بالكامل؛ الباك لا يرسل ألوانًا ولا CSS.
- أعد جلب `tiers` (أو `configuration`) بعد فتح التطبيق/تحديث الإعدادات لالتقاط تغيير الاختيار.

## كود الخصم في الملخص (promoCode)

`summary` يقبل `promoCode` اختياريًا ويرجع كائن `coupon`:

```http
GET /customer/subscription-flow/summary?programId=..&bundleId=..&tier=Basic&durationDays=26&startDate=YYYY-MM-DD&promoCode=WELCOME10
```

```json
"coupon": {
  "code": "WELCOME10",
  "status": "valid",
  "offerId": "7831ce0d-...",
  "discountAmount": 10.000,
  "messageAr": "تم تطبيق الكوبون بنجاح",
  "messageEn": "Coupon applied successfully"
}
```

- `status` واحد من: `valid` | `not_found` | `inactive` | `not_started` | `expired` | `usage_exhausted` | `stacking_conflict` | `ineligible`. اعرض `messageAr`/`messageEn` حسب لغة التطبيق.
- الكوبون المرفوض ليس خطأ: الملخص يرجع `200` بسعر بدون خصم و`status` يوضح السبب. أظهر الكود كقابل للتعديل/الإزالة.
- **لا تجمع** `campaignDiscount` مع `coupon.discountAmount` — خصم العرض يظهر داخل `campaignDiscount` في محرك التسعير، و`coupon.discountAmount` يكرر المبلغ المطبق فعليًا لتسهيل العرض. المبلغ النهائي دائمًا `finalPrice` من الخادم.
- تقييم الملخص قراءة فقط: لا يستهلك استخدام الكوبون ولا يحجزه. السعر مضمون فقط من `checkout`.
- تطبيق/إزالة الكوبون أو تغيير المدة يعيد استدعاء `summary` ثم `payment-methods` بالمبلغ الجديد. لا تحسب الخصم داخل التطبيق.

## بدء الـCheckout

- إذا كان للعميل اشتراك فردي حالي (PendingPayment/Paid/Scheduled/Active/Frozen/SuspendedForReview) يُرفض checkout فردي جديد بـ `409 checkout.existing_subscription`.
- نفس الكود يُرفض أيضًا عند وجود مجموعة عائلية للعميل كمدير بحالة Draft / CheckoutPending / Active / Frozen.
- عند وجود اشتراك فردي حالي: `GET /customer/subscription-flow/types` يجعل `INDIVIDUAL.isAvailable = false` مع `disabledReason = checkout.existing_subscription`، و`FAMILY.requiresConversion = true` مع `conversionEndpoint`؛ استخدم `GET /customer/subscriptions/current/conversion-options` ثم `POST /customer/family-subscription-flow/convert-from-individual`. لإلغاء المسودة: `POST /customer/family-subscription-flow/abandon-draft`. التفاصيل: [`mobile/family-subscription-api.md`](./mobile/family-subscription-api.md).

```json
{
  "programId": "b59a2251-ba7f-4a78-a6bb-d43defa2e4d2",
  "bundleId": "a6497534-efb2-4744-b190-9ac81a5b5102",
  "tier": "Basic",
  "durationDays": 6,
  "startDate": "2026-08-27",
  "callbackUrl": "mealmate://payment/success",
  "errorUrl": "mealmate://payment/error",
  "includeFriday": false,
  "paymentMethodId": 1,
  "correlationId": "mobile-generated-id",
  "promoCode": "WELCOME10"
}
```

`promoCode` اختياري. أرسله بنفس القيمة التي أظهرها الملخص؛ الباك يعيد التحقق ويحسب السعر بنفسه ولا يقبل مبلغًا من التطبيق. إذا صار الكود غير صالح بين الملخص والدفع يُرفض الطلب بـ`400 checkout.coupon_rejected` مع تفاصيل الكوبون قبل إنشاء أي دفع — أعد عرض الملخص بنفس الرسالة. تفاصيل كود الخصم ودورة الدفع: [`mobile-checkout-flow.md`](./mobile-checkout-flow.md).

أرسل `startDate` = يوم اختيار العميل (نفس قيمة الملخص). لا ترسل `restaurantId` ولا `regionId` ولا `deliveryAddress`. الباك يملّي المطعم/المنطقة من عنوان العميل، و`startDate` في الرد = أول يوم توصيل (`chosenStartDate + 3`). الرد فيه `chosenStartDate`, `startDate`, `serviceDates` مع `paymentUrl`.

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
