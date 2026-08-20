# 📱 دليل مبرمج الموبايل: تقويم الوجبات واختيار المطاعم والاشتراكات
### Mobile Developer Integration Guide — MealMate Calendar & Meal Selection Engine

---

## 📑 الفهرس
1. [نظرة عامة وقواعد العمل (Business Rules)](#1-نظرة-عامة-وقواعد-العمل)
2. [دورة حياة اليوم وحالات التقويم (Calendar Day Lifecycle)](#2-دورة-حياة-اليوم-وحالات-التقويم)
3. [الشاشات والـ Endpoints ونماذج الـ JSON](#3-الشاشات-والـ-endpoints-ونماذج-الـ-json)
   - [شاشة 05.10: تقويم الوجبات (Meal Calendar)](#شاشة-0510-تقويم-الوجبات-meal-calendar)
   - [شاشة 05.11: استعراض واختيار المطاعم (Browse Restaurants)](#شاشة-0511-استعراض-واختيار-المطاعم-browse-restaurants)
   - [شاشة 05.12: بروفايل المطعم والمنيو (Restaurant Profile & Menu)](#شاشة-0512-بروفايل-المطعم-والمنيو-restaurant-profile--menu)
   - [شاشة 05.14: القيمة الغذائية والماكروز (Meal Nutrition Details)](#شاشة-0514-تفاصيل-القيمة-الغذائية-والماكروز-nutrition-details)
4. [أكشن حفظ وتأكيد الوجبات (Submit Meal Selection)](#4-أكشن-حفظ-وتأكيد-الوجبات)
5. [معالجة الأخطاء والاستثناءات (Error Handling)](#5-معالجة-الأخطاء-والاستثناءات)

---

## 1. نظرة عامة وقواعد العمل

| القاعدة | التفاصيل للموبايل |
| :--- | :--- |
| **فترة التجهيز وبدء الاشتراك** | أول يوم تسليم يبدأ بعد مهلة إعداد (48 أو 72 ساعة) من الدفع. |
| **إجازة يوم الجمعة** | يوم الجمعة إجازة افتراضية (`IncludeFriday = false`). لا يحتسب ضمن الأيام ولا يُطلب له وجبة. يظهر في التقويم كـ `OffDay`. وإذا كانت الباقة تشمل الجمعة (`IncludeFriday = true`) يظهر كيوم خدمة عادي. |
| **موعد إغلاق اليوم (Lock Cut-off)** | يغلق التعديل قبل يوم التسليم بيومين عند الساعة 10:00 صباحاً (`LockAtUtc`). |
| **توزيع الحصص على المطاعم (Quota)** | كل مطعم له حد أقصى للأيام (`AllowedDaysQuota`). إذا وصل المستخدم للحد الأقصى يصبح المطعم `IsQuotaExhausted = true` ويجب تعطيل اختياره في هذا اليوم. |
| **الحساسية والتفضيلات** | أي وجبة تحتوي على مسبب حساسية لدى العميل يُوضع عليها `HasCustomerAllergenConflict = true` ويجب إظهار تنبيه أو فلترتها. |
| **الاختيار التلقائي (Auto-Selection)** | إذا انتهى وقت العداد دون اختيار من العميل، يختار السيرفر وجبات آمنة تلقائياً وتتحول حالة اليوم إلى `AutoSelected`. |

---

## 2. دورة حياة اليوم وحالات التقويم

قيمة حقل `Status` المرتجعة لكل يوم في التقويم:

- `Open`: اليوم متاح لاختيار المطعم والوجبات (لم يحدد بعد).
- `Selected`: العميل اختار المطعم والوجبات وبانتظار موعد الإغلاق.
- `AutoSelected`: تم اختيار وجبات آمنة تلقائياً بعد انتهاء وقت الإغلاق.
- `Locked`: اليوم مغلق وجاري تجهيزه من قبل المطعم.
- `InPreparation`: المطعم أكد الطلب وهو قيد الطهي.
- `OutForDelivery`: الطلب مع السائق للتوصيل.
- `Delivered`: تم تسليم البوكس بنجاح.
- `OffDay`: يوم راحة/إجازة (مثل الجمعة غير المشمولة).
- `Frozen`: اليوم تم تجميده/ترحيله بطلب العميل.

---

## 3. الشاشات والـ Endpoints ونماذج الـ JSON

---

### شاشة 05.10: تقويم الوجبات (Meal Calendar)

#### الـ Endpoint:
```http
GET /api/v1/customer/calendar/month?year=2026&month=9
Authorization: Bearer {token}
```

#### نموذج الاستجابة (JSON Response):
```json
{
  "monthYearAr": "سبتمبر 2026",
  "totalDays": 26,
  "completedDays": 5,
  "remainingDays": 21,
  "days": [
    {
      "subscriptionDayId": "3fa85f64-5717-4562-b3fc-2c963f66afa6",
      "serviceDate": "2026-09-25",
      "dayOfWeekAr": "الأحد",
      "dayNumber": 25,
      "status": "Open",
      "isAutoSelected": false,
      "lockAtUtc": "2026-09-23T10:00:00Z",
      "countdownRemainingText": "متاح للتعديل أو تغيير الوجبة حتى 23 سبتمبر - 10:00 ص",
      "remainingSeconds": 153900,
      "selectedRestaurantId": null,
      "selectedRestaurantName": null,
      "selectedMeals": []
    },
    {
      "subscriptionDayId": "4bb85f64-5717-4562-b3fc-2c963f66afa7",
      "serviceDate": "2026-09-26",
      "dayOfWeekAr": "الاثنين",
      "dayNumber": 26,
      "status": "Selected",
      "isAutoSelected": false,
      "lockAtUtc": "2026-09-24T10:00:00Z",
      "countdownRemainingText": "متاح للتعديل حتى 24 سبتمبر - 10:00 ص",
      "remainingSeconds": 240300,
      "selectedRestaurantId": "a1111111-1111-1111-1111-111111111111",
      "selectedRestaurantName": "بروتين كيتشن",
      "selectedMeals": [
        {
          "mealId": "b2222222-2222-2222-2222-222222222222",
          "mealName": "دجاج مشوي مع الأرز البني",
          "slot": "Lunch",
          "calories": 550,
          "proteinGrams": 45.0,
          "carbsGrams": 60.0,
          "fatGrams": 15.0,
          "photoUrl": "https://cdn.mealmate.com/meals/chicken-rice.jpg"
        }
      ]
    }
  ]
}
```

#### ملاحظات للمطور:
- احسب العداد التنازلي للشاشة عبر `remainingSeconds` أو باستخدام `lockAtUtc`.
- إذا كان `status == "Open"` أظهر زر `+ اختيار مطعم`.
- إذا كان `status == "Selected"` أظهر تفاصيل الوجبات المختارة مع إمكانية التعديل طالما لم يغلق العداد.

---

### شاشة 05.11: استعراض واختيار المطاعم (Browse Restaurants)

#### الـ Endpoint:
```http
GET /api/v1/customer/calendar/days/2026-09-25/eligible-restaurants?search={query}
Authorization: Bearer {token}
```

#### نموذج الاستجابة (JSON Response):
```json
{
  "serviceDate": "2026-09-25",
  "dayOfWeekAr": "الأحد",
  "restaurants": [
    {
      "restaurantId": "a1111111-1111-1111-1111-111111111111",
      "nameAr": "بروتين كيتشن",
      "nameEn": "The Protein Kitchen",
      "logoUrl": "https://cdn.mealmate.com/logos/protein-kitchen.png",
      "rating": 4.8,
      "availableMealsCount": 28,
      "allowedDaysQuota": 2,
      "usedDaysCount": 1,
      "remainingDaysQuota": 1,
      "isQuotaExhausted": false,
      "badges": ["وجبات صحية متوازنة بمكونات طبيعية"]
    },
    {
      "restaurantId": "a2222222-2222-2222-2222-222222222222",
      "nameAr": "دايت هاوس",
      "nameEn": "Diet House",
      "logoUrl": "https://cdn.mealmate.com/logos/diet-house.png",
      "rating": 4.8,
      "availableMealsCount": 28,
      "allowedDaysQuota": 2,
      "usedDaysCount": 2,
      "remainingDaysQuota": 0,
      "isQuotaExhausted": true,
      "badges": ["وجبات دايت محسوبة السعرات"]
    }
  ]
}
```

#### ملاحظات للمطور:
- إذا كان `isQuotaExhausted == true`: قم بتعطيل البطاقة (Disable Card) وأظهر بادج توضيحي: `استنفدت الحد الأقصى لهذا المطعم (2/2 يوم)`.

---

### شاشة 05.12: بروفايل المطعم والمنيو (Restaurant Profile & Menu)

#### الـ Endpoint:
```http
GET /api/v1/customer/calendar/days/2026-09-25/restaurants/a1111111-1111-1111-1111-111111111111/menu
Authorization: Bearer {token}
```

#### نموذج الاستجابة (JSON Response):
```json
{
  "restaurantId": "a1111111-1111-1111-1111-111111111111",
  "nameAr": "بروتين كيتشن",
  "nameEn": "The Protein Kitchen",
  "descriptionAr": "نقدم وجبات صحية عالية الجودة باستخدام مكونات طازجة وطبيعية لمساعدتك على تحقيق أهدافك الغذائية والرياضية.",
  "coverImageUrl": "https://cdn.mealmate.com/covers/protein-kitchen.jpg",
  "logoUrl": "https://cdn.mealmate.com/logos/protein-kitchen.png",
  "rating": 4.8,
  "ingredientOrigins": [
    {
      "category": "الزيوت",
      "origin": "مستوردة إسبانيا",
      "countryFlagCode": "ES",
      "isImported": true
    },
    {
      "category": "الأسماك",
      "origin": "مستوردة النرويج",
      "countryFlagCode": "NO",
      "isImported": true
    },
    {
      "category": "الدواجن",
      "origin": "طازجة محلية كويتية",
      "countryFlagCode": "KW",
      "isImported": false
    },
    {
      "category": "اللحوم",
      "origin": "مستوردة ألمانيا",
      "countryFlagCode": "DE",
      "isImported": true
    }
  ],
  "categorizedMeals": {
    "Breakfast": [
      {
        "mealId": "b1111111-1111-1111-1111-111111111111",
        "nameAr": "أومليت الشوفان والخضار",
        "nameEn": "Oatmeal Veggie Omelette",
        "slot": "Breakfast",
        "photoUrl": "https://cdn.mealmate.com/meals/omelette.jpg",
        "calories": 420,
        "proteinGrams": 28.0,
        "carbsGrams": 35.0,
        "fatGrams": 12.0,
        "fiberGrams": 6.0,
        "ingredients": ["بيض طازج", "شوفان عضوي", "فلفل رومي", "سبانخ"],
        "allergens": ["بيض", "شوفان"],
        "hasCustomerAllergenConflict": false
      }
    ],
    "Lunch": [
      {
        "mealId": "b2222222-2222-2222-2222-222222222222",
        "nameAr": "دجاج مشوي مع الأرز البني",
        "nameEn": "Grilled Chicken with Brown Rice",
        "slot": "Lunch",
        "photoUrl": "https://cdn.mealmate.com/meals/chicken-rice.jpg",
        "calories": 550,
        "proteinGrams": 45.0,
        "carbsGrams": 60.0,
        "fatGrams": 15.0,
        "fiberGrams": 7.0,
        "ingredients": ["دجاج مشوي", "أرز بني", "بروكلي", "جزر", "ليمون"],
        "allergens": ["جلوتين"],
        "hasCustomerAllergenConflict": false
      }
    ],
    "Dinner": [],
    "Snack": []
  }
}
```

---

### شاشة 05.14: تفاصيل القيمة الغذائية والماكروز (Nutrition Details)

#### الـ Endpoint:
```http
GET /api/v1/customer/meals/b2222222-2222-2222-2222-222222222222/nutrition-details
Authorization: Bearer {token}
```

#### نموذج الاستجابة (JSON Response):
```json
{
  "mealId": "b2222222-2222-2222-2222-222222222222",
  "nameAr": "دجاج مشوي مع الأرز البني",
  "nameEn": "Grilled Chicken with Brown Rice",
  "descriptionAr": "وجبة متكاملة عالية البروتين ومصنوعة من مكونات طازجة، مناسبة لبرنامج خسارة الوزن وبناء العضلات.",
  "photoUrl": "https://cdn.mealmate.com/meals/chicken-rice.jpg",
  "restaurantRating": 4.8,
  "ratingCount": 520,
  "ingredients": [
    "دجاج مشوي",
    "أرز بني",
    "جزر",
    "بروكلي",
    "خس",
    "ليمون"
  ],
  "calories": 550,
  "proteinGrams": 45.0,
  "carbsGrams": 60.0,
  "fatGrams": 15.0,
  "fiberGrams": 7.0,
  "proteinPercentage": 32.4,
  "carbsPercentage": 43.2,
  "fatPercentage": 24.3,
  "allergens": [
    "جلوتين",
    "لاكتوز",
    "مكسرات"
  ]
}
```

#### ملاحظات للمطور لرسم الرسوم الدائرية (Donut Charts):
- نسبة الكربوهيدرات: `carbsPercentage` (تُعرض بلون أزرق/أخضر داكن 45%).
- نسبة البروتين: `proteinPercentage` (تُعرض بلون برتقالي 30%).
- نسبة الدهون: `fatPercentage` (تُعرض بلون أخضر فاتح 25%).
- مسببات الحساسية: اعرض عناصر `allergens` كـ Badges في أسفل الشاشة.

---

## 4. أكشن حفظ وتأكيد الوجبات

عند قيام المستخدم باختيار وجباته لليوم والضغط على زر "تأكيد الوجبات":

#### الـ Endpoint:
```http
POST /api/v1/customer/calendar/days/2026-09-25/select-meals
Authorization: Bearer {token}
Content-Type: application/json
```

#### نموذج الـ Request Payload:
```json
{
  "restaurantId": "a1111111-1111-1111-1111-111111111111",
  "mealSelections": [
    {
      "mealId": "b1111111-1111-1111-1111-111111111111",
      "slot": "Breakfast",
      "sortOrder": 0
    },
    {
      "mealId": "b2222222-2222-2222-2222-222222222222",
      "slot": "Lunch",
      "sortOrder": 1
    }
  ]
}
```

#### الاستجابة الناجحة (200 OK):
```json
{
  "success": true,
  "message": "تم حفظ وتأكيد وجباتك بنجاح",
  "dayStatus": "Selected"
}
```

---

## 5. معالجة الأخطاء والاستثناءات

| رمز الخطأ (`error`) | سبب الخطأ | الإجراء المطلوب في واجهة الموبايل |
| :--- | :--- | :--- |
| `calendar.day_locked` | انتهى وقت التعديل واليوم أصبح مغلقاً للتجهيز. | تعطيل أزرار التعديل وعرض رسالة "تم إغلاق طلب هذا اليوم للتجهيز". |
| `calendar.quota_exhausted` | استنفاد الحد الأقصى للمطعم المختار. | عرض تنبيه "لقد استنفدت حصتك في هذا المطعم لهذا الاشتراك، يرجى اختيار مطعم آخر". |
| `calendar.allergen_conflict` | محاولة اختيار وجبة تتعارض مع حساسية مسجلة للمستخدم. | إظهار Dialog تأكيد إجباري للعميل أو منعه حسب سياسة التطبيق. |
| `calendar.invalid_slot_count` | عدد الوجبات المختارة لا يطابق الباقة (مثلاً باقة وجبتين والمستخدم اختار وجبة واحدة). | إظهار Toast "يرجى استكمال اختيار الوجبات المحددة لباقاتك". |
