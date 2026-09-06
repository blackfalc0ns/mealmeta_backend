# الدليل الشامل: تعديل عنوان التوصيل وتغطية المطاعم (Flutter Guide)

دليل مطوري الموبايل (Flutter) للتعامل مع إضافة وتعديل عناوين التوصيل، التفعيل الفوري للعنوان، وتنبيه تغطية المطاعم وقاعدة طبع الفاتورة.

---

## 1. الروابط ونقاط النهاية (Endpoints)

- **Base URL:** `https://maelmate.runasp.net/api/v1`
- **إضافة عنوان جديد:** `POST /customers/me/addresses`
- **تعديل عنوان حالي:** `PUT /customers/me/addresses/{addressId}`
- **حذف عنوان:** `DELETE /customers/me/addresses/{addressId}`

### Headers المطلوبة:
```http
Authorization: Bearer <customer-access-token>
Content-Type: application/json
Accept: application/json
Accept-Language: ar
```

---

## 2. القواعد الأساسية للنظام (Business Rules)

### 1) التفعيل الفوري للعنوان (إلغاء مهلة الـ 72 ساعة)
- **سابقاً:** كان يتم جدولة التوصيل على العنوان الجديد بعد 72 ساعة.
- **الآن:** بمجرد حفظ أو تعديل العنوان كافتراضي (`isDefault: true`) أو تحديث العنوان النشط، يتفعل كعنوان توصيل **فوراً** (`isDeliveryActive: true`) بدون أي تأخير زمني.

### 2) التحقق من تغطية مطعم الاشتراك الحالي (Restaurant Coverage Guard)
- إذا كان العميل يمتلك **اشتراكاً نشطاً أو مجدولاً (Scheduled / Active / Paid)**:
  - يقوم الـ API بالتحقق من أن المطعم الذي يجهز الوجبات القادمة للمشترك **يدعم التوصيل إلى منطقة العنوان الجديد (`RegionId`)**.
  - إذا كان المطعم **لا يخدم** المنطقة الجديدة:
    - يرفض الـ API حفظ التعديل ويرجع خطأ `409 Conflict`.
    - كود الخطأ: `address.restaurant_does_not_service_region`.
    - **سلوك النظام:** **لا يتم حفظ العنوان في قاعدة البيانات إطلاقاً، ولا يتم تعيينه كافتراضي.**
    - **إجراء الموبايل:** إظهار Dialog تنبيهي للمستخدم يوضح أن مطعمه الحالي لا يوصل لهذه المنطقة، مع إبقاء العنوان القديم.

### 3) قاعدة مرحلة طبع الفاتورة (Invoice Printing Cutoff)
- عندما يغير العميل عنوان التوصيل ولديه أيام اشتراك قادمة:
  - **الأيام التي لم تُطبع فواتيرها بعد:** يتم تحديث عنوان توصيلها ومنطقتها فوراً إلى العنوان الجديد.
  - **الأيام التي طُبعت فواتيرها بالفعل (`MealInvoice`):** تبقى على العنوان القديم المطبوع لتجنب أي تضارب تشغيلي في المطبخ وشركات التوصيل.

### 4) تغير مطاعم التقويم بحسب العنوان والتغطية (Calendar Restaurants By Region)
- عند فتح يوم في التقويم (`GET /api/v1/customer/subscriptions/{id}/calendar/days/{date}/restaurants`):
  - يقوم النظام بفلترة المطاعم تلقائياً بحسب **منطقة التوصيل المحددة لليوم** (`day.DeliveryRegionId`).
  - المطاعم التي **لا تخدم** عنوان هذا اليوم **تختفي تماماً من قائمة التقويم** لهذا اليوم.
  - تظهر فقط المطاعم التي **تغطي عنوان اليوم فعلياً**.
  - تظهر نافذة التوصيل (`deliveryWindowStart` و `deliveryWindowEnd`) الخاصة بمنطقة هذا اليوم حصراً.

