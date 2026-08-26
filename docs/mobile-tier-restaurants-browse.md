# استعراض مطاعم التصنيف — ربط الموبايل

ملف لزر **استعراض المطاعم** على كروت التصنيف (سيلكت / بلس / ماكس) في فلو الاشتراك.

الدليل الكامل للمدة والتصنيف: [`mobile-duration-tier-flow.md`](./mobile-duration-tier-flow.md).  
ترتيب كل شاشات الاشتراك: [`mobile-customer-subscription-flow.md`](./mobile-customer-subscription-flow.md).

Base URL:

```text
https://maelmate.runasp.net/api/v1
```

كل الطلبات تحتاج Customer access token:

```http
Authorization: Bearer <customer-access-token>
Accept: application/json
Accept-Language: ar
```

لا ترسل `customerId`. الباك يأخذه من الـJWT.

---

## مهم: في endpointين للمطاعم. لا تخلط بينهم

| الشاشة | Endpoint | متى |
|---|---|---|
| معاينة على كرت التصنيف (الـoverlay/الشبكة اللي في التصميم) | `GET .../programs/{programId}/bundles/{bundleId}/tiers/{tier}/restaurants` | زر «استعراض المطاعم» **قبل** اختيار المنطقة والتاريخ |
| أهلية المطاعم بعد الاشتراك | `GET /customer/subscription-flow/restaurants?...context` | بعد المدة + التصنيف + المنطقة + التاريخ |

هذا الملف للصف الأول فقط. قائمة الأهلية تفحص المنيو والسعة والتغطية والحساسية؛ قائمة المعاينة **لا** تفعل ذلك.

كروت اللوجوهات الدائرية على كرت التصنيف **ليست** كروت الشبكة. هي معاينة فقط من `GET .../tiers`.

---

## منين تجيب القيم

من الشاشات السابقة احتفظ بـ:

| القيمة | المصدر |
|---|---|
| `programId` | شاشة البرامج |
| `bundleId` | شاشة الباقات |
| `tier` | كرت التصنيف المضغوط عليه: `Basic` أو `Platinum` أو `Elite` |
| `restaurantIds` (اختياري) | `tiers[].restaurants[].restaurantId` لنفس الكرت |

قيم `tier` من الباك كما هي. لا تحوّل «سيلكت» إلى enum محلي. النص العربي للعرض يأتي من `nameAr` على كرت التصنيف.

| قيمة `tier` | الاسم الظاهر عادة (من الأدمن، قابل للتغيير) |
|---|---|
| `Basic` | سيلكت |
| `Platinum` | بلس |
| `Elite` | ماكس |

---

## 1. كرت التصنيف (مصدر الـIDs واللوجوهات)

```http
GET /customer/subscription-flow/programs/{programId}/bundles/{bundleId}/tiers?includeFriday=false
```

كل كرت تصنيف فيه:

```json
{
  "tier": "Basic",
  "nameAr": "سيلكت",
  "nameEn": "Select",
  "restaurantCount": 25,
  "restaurants": [
    {
      "restaurantId": "97db2bbb-b652-4aaa-8584-3414e6966792",
      "logoUrl": "/api/v1/uploads/content?key=uploads%2Frestaurant%2Fbranches%2Fmain.webp"
    }
  ]
}
```

`restaurants[]` هنا **لوجو + ID فقط**. لا يوجد اسم ولا تقييم ولا عدد وجبات. ارسم الدوائر المتداخلة وعداد `+N` من `restaurantCount`.

زر «استعراض المطاعم» على **نفس الكرت** يفتح الـoverlay ويستدعي endpoint الشبكة أدناه بـ`tier` هذا الكرت.

---

## 2. شبكة استعراض المطاعم (الـoverlay)

```http
GET /customer/subscription-flow/programs/{programId}/bundles/{bundleId}/tiers/{tier}/restaurants
```

مع IDs من كرت التصنيف (اختياري، يحافظ على ترتيب الدوائر):

```http
GET /customer/subscription-flow/programs/{programId}/bundles/{bundleId}/tiers/{tier}/restaurants?restaurantIds={id1}&restaurantIds={id2}
```

### قواعد الـquery

