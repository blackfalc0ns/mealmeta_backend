# دليل الربط: Pagination لمطاعم اليوم + إشعارات العميل ومؤشر غير المقروء

> **للموبايل (Flutter):** هذا الدليل يشرح كل ما تحتاجه لربط شاشات التقويم والإشعارات بالـAPI. كل العقود المذكورة هنا **مطبقة ومختبرة** على الكود، وستعمل بعد نشر النسخة الجديدة وتطبيق الـmigration على البيئة المنشورة.
>
> المصدر الكامل لفلو الاشتراك في [mobile-customer-subscription-flow.md](mobile-customer-subscription-flow.md).

## ما الجديد في هذا التحديث

| الميزة | أثرها على التطبيق |
|---|---|
| Pagination لمطاعم اليوم | تحميل تدريجي لقائمة مطاعم اليوم بدل قائمة واحدة كبيرة |
| مؤشر الإشعارات غير المقروءة | حقلان جديدان في جذر رد التقويم لعلم الجرس (Bell) |
| صندوق إشعارات العميل | 3 endpoints جديدة لعرض الإشعارات وقراءتها |
| إصلاح سجل Push | الإشعار يظهر دائمًا حتى لو فشل إرسال الـPush — لا تعتمدوا على نجاح الـPush |
| توثيق قواعد العرض | اللغة، التقييم null، الحساسية، نسبة السعرات — قواعد نهائية للواجهة |

---

## 1) Pagination لمطاعم اليوم

### الـendpoint

```
GET /api/v1/customer/subscriptions/{subscriptionId}/days/{date}/restaurants?query=&page=&pageSize=
```

| المعامل | النوع | الوضع |
|---|---|---|
| `query` | نص اختياري | بحث في `nameAr`/`nameEn` (تجاهل حالة الأحرف) — موجود مسبقًا |
| `page` | رقم اختياري | رقم الصفحة (يبدأ من 1) |
| `pageSize` | رقم اختياري | حجم الصفحة — الافتراضي 20 والحد الأقصى 100 |

### العقد القديم محفوظ

- **ما لم ترسل `page` أو `pageSize`:** يرجع الرد بكل المطاعم كما في الوضع القديم تمامًا (لكن الحقول الجديدة أدناه موجودة دائمًا في الرد).
- **إذا أرسلت أيًا منهما (واحد يكفي):** يتفعّل التقسيم.
- `page` أقل من 1 تُعامل كـ1، و`pageSize` أقل من 1 تعود إلى 20، و`pageSize` أكبر من 100 تقصر إلى 100.

### شكل الرد

```json
{
  "serviceDate": "2026-10-15",
  "limit": 8,
  "adminCeiling": 8,
  "restaurants": [
    {
      "restaurantId": "f8575d1f-…",
      "nameAr": "[تجربة] مطبخ دايت كير",
      "nameEn": "[Test] Diet Care Kitchen",
      "logoUrl": "…",
      "tier": "Basic",
      "usedCount": 8,
      "limit": 8,
      "remainingCount": 0,
      "isBusy": false,
      "isQuotaExhausted": true,
      "isEnabled": false,
      "disabledReason": "calendar.quota_exhausted",
      "safeMealCount": 4,
      "rating": null,
      "ratingCount": 0,
      "deliveryWindowStart": "08:00:00",
      "deliveryWindowEnd": "16:00:00",
      "deliveryTimeText": "8:00 ص - 4:00 م",
      "tierNameAr": "أساسي",
      "tierNameEn": "Basic",
      "tierName": "أساسي",
      "tierLogoUrl": null
    }
  ],
  "page": 1,
  "pageSize": 20,
  "totalCount": 2,
  "totalPages": 1,
  "hasNextPage": false,
  "hasPreviousPage": false
}
```

> **مهم:** حقول الـpagination الستة موجودة **دائمًا** في الرد (وهذا مختلف عن قوائم الفلو الأخرى التي تضيفها فقط عند الطلب). في الوضع غير المقسّم: `page: 1` و`pageSize = totalCount` (أو `pageSize: 1` إذا كانت القائمة فارغة).

