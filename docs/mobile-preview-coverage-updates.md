# تحديثات عقد فحص التغطية — preview-coverage (2026-10-06)

> **للفريق:** هذا الملف يوثّق تعديلين نُشرا على السيرفر الحي (`maelmate.runasp.net`) بتاريخ 2026-10-06 ومتحقَّق منهما فعليًا على الـ API الحي. الملفان المرجعيان التفصيليان: `docs/mobile/customer-delivery-address-update-guide.md` و `docs/mobile/address-and-restaurant-browse.md`.

## الخلاصة السريعة

| # | التعديل | القاعدة الجديدة |
|---|---------|----------------|
| 1 | **Pagination على preview-coverage** | إرسال `page`/`pageSize` يُرجع كائن `pagination` ويجزّئ `servingRestaurants`؛ بدون إرسالهما يبقى الشكل القديم مع `pagination: null` |
| 2 | **`restaurantCoverageNotice` مشروط بالاشتراك** | يُعاد **فقط** عند وجود اشتراك نشط (`hasActiveSubscription = true`)؛ بدون اشتراك نشط قيمته `null` |

---

## 1) Pagination على preview-coverage

### نقاط النهاية الأربعة ومصدر الباراميترات

| Endpoint | مصدر `page`/`pageSize` |
|----------|------------------------|
| `POST /api/v1/customers/me/addresses/preview-coverage` | Body |
| `GET /api/v1/customers/me/addresses/{id}/preview-coverage` | Query string |
| `POST /api/v1/family/members/{memberId}/addresses/preview-coverage` | Body |
| `GET /api/v1/family/members/{memberId}/addresses/{addressId}/preview-coverage` | Query string |

### قواعد العقد (Dual Contract)

- **إرسال `page` و/أو `pageSize`** (أي واحد منهما يكفي لتفعيل التجزئة): الرد يضاف له كائن `pagination` ويُجزَّأ `servingRestaurants` لصفحة واحدة.
  - الباراميتر الناقص يأخذ القيمة الافتراضية: `page = 1`، `pageSize = 20`.
- **عدم إرسالهما**: يبقى الرد بالشكل القديم الكامل (كل المطاعم) مع `pagination: null` — الحقل موجود دائمًا في الرد ولا يختفي.
- **صفحة خارج النطاق** (مثلاً `page = 99`): لا خطأ — تُرجع `servingRestaurants: []` مع بيانات الـ pagination الصحيحة.
- **الترتيب ثابت**: القائمة مرتبة دائمًا بـ `nameAr` (ثم id داخليًا) — لا تكرار عناصر بين الصفحات مهما تغيّر ترتيب قاعدة البيانات.
- كل المطاعم الأخرى في الرد (الأسماء، الإحداثيات، `canSetAsDefault`...) غير متأثرة بالتجزئة.

### شكل كائن الـ pagination

```json
"pagination": {
  "totalCount": 4,
  "page": 1,
  "pageSize": 2,
  "totalPages": 2,
  "hasNextPage": true,
  "hasPreviousPage": false
}
```

هذا نفس كائن الـ pagination المعتمد في endpoints الاشتراكات والتقويم (`CustomerPaginationMetaDto`) — نفس الأسماء بالحرف.

### مثال حي (تم تنفيذه على السيرفر الحي 2026-10-06)

طلب: `POST /customers/me/addresses/preview-coverage` بـ body فيه `{"regionId": "...", "isDefault": true, "page": 1, "pageSize": 2}` لعميل جديد بدون اشتراك — المنطقة: السالمية (4 مطاعم تغطيها):

```json
{
  "canSetAsDefault": true,
  "isCurrentSubscriptionRestaurantCovered": true,
  "conflictMessage": null,
  "resolvedRegionId": "20344d90-d50d-4607-8101-f25ac40e229c",
  "resolvedRegionNameAr": "السالمية",
  "governorate": "حولي",
  "area": "السالمية",
  "restaurantCoverageNotice": null,
  "servingRestaurants": [
    { "id": "f8379400-...", "nameAr": "[تجربة] بروتين لاب", "rating": null },
    { "id": "7ccfc18b-...", "nameAr": "[تجربة] جرين بول", "rating": null }
  ],
  "hasActiveSubscription": false,
  "pagination": {
    "totalCount": 4, "page": 1, "pageSize": 2,
    "totalPages": 2, "hasNextPage": true, "hasPreviousPage": false
  }
}
```