- `tier` في المسار: نفس `tier` من الكرت (`Basic` / `Platinum` / `Elite`).
- بدون `restaurantIds`: كل مطاعم هذا التصنيف على نفس البرنامج والباقة.
- مع `restaurantIds`: الكروت بترتيب الـIDs المرسلة. أي ID ليس في هذا التصنيف يُتجاهل (لا يرجع خطأ).
- كرّر المفتاح: `restaurantIds=guid&restaurantIds=guid`. لا ترسل JSON في الـbody. الطلب `GET`.
- لو هتبعت كل الـIDs من الكرت أو هتسيبها فاضية، النتيجة لنفس التصنيف واحدة. الأسهل: **متبعتش IDs** إلا لو محتاج ترتيب الدوائر.

### Dio

```dart
Future<List<RestaurantBrowseCard>> fetchTierRestaurants({
  required String programId,
  required String bundleId,
  required String tier, // 'Basic' | 'Platinum' | 'Elite'
  List<String>? restaurantIds,
}) async {
  final response = await dio.get(
    '/customer/subscription-flow/programs/$programId/bundles/$bundleId/tiers/$tier/restaurants',
    queryParameters: {
      if (restaurantIds != null && restaurantIds.isNotEmpty)
        'restaurantIds': restaurantIds,
    },
  );
  final list = response.data as List<dynamic>;
  return list
      .map((item) => RestaurantBrowseCard.fromJson(item as Map<String, dynamic>))
      .toList();
}
```

Dio مع `List<String>` يبني `restaurantIds=id1&restaurantIds=id2`. هذا هو الشكل الصحيح.

---

## 3. رسم الكرت

الاستجابة مصفوفة. كل عنصر كرت واحد:

```json
[
  {
    "restaurantId": "97db2bbb-b652-4aaa-8584-3414e6966792",
    "nameAr": "دايت هاوس",
    "nameEn": "Diet House",
    "logoUrl": "/api/v1/uploads/content?key=uploads%2Frestaurant%2Fbranches%2Fdiet-house.webp",
    "rating": 4.8,
    "mealCount": 28,
    "bundleMealCount": 28,
    "tier": "Basic",
    "tierNameAr": "سيلكت",
    "tierNameEn": "Select",
    "tierLogoUrl": "/api/v1/uploads/content?key=uploads%2Fadmin%2Ftiers%2Fbasic.webp",
    "classifications": [
      {
        "logoUrl": "/api/v1/uploads/content?key=uploads%2Fadmin%2Ftiers%2Fbasic.webp",
        "tier": "Basic"
      }
    ]
  }
]
```

### ربط الحقول بالتصميم

| عنصر الـUI | الحقل | ملاحظات |
|---|---|---|
| لوجو المطعم | `logoUrl` | لو `null` اعرض Placeholder. المسار نسبي على نفس الـhost |
| الاسم | `nameAr` أو `nameEn` | حسب `Accept-Language` / لغة التطبيق |
| التقييم | `rating` | رقم عشري أو `null` إذا لا توجد تقييمات. المفتاح موجود دائمًا |
| عدد وجبات المطعم | `mealCount` | مثال: `28 وجبة` |
| بادج الباقة | `bundleMealCount` | مثال: `28 وجبة في الباقة` |
| التصنيفات المربوطة بالمطعم | `classifications[]` | `logoUrl` + `tier` فقط. المطعم قد يكون في أكثر من تصنيف عبر باقات أخرى |
| عنوان الـoverlay | ثابت من التطبيق | «استعراض المطاعم» |

`tier` / `tierNameAr` / `tierLogoUrl` على الكرت = تصنيف المطعم **في هذه الباقة**.  
`classifications` = كل تصنيفاته الظاهرة للعميل (قد تختلف عن تصنيف الكرت الحالي).

### صور الـAPI

`logoUrl` و`tierLogoUrl` و`classifications[].logoUrl` غالبًا مسار نسبي:

```text
/api/v1/uploads/content?key=uploads%2Frestaurant%2Fbranches%2Fdiet-house.webp
```

اعرضه كـURL كامل على نفس الـbase، أو مرّره لودجت الصور كما تفعل مع لوجوهات كرت التصنيف. لا تلصق الـkey يدويًا.

### لغة العرض

```dart
final isAr = currentLanguageCode == 'ar';
final name = isAr ? card.nameAr : card.nameEn;
```