### قواعد التقسيم

1. **الترتيب ثابت:** المطعم المختار مبدئيًا أولًا، ثم حسب الاسم الإنجليزي، ثم `restaurantId` عند تساوي الأسماء. اتحاد الصفحات = القائمة كاملة بلا تكرار، بشرط ألا تتغير البيانات بين الطلبين.
2. **صفحة بعد النهاية ترجع `200`** مع `restaurants` فارغة و`totalCount` الحقيقي — **ليست خطأ**. عاملها كـ"لا مزيد".
3. **أهلية المطعم لا تتأثر بالبحث أو الصفحات:** المنع (quota مستنفد / `isBusy` / عدم تغطية الحساسية / خارج منطقة التوصيل) يُحسب على **كل** مطاعم اليوم قبل البحث والتقسيم. يعني: قد يظهر مطعم `isEnabled: false` على صفحتك حتى لو البديل المتاح خارج الصفحة أو خارج نتائج البحث — **هذا مقصود**، فلا تعيدوا تفعيله محليًا.
4. **`limit` ليس حجم الصفحة:** هو حد تكرار المطعم داخل الاشتراك (يظهر في `usedCount`/`remainingCount`)، ولا علاقة له بـpagination.

### سيناريو الربط المقترح

- عند فتح شاشة اليوم: اطلب الصفحة 1 بحجم مناسب (مثلًا 20).
- عند Scroll للنهاية: إذا `hasNextPage == true` اطلب `page + 1` وأضف العناصر (استخدم `restaurantId` كمفتاح لمنع التكرار).
- عند تغيير **التاريخ** أو **نص البحث**: أعد التحميل من الصفحة 1 وتجاهل رد أي طلب قديم (race guard) — الترتيب قد يتغير.
- لا تطلب الصفحات بالتوازي؛ اطلبها بالتسلسل عند الحاجة.

---

## 2) مؤشر الإشعارات غير المقروءة (جرس الحساب)

رد `GET /api/v1/customer/subscriptions/{subscriptionId}/calendar` أضاف حقلين في الجذر:

```json
{
  "subscriptionId": "…",
  "programId": "…",
  "bundleId": "…",
  "requiredCount": 3,
  "slotRequirements": [],
  "isFulfillmentReady": true,
  "days": [],
  "hasUnreadNotifications": true,
  "unreadNotificationsCount": 2
}
```

القواعد:

- المصدر **صندوق إشعارات العميل** (أحداث فعلية)، وليس سجل إرسال Push.
- النطاق حاليًا **كل إشعارات الحساب** (جرس الحساب)، وليس اشتراكًا واحدًا — لا تفلتره محليًا على اشتراك معين.
- `hasUnreadNotifications` مشتقة من نفس العد (`count > 0`) — عاملها كحقل واحد منطقي، ولا تحسبوا العد من قائمة منفصلة.
- **`GET calendar` لا يقرأ الإشعارات أبدًا.** القراءة تتم فقط بأوامر PUT الصريحة (القسم التالي). جرب فتح التقويم مئة مرة — العدّاد لن يتغير.
- حدّثوا الجرس في هذه الحالات: بعد نجاح أمر قراءة، وعند عودة التطبيق للمقدمة (أعد جلب التقويم أو قائمة الإشعارات).

---

## 3) صندوق إشعارات العميل

### المسارات

| المسار | الغرض |
|---|---|
| `GET /api/v1/customers/me/notifications?page=1&pageSize=20` | قائمة الإشعارات، الأحدث أولًا، مغلف Paged |
| `PUT /api/v1/customers/me/notifications/{id}/read` | قراءة إشعار واحد — idempotent |
| `PUT /api/v1/customers/me/notifications/read-all` | قراءة الكل لعنوان الحساب الحالي |

### قائمة الإشعارات

الرد بنفس مغلف الـPaged المعروف:

