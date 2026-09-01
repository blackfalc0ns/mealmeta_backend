# الموبايل: عنوان التوصيل + استعراض المطاعم

دليل ربط Flutter لمسارين مرتبطين:

1. **عنوان المنزل من الخريطة** — autocomplete / pin / GPS ثم حفظ العنوان.
2. **استعراض مطاعم التصنيف** — يرجع فقط المطاعم اللي **بتوصل** لمنطقة عنوان العميل.

Base URL:

```text
https://maelmate.runasp.net/api/v1
```

Headers لكل طلبات العميل:

```http
Authorization: Bearer <customer-access-token>
Accept: application/json
Accept-Language: ar
```

---

## ترتيب الشاشات (الفلو الكامل)

```text
1) خريطة + فورم عنوان المنزل
   → POST /maps/reverse-geocode
   → POST /customers/me/addresses  (لازم regionId + lat/lng)

2) فلو الاشتراك (برنامج → باقة → مدة → تصنيف)
   → GET .../tiers?...     (كروت التصنيف + عداد المطاعم — العنوان من التوكن)
   → GET .../tiers/{tier}/restaurants   (شبكة استعراض المطاعم — العنوان من التوكن)
   → لو 409 checkout.address_required → شاشة حفظ العنوان ثم الرجوع وإعادة الطلب

3) لاحقًا (بعد اختيار التاريخ والمنطقة في الـ context)
   → GET /customer/subscription-flow/restaurants?...   (أهلية كاملة: منيو + سعة + حساسية)
```

**مهم:** خطوة (2) ≠ خطوة (3). المعاينة تفحص **تغطية التوصيل + التصنيف** فقط. الأهلية الكاملة في endpoint منفصل.

---

## الجزء 1 — عنوان المنزل من الخريطة

التفاصيل الكاملة: [`customer-address-google-maps.md`](./customer-address-google-maps.md)

### 1.1 تأكيد الموقع على الخريطة

```http
POST /maps/reverse-geocode
Content-Type: application/json
```

```json
{
  "latitude": 29.339405795601476,
  "longitude": 48.08187089860439
}
```

**Anonymous** — مش محتاج token للـ geocode.

### 1.2 ربط الاستجابة على الفورم (نفس ترتيب التصميم)

| # | حقل UI | حقل API | من الخريطة؟ |
|---|--------|---------|-------------|
| 1 | المحافظة | `suggestedGovernorateId` | نعم |
| 2 | المنطقة | `suggestedRegionId` | **نعم — مطلوب للحفظ والتغطية** |
| 3 | القطعة | `suggestedBlockId` | نعم (لو null → المستخدم يختار) |
| 4 | الشارع | `street` | نعم |
| 5 | الجادة (اختياري) | `avenue` | نعم — **منفصلة عن البناية** |
| 6 | رقم المنزل / البناية | `building` | **لا — يدوي** |
| 7 | الدور (اختياري) | `floor` | **لا — يدوي** |
| 8 | رقم الشقة (اختياري) | `unit` | **لا — يدوي** |

```json
{
  "formattedAddress": "17-27 شارع 2 جادة 2، السالمية، الكويت",
  "latitude": 29.3412132,
  "longitude": 48.082421,
  "governorate": "حولي",
  "area": "السالمية",
  "block": "قطعة 5",
  "street": "شارع 2",
  "avenue": "جادة 2",
  "building": null,
  "suggestedGovernorateId": "ec19c873-a752-4797-a6e3-068ddd3ccc2a",
  "suggestedGovernorateNameAr": "حولي",
  "suggestedRegionId": "d4a9acc2-afb8-49d8-b440-f93b8622239c",
  "suggestedRegionNameAr": "السالمية",
  "suggestedBlockId": "817ce4d2-7941-4c58-a0a9-8aa72bb93610",
  "suggestedBlockNameAr": "قطعة 5"
}
```

**اللغة:** `/api/v1/maps/*` بترجع أسماء العنوان **بالعربي** حتى مع `Language: en`.

**مطابقة المنطقة من الإحداثيات:** بعد Google، الـ API بيربط الـ pin بـ Area في كatalog MealMate عبر اسم المنطقة + أقرب `Centroid` حقيقي. لو الاسم بعيد عن الـ pin، يتجاهل الاسم وياخد أقرب منطقة. التغطية بعد الحفظ تعتمد على `regionId` ده مع مناطق خدمة المطعم.