`classifications[]` لا ترجع `nameAr` / `nameEn`. اعرض شعار التصنيف من `logoUrl`، واستخدم `tier` (`Basic` / `Platinum` / `Elite`) لو محتاج تطابقه مع كروت شاشة التصنيف. اسم المطعم من `nameAr` / `nameEn` على الكرت نفسه.

---

## 4. بطاقة مطعم واحد (اختياري)

نفس شكل الكرت، لكن كائن واحد وليس مصفوفة:

```http
GET /customer/subscription-flow/programs/{programId}/bundles/{bundleId}/restaurants/{restaurantId}
```

استخدمه فقط لو محتاج كرت واحد بدون تفاصيل. عند الضغط على كرت في الشبكة افتح صفحة التفاصيل:

```http
GET /customer/subscription-flow/programs/{programId}/bundles/{bundleId}/restaurants/{restaurantId}/details
```

---

## 5. تفاصيل المطعم (لوجو، منشأ، وجبات حسب الوجبة)

هذه شاشة التصميم: صورة الغلاف، لوجو واسم المطعم، بلد منشأ المكونات الأساسية، ثم تبويبات الإفطار/الغداء/العشاء/السناك.

لا تستخدم `GET /customer/subscription-flow/restaurants/{restaurantId}` هنا. ذلك المسار يحتاج منطقة وتاريخ وأهلية.

```http
GET /customer/subscription-flow/programs/{programId}/bundles/{bundleId}/restaurants/{restaurantId}/details
Authorization: Bearer <customer-access-token>
Accept-Language: ar
```

### استجابة `200`

```json
{
  "restaurantId": "97db2bbb-b652-4aaa-8584-3414e6966792",
  "nameAr": "مطبخ البروتين",
  "nameEn": "The Protein Kitchen",
  "logoUrl": "/api/v1/uploads/content?key=uploads%2Frestaurant%2Fbranches%2Fprotein.webp",
  "coverImageUrl": "/api/v1/uploads/content?key=uploads%2Frestaurant%2Fmeals%2Fbreakfast.webp",
  "coreIngredients": [
    {
      "ingredientId": "aaaaaaaa-aaaa-aaaa-aaaa-aaaaaaaaaaaa",
      "nameAr": "زيت زيتون",
      "nameEn": "Olive oil",
      "iconUrl": "/api/v1/uploads/content?key=uploads%2Frestaurant%2Fingredients%2Foils.webp",
      "isLocal": false,
      "originCountryCode": "ES",
      "originCountryNameAr": "اسبانيا",
      "originCountryNameEn": "Spain",
      "flagEmoji": "🇪🇸"
    },
    {
      "ingredientId": "bbbbbbbb-bbbb-bbbb-bbbb-bbbbbbbbbbbb",
      "nameAr": "دجاج مشوي",
      "nameEn": "Grilled chicken",
      "iconUrl": "/api/v1/uploads/content?key=uploads%2Frestaurant%2Fingredients%2Fchicken.webp",
      "isLocal": true,
      "originCountryCode": "KW",
      "originCountryNameAr": "الكويت",
      "originCountryNameEn": "Kuwait",
      "flagEmoji": "🇰🇼"
    }
  ],
  "ingredientOrigins": [
    {
      "category": "oils",
      "nameAr": "الزيوت",
      "nameEn": "Oils",
      "isLocal": false,
      "sourceAr": "مستوردة",
      "sourceEn": "Imported",
      "originCountryCode": "ES",
      "originCountryNameAr": "اسبانيا",
      "originCountryNameEn": "Spain",
      "flagEmoji": "🇪🇸",
      "iconUrl": "/api/v1/uploads/content?key=uploads%2Frestaurant%2Fingredients%2Foils.webp"
    },
    {
      "category": "seafood",
      "nameAr": "الأسماك",
      "nameEn": "Fish",
      "isLocal": false,
      "sourceAr": "مستوردة",
      "sourceEn": "Imported",
      "originCountryCode": "NO",
      "originCountryNameAr": "النرويج",
      "originCountryNameEn": "Norway",
      "flagEmoji": "🇳🇴"
    },
    {
      "category": "poultry",
      "nameAr": "الدواجن",
      "nameEn": "Poultry",
      "isLocal": true,
      "sourceAr": "طازجة محلية",
      "sourceEn": "Local fresh",
      "originCountryCode": "KW",
      "originCountryNameAr": "الكويت",
      "originCountryNameEn": "Kuwait",
      "flagEmoji": "🇰🇼"
    },
    {
      "category": "meat",
      "nameAr": "اللحوم",
      "nameEn": "Meat",
      "isLocal": false,
      "sourceAr": "مستوردة",
      "sourceEn": "Imported",
      "originCountryCode": "AU",
      "originCountryNameAr": "أستراليا",
      "originCountryNameEn": "Australia",
      "flagEmoji": "🇦🇺"
    }
  ],
  "mealGroups": [
    {
      "slot": "breakfast",
      "nameAr": "الإفطار",
      "nameEn": "Breakfast",
      "meals": [
        {
          "mealId": "11111111-1111-1111-1111-111111111111",
          "nameAr": "اسم الوجبة",
          "nameEn": "Meal name",
          "imageUrl": "/api/v1/uploads/content?key=uploads%2Frestaurant%2Fmeals%2Fbreakfast.webp",
          "calories": 550,
          "proteinG": 35,
          "carbsG": 55,
          "fatG": 18,
          "ingredients": [
            { "ingredientId": "22222222-2222-2222-2222-222222222222", "nameAr": "دجاج مشوي", "nameEn": "Grilled chicken", "category": "poultry", "isCore": true, "iconUrl": "/api/v1/uploads/content?key=uploads%2Frestaurant%2Fingredients%2Fchicken.webp" },
            { "ingredientId": "33333333-3333-3333-3333-333333333333", "nameAr": "أرز", "nameEn": "Rice", "category": "grains", "isCore": false, "iconUrl": "/api/v1/uploads/content?key=uploads%2Frestaurant%2Fingredients%2Frice.webp" }
          ]
        }
      ]
    }
  ]
}
```