```json
{
  "items": [
    {
      "id": "9f3c2b1e-…",
      "eventKey": "order.confirmed_by_restaurant",
      "subjectId": "0101f62e-…",
      "subscriptionId": "66c83ef5-…",
      "titleAr": "تم تأكيد طلبك 👨‍🍳",
      "titleEn": "Order Confirmed 👨‍🍳",
      "bodyAr": "المطعم أكد وجبتك ليوم 2026-10-15 وبدأ التجهيز.",
      "bodyEn": "The restaurant confirmed your meal for 2026-10-15.",
      "deepLink": "mealmate://orders",
      "createdAtUtc": "2026-10-05T09:00:00Z",
      "readAtUtc": null
    }
  ],
  "totalCount": 12,
  "page": 1,
  "pageSize": 20,
  "totalPages": 1,
  "hasNextPage": false,
  "hasPreviousPage": false
}
```

- `titleAr/titleEn/bodyAr/bodyEn` محفوظان **دائمًا بالاتنين** على السيرفر — اعرون حسب لغة التطبيق دون إعادة طلب.
- غير المقروء = `readAtUtc == null`.
- الترتيب بالأحدث (`createdAtUtc`) أولًا. `pageSize` بنفس قواعد القسم 1 (افتراضي 20، أقصى 100، normalization تلقائي).

### جدول الأحداث (`eventKey`) للعرض والتنقل

| eventKey | المعنى | deepLink | `subjectId` هو | `subscriptionId` |
|---|---|---|---|---|
| `subscription.payment_confirmed` | تفعيل الاشتراك | `mealmate://calendar` | subscriptionId | معبأ |
| `order.locked_72h` | يوم أُرسل للمطعم | `mealmate://calendar` | subscriptionDayId | معبأ |
| `order.confirmed_by_restaurant` | المطعم أكد الوجبة | `mealmate://orders` | subscriptionDayId | معبأ |
| `delivery.prep_24h` | التوصيل غدًا | `mealmate://calendar` | subscriptionDayId | معبأ |
| `delivery.out_for_delivery` | السائق في الطريق | `mealmate://orders` | subscriptionDayId | معبأ |
| `delivery.delivered` | تم التسليم | `mealmate://orders` | subscriptionDayId | معبأ |
| `influencer.promoted` | ترقية حساب لمؤثر | `mealmate://influencer-portal` | userId | **null** |
| `order.replaced` | تحويل تلقائي لمطعم بديل | `/orders/{dayId}` | subscriptionDayId | معبأ |

> **تنبيه:** `deepLink` في إشعار `order.replaced` يأتي بصيغة `/orders/{dayId}` (بدون scheme) — خليوا الـrouter يتعامل مع الصيغتين. و`subscriptionId` قد يكون **null** (إشعارات عامة للحساب) — لا تفترضوا أنه معبأ دائمًا.

### قراءة الإشعارات

```
PUT /api/v1/customers/me/notifications/{id}/read    → 204 No Content
PUT /api/v1/customers/me/notifications/read-all     → 204 No Content
```

- **Idempotent:** تكرار أمر القراءة لن يغيّر `readAtUtc` الأول ولا يفسد شيئًا — آمن للاستدعاء المتكرر.
- إشعار يخص مستخدمًا آخر → **404**. مستخدم غير عميل على المسارات → **403**. بدون توثيق → **401**.
- الأخطاء ترجع ProblemDetails فيها حقل `code` (مثل `common.not_found`) — راجعوا الـcode لا العنوان.

### سيناريو الربط المقترح (الجرس + شاشة الإشعارات)

1. افتح التطبيق / عد لمقدمته: اجلب التقويم وارفع الجرس من `unreadNotificationsCount` (أو اجلب قائمة الإشعارات إذا كانت الشاشة مفتوحة).
2. المستخدم فتح إشعارًا: اعمل `PUT read` محليًا فور العرض، ثم خفّض العدّاد محليًا 1 (إذا كان `readAtUtc == null` قبل القراءة).
3. زر "قراءة الكل": `PUT read-all` ثم صفّر الجرس محليًا.
4. لا تقلب حالة العنصر محليًا قبل نجاح الـPUT — عند الفشل أعد المحاولة أو أعد الجلب.

