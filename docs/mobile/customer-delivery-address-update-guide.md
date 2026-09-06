# الدليل الشامل: معاينة تغطية المطاعم وتأكيد عناوين التوصيل (Flutter Mobile Guide)

دليل مطوري الموبايل (Flutter) لتطبيق آلية **المعاينة قبل الحفظ (Preview Before Save)** و**التأكيد (Confirmation)** عند إضافة عنوان جديد أو تعيين عنوان حالي كافتراضي، مع التحقق من تغطية مطاعم اشتراك العميل الحصري.

---

## 1. نظرة عامة على دورة العمل (Two-Step Workflow)

لضمان عدم حفظ أي عنوان غير مدعوم ولإعطاء العميل رؤية واضحة للمطاعم المتاحة قبل اتخاذ القرار، تم تقسيم العملية إلى خطوتين إجباريتين:

```
[1. اختيار العنوان / الخريطة / القائمة]
                     │
                     ▼
[2. طلب المعاينة (Preview Coverage)] ────► GET/POST /api/v1/customers/me/addresses/preview-coverage
                     │
         ┌───────────┴───────────┐
         ▼                       ▼
  [غير مغطى: CanSetAsDefault=false] [مغطى: CanSetAsDefault=true]
  - يظهر اسم المطعم غير المغطي      - تظهر المطاعم المغطية من باقة الاشتراك
  - زر التأكيد معطل (Disabled)     - زر التأكيد مفعل (Enabled)
  - لا يُحفظ العنوان في الـ DB     │
                                  ▼
                     [3. ضغط زر التأكيد (Confirm)]
                     - إضافة عنوان جديد: POST /customers/me/addresses
                     - تعيين كافتراضي:   POST /customers/me/addresses/{id}/set-default
```

---

## 2. الروابط ونقاط النهاية (Endpoints Summary)

- **Base URL:** `https://maelmate.runasp.net/api/v1`

### أولاً: نقاط نهاية المعاينة (Preview Endpoints - قبل الحفظ / التغيير)
1. **معاينة عنوان جديد أو إحداثيات/منطقة قبل الحفظ:**
   - `POST /customers/me/addresses/preview-coverage`
2. **معاينة عنوان موجود في العناوين المسجلة لجعله افتراضي:**
   - `GET /customers/me/addresses/{addressId}/preview-coverage`

### ثانياً: نقاط نهاية التأكيد والحفظ (Save & Set Default Endpoints)
1. **تأكيد وحفظ عنوان جديد:**
   - `POST /customers/me/addresses`
2. **تأكيد تعيين عنوان موجود كافتراضي:**
   - `POST /customers/me/addresses/{addressId}/set-default`
3. **تعديل بيانات عنوان موجود:**
   - `PUT /customers/me/addresses/{addressId}`
4. **حذف عنوان غير نشط:**
   - `DELETE /customers/me/addresses/{addressId}`

### Headers المطلوبة دائماً:
```http
Authorization: Bearer <customer-access-token>
Content-Type: application/json
Accept: application/json
Accept-Language: ar
```

---

## 3. قواعد العمل الحيوية (Core Business Rules)

### 1) حصر المطاعم بباقة اشتراك العميل (Scoped Subscription Restaurant Pool)
- **إذا كان العميل مشتركاً في باقة نشطة (`hasActiveSubscription: true`):**
  - قائمة `servingRestaurants` المُرجعة **لا تعرض جميع مطاعم الكويت**؛ بل تعرض **حصرياً** المطاعم المدرجة في باقة اشتراكه (`SubscriptionRestaurantSnapshots`) التي تدعم وتخدم منطقة هذا العنوان.
  - إذا كان مطعم تحضير وجبات العميل القادمة لا يوصل لهذا العنوان:
    - يرجع `canSetAsDefault: false`.
    - يرجع `isCurrentSubscriptionRestaurantCovered: false`.
    - يرجع اسم المطعم بالعربية والإنجليزية في `uncoveredRestaurantNameAr` و `uncoveredRestaurantNameEn`.
    - ترجع رسالة شارحة واضحة في `conflictMessage`.
- **إذا لم يكن لدى العميل اشتراك نشط (عميل جديد أو منتهي اشتراكه):**
  - يرجع `canSetAsDefault: true` دائماً.
  - ترجع `servingRestaurants` كافة المطاعم النشطة والمعتمدة التي تخدم المنطقة في النظام ككل لمساعدته في استكشاف الخدمة.