`mealGroups` ترجع فقط الـslots اللي فيها وجبات (فطار / غداء / عشاء / سناك). لا ترسم تبويب فاضي.

### كرت الوجبة من برّه

هذا شكل الكرت الظاهر في قائمة الوجبات. **لا** ترجع وصفًا ولا أليرجين ولا منشأ لكل مكوّن هنا. التفاصيل الأعمق لو احتجتها لاحقًا من مسار الأهلية بعد المنطقة والتاريخ.

عنوان القسم فوق الكرت: `وجبات {mealGroups[].nameAr}` مثل «وجبات الإفطار».

| عنصر الكرت | الحقل | عرض الموبايل |
|---|---|---|
| اسم الوجبة | `nameAr` / `nameEn` | عنوان كبير |
| صورة الوجبة | `imageUrl` | مربعة بزوايا دائرية. `null` → Placeholder |
| سعرات | `calories` | `{calories} سعرة حرارية` — لو `null` اخفِ العمود |
| بروتين | `proteinG` | `{proteinG} جم بروتين` |
| كربوهيدرات | `carbsG` | `{carbsG} جم كربوهيدرات` |
| دهون | `fatG` | `{fatG} جم دهون` |
| شيبس المكونات | `ingredients[]` | الاسم + `iconUrl` (أيقونة الوجبة). لو `iconUrl` فاضي استخدم أيقونة `category` محليًا. `isCore` بادج اختياري. أول 4 ظاهرة وآخر شيبس «المزيد ..» محليًا لو العدد أكبر |

مثال عنصر الوجبة فقط:

```json
{
  "mealId": "11111111-1111-1111-1111-111111111111",
  "nameAr": "اسم الوجبة",
  "nameEn": "Meal name",
  "imageUrl": "/api/v1/uploads/content?key=uploads%2Frestaurant%2Fmeals%2Fbreakfast.webp",
  "calories": 550,
  "proteinG": 35,
  "carbsG": 55,
  "fatG": 18,
  "ingredients": [
    { "ingredientId": "22222222-2222-2222-2222-222222222222", "nameAr": "دجاج مشوي", "nameEn": "Grilled chicken", "category": "poultry", "isCore": true, "iconUrl": "/api/v1/uploads/content?key=uploads%2Frestaurant%2Fingredients%2Fchicken.webp" },
    { "ingredientId": "33333333-3333-3333-3333-333333333333", "nameAr": "أرز بني", "nameEn": "Brown rice", "category": "grains", "isCore": false, "iconUrl": "/api/v1/uploads/content?key=uploads%2Frestaurant%2Fingredients%2Frice.webp" },
    { "ingredientId": "44444444-4444-4444-4444-444444444444", "nameAr": "بروكلي", "nameEn": "Broccoli", "category": "produce" },
    { "ingredientId": "55555555-5555-5555-5555-555555555555", "nameAr": "جزر", "nameEn": "Carrot", "category": "produce" }
  ]
}
```