---

## 4) قواعد العرض: اللغة، التقييم، الحساسية، السعرات

### اللغة

- حقول `name` / `description` / `slotName` / `tierName` تتبع لغة الطلب (`Accept-Language: ar` أو `en`).
- لجلب العربي والإنجليزي معًا: أرسل `X-Bilingual: true` فيرجع `…Ar` و`…En` كلاهما.
- **لا يوجد نقص حقول إنجليزية** في شاشات التقويم — الرد العربي يحتوي العربية فقط بحكم العقد. لو ظهرت شاشة إنجليزية بنصوص عربية فالمشكلة في الهيدر المرسل، وليست في الـAPI.

### التقييم

- بدون مراجعات: `rating: null` و`ratingCount: 0` — اعرضوا «لا توجد تقييمات».
- **أزيلوا الـfallback إلى 4.5**: قيمة وهمية تُخدع المستخدم وتضلل ترتيب المطاعم. الـnull مقصودة من السيرفر.

### الحساسية

- أكواد `presentAllergenCodes` (الوجبة) و`allergenCodes` (المكونات) و`allergens/allergensAr/allergensEn/allergenDetails` (المجموع بأسماء معربة) تأتي من كتالوج المطعم: **الوجبة + مكوناتها مدمجة تلقائيًا**.
- **ملاحظة مثبتة من الكود:** المطاعم التجريبية الحالية مزروعة بأكواد حساسية فارغة (وجبة اسمها "فطور البيض والأفوكادو" بدون كود EGG). هذا نقص **بيانات كتالوج** وليس خلل API — سيُصحح على مستوى البيانات لاحقًا.
- قاعدتان للواجهة: **لا تستنتجوا الحساسية من اسم الوجبة**، و**لا تعتبروا المصفوفة الفارغة شهادة خلو من الحساسية**.

### السعرات

- `caloriesPercentage` في `nutritionTotals` والوجبات محسوبة على مرجع يومي ثابت **2400 kcal** — وليست هدف عميل مخصص.
- أي تحويل لهدف شخصي (من بروفايل أو برنامج) قرار أعمال مستقبلية يحتاج مصدر بيانات للهدف — لا تفترضوا أنه 2400 دائمًا في تصميم الواجهة، واعرضوا النسبة كنسبة من "المرجع اليومي".

---

## 5) قائمة تحقق قبل إعلان الربط جاهزًا

- [ ] شاشة مطاعم اليوم تعمل بصفحات، وتعود للصفحة 1 عند تغيير التاريخ/البحث، وتتجاهل الطلبات القديمة (race guard).
- [ ] الوضع القديم (بدون معاملات pagination) لم يكسر أي شاشة قائمة — الحقول الجديدة مضافة دائمًا.
- [ ] الجرس يقرأ `unreadNotificationsCount` من رد التقويم ولا يُحسب محليًا من مصدر آخر.
- [ ] شاشة الإشعارات تعرض القائمة بالصفحات وباللغة الصحيحة، ويميز غير المقروء (`readAtUtc == null`).
- [ ] قراءة إشعار/الكل تُرسل PUT الصريح وتحدث الجرس بعد النجاح فقط.
- [ ] `deepLink` يعمل للصيغتين `mealmate://…` و`/orders/…`، ولا يكسر شيء مع `subscriptionId: null`.
- [ ] التقييم null يعرض «لا توجد تقييمات» — بدون أي fallback رقمي.
- [ ] لا يوجد كود يستنتج الحساسية من اسم الوجبة أو يعامل المصفوفة الفارغة كخلو مؤكد.

> **ملاحظة نشر:** العقود أعلاه غير متاحة على `maelmate.runasp.net` حتى نشر النسخة الجديدة (السيرفر يطبق الـmigration تلقائيًا عند الإقلاع). اختبروا على البيئة المحلية أولًا إن احتجت.