### 5) تنبيه تغير المطاعم وقائمة المطاعم الخادمة (Coverage Notice & Serving Restaurants)
- يرجع حقل `restaurantCoverageNotice` فقط عندما يتم تغيير أو اعتماد العنوان كعنوان توصيل افتراضي/نشط للمستخدم:
  1. `restaurantCoverageNotice`: رسالة مترجمة توضح للمستخدم أن تغيير عنوان التوصيل سيؤدي لاختلاف المطاعم المتاحة لاشتراكه في التقويم.
  2. `servingRestaurants`: قائمة كاملة بالمطاعم النشطة والمعتمدة التي تخدم المنطقة الجديدة فعلياً (`Id`, `NameAr`, `NameEn`, `LogoUrl`, `Rating`).

---

## 3. عقود الطلب والاستجابة (Request & Response)

### نموذج إرسال الطلب (Request Body - POST / PUT)

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
  "unit": "5",
  "notes": "بجوار المسجد"
}
```

### نموذج الاستجابة الناجحة (Response 200 OK)

```json
{
  "id": "d7494337-f004-48fd-9755-e1764681bce5",
  "label": "المنزل",
  "addressLine": "17-27 شارع 2 جادة 2، السالمية، الكويت",
  "regionId": "d4a9acc2-afb8-49d8-b440-f93b8622239c",
  "latitude": 29.3412132,
  "longitude": 48.082421,
  "notes": "بجوار المسجد",
  "isDefault": true,
  "notifyOnDelivery": true,
  "governorate": "حولي",
  "area": "السالمية",
  "block": "قطعة 5",
  "street": "شارع 2",
  "avenue": "جادة 2",
  "building": "17",
  "floor": "2",
  "unit": "5",
  "blockRegionId": "817ce4d2-7941-4c58-a0a9-8aa72bb93610",
  "isDeliveryActive": true,
  "deliveryEffectiveFromUtc": null,
  "deliveryChangeNotice": null,
  "restaurantCoverageNotice": {
    "ar": "تنبيه: عند اعتماد هذا العنوان للتوصيل، قد تتغير قائمة المطاعم المتاحة لاشتراكك؛ ستظهر مطاعم تغطي منطقتك الجديدة وتختفي مطاعم لا تدعم التوصيل إليها.",
    "en": "Notice: When this address is used for delivery, available restaurants may change; restaurants serving your new area will appear, and restaurants not covering it will no longer be available."
  },
  "servingRestaurants": [
    {
      "id": "97db2bbb-b652-4aaa-8584-3414e6966792",
      "nameAr": "دايت هاوس",
      "nameEn": "Diet House",
      "logoUrl": "/api/v1/uploads/content?key=restaurants/logos/diethouse.png",
      "rating": 4.8
    },
    {
      "id": "3cc74026-62b1-419b-a010-b99ca8627b0b",
      "nameAr": "هيلثي كورنر",
      "nameEn": "Healthy Corner",
      "logoUrl": "/api/v1/uploads/content?key=restaurants/logos/healthycorner.png",
      "rating": 4.6
    }
  ]
}
```

---

## 4. التعامل مع الأخطاء (Error Handling)

| HTTP Status | كود الخطأ (`code` / `error`) | السبب | الإجراء المطلوب في الموبايل |
|-------------|----------------------------|-------|-----------------------------|
| **409 Conflict** | `address.restaurant_does_not_service_region` | المطعم الحالي في اشتراك العميل لا يدعم التوصيل لمنطقة العنوان الجديد | إظهار تنبيه يوضح للعميل أن المطعم لا يخدم هذه المنطقة، مع إبقاء العنوان القديم |
| **409 Conflict** | `address.active_delivery_cannot_delete` | محاولة حذف العنوان الافتراضي/النشط حالياً | توجيه العميل لتعيين عنوان آخر كافتراضي أولاً ثم حذف هذا العنوان |
| **400 Bad Request** | `validation.invalid_format` | الإحداثيات (Lat/Lng) خارج حدود دولة الكويت | التأكد من تحديد الموقع على الخريطة داخل الكويت |
| **400 Bad Request** | `address.area_required` | لم يتم تحديد منطقة الكتالوج (`regionId`) | اختيار المنطقة من الـ Dropdown أو الخريطة |

### شكل استجابة خطأ التغطية (409):
```json
{
  "type": "Conflict",
  "code": "address.restaurant_does_not_service_region",
  "message": "Cannot change address because your subscribed restaurant does not deliver to this new address."
}
```

---

## 5. كود Flutter المقترح (Dart & Dio)

```dart
import 'package:dio/dio.dart';

