# تحديثات فلو الاشتراك الفردي — Pagination والتصميم وكود الخصم

> **تاريخ التحديث:** 2026-10-05 · **الحالة:** منفّذ على الباك، يُتوفر بعد النشر · العقد القديم محفوظ بالكامل — التطبيق الحالي يعمل بدون أي تعديل.

هذا الملف يشرح **3 تعديلات جديدة** على فلو الاشتراك الفردي والـcheckout:

| # | التعديل | الشاشات المتأثرة | خطر على التطبيق القديم |
|---|---|---|---|
| 1 | Pagination للقوائم | البرامج، الباقات، مطاعم التصنيف، مطاعم الملخص | صفر — بدون معاملات يبقى الرد القديم |
| 2 | اختيار تصميم شاشة التصنيف (theme) | شاشة التصنيف | صفر — حقل جديد، `null` = التصميم الحالي |
| 3 | كود الخصم (promoCode) من الملخص للدفع | الملخص، طرق الدفع، الدفع | صفر — كل الحقول اختيارية |

التفاصيل الكاملة لكل فلو تبقى في: [`mobile-customer-subscription-flow.md`](./mobile-customer-subscription-flow.md) و[`mobile-checkout-flow.md`](./mobile-checkout-flow.md).

---

## 1) Pagination — تقسيم القوائم لصفحات

### القاعدة الذهبية

**لا ترسل معاملات pagination = تحصل على العقد القديم تمامًا.** أرسل `page` أو `pageSize` (واحد يكفي) لتفعيل الرد الجديد:

| القائمة | المعاملات | شكل الرد الجديد |
|---|---|---|
| `GET /customer/subscription-flow/programs` | `page`, `pageSize` | مغلف Paged |
| `GET .../programs/{programId}/bundles` | `page`, `pageSize` | نفس الـwrapper + حقل `pagination` |
| `GET .../programs/{programId}/bundles/{bundleId}/tiers/{tier}/restaurants` | `page`, `pageSize` | مغلف Paged |
| `GET .../programs/{programId}/tiers/{tier}/restaurants` | `page`, `pageSize` | مغلف Paged |
| `GET /customer/subscription-flow/summary` | `restaurantsPage`, `restaurantsPageSize` | نفس كائن الملخص + حقل `restaurantsPagination` |

### مغلف Paged (للمسارات المستقلة)

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

*(مثال لعقد قائمة فارغة، وليس بيانات حقيقية.)*

### قواعد لازم تحترمها في التطبيق

- **الـdefaults:** `pageSize` = 20 والحد الأقصى 100 (أعلى من كده يرجع 100 تلقائيًا)، `page` أقل من 1 تُعامل كـ1. قيمة غير رقمية في الـquery ترجع `400`.
- **صفحة بعد النهاية ليست خطأ:** ترجع `200` مع `items` فارغة و`totalCount` الحقيقي. عندك زر "المزيد" يختفي اعتمادًا على `hasNextPage`.
- **`totalCount` = المجموعة الكاملة المؤهلة** بعد الفلترة، مش حجم الصفحة. في الملخص: `restaurantCount` يفضل إجمالي المطاعم حتى لو الصفحة فيها كرت واحد.
- **اتحاد الصفحات = المجموعة الكاملة بلا تكرار** ما دامت البيانات ثابتة. عند دمج الصفحات صفّي على المعرّف (`programId` / `bundleId` / `restaurantId`) احتياطًا لأي تغيير كتالوج بين طلبين.
- **الترتيب ثابت** من الباك (SortOrder ثم Id للبرامج والباقات، وترتيب العرض + RestaurantId للمطاعم) — لا تعيد ترتيب الصفحات بنفسك قبل الدمج.

### قواعد تجميع الصفحات وإعادة التحميل

- كل قائمة لها pagination مستقلة: تحميل أولي، "تحميل المزيد"، إعادة محاولة، ونهاية القائمة — لا تغيير صفحة في قائمة يعيد تحميل قائمة تانية.
- **أعد التحميل من الصفحة 1** عند تغيير `program` / `bundle` / `tier` / عنوان التوصيل أو أي سياق مؤثر على الفلترة.
- **تجاهل رد الطلب القديم:** لو المستخدم غيّر الاختيار وطلب قديم لسه راجع، ارمِ الرد (احفظ رقم الطلب/الإصدار مع كل request وقارنه عند الوصول).
- حالات الواجهة المطلوبة: loading أولي، loading إضافي (append)، فشل مع retry، و"وصلت لنهاية القائمة".