### 2) عدم الحفظ في الـ Database أثناء المعاينة
- استدعاء `POST /customers/me/addresses/preview-coverage` هو استعلام قراءة وتحقق (Read/Validate Only)؛ **لا يُنشئ أي سجل في قاعدة البيانات**.
- لا يتم حفظ العنوان أو تعديله إلا إذا قام المستخدم بالضغط على زر "تأكيد" واستدعى التطبيق `POST /customers/me/addresses` أو `POST /customers/me/addresses/{id}/set-default`.

### 3) التفعيل الفوري للعنوان عند التأكيد
- بمجرد استدعاء نقطة الحفظ كافتراضي أو `set-default`، يصبح العنوان نشطاً للتوصيل **فوراً** (`isDeliveryActive: true`) ويتم تحديث الأيام القادمة في الاشتراك التي لم تُطبع فواتيرها بعد.

---

## 4. تفاصيل عقود الـ API (Contracts & Payloads)

### أ) معاينة عنوان جديد قبل الحفظ (POST /customers/me/addresses/preview-coverage)

#### Request Body:
يمكن إرسال المعرف `regionId` أو الإحداثيات أو اسم المحافظة والمنطقة:
```json
{
  "regionId": "d4a9acc2-afb8-49d8-b440-f93b8622239c",
  "blockRegionId": "817ce4d2-7941-4c58-a0a9-8aa72bb93610",
  "latitude": 29.3412132,
  "longitude": 48.082421,
  "governorate": "حولي",
  "area": "السالمية",
  "block": "قطعة 5",
  "isDefault": true
}
```

> **ملاحظة الإدخال اليدوي:** إذا لم يختر العميل الموقع من الخريطة، يمكن إرسال `latitude` و `longitude` كـ `null`، وسيقوم النظام تلقائياً باعتماد إحداثيات مركز المنطقة (`CentroidLat/Lng`).

#### Response Body (200 OK - عند وجود مطعم لا يغطي المنطقة):
```json
{
  "canSetAsDefault": false,
  "isCurrentSubscriptionRestaurantCovered": false,
  "uncoveredRestaurantNameAr": "دايت ستور العاصمة",
  "uncoveredRestaurantNameEn": "Diet Store Capital",
  "conflictMessage": "مطعم اشتراكك الحالي (دايت ستور العاصمة) لا يدعم التوصيل إلى منطقة السالمية.",
  "resolvedRegionId": "d4a9acc2-afb8-49d8-b440-f93b8622239c",
  "resolvedRegionNameAr": "السالمية",
  "resolvedRegionNameEn": "Salmiya",
  "governorate": "حولي",
  "area": "السالمية",
  "block": "قطعة 5",
  "latitude": 29.3375,
  "longitude": 48.0753,
  "hasActiveSubscription": true,
  "restaurantCoverageNotice": {
    "ar": "تنبيه: عند اعتماد هذا العنوان للتوصيل، ستقتصر المطاعم المتاحة على المطاعم التي تدعم التوصيل لمنطقتك.",
    "en": "Notice: When this address is used for delivery, only restaurants serving this region will be available."
  },
  "servingRestaurants": [
    {
      "id": "3cc74026-62b1-419b-a010-b99ca8627b0b",
      "nameAr": "هيلثي كورنر (من باقتك)",
      "nameEn": "Healthy Corner",
      "logoUrl": "/api/v1/uploads/content?key=restaurants/logos/healthycorner.png",
      "rating": 4.7
    }
  ]
}
```

#### Response Body (200 OK - المنطقة مغطاة بالكامل):
```json
{
  "canSetAsDefault": true,
  "isCurrentSubscriptionRestaurantCovered": true,
  "uncoveredRestaurantNameAr": null,
  "uncoveredRestaurantNameEn": null,
  "conflictMessage": null,
  "resolvedRegionId": "d4a9acc2-afb8-49d8-b440-f93b8622239c",
  "resolvedRegionNameAr": "السالمية",
  "resolvedRegionNameEn": "Salmiya",
  "governorate": "حولي",
  "area": "السالمية",
  "block": "قطعة 5",
  "latitude": 29.3412132,
  "longitude": 48.082421,
  "hasActiveSubscription": true,
  "restaurantCoverageNotice": {
    "ar": "تنبيه: عند اعتماد هذا العنوان للتوصيل، ستقتصر المطاعم المتاحة على المطاعم التي تدعم التوصيل لمنطقتك.",
    "en": "Notice: When this address is used for delivery, only restaurants serving this region will be available."
  },
  "servingRestaurants": [
    {
      "id": "97db2bbb-b652-4aaa-8584-3414e6966792",
      "nameAr": "دايت ستيشن",
      "nameEn": "Diet Station",
      "logoUrl": "/api/v1/uploads/content?key=restaurants/logos/dietstation.png",
      "rating": 4.9
    }
  ]
}
```

