# 05 — آخر تحديثات تقويم واختيار المطاعم (موبايل Flutter)

> **الجمهور:** مطورو Flutter  
> **الهدف:** توثيق شامل للتحسينات الأخيرة على تقويم العملاء وقوائم اختيار المطاعم والبحث والتقييمات وإصلاحات الصلاحيات.  
> **تاريخ التحديث:** سبتمبر 2026  

---

## 1. ملخص التغييرات والتحسينات الأخيرة

| # | التغيير / التعديل | المسارات المتأثرة | التفاصيل |
|---|------------------|-------------------|----------|
| 1 | **إصلاح تعارض الاشتراكات الفردية** | جميع مسارات التقويم | حل مشكلة `409 Conflict (family.group_not_active)` للاشتراكات الفردية التي تمتلك مسودة عائلة غير مفعّلة. |
| 2 | **إظهار تقييمات ومواعيد التوصيل** | `/calendar`, `/days/{date}`, `/days/{date}/restaurants` | إضافة متوسط تقييم المطعم وعدد التقييمات وفترة التوصيل المنسقة. |
| 3 | **إظهار تصنيف الفئة وبرنامج الاشتراك** | جميع مسارات التقويم وقوائم المطاعم | إضافة فئة المطعم (`tier`), اسم الفئة المترجم (`tierName`), شعار الفئة (`tierLogoUrl`), واسم البرنامج (`programName`). |
| 4 | **دعم البحث الفوري عن المطاعم** | `/days/{date}/restaurants?query=...`, `/restaurants?query=...` | فلترة وتصفية المطاعم باسم المطعم (عربي/إنجليزي) عبر `query`. |

---

## 2. تفاصيل البيانات الجديدة في الاستجابات (DTO Fields)

### أ) بيانات التقويم واليوم واختيار المطاعم

تم تزويد كائنات اليوم وقوائم المطاعم بالحقول الجديدة التالية:

```json
{
  "restaurantRating": 4.5,
  "restaurantRatingCount": 128,
  "deliveryWindowStart": "18:00:00",
  "deliveryWindowEnd": "19:00:00",
  "deliveryTimeText": "6:00 م - 7:00 م",
  "tier": "Platinum",
  "tierNameAr": "بلاتينيوم",
  "tierNameEn": "Platinum",
  "tierName": "بلاتينيوم",
  "tierLogoUrl": "http://maelmate.runasp.net/api/v1/uploads/content?key=uploads/admin/tiers/platinum-star.png",
  "programNameAr": "وزن",
  "programNameEn": "Weight Loss",
  "programName": "وزن"
}
```

#### وصف الحقول:
* **`restaurantRating`** (`decimal?`): متوسط تقييم المطعم من 1 إلى 5 مقربًا لأقرب رقم عشري.
* **`restaurantRatingCount`** (`int`): عدد التقييمات الإجمالي للمطعم.
* **`deliveryWindowStart` / `deliveryWindowEnd`** (`string?`): وقت بداية ونهاية نافذة التوصيل بصيغة `HH:mm:ss`.
* **`deliveryTimeText`** (`string?`): نص منسق وجاهز للعرض المباشر في الواجهة حسب لغة الطلب (مثال: `"6:00 م - 7:00 م"` للعربية أو `"6:00 PM - 7:00 PM"` للإنجليزي).
* **`tier`** (`string?`): رمز الفئة (`Basic`, `Platinum`, `Elite`).
* **`tierName`** (`string?`): اسم الفئة المترجم المعتمد على ترويسة `Accept-Language`.
* **`tierLogoUrl`** (`string?`): رابط الشعار/الأيقونة الخاصة بالفئة.
* **`programName`** (`string?`): اسم برنامج الاشتراك المترجم (مثل `"صحي"` / `"وزن"`).

---

## 3. البحث في المطاعم (Search Query)