### خصيصًا في `bundles`

الرد يبقى الـwrapper نفسه (`hasDeliveryAddress`, `hasAllergies`, `hasDietaryPreferences`, `isProfileComplete`, `bundles`) ويُضاف حقل `pagination`:

```json
"pagination": { "totalCount": 6, "page": 1, "pageSize": 2, "totalPages": 3, "hasNextPage": true, "hasPreviousPage": false }
```

- شارة `isMostSelected` وشارة الباقة و`restaurantCount` وقوائم شعارات المعاينة (`restaurants`) **تُحسب على كل الباقات دائمًا** — الصفحات لا تؤثر عليها، فلا تحاول إعادة حسابها محليًا حسب الصفحة.

### خصيصًا في `summary`

- أرسل `restaurantsPage` / `restaurantsPageSize` **غير** `page`/`pageSize` — الصفحات هنا تخص كروت المطاعم فقط، مش الملخص كله.
- الرد يضيف `restaurantsPagination` (نفس حقول `pagination` أعلاه بلا `items`) بجوار `restaurants`.
- الأسعار و`canCheckout` و`restaurantRepetition` و`includes` تُحسب على **كل** المطاعم المؤهلة، والصفحة تقلص الكروت المعروضة فقط.
- المطاعم في الملخص قد تختلف عن شاشة تصفح التصنيف لأن الأهلية في الملخص أوسع بسياق إضافي — لا تفترض تطابق المجموعتين.
- `payment-methods` بدون pagination (القائمة صغيرة) — بس لازم تُعاد بالمبلغ الصحيح (شوف قسم الكوبون تحت).

---

## 2) اختيار تصميم شاشة التصنيف (theme)

المسؤول يختار من لوحة الإدارة **تصميم كامل** ينفذه Flutter (أرقام وأسماء التصاميم معلنة مسبقًا مع فريق الموبايل). الباك لا يرسل ألوان ولا CSS — يرسل الاختيار فقط.

**أين يظهر:** في رد `tiers` داخل كل عنصر، وفي `configuration`:

```json
"theme": { "designId": 2, "designName": "Aurora" }
```

### قواعد التطبيق

- **التصاميم كتالوج مغلق في الباك (`TierThemeDesign` enum)** — الأرقام المعتمدة حاليًا:

  | designId | designName |
  |---|---|
  | `1` | `Classic` |
  | `2` | `Aurora` |

  `designName` مشتق من الباك دائمًا من الكتالوج — مش نص مدخل. المسؤول يختار من القائمة دي فقط، وأي رقم غير معروف يُرفض عند الحفظ، وأي قيمة قديمة غير معروفة ترجع `theme: null`.
- **`designId` هو المعرّف الرسمي للتصميم** — طابقه مع widget factory داخل التطبيق. `designName` للفهم والتوثيق والتشخيص فقط.
- إضافة تصميم جديد = إضافة قيمة **في آخر** الـenum في الباك (لا إعادة ترقيم ولا حذف قيمة صدرت قبل كده) + شحن الـwidget في إصدار Flutter.
- اختيار واحد للشاشة كلها (مش لكل Basic/Platinum/Elite) — كل عناصر `tiers` تعكس نفس القيمة.
- **`theme: null`** = لم يُختر تصميم — استخدم التصميم الحالي الأساسي. لا تختلق تصميمًا افتراضيًا من عندك.
- **تطبيق قديم أو `designId` غير مدعوم** = يعرض تصميمه المتوافق ويعمل طبيعي، بدون crash. هذا fallback مقصود وليس خطأ بيانات.
- أعد جلب الإعداد عند فتح التطبيق / تحديث الإعدادات لتلتقط تغيير الاختيار — تغيير الإعداد المحفوظ يغيّر التصميم على التطبيق الذي يدعمه بعد إعادة الجلب.

---

## أسماء العنوان باللغتين (governorate / area / block)

