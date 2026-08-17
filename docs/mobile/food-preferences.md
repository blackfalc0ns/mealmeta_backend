# ربط شاشات التفضيلات الغذائية (موبايل)

عقد الـ API لثلاث شاشات في الملف الشخصي. القيم دي هي فلتر وجبات المشترك داخل البرنامج (التقويم / اختيار الوجبة / الاختيار التلقائي). الموبايل بيحفظ الاختيار بس.

المكوّنات والحساسية في الكتالوج جاية من **مكوّنات المطاعم المعتمدة**. البرامج الغذائية كتالوج تعريفات، والتطبيق على الوجبة من تاج الوجبة + تاج المكوّنات.

قوائم الوجبات المعروضة (`available`، تصفح المطعم، اختيار تلقائي) بترجع الوجبات الآمنة فقط. الموبايل يعرض اللي راجع.

في الـ checkout: المطعم بيتعمل له استبعاد لو مفيش وجبات كافية بعد الفلتر (`no_safe_meals`).

1. **الأكلة غير المفضلة**
2. **الحساسية**
3. **برامج غذائية** (نباتي / كيتو / …) — **مش** برامج الاشتراك زي خسارة الوزن

### إزاي الفلتر يشتغل على الوجبة

الوجبة تظهر للمشترك في البرنامج لو عدّت الثلاثة مع بعض:

| الشاشة | القاعدة | فاضي يعني |
|---|---|---|
| حساسية | تتشال لو فيها الحساسية دي في `present` أو `mayContain` أو في أي مكوّن. الكتالوج بس الأكواد الموجودة على مكوّنات المطاعم (+ اختيار محفوظ) | مفيش قيد حساسية |
| أكل غير مفضل | اختيار واحد يمنع أي وجبة فيها مكوّن **بنفس الاسم** (عربي أو إنجليزي) في أي مطعم. الكتالوج صف واحد لكل اسم | مفيش قيد مكوّنات |
| برامج غذائية | لازم الوجبة تطابق **كل** الأكواد المختارة (AND) عن طريق تاج الوجبة أو تاج كل المكوّنات | مفيش قيد برنامج غذائي |

مثال: لو اختار `KETO` + `GLUTEN_FREE`، الوجبة لازم تكون الاتنين. لو اختار حساسية `MILK`، أي وجبة فيها لبن أو may-contain لبن مش هتظهر. لو اختار «دجاج» من مطعم، دجاج أي مطعم تاني مش هيظهر.

نفس الصفوف مستخدمة في أهلية الـ checkout، تجميد وجبات اليوم وقت الدفع، و`GET .../days/{date}/meals/available` مع الاختيار التلقائي وتصفح المطعم.

اشتراكات جديدة: الـ snapshot يخزّن كل وجبات اليوم القابلة للتجميد، والتقويم يفلتر بالاختيار الحالي. لو خفّف التفضيل بعد الدفع، الوجبات ترجع تظهر. الاشتراكات القديمة اللي الـ JSON بتاعها كان متفلتر وقت الدفع تفضل زي ما هي.

### خيار «بدون قيود» (أول عنصر في كل قائمة)

أول عنصر في `allergens` / `ingredients` / `dietaryPreferences` هو خيار بدون قيود. اعرض القائمة بالترتيب؛ متضيفش صف إضافي من `noneOption`.

| الشاشة | أول عنصر | nameAr |
|---|---|---|
| حساسية | `allergens[0]` | لا توجد حساسية |
| أكل غير مفضل | `ingredients[0]` | أتناول كل أنواع الطعام |
| برامج غذائية | `dietaryPreferences[0]` | لا أتبع نظاماً غذائياً معيناً |

- الكود `NONE`. اختياره = احفظ array فاضي (أو ابعت `NONE` والسيرفر يتجاهله).
- لو `isSelected == true` على الصف ده، باقي العناصر تكون غير مختارة.
- العنصر ده يظهر في **الصفحة الأولى فقط** عشان الـ infinite scroll ما يكررش.

### Pagination والبحث

`GET` يقبل:

- `page` (افتراضي `1`)
- `pageSize` (افتراضي `20`، أقصى `50`)
- `search` اختياري (اسم عربي / إنجليزي)

`noneOption` موجود للتوافق، لكن الصف المعروض هو أول عنصر في القائمة. متتعرضوش مرتين.

الحفظ مش صفحة صفحة: الـ `PUT` لازم يحتوي **كل** الاختيارات الحالية عبر كل الصفحات.

- Auth: `Authorization: Bearer <customer_jwt>`
- Role: عميل فقط. غير كده = `403`
- بدون توكن = `401`
- JSON: `camelCase`
- كل شاشة = `GET` واحد للتحميل + `PUT` واحد للحفظ
- الـ `GET` والـ `PUT` لنفس الشاشة بيرجعوا **نفس الشكل**

لا تستخدم `GET /api/v1/customers/me/food-preferences` ولا `GET .../catalog` في الشاشات دي. موجودين للتوافق القديم فقط.

لا تستخدم `PUT /api/v1/customers/me/food-preferences` من الشاشات دي. ده بيستبدل الحساسية + الأكل غير المفضل + البرامج الغذائية مرة واحدة.