---

### ب) معاينة عنوان مسجل قبل جعله افتراضي (GET /customers/me/addresses/{id}/preview-coverage)

لا يتطلب أي Request Body؛ فقط أرسل `id` العنوان الحالي في الرابط.
الـ Response يرجع نفس نموذج `AddressCoveragePreviewDto` أعلاه.

---

### ج) تأكيد تعيين عنوان مسجل كافتراضي (POST /customers/me/addresses/{id}/set-default)

عندما يضغط المستخدم "تأكيد تعيين كافتراضي":
- **الرابط:** `POST /customers/me/addresses/{id}/set-default`
- **Request Body:** فارغ `{}`
- **Response (200 OK):** كائن العنوان المُحدّث:
```json
{
  "id": "d7494337-f004-48fd-9755-e1764681bce5",
  "label": "المنزل",
  "addressLine": "السالمية، قطعة 5، شارع 2، مبنى 17",
  "regionId": "d4a9acc2-afb8-49d8-b440-f93b8622239c",
  "latitude": 29.3412132,
  "longitude": 48.082421,
  "isDefault": true,
  "isDeliveryActive": true,
  "servingRestaurants": [...]
}
```
- **إذا حاول الالتفاف دون فحص التغطية:** يرجع `409 Conflict`:
```json
{
  "type": "Conflict",
  "code": "address.restaurant_does_not_service_region",
  "message": "Cannot change address because your subscribed restaurant (دايت ستور العاصمة) does not deliver to this new address."
}
```

---

### د) تأكيد وحفظ عنوان جديد (POST /customers/me/addresses)

يُستدعى فقط بعد إتمام خطوة المعاينة وموافقة العميل:
```json
{
  "label": "العمل",
  "addressLine": "السالمية، قطعة 5، شارع 2، مبنى 17، دور 2",
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
  "building": "17",
  "floor": "2",
  "unit": "4"
}
```

---

## 5. أكواد ونماذج Flutter المقترحة (Dart Models & Service)

### أ) نماذج البيانات (Dart Models)

```dart
class AddressCoveragePreviewResponse {
  final bool canSetAsDefault;
  final bool isCurrentSubscriptionRestaurantCovered;
  final String? uncoveredRestaurantNameAr;
  final String? uncoveredRestaurantNameEn;
  final String? conflictMessage;
  final String? resolvedRegionId;
  final String? resolvedRegionNameAr;
  final String? resolvedRegionNameEn;
  final String? governorate;
  final String? area;
  final String? block;
  final double? latitude;
  final double? longitude;
  final bool hasActiveSubscription;
  final LocalizedNotice? restaurantCoverageNotice;
  final List<ServingRestaurant> servingRestaurants;

  AddressCoveragePreviewResponse({
    required this.canSetAsDefault,
    required this.isCurrentSubscriptionRestaurantCovered,
    this.uncoveredRestaurantNameAr,
    this.uncoveredRestaurantNameEn,
    this.conflictMessage,
    this.resolvedRegionId,
    this.resolvedRegionNameAr,
    this.resolvedRegionNameEn,
    this.governorate,
    this.area,
    this.block,
    this.latitude,
    this.longitude,
    required this.hasActiveSubscription,
    this.restaurantCoverageNotice,
    required this.servingRestaurants,
  });

  factory AddressCoveragePreviewResponse.fromJson(Map<String, dynamic> json) {
    return AddressCoveragePreviewResponse(
      canSetAsDefault: json['canSetAsDefault'] ?? true,
      isCurrentSubscriptionRestaurantCovered:
          json['isCurrentSubscriptionRestaurantCovered'] ?? true,
      uncoveredRestaurantNameAr: json['uncoveredRestaurantNameAr'],
      uncoveredRestaurantNameEn: json['uncoveredRestaurantNameEn'],
      conflictMessage: json['conflictMessage'],
      resolvedRegionId: json['resolvedRegionId'],
      resolvedRegionNameAr: json['resolvedRegionNameAr'],
      resolvedRegionNameEn: json['resolvedRegionNameEn'],
      governorate: json['governorate'],
      area: json['area'],
      block: json['block'],
      latitude: json['latitude'] != null ? (json['latitude'] as num).toDouble() : null,
      longitude: json['longitude'] != null ? (json['longitude'] as num).toDouble() : null,
      hasActiveSubscription: json['hasActiveSubscription'] ?? false,
      restaurantCoverageNotice: json['restaurantCoverageNotice'] != null
          ? LocalizedNotice.fromJson(json['restaurantCoverageNotice'])
          : null,
      servingRestaurants: (json['servingRestaurants'] as List? ?? [])
          .map((e) => ServingRestaurant.fromJson(e))
          .toList(),
    );
  }
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
      nameAr: json['nameAr'] ?? '',
      nameEn: json['nameEn'] ?? '',
      logoUrl: json['logoUrl'],
      rating: json['rating'] != null ? (json['rating'] as num).toDouble() : null,
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
```