### 1.3 Flutter — بعد «تأكيد الموقع»

```dart
final place = await mapsApi.reverseGeocode(lat, lng);

governorateDropdown.value = place.suggestedGovernorateId;
areaDropdown.value = place.suggestedRegionId;      // ← لازم للتغطية لاحقًا
blockDropdown.value = place.suggestedBlockId;

streetCtrl.text = place.street ?? '';
avenueCtrl.text = place.avenue ?? '';              // NOT building
buildingCtrl.clear();
floorCtrl.clear();
unitCtrl.clear();

addressLineCtrl.text = place.formattedAddress;
latitude = place.latitude;
longitude = place.longitude;
```

### 1.4 حفظ العنوان

```http
POST /customers/me/addresses
```

```json
{
  "label": "المنزل",
  "addressLine": "17-27 شارع 2 جادة 2، السالمية، الكويت",
  "regionId": "d4a9acc2-afb8-49d8-b440-f93b8622239c",
  "blockRegionId": "817ce4d2-7941-4c58-a0a9-8aa72bb93610",
  "latitude": 29.3412132,
  "longitude": 48.082421,
  "isDefault": true,
  "notifyOnDelivery": true,
  "governorate": "حولي",
  "area": "السالمية",
  "block": "قطعة 5",
  "street": "شارع 2",
  "avenue": "جادة 2",
  "building": "17",
  "floor": "2",
  "unit": "5"
}
```

**مطلوب للحفظ:**
- `label`, `addressLine`
- `regionId` (من `suggestedRegionId`)
- `latitude` + `longitude` داخل الكويت (lat `28.5–30.2`, lng `46.5–48.5`)

احفظ العنوان قبل شاشة التصنيف — الـ API بياخد التغطية من عنوان العميل في التوكن (مش `addressId` في الـ query).

---

## الجزء 2 — استعراض المطاعم حسب عنوان العميل

التفاصيل الكاملة للـ overlay: [`../mobile-tier-restaurants-browse.md`](../mobile-tier-restaurants-browse.md)

### 2.1 متى يتنادى؟

- **بعد** ما العميل يكون حفظ عنوان توصيل فيه `regionId`.
- على شاشة **اختيار التصنيف** (سيلكت / بلس / ماكس).
- زر «استعراض المطاعم» على كرت التصنيف.

### 2.2 كيف الباك يحدد العنوان؟

من **Bearer token** فقط (عميل مسجّل). **مفيش** `?addressId=` على الـ endpoints دي.

| الأولوية | المصدر |
|----------|--------|
| 1 | `CustomerProfile.ActiveDeliveryRegionId` |
| 2 | عنوان افتراضي / آخر عنوان (`IsDefault` ثم الأحدث) فيه `RegionId` |

يُستخدم `CustomerAddresses.RegionId` (منطقة الكatalog) لمطابقة `RestaurantServiceRegions`.

لو مفيش عنوان → `409 checkout.address_required` → الموبايل يودّي شاشة إضافة العنوان، يحفظ، يرجع لنفس الشاشة ويعيد الطلب.

### 2.3 كروت التصنيف (عداد + دوائر اللوجو)

```http
GET /customer/subscription-flow/programs/{programId}/bundles/{bundleId}/tiers?includeFriday=false
Authorization: Bearer <customer-access-token>
```

- `restaurantCount` و `restaurants[]` = **مطاعم مغطية لعنوان العميل فقط**.
- لو مفيش عنوان → `409 { "error": "checkout.address_required" }`.
- لو مفيش مطاعم مغطية → `409 { "error": "subscription_flow.no_restaurants_for_address" }`.

### 2.4 شبكة استعراض المطاعم (الـ overlay)

```http
GET /customer/subscription-flow/programs/{programId}/bundles/{bundleId}/tiers/{tier}/restaurants
Authorization: Bearer <customer-access-token>
```

`tier` = `Basic` | `Platinum` | `Elite` (من الباك كما هي).

اختياري — حفظ ترتيب الدوائر من كرت التصنيف:

```http
GET .../tiers/Platinum/restaurants?restaurantIds={id1}&restaurantIds={id2}
```

### 2.5 Flutter — Dio

