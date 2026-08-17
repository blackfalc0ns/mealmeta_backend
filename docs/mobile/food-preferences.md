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

### «مفيش قيود» (مفيش حساسية / باكل كل حاجة)

الحالة الافتراضية للعميل هي **مفيش أي قيد**. مش لازم يختار حاجة.

- إرسال `PUT` بـ array فاضي = العميل مالوش قيد على الشاشة دي (بيمسح اختياراته لو كان مختار قبل كده).
- كل شاشة `GET` بترجع علامة صريحة لما `selectedCount == 0`:
  - الحساسية: `hasNoAllergies: true`
  - الأكل غير المفضل: `eatsEverything: true`
  - البرامج الغذائية: `hasNoDietaryRestriction: true`
- في الحالة دي كل الوجبات آمنة على الشاشة دي (مفيش استبعاد بسببها).
- الـ UI يقدر يعرض checkbox زي «مفيش عندي حساسية» / «باكل كل حاجة» — تفعيله = ابعت array فاضي.

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

`GET /api/v1/customers/me/food-preferences/disliked-ingredients`

القائمة من مكونات المطاعم المعتمدة، **صف واحد لكل اسم**. الكود `INGREDIENT:{guid}` لممثل المجموعة. اختيار «دجاج» يمنع دجاج كل المطاعم حتى لو الـ GUID مختلف. متبعتش كود حساسية هنا.

```json
{
  "ingredients": [
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
  "eatsEverything": false
}
```

اعرض `nameAr` / `nameEn`. الـ checkbox = `isSelected`. `eatsEverything: true` (أو `selectedCount == 0`) = العميل مش بيكره أي مكوّن، اعرض حالة «باكل كل حاجة».

### الحفظ

`PUT /api/v1/customers/me/food-preferences/disliked-ingredients`

ابعت **كل** الاختيارات الحالية، مش الـ delta. Array فاضي = مسح الأكل غير المفضل فقط (الحساسية والبرامج الغذائية مش بتتأثر).

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

`GET /api/v1/customers/me/food-preferences/allergies`

القائمة من أكواد الحساسية الموجودة على مكوّنات المطاعم المعتمدة (الأسماء من كتالوج التعريفات). اختيار محفوظ يظهر حتى لو المطعم شاله بعدين.

```json
{
  "allergens": [
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
  "hasNoAllergies": false
}
```

`hasNoAllergies: true` (أو `selectedCount == 0`) = مفيش حساسية عند العميل، اعرض حالة «مفيش عندي حساسية».

### الحفظ

`PUT /api/v1/customers/me/food-preferences/allergies`

ابعت العناصر المختارة فقط. Array فاضي = مسح الحساسية فقط.

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

`GET /api/v1/customers/me/food-preferences/dietary`

دي تفضيلات أكل (نباتي / كيتو / …)، **مش** برنامج الاشتراك الشهري.

```json
{
  "dietaryPreferences": [
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
  "hasNoDietaryRestriction": false
}
```

يسمح باختيار أكتر من برنامج. `hasNoDietaryRestriction: true` (أو `selectedCount == 0`) = مفيش نظام غذائي، اعرض حالة «باكل كل حاجة عادي».

### الحفظ

`PUT /api/v1/customers/me/food-preferences/dietary`

ابعت **كل** الأكواد المختارة. Array فاضي = مسح البرامج الغذائية فقط.

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
| أكل غير مفضل | `GET .../disliked-ingredients` | `PUT` نفس المسار | `{ ingredientCodes: ["INGREDIENT:{guid}"] }` |
| حساسية | `GET .../allergies` | `PUT` نفس المسار | `{ allergies: [{ code, severity? }] }` |
| برامج غذائية | `GET .../dietary` | `PUT` نفس المسار | `{ dietaryPreferenceCodes: ["KETO"] }` |

كل `PUT` بيمسّ الشاشة دي بس. حفظ الحساسية مش بيمسح الأكل غير المفضل، والعكس.

---

## أخطاء شائعة

- متبعتش كود حساسية (`MILK`) في شاشة الأكل غير المفضل
- متبعتش `INGREDIENT:{guid}` في شاشة الحساسية
- متخلطش برامج غذائية (`KETO`) مع برامج الاشتراك (`programId`)
- متستخدمش الـ PUT الكامل `/food-preferences` وأنت فاتح شاشة واحدة — هتمسح الباقي لو مبعتوش