`category` للشيبس: `oils` / `seafood` / `poultry` / `meat` / `dairy` / `produce` / `grains` / `other`. الأيقونة من عند الموبايل حسب القيمة. «المزيد ..» ليست من الباك.

الوحدات (`سعرة حرارية` / `جم`) تترسم في التطبيق. الباك يرجع أرقام فقط.

### تفاصيل الوجبة عند الضغط (عرض فقط)

```http
GET /customer/subscription-flow/programs/{programId}/bundles/{bundleId}/restaurants/{restaurantId}/meals/{mealId}
```

`mealId` من كرت الوجبة. الشاشة عرض فقط؛ لا اختيار يوم ولا إضافة للسلة هنا.

```json
{
  "mealId": "11111111-1111-1111-1111-111111111111",
  "nameAr": "دجاج مشوي مع الأرز البني",
  "nameEn": "Grilled chicken with brown rice",
  "descriptionAr": "وجبة متكاملة عالية البروتين ومصنوعة من مكونات طازجة.",
  "descriptionEn": "A complete high-protein meal made from fresh ingredients.",
  "imageUrl": "/api/v1/uploads/content?key=uploads%2Frestaurant%2Fmeals%2Fchicken.webp",
  "slot": "lunch",
  "slotNameAr": "الغداء",
  "slotNameEn": "Lunch",
  "restaurantId": "97db2bbb-b652-4aaa-8584-3414e6966792",
  "restaurantNameAr": "مطبخ البروتين",
  "restaurantNameEn": "The Protein Kitchen",
  "restaurantLogoUrl": "/api/v1/uploads/content?key=uploads%2Frestaurant%2Fbranches%2Fprotein.webp",
  "restaurantRating": 4.8,
  "restaurantRatingCount": 520,
  "deliveryWindowStart": null,
  "deliveryWindowEnd": null,
  "ingredients": [
    { "ingredientId": "22222222-2222-2222-2222-222222222222", "nameAr": "دجاج مشوي", "nameEn": "Grilled chicken", "category": "poultry", "isCore": true, "iconUrl": "/api/v1/uploads/content?key=uploads%2Frestaurant%2Fingredients%2Fchicken.webp" }
  ],
  "nutrition": {
    "calories": 550,
    "proteinG": 65,
    "carbsG": 60,
    "fatG": 15,
    "fiberG": 7,
    "carbsPercent": 38,
    "proteinPercent": 41,
    "fatPercent": 21
  },
  "allergens": [
    { "code": "GLUTEN", "nameAr": "جلوتين", "nameEn": "Gluten" },
    { "code": "MILK", "nameAr": "لاكتوز", "nameEn": "Lactose" },
    { "code": "TREE_NUTS", "nameAr": "مكسرات", "nameEn": "Nuts" }
  ]
}
```

| عنصر الصفحة | الحقل |
|---|---|
| صورة الهيدر | `imageUrl` |
| تقييم المطعم | `restaurantRating` + `restaurantRatingCount` — لو التقييم `null` اخفِ الكرت |
| اسم الوجبة | `nameAr` |
| اسم ولوجو المطعم | `restaurantNameAr` + `restaurantLogoUrl` — علامة التوثيق ثابتة |
| الوصف | `descriptionAr` |
| وقت التوصيل | `deliveryWindowStart` / `End` — في المعاينة `null`؛ اخفِ الصف |
| المكونات | `ingredients[]` — الاسم + `iconUrl` (أيقونة الوجبة). لو فاضي فالـfallback من `category`. `isCore` بادج «أساسي» اختياري |
| ألياف / بروتين / كارب / دهون / سعرات | `nutrition.fiberG` / `proteinG` / `carbsG` / `fatG` / `calories` |
| حلقات التوزيع | `nutrition.carbsPercent` / `proteinPercent` / `fatPercent` — مجموعها 100 أو الكل `null` |
| مسببات الحساسية | `allergens[]` أسماء قصيرة: جلوتين، لاكتوز، مكسرات. الأيقونة من `code` عند الموبايل |