برامج الاشتراك (`/api/v1/customer/subscription-flow/programs`) مكانها شاشة الاشتراك، مش الشاشة دي.

---

## Flutter paths

```dart
static const String customerFoodPreferencesDisliked =
    '/api/v1/customers/me/food-preferences/disliked-ingredients';
static const String customerFoodPreferencesAllergies =
    '/api/v1/customers/me/food-preferences/allergies';
static const String customerFoodPreferencesDietary =
    '/api/v1/customers/me/food-preferences/dietary';
```

الـ paths القديمة (`/food-preferences` و `/food-preferences/catalog`) متستخدمهاش في الشاشات الثلاث.

---

## 1) الأكلة غير المفضلة

`GET /api/v1/customers/me/food-preferences/disliked-ingredients?page=1&pageSize=20&search=`

القائمة من مكونات المطاعم المعتمدة، **صف واحد لكل اسم**. الكود `INGREDIENT:{guid}` لممثل المجموعة. اختيار «دجاج» يمنع دجاج كل المطاعم حتى لو الـ GUID مختلف. متبعتش كود حساسية هنا.

```json
{
  "noneOption": {
    "code": "NONE",
    "nameAr": "أتناول كل أنواع الطعام",
    "nameEn": "I eat all foods",
    "descriptionAr": "لا توجد أطعمة غير مرغوبة لدي",
    "descriptionEn": "I have no disliked foods",
    "isSelected": false
  },
  "ingredients": [
    {
      "code": "NONE",
      "nameAr": "أتناول كل أنواع الطعام",
      "nameEn": "I eat all foods",
      "category": null,
      "allergenCodes": [],
      "isSelected": false
    },
    {
      "code": "INGREDIENT:3fa85f64-5717-4562-b3fc-2c963f66afa6",
      "nameAr": "دجاج",
      "nameEn": "Chicken",
      "category": "poultry",
      "allergenCodes": [],
      "isSelected": true
    }
  ],
  "selectedCount": 1,
  "eatsEverything": false,
  "page": 1,
  "pageSize": 20,
  "totalCount": 1,
  "totalPages": 1,
  "hasNextPage": false,
  "hasPreviousPage": false
}
```

اعرض `ingredients` بالترتيب. أول صف = **أتناول كل أنواع الطعام**.

### الحفظ

`PUT /api/v1/customers/me/food-preferences/disliked-ingredients`

ابعت **كل** الاختيارات الحالية من كل الصفحات، مش الـ delta. Array فاضي أو `["NONE"]` = «أتناول كل أنواع الطعام» (الحساسية والبرامج الغذائية مش بتتأثر).

```json
{
  "ingredientCodes": [
    "INGREDIENT:3fa85f64-5717-4562-b3fc-2c963f66afa6"
  ]
}
```

الرد نفس شكل الـ GET.

كود غلط أو مكوّن مش متاح = `400` `food_preferences.invalid_ingredient`.

---

## 2) الحساسية

`GET /api/v1/customers/me/food-preferences/allergies?page=1&pageSize=20&search=`

القائمة من أكواد الحساسية الموجودة على مكوّنات المطاعم المعتمدة (الأسماء من كتالوج التعريفات). اختيار محفوظ يظهر حتى لو المطعم شاله بعدين.

```json
{
  "noneOption": {
    "code": "NONE",
    "nameAr": "لا توجد حساسية",
    "nameEn": "No allergies",
    "descriptionAr": "يمكنني تناول الوجبات بدون قيود حساسية",
    "descriptionEn": "I can eat meals without allergy restrictions",
    "isSelected": false
  },
  "allergens": [
    {
      "code": "NONE",
      "nameAr": "لا توجد حساسية",
      "nameEn": "No allergies",
      "isSelected": false,
      "severity": null
    },
    {
      "code": "MILK",
      "nameAr": "الحليب",
      "nameEn": "Milk",
      "isSelected": true,
      "severity": "High"
    },
    {
      "code": "GLUTEN",
      "nameAr": "القمح والجلوتين",
      "nameEn": "Wheat and gluten",
      "isSelected": false,
      "severity": null
    }
  ],
  "selectedCount": 1,
  "hasNoAllergies": false,
  "page": 1,
  "pageSize": 20,
  "totalCount": 2,
  "totalPages": 1,
  "hasNextPage": false,
  "hasPreviousPage": false
}
```

اعرض `allergens` بالترتيب. أول صف = **لا توجد حساسية**.

### الحفظ

`PUT /api/v1/customers/me/food-preferences/allergies`

ابعت العناصر المختارة فقط من كل الصفحات. Array فاضي أو `{ "allergies": [{ "code": "NONE" }] }` = «ليس لدي أي حساسية».

```json
{
  "allergies": [
    { "code": "MILK", "severity": "High" }
  ]
}
```

- `code` مطلوب من قائمة الـ GET
- `nameAr` / `nameEn` اختياريين (السيرفر بيملأهم)
- `severity` اختياري، string أقصى 50 حرف

الرد نفس شكل الـ GET (`allergens` + `isSelected`). متستخدمش مفتاح `allergies` في الرد.