### أ) البحث عند اختيار مطعم لليوم في التقويم
**Request:**
```http
GET /api/v1/customer/subscriptions/{subscriptionId}/days/{date}/restaurants?query=مطعم
Authorization: Bearer <TOKEN>
Accept-Language: ar
```

**Response (200 OK):**
```json
{
  "serviceDate": "2026-09-06",
  "limit": 3,
  "adminCeiling": 5,
  "restaurants": [
    {
      "restaurantId": "3fa85f64-5717-4562-b3fc-2c963f66afa6",
      "nameAr": "مطعم الصحة والرشاقة",
      "nameEn": "Health & Fitness Restaurant",
      "logoUrl": "http://maelmate.runasp.net/api/v1/uploads/content?key=uploads/restaurant/logos/logo.jpg",
      "tier": "Platinum",
      "usedCount": 1,
      "limit": 3,
      "remainingCount": 2,
      "isBusy": false,
      "isQuotaExhausted": false,
      "isEnabled": true,
      "disabledReason": null,
      "safeMealCount": 12,
      "rating": 4.8,
      "ratingCount": 94,
      "deliveryWindowStart": "18:00:00",
      "deliveryWindowEnd": "19:00:00",
      "deliveryTimeText": "6:00 م - 7:00 م",
      "tierNameAr": "بلاتينيوم",
      "tierNameEn": "Platinum",
      "tierName": "بلاتينيوم",
      "tierLogoUrl": "http://maelmate.runasp.net/api/v1/uploads/content?key=uploads/admin/tiers/platinum-star.png"
    }
  ]
}
```

---

### ب) البحث في قائمة مطاعم الاشتراك الباقة المجمّدة
**Request:**
```http
GET /api/v1/customer/subscriptions/{subscriptionId}/restaurants?query=Healthy
Authorization: Bearer <TOKEN>
Accept-Language: en
```

**Response (200 OK):**
```json
{
  "restaurantPoolSnapshotId": "1a2b3c4d-5678-90ab-cdef-1234567890ab",
  "eligibilityCheckedAtUtc": "2026-09-01T12:00:00Z",
  "restaurantCount": 4,
  "restaurants": [
    {
      "restaurantId": "3fa85f64-5717-4562-b3fc-2c963f66afa6",
      "nameAr": "مطعم الصحة والرشاقة",
      "nameEn": "Health & Fitness Restaurant",
      "tier": "Platinum",
      "isInitiallySelected": true,
      "capturedAtUtc": "2026-09-01T12:00:00Z",
      "logoUrl": "http://maelmate.runasp.net/api/v1/uploads/content?key=uploads/restaurant/logos/logo.jpg",
      "rating": 4.8,
      "ratingCount": 94,
      "tierNameAr": "بلاتينيوم",
      "tierNameEn": "Platinum",
      "tierName": "Platinum",
      "tierLogoUrl": "http://maelmate.runasp.net/api/v1/uploads/content?key=uploads/admin/tiers/platinum-star.png"
    }
  ]
}
```

---

## 4. إرشادات الدمج والتطبيق في تطبيقات Flutter

1. **حقل التوصيل `deliveryTimeText`**:
   * استخدم هذا النص مباشرة في الواجهة (مثل كارت اليوم أو مودال اختيار المطعم) دون الحاجة لتركيب الأوقات يدوياً.

2. **التقييمات `restaurantRating` & `restaurantRatingCount`**:
   * قم بعرض النجوم فقط إذا كان `restaurantRating != null` و `restaurantRatingCount > 0`.

3. **أيقونة الفئة `tierLogoUrl`**:
   * يفضّل استخدام `CachedNetworkImage` أو `Image.network` لعرض أيقونة الفئة إلى جانب اسم الفئة `tierName`.

4. **شريط البحث (Search Bar)**:
   * عند إضافة `Debounce` (300ms) في شريط البحث في Flutter، قم بتمرير النص المدخل إلى `GET .../restaurants?query=text`.