الـ`address` في رد `summary` و`readiness` أصبح يرجع أسماء المنطقة/المحافظة/القطعة **بالعربي والإنجليزي** محسومة live من كتالوج المناطق (نفس المصدر اللي بيستخدمه تطبيقك في شاشة اختيار العنوان):

```json
"address": {
  "id": "79f645b0-...",
  "label": "المنزل",
  "addressLine": "حولي, السالمية, قطعة 4",
  "governorate": "حولي",
  "area": "السالمية",
  "block": "قطعة 4",
  "regionId": "20344d90-...",
  "governorateNameAr": "حولي",
  "governorateNameEn": "Hawalli",
  "areaNameAr": "السالمية",
  "areaNameEn": "Salmiya",
  "blockNameAr": "قطعة 4",
  "blockNameEn": "Block 4"
}
```

- اعرض `*NameAr` / `*NameEn` حسب لغة الواجهة بدل الحقول القديمة `governorate` / `area` / `block`.
- الحقول القديمة محفوظة للتوافق (نص snapshot وقت إنشاء العنوان) — ما تعتمدش عليها للعرض.
- `label` و`addressLine` نص المستخدم نفسه زي ما كتبه — يظهر كما هو في أي لغة (ده طبيعي زي أي app).
- لو `regionId`/`blockRegionId` مش مربوطين بكتالوج المناطق، الحقول الجديدة ترجع `null` — fallback للحقل القديم.

---

## 3) كود الخصم (promoCode) — من الملخص للدفع

الكوبونات تأتي من **العروض (Offers)** التي يديرها المسؤول. المستخدم يكتب الكود، والباك يقيّمه ويعيد التسعير بنفسه — **التطبيق لا يحسب أي خصم محليًا أبدًا**.

### في الملخص

`summary` يقبل `promoCode` اختياريًا ويرجع كائن `coupon`:

```http
GET /customer/subscription-flow/summary?programId=..&bundleId=..&tier=Basic&durationDays=26&startDate=YYYY-MM-DD&promoCode=WELCOME10
```

```json
"coupon": {
  "code": "WELCOME10",
  "status": "valid",
  "offerId": "7831ce0d-8b1c-4d9e-9d9e-1f2a3b4c5d6e",
  "discountAmount": 10.000,
  "messageAr": "تم تطبيق الكوبون بنجاح",
  "messageEn": "Coupon applied successfully"
}
```

### حالات الكوبون (`status`) والرسائل

| status | المعنى | messageAr | messageEn |
|---|---|---|---|
| `valid` | مُطبّق | تم تطبيق الكوبون بنجاح | Coupon applied successfully |
| `not_found` | غير موجود | كود الخصم غير موجود | This coupon code does not exist |
| `inactive` | غير مُفعّل | كود الخصم غير مُفعّل حاليًا | This coupon code is not active |
| `not_started` | لم يبدأ بعد | لم يبدأ تاريخ صلاحية كود الخصم بعد | This coupon code is not valid yet |
| `expired` | منتهي | انتهت صلاحية كود الخصم | This coupon code has expired |
| `usage_exhausted` | استُنفد | تم استنفاد عدد مرات استخدام كود الخصم | This coupon code has reached its usage limit |
| `stacking_conflict` | تعارض جمع | لا يمكن الجمع بين كود الخصم والعرض الحالي | This coupon code cannot be combined with the current offer |
| `ineligible` | غير مؤهل | كود الخصم غير مؤهل لهذا الاشتراك | This coupon code is not eligible for this subscription |

- `code` يرجع دائمًا بصيغة موحدة (UPPERCASE بعد إزالة المسافات) — اعرضه كما رجع.
- **الكوبون المرفوض ليس خطأ:** الملخص يرجع `200` بسعر بدون خصم و`status` يوضح السبب. اعرض الرسالة واترك الكود قابلًا للتعديل/الإزالة — لا تحجب الشاشة.
- **تحذير مهم:** خصم العرض يظهر داخل حقل `campaignDiscount` في محرك التسعير، و`coupon.discountAmount` يكرر المبلغ المطبق فعليًا. **لا تجمع `campaignDiscount` + `coupon.discountAmount`** — المبلغ النهائي دائمًا `finalPrice` من الخادم.
- عرض الملخص **قراءة فقط**: لا يستهلك استخدام الكوبون ولا يحجزه. السعر مضمون فقط من رد الـcheckout.

