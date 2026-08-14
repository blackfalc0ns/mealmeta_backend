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

استخدمه فقط لو فتحت تفاصيل كرت واحد من الشبكة. لزر «استعراض المطاعم» استخدم قائمة `tiers/{tier}/restaurants` وليس N طلب لكل ID.

---

## 5. أخطاء هذه الشاشة

| HTTP | الكود | المعنى | تصرف الموبايل |
|---:|---|---|---|
| 401 | — | التوكن منتهي | Refresh ثم أعد الطلب مرة واحدة |
| 403 | — | التوكن ليس Customer | أعد تسجيل الدخول |
| 404 | `subscription_flow.bundle_not_found` | البرنامج أو الباقة غير موجودة/غير فعالة | ارجع لشاشة الباقات |
| 400 | — | `tier` ليس `Basic` / `Platinum` / `Elite` | استخدم `tier` الراجع من `GET .../tiers` كما هو |
| 200 + `[]` | — | لا مطاعم في هذا التصنيف، أو كل الـIDs المرسلة لا تنتمي له | شاشة فاضية داخل الـoverlay، لا ترجع للخلف تلقائيًا |

مطعم واحد غير موجود على مسار البطاقة المفردة يرجع `404` و`subscription_flow.restaurant_not_found`. قائمة التصنيف لا تفعل ذلك: تتخطى الـID وترجع الباقي.

---

## 6. خطوات الربط

1. المستخدم في شاشة التصنيفات بعد اختيار المدة.
2. `GET .../tiers?includeFriday={includeFriday}` بدون `durationDays` عند أول رسم.
3. ارسم كروت سيلكت/بلس/ماكس من `nameAr`/`nameEn` و`logoUrl` و`restaurants[].logoUrl`.
4. عند الضغط على **استعراض المطاعم** على كرت:
   - افتح overlay بعنوان «استعراض المطاعم».
   - استدعِ `GET .../tiers/{tier}/restaurants`.
   - `tier` = `card.tier` (مثل `Basic`).
   - اختياري: `restaurantIds` = `card.restaurants.map((r) => r.restaurantId)`.
5. ارسم Grid من الكروت: لوجو، اسم، تقييم، `mealCount`، `classifications`، بادج `bundleMealCount`.
6. إغلاق الـoverlay لا يغيّر التصنيف المختار ولا يكمل الفلو. زر **التالي** في شاشة التصنيف هو اللي يكمل للملخص.
7. بعد اختيار التصنيف + المدة + المنطقة + التاريخ استخدم `GET /customer/subscription-flow/restaurants` للأهلية، وليس endpoint المعاينة.

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