مفيش صوت للوجبة في الـAPI. زر السماعة UI فقط أو اخفه.

### ربط الشاشة

| عنصر الـUI | الحقل |
|---|---|
| صورة الهيدر | `coverImageUrl` — أول صورة وجبة، أو Placeholder |
| اللوجو الدائري | `logoUrl` |
| اسم المطعم | `nameAr` / `nameEn` |
| علامة التوثيق | المطعم المعتمد أصلًا؛ يمكن عرضها ثابتة |
| كروت المكونات الأساسية (شيبس) | `coreIngredients[]` — على مستوى المطعم، مش جوه كل وجبة |
| اسم المكون الأساسي | `coreIngredients[].nameAr` / `nameEn` |
| أيقونة المكون الأساسي | `coreIngredients[].iconUrl` (`RestaurantIconUrl` ثم `MealIconUrl`) |
| بلد المنشأ | `originCountryCode` + `originCountryNameAr` / `En` + `flagEmoji` + `isLocal` — `null` لو المنشأ مخفي عن العميل |
| كروت «بلد المنشأ للمكونات الأساسية» | `ingredientOrigins[]` — فقط المكوّنات المعلَّمة `isCore` ومرئية للعميل |
| عنوان الكرت | `nameAr` (الزيوت / الأسماك / الدواجن / اللحوم) |
| أيقونة الكرت | `iconUrl` (أيقونة المطعم). لو `null` استخدم أيقونة `category` محليًا |
| السطر تحت العنوان | `{sourceAr} {originCountryNameAr}` مثل «مستوردة اسبانيا» أو «طازجة محلية الكويت» |
| العلم | `flagEmoji` |
| تبويبات الوجبات | `mealGroups[].slot` بالترتيب الراجع |
| عنوان قائمة الوجبات | «وجبات {nameAr}» من المجموعة المحددة |
| صورة الوجبة | `meals[].imageUrl` |
| اسم الوجبة | `meals[].nameAr` |
| السعرات / بروتين / كارب / دهون | `calories` / `proteinG` / `carbsG` / `fatG` — لو `null` اخفِ الرقم |
| شيبس المكونات | `meals[].ingredients[]` للمكونات **غير الأساسية** فقط + `iconUrl`، مع fallback على `category` |

المكونات الأساسية تظهر مرة واحدة في `coreIngredients[]` أعلى تفاصيل المطعم — لا تكررها من داخل `mealGroups`.

لا تثبت «الزيوت» أو «مستوردة» في التطبيق. استخدم `nameAr` و`sourceAr` من الباك.

### Dio

```dart
final details = await dio.get(
  '/customer/subscription-flow/programs/$programId/bundles/$bundleId/restaurants/$restaurantId/details',
);
final meal = await dio.get(
  '/customer/subscription-flow/programs/$programId/bundles/$bundleId/restaurants/$restaurantId/meals/$mealId',
);
```

---

## 6. أخطاء هذه الشاشة

| HTTP | الكود | المعنى | تصرف الموبايل |
|---:|---|---|---|
| 401 | — | التوكن منتهي | Refresh ثم أعد الطلب مرة واحدة |
| 403 | — | التوكن ليس Customer | أعد تسجيل الدخول |
| 404 | `subscription_flow.bundle_not_found` | البرنامج أو الباقة غير موجودة/غير فعالة | ارجع لشاشة الباقات |
| 404 | `subscription_flow.restaurant_not_found` | المطعم ليس في تصنيف هذه الباقة | ارجع للشبكة |
| 404 | `subscription_flow.meal_not_found` | الوجبة ليست في باقة المطعم | ارجع لتفاصيل المطعم |
| 400 | — | `tier` ليس `Basic` / `Platinum` / `Elite` | استخدم `tier` الراجع من `GET .../tiers` كما هو |
| 200 + `[]` | — | لا مطاعم في هذا التصنيف، أو كل الـIDs المرسلة لا تنتمي له | شاشة فاضية داخل الـoverlay، لا ترجع للخلف تلقائيًا |