class CustomerAddressService {
  final Dio dio;

  CustomerAddressService(this.dio);

  Future<CustomerAddressResponse> updateAddress({
    required String addressId,
    required Map<String, dynamic> body,
  }) async {
    try {
      final response = await dio.put(
        '/customers/me/addresses/$addressId',
        data: body,
      );
      
      final data = CustomerAddressResponse.fromJson(response.data);
      return data;
    } on DioException catch (e) {
      if (e.response?.statusCode == 409) {
        final errorCode = e.response?.data['code'] ?? e.response?.data['error'];
        if (errorCode == 'address.restaurant_does_not_service_region') {
          throw AddressRestaurantCoverageException(
            message: 'المطعم المشترك معه حالياً لا يخدم هذه المنطقة. يرجى اختيار عنوان آخر.',
          );
        }
      }
      rethrow;
    }
  }
}

// نموذج الموديل في فلاتر
class CustomerAddressResponse {
  final String id;
  final String label;
  final String addressLine;
  final bool isDefault;
  final bool isDeliveryActive;
  final LocalizedNotice? restaurantCoverageNotice;
  final List<ServingRestaurant> servingRestaurants;

  CustomerAddressResponse({
    required this.id,
    required this.label,
    required this.addressLine,
    required this.isDefault,
    required this.isDeliveryActive,
    this.restaurantCoverageNotice,
    required this.servingRestaurants,
  });

  factory CustomerAddressResponse.fromJson(Map<String, dynamic> json) {
    return CustomerAddressResponse(
      id: json['id'],
      label: json['label'],
      addressLine: json['addressLine'],
      isDefault: json['isDefault'] ?? false,
      isDeliveryActive: json['isDeliveryActive'] ?? false,
      restaurantCoverageNotice: json['restaurantCoverageNotice'] != null
          ? LocalizedNotice.fromJson(json['restaurantCoverageNotice'])
          : null,
      servingRestaurants: (json['servingRestaurants'] as List? ?? [])
          .map((e) => ServingRestaurant.fromJson(e))
          .toList(),
    );
  }
}

class LocalizedNotice {
  final String ar;
  final String en;

  LocalizedNotice({required this.ar, required this.en});

  factory LocalizedNotice.fromJson(Map<String, dynamic> json) =>
      LocalizedNotice(ar: json['ar'] ?? '', en: json['en'] ?? '');
}

class ServingRestaurant {
  final String id;
  final String nameAr;
  final String nameEn;
  final String? logoUrl;
  final double? rating;

  ServingRestaurant({
    required this.id,
    required this.nameAr,
    required this.nameEn,
    this.logoUrl,
    this.rating,
  });

  factory ServingRestaurant.fromJson(Map<String, dynamic> json) {
    return ServingRestaurant(
      id: json['id'],
      nameAr: json['nameAr'],
      nameEn: json['nameEn'],
      logoUrl: json['logoUrl'],
      rating: json['rating'] != null ? (json['rating'] as num).toDouble() : null,
    );
  }
}

class AddressRestaurantCoverageException implements Exception {
  final String message;
  AddressRestaurantCoverageException({required this.message});
}
```

---

## 6. توصيات واجهة المستخدم (UI/UX Best Practices)

1. **عند نجاح التعديل:**
   - افتح **Modal BottomSheet** أو **Dialog**:
     - ضع نص التنبيه: `restaurantCoverageNotice.ar`.
     - اعرض قائمة أفقية أو شبكة بكروت المطاعم التي تخدم المنطقة الجديدة (`servingRestaurants`).
     - إذا كانت المصفوفة فارغة `[]`، اعرض تنبيهاً باللون البرتقالي: *"لا توجد مطاعم تخدم هذه المنطقة حالياً"*.
2. **عند حدوث خطأ التغطية (409):**
   - اعرض رسالة تنبيه واضحة تفيد بأن المطعم الحالي للمشترك لا يوصل لهذا العنوان الجديد، مع توفير زر لإعادة فتح الخريطة واختيار منطقة يدعمها المطعم.