```dart
Future<List<TierOption>> fetchTiers({
  required String programId,
  required String bundleId,
  bool includeFriday = false,
}) async {
  final res = await dio.get(
    '/customer/subscription-flow/programs/$programId/bundles/$bundleId/tiers',
    queryParameters: {
      'includeFriday': includeFriday,
    },
  );
  return (res.data as List)
      .map((e) => TierOption.fromJson(e as Map<String, dynamic>))
      .toList();
}

Future<List<RestaurantBrowseCard>> fetchTierRestaurants({
  required String programId,
  required String bundleId,
  required String tier,
  List<String>? restaurantIds,
}) async {
  final res = await dio.get(
    '/customer/subscription-flow/programs/$programId/bundles/$bundleId/tiers/$tier/restaurants',
    queryParameters: {
      if (restaurantIds != null && restaurantIds.isNotEmpty)
        'restaurantIds': restaurantIds,
    },
  );
  return (res.data as List)
      .map((e) => RestaurantBrowseCard.fromJson(e as Map<String, dynamic>))
      .toList();
}
```

عند `409` + `checkout.address_required`: افتح شاشة حفظ العنوان، بعد النجاح `pop` وارجع وأعد نفس الطلب.
### 2.6 استجابة شبكة المطاعم (مختصر)

```json
[
  {
    "restaurantId": "97db2bbb-b652-4aaa-8584-3414e6966792",
    "nameAr": "دايت هاوس",
    "nameEn": "Diet House",
    "logoUrl": "/api/v1/uploads/content?key=...",
    "rating": 4.8,
    "mealCount": 28,
    "bundleMealCount": 28,
    "tier": "Platinum",
    "tierNameAr": "بلس",
    "tierNameEn": "Platinum",
    "tierLogoUrl": "/api/v1/uploads/content?key=...",
    "classifications": [
      { "logoUrl": "/api/v1/uploads/content?key=...", "tier": "Basic" }
    ]
  }
]
```

قائمة فاضية `[]` = مفيش مطاعم في هذا التصنيف **بتوصل** لمنطقة العنوان (مش خطأ HTTP).

---

## أكواد الأخطاء الشائعة

| HTTP | `error` | معنى | إجراء الموبايل |
|------|---------|------|----------------|
| 409 | `checkout.address_required` | مفيش عنوان / `regionId` | افتح شاشة إضافة عنوان من الخريطة، احفظ، ارجع وأعد الطلب |
| 409 | `subscription_flow.no_restaurants_for_address` | عنوان موجود بس مفيش مطاعم مغطية | رسالة + اقتراح تغيير العنوان أو تصنيف آخر |
| 400 | `validation.invalid_format` | lat/lng خارج الكويت أو ناقص | تأكد من الخريطة داخل الكويت |
| 400 | `maps.api_key_missing` | مفتاح Google server مش مضبوط على الـ host | للأدمن — redeploy + env |
| 404 | `subscription_flow.bundle_not_found` | برنامج/باقة غير نشطة | رجوع للشاشة السابقة |

---

## Checklist للمطور

- [ ] بعد «تأكيد الموقع» → `reverse-geocode` → املأ dropdowns من `suggested*Id`
- [ ] الجادة على `avenue` — **مش** `building`
- [ ] building / floor / unit يدوي من المستخدم
- [ ] احفظ العنوان بـ `regionId` + lat/lng قبل فلو الاشتراك (أو عند `409 checkout.address_required`)
- [ ] `GET .../tiers` و `GET .../tiers/{tier}/restaurants` **بدون** `addressId` — العنوان من التوكن
- [ ] تعامل مع `409 checkout.address_required`: حفظ عنوان → رجوع → إعادة الطلب
- [ ] لا تخلط بين **استعراض المطاعم** و **أهلية المطاعم** (`/restaurants?context`)

---

## ملفات ذات صلة

| ملف | محتوى |
|-----|--------|
| [`customer-address-google-maps.md`](./customer-address-google-maps.md) | عقد الـ geocode + حفظ العنوان بالتفصيل |
| [`../mobile-tier-restaurants-browse.md`](../mobile-tier-restaurants-browse.md) | UI كرت التصنيف + overlay + تفاصيل الكروت |
| [`../mobile-customer-subscription-flow.md`](../mobile-customer-subscription-flow.md) | ترتيب شاشات الاشتراك الكامل |