---

## 7. خطوات الربط

1. المستخدم في شاشة التصنيفات بعد اختيار المدة.
2. `GET .../tiers?includeFriday={includeFriday}` بدون `durationDays` عند أول رسم.
3. ارسم كروت سيلكت/بلس/ماكس من `nameAr`/`nameEn` و`logoUrl` و`restaurants[].logoUrl`.
4. عند الضغط على **استعراض المطاعم** على كرت:
   - افتح overlay بعنوان «استعراض المطاعم».
   - استدعِ `GET .../tiers/{tier}/restaurants`.
   - `tier` = `card.tier` (مثل `Basic`).
   - اختياري: `restaurantIds` = `card.restaurants.map((r) => r.restaurantId)`.
5. ارسم Grid من الكروت: لوجو، اسم، تقييم، `mealCount`، `classifications`، بادج `bundleMealCount`.
6. الضغط على كرت مطعم يفتح التفاصيل: `GET .../restaurants/{restaurantId}/details`.
7. الضغط على وجبة يفتح صفحة العرض: `GET .../restaurants/{restaurantId}/meals/{mealId}`.
8. إغلاق الصفحات دي لا يغيّر التصنيف المختار. زر **التالي** في شاشة التصنيف هو اللي يكمل للملخص.
9. بعد اختيار التصنيف + المدة + المنطقة + التاريخ استخدم `GET /customer/subscription-flow/restaurants` للأهلية، وليس endpoint المعاينة.

---

## ما لا يفعله الموبايل

- لا تستدعِ `GET /customer/subscription-flow/restaurants` من زر المعاينة (ناقص `startDate` و`regionId`).
- لا تعمل loop على `GET .../restaurants/{restaurantId}` لبناء الشبكة.
- لا تفترض أن `tiers[].restaurants` فيها اسم أو تقييم.
- لا تضيف `NameAr` / `NameEn` على موديل لوجوهات كرت التصنيف؛ العقد هناك `restaurantId` + `logoUrl` فقط.
- لا تعتبر قائمة المعاينة قائمة الطلب النهائي. الأهلية لاحقًا قد تستبعد مطعمًا حسب المنطقة أو السعة أو الحساسية.
- لا تحسب التقييم أو عدد الوجبات محليًا.

---

## Checklist

- [ ] زر «استعراض المطاعم» على كرت سيلكت يستدعي `tiers/Basic/restaurants` (أو قيمة `tier` الراجعة)
- [ ] كرت بلس → `Platinum`، كرت ماكس → `Elite`، من الـAPI مش من الاسم العربي
- [ ] الشبكة من طلب واحد (مصفوفة) وليس طلب لكل مطعم
- [ ] الكرت يعرض `logoUrl` + الاسم + `rating` + `mealCount` + `classifications`
- [ ] `bundleMealCount` على بادج الباقة
- [ ] `logoUrl == null` → Placeholder
- [ ] `rating == null` → اخفِ الرقم أو اعرض حالة «بدون تقييم»، لا تعرض `0`
- [ ] الصور النسبية تتحل على نفس الـAPI host
- [ ] الـoverlay للمعاينة فقط؛ الفلو يكمل بزر التالي وليس باختيار مطعم من هنا
- [ ] الضغط على كرت مطعم يستدعي `.../restaurants/{id}/details` وليس مسار الأهلية
- [ ] التفاصيل تعرض `logoUrl` + `coreIngredients` + `ingredientOrigins` + `mealGroups` حسب الفطار/الغداء/العشاء/السناك
- [ ] شيبس المكونات الأساسية من `coreIngredients[]` فقط — مش من داخل كل وجبة
- [ ] تبويب الوجبة يظهر فقط إذا رجعت مجموعته في `mealGroups`
- [ ] الضغط على وجبة يستدعي `.../meals/{mealId}` — عرض فقط، بدون اختيار يوم أو سلة
- [ ] مسببات الحساسية أسماء قصيرة (`جلوتين` / `لاكتوز` / `مكسرات`) من `allergens[].nameAr`
- [ ] وقت التوصيل مخفي عندما `deliveryWindowStart` و`deliveryWindowEnd` = `null`