### دورة الإدخال في الواجهة

1. المستخدم يكتب الكود → أعد استدعاء `summary` بـ`promoCode` → اعرض `coupon` والسعر الجديد.
2. الإزالة → أعد `summary` بدون `promoCode`.
3. تغيير المدة أو الجمعة مع كوبون موجود → أعد `summary` بنفس الكود.
4. بعد كل تغيير في السعر → أعد جلب `payment-methods` بالمبلغ الجديد (`GET .../payment-methods?amount={finalPrice}`) — `serviceCharge` و`totalAmount` يتغيران مع المبلغ.

### في الدفع (`POST /customer/subscriptions/checkout`)

أضف `promoCode` اختياريًا في الجسم بنفس القيمة المعروضة في الملخص:

```json
{
  "programId": "b59a2251-...",
  "bundleId": "a6497534-...",
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

- الباك يعيد التحقق ويحسب المبلغ بنفسه؛ `finalPrice` في الرد هو المعتمد ولا يُقبل مبلغ من التطبيق.
- إذا صار الكود غير صالح بين الملخص والدفع يُرفض الطلب **قبل إنشاء أي اشتراك أو جلسة دفع**:

```json
{
  "error": "checkout.coupon_rejected",
  "coupon": {
    "code": "WELCOME10",
    "status": "expired",
    "offerId": null,
    "discountAmount": 0,
    "messageAr": "انتهت صلاحية كود الخصم",
    "messageEn": "This coupon code has expired"
  }
}
```

هذا `400` — أعد المستخدم للملخص بنفس الرسالة واسمح له بإزالة الكود وإعادة المحاولة.

- **السعر صفر بعد الخصم:** يُرفض بـ`400 checkout.coupon_full_discount_unsupported` — لا يوجد مسار دفع بمبلغ صفري حاليًا، فاعرض رسالة واضحة دون بدء الدفع.
- **استئناف دفع غير مكتمل (`resume`):** لا ينشئ خصمًا جديدًا — الكوبون المحجوز للاشتراك يُعاد التحقق منه ويُعاد التسعير منه. إذا لم يعد صالحًا يرجع `400 checkout.coupon_no_longer_valid` بنفس شكل `coupon` أعلاه قبل بدء الدفع. الاستئناف لا يحتاج إرسال الكود من التطبيق.
- استهلاك الكوبون: الملخص لا يحجزه؛ الـcheckout يسجّل **حجزًا** يتحول لاستخدام نهائي عند نجاح الدفع، ويُحرَّر تلقائيًا عند فشل الدفع أو انتهاء مهلة الحجز — فمجرد الضغط على checkout ثم الفشل لا يحرق استخدام الكوبون على العميل.

---

## ما لا يتغير

- كل الردود الحالية بدون معاملات pagination — نفس الأشكال الحرفية.
- `types`, `durations`, `tiers` (عدا إضافة `theme`), `configuration` (عدا إضافة `theme`), بطاقات المطعم والوجبات، `readiness`.
- فلو العائلة وسياسة الجدولة والأهلية والعنوان — بدون أي تغيير.
- الترتيب: `summary` → `payment-methods` → `checkout` → استعلام الـcalendar بعد الرجوع من الدفع.

## Checklist قبل الرفع

- [ ] صفحات كل قائمة تتجمع بلا تكرار، وزر "المزيد" مخفي عند `hasNextPage=false`.
- [ ] إعادة التحميل من الصفحة 1 عند تغيير البرنامج/الباقة/التصنيف/العنوان، والردود القديمة تُتجاهل عند تغيّر الاختيار أثناء التحميل.
- [ ] `theme` مطبقة مع fallback آمن لرقم غير مدعوم و`null`.
- [ ] الكوبون: تطبيق/إزالة يعيد `summary` ثم `payment-methods`؛ لا جمع بين `campaignDiscount` و`coupon.discountAmount`؛ لا حساب خصم محلي.
- [ ] `checkout.coupon_rejected` / `checkout.coupon_no_longer_valid` / `checkout.coupon_full_discount_unsupported` لكل منها رسالة واضحة وإعادة توجيه للملخص.
- [ ] تجربة على بيئة الاختبار بكوبون حقيقي: حالة `valid` وحالة مرفوضة، وآخر كوبون بطلبين متتاليين.