(المثال مختصر — الرد الحقيقي يحوي الحقول الأخرى كاملة: `latitude`/`longitude`/`block`... إلخ.)

---

## 2) `restaurantCoverageNotice` يُعاد فقط مع اشتراك نشط

### السبب

نص التنبيه الحالي يقول: *«تنبيه: عند اعتماد هذا العنوان للتوصيل، قد تتغير قائمة المطاعم المتاحة **لاشتراكك**؛ ستظهر مطاعم تغطي منطقتك الجديدة وتختفي مطاعم لا تدعم التوصيل إليها.»* — الرسالة تفترض اشتراكًا قائمًا قائمة مطاعمه قد تتغير، وكانت تُعاد سابقًا حتى للعميل الجديد الذي لا يملك اشتراكًا أصلًا، فكانت مربكة. (الملاحظة وصلت من فريق الموبايل — شكرًا لها، وكانت في محلها.)

### القاعدة الجديدة

- **عميل لديه اشتراك نشط** (`hasActiveSubscription = true`): الحقل يُعاد بنصه كالسابق.
- **عميل بدون اشتراك نشط** (`hasActiveSubscription = false`): الحقل يُعاد بقيمة **`null`**.
- علامة "الاشتراك النشط" هنا نفس تعريف السيرفر داخليًا: اشتراك بحالة Active / Paid / Scheduled / Frozen / CancellationPending / Upgraded.
- **ينطبق على كل مسارات العناوين**، وليس الـ preview فقط:
  - `preview-coverage` الأربعة (عملاء + عائلة، POST و GET).
  - ردود **إضافة عنوان**، **تعديل عنوان**، و**تعيين العنوان الافتراضي** — كلها تحمل `restaurantCoverageNotice` بنفس القاعدة.

### المطلوب من الموبايل

- اعرضوا التنبيه فقط عندما تكون القيمة غير `null` (أو عندما `hasActiveSubscription == true` — نفس المعنى الآن).
- لا تفترضوا أن الحقل نص دائمًا؛ تعاملوا مع `null` كحالة عادية تمامًا.
- `servingRestaurants` والـ pagination وسير الـ conflict (`conflictMessage` و `canSetAsDefault`) **لم تتغير**.

### ملاحظة عن شكل الحقل (تصحيح للدوكيومنتات القديمة)

بعض الأدلة القديمة تُظهر الحقل ككائن `{"ar": "...", "en": "..."}` — **العقد الحي الحالي يُرجعه نصًا واحدًا (string) حسب لغة الطلب** (`Accept-Language`): عربي مع `ar` وإنجليزي مع `en`/بدون هيدر. هذا هو نفس سلوك باقي حقول النصوص المترجمة في الـ API، فاعتمدوا الشكل النصي.

---

## للتجربة السريعة

حساب اختباري جاهز على السيرفر الحي (بدون اشتراك — يخدم فحص حالة `null` مباشرة):

```
POST https://maelmate.runasp.net/api/v1/auth/customers/login
{ "phone": "+96551123457", "password": "Test#1234", "countryCode": "KW" }
```

ثم استخدموا الـ token في `preview-coverage` مع/بدون `page`/`pageSize` لرؤية الحالتين. حالة "مع اشتراك نشط" يمكن رؤيتها بأي حساب مشترك قائم.

---

## ملخص الحالات

| الحالة | `restaurantCoverageNotice` | `pagination` |
|--------|---------------------------|--------------|
| بدون اشتراك + بدون page/pageSize | `null` | `null` |
| بدون اشتراك + مع page/pageSize | `null` | كائن مكتمل |
| مع اشتراك نشط + بدون page/pageSize | نص التنبيه | `null` |
| مع اشتراك نشط + مع page/pageSize | نص التنبيه | كائن مكتمل |
| اشتراك نشط + مطعم الاشتراك لا يغطي المنطقة | نص التنبيه + `conflictMessage` غير null و `canSetAsDefault: false` | حسب الباراميترات |