كود مش موجود في الكتالوج = `400` `food_preferences.invalid_allergen`.

### أكواد الحساسية الحالية

| code | nameAr | nameEn |
|---|---|---|
| `GLUTEN` | القمح والجلوتين | Wheat and gluten |
| `EGGS` | البيض | Eggs |
| `MILK` | الحليب | Milk |
| `PEANUTS` | الفول السوداني | Peanuts |
| `TREE_NUTS` | المكسرات | Tree nuts |
| `FISH` | السمك | Fish |
| `CRUSTACEANS` | القشريات | Crustaceans |
| `SOY` | الصويا | Soy |
| `SESAME` | السمسم | Sesame |

اعتمد على الـ GET، متـhardcode-ش الأكواد في التطبيق. لو مفيش مطعم حاطط السمك كمكوّن، `FISH` مش هيظهر في القائمة.

---

## 3) برامج غذائية

`GET /api/v1/customers/me/food-preferences/dietary?page=1&pageSize=20&search=`

دي تفضيلات أكل (نباتي / كيتو / …)، **مش** برنامج الاشتراك الشهري.

```json
{
  "noneOption": {
    "code": "NONE",
    "nameAr": "لا أتبع نظاماً غذائياً معيناً",
    "nameEn": "I don't follow a specific diet",
    "descriptionAr": "تناسبني الوجبات بدون قيود برنامج غذائي",
    "descriptionEn": "Meals are not limited by a diet program",
    "isSelected": false
  },
  "dietaryPreferences": [
    {
      "code": "NONE",
      "nameAr": "لا أتبع نظاماً غذائياً معيناً",
      "nameEn": "I don't follow a specific diet",
      "isSelected": false
    },
    {
      "code": "KETO",
      "nameAr": "كيتو",
      "nameEn": "Keto",
      "isSelected": true
    },
    {
      "code": "VEGETARIAN",
      "nameAr": "نباتي",
      "nameEn": "Vegetarian",
      "isSelected": false
    }
  ],
  "selectedCount": 1,
  "hasNoDietaryRestriction": false,
  "page": 1,
  "pageSize": 20,
  "totalCount": 2,
  "totalPages": 1,
  "hasNextPage": false,
  "hasPreviousPage": false
}
```

يسمح باختيار أكتر من برنامج. اعرض `dietaryPreferences` بالترتيب. أول صف = **لا أتبع نظاماً غذائياً معيناً**.

### الحفظ

`PUT /api/v1/customers/me/food-preferences/dietary`

ابعت **كل** الأكواد المختارة من كل الصفحات. Array فاضي أو `["NONE"]` = «لا أتبع نظاماً غذائياً معيناً».

```json
{
  "dietaryPreferenceCodes": ["KETO", "VEGETARIAN"]
}
```

الرد نفس شكل الـ GET.

كود مش معروف أو inactive = `400` `food_preferences.invalid_dietary_preference`.

### أكواد البرامج الغذائية الحالية

| code | nameAr | nameEn |
|---|---|---|
| `VEGETARIAN` | نباتي | Vegetarian |
| `KETO` | كيتو | Keto |
| `LOW_CARB` | قليل الكربوهيدرات | Low carb |
| `LACTOSE_FREE` | خالٍ من اللاكتوز | Lactose free |
| `GLUTEN_FREE` | خالٍ من الجلوتين | Gluten free |

اعتمد على الـ GET. الأدمن يقدر يضيف/يعطّل أكواد.

---

## ملخص الربط

| الشاشة | التحميل | الحفظ | body |
|---|---|---|---|
| أكل غير مفضل | `GET .../disliked-ingredients?page&pageSize&search` | `PUT` نفس المسار | `{ ingredientCodes: ["INGREDIENT:{guid}"] }` أو `[]` / `["NONE"]` |
| حساسية | `GET .../allergies?page&pageSize&search` | `PUT` نفس المسار | `{ allergies: [{ code, severity? }] }` أو `[]` |
| برامج غذائية | `GET .../dietary?page&pageSize&search` | `PUT` نفس المسار | `{ dietaryPreferenceCodes: ["KETO"] }` أو `[]` |

كل `PUT` بيمسّ الشاشة دي بس. حفظ الحساسية مش بيمسح الأكل غير المفضل، والعكس.

---

## أخطاء شائعة

- متبعتش كود حساسية (`MILK`) في شاشة الأكل غير المفضل
- متبعتش `INGREDIENT:{guid}` في شاشة الحساسية
- متخلطش برامج غذائية (`KETO`) مع برامج الاشتراك (`programId`)
- متستخدمش الـ PUT الكامل `/food-preferences` وأنت فاتح شاشة واحدة — هتمسح الباقي لو مبعتوش
- متـhardcode-ش نص «لا توجد حساسية» في التطبيق؛ خده من أول عنصر في القائمة
- متبعتش اختيارات الصفحة الحالية بس في الـ PUT — ابعت كل المختار عبر الصفحات
- أول عنصر (`NONE`) يظهر في الصفحة الأولى فقط؛ متتعرضوش من `noneOption` كمان مرة