### ب) خدمة العناوين (CustomerAddressService)

```dart
import 'package:dio/dio.dart';

class CustomerAddressService {
  final Dio dio;

  CustomerAddressService(this.dio);

  // 1. معاينة تغطية عنوان جديد قبل الحفظ
  Future<AddressCoveragePreviewResponse> previewNewAddressCoverage({
    String? regionId,
    String? blockRegionId,
    double? latitude,
    double? longitude,
    String? governorate,
    String? area,
    String? block,
    bool isDefault = true,
  }) async {
    final response = await dio.post(
      '/customers/me/addresses/preview-coverage',
      data: {
        'regionId': regionId,
        'blockRegionId': blockRegionId,
        'latitude': latitude,
        'longitude': longitude,
        'governorate': governorate,
        'area': area,
        'block': block,
        'isDefault': isDefault,
      },
    );
    return AddressCoveragePreviewResponse.fromJson(response.data);
  }

  // 2. معاينة تغطية عنوان موجود قبل تعيينه كافتراضي
  Future<AddressCoveragePreviewResponse> previewExistingAddressCoverage(
    String addressId,
  ) async {
    final response = await dio.get(
      '/customers/me/addresses/$addressId/preview-coverage',
    );
    return AddressCoveragePreviewResponse.fromJson(response.data);
  }

  // 3. تأكيد تعيين عنوان موجود كافتراضي بعد المعاينة
  Future<void> confirmSetDefault(String addressId) async {
    await dio.post('/customers/me/addresses/$addressId/set-default');
  }

  // 4. تأكيد وحفظ عنوان جديد بعد المعاينة
  Future<void> confirmSaveNewAddress(Map<String, dynamic> addressPayload) async {
    await dio.post('/customers/me/addresses', data: addressPayload);
  }
}
```

---

## 6. خطوات الواجهة المقترحة للموبايل (UI Flow & BottomSheet)

1. **شاشة إدخال العنوان:**
   - يحدد المستخدم الموقع على الخريطة أو يختار من القوائم (محافظة / منطقة / قطعة).
   - يضغط على زر: **"متابعة وتحقق من التغطية"** (بدلاً من "حفظ مباشر").
2. **استدعاء المعاينة:**
   - يظهر مؤشر تحميل (Loading Spinner).
   - يتم إرسال طلب `preview-coverage`.
3. **فتح شاشة التأكيد السفلية (Coverage Preview Modal BottomSheet):**
   - **إذا كان `canSetAsDefault == true`:**
     - تظهر رسالة نجاح خضراء: *"منطقتك مغطاة بمطاعم اشتراكك"*.
     - عرض كروت المطاعم التي تخدم المنطقة من باقته (`servingRestaurants`).
     - زر **"تأكيد وحفظ العنوان"** مفعل بلون مميز. عند الضغط عليه، يتم استدعاء `confirmSaveNewAddress` أو `confirmSetDefault`.
   - **إذا كان `canSetAsDefault == false`:**
     - تظهر لافتة تحذيرية حمراء:
       - *"مطعمك الحالي (${preview.uncoveredRestaurantNameAr}) لا يدعم التوصيل إلى هذه المنطقة."*
     - زر **"تأكيد وتعيين كافتراضي"** يكون **معطلاً (Disabled)** مع نص توضيحي.
     - زر ثانوي: *"اختيار عنوان آخر"* لإغلاق الـ BottomSheet والرجوع للخريطة.
