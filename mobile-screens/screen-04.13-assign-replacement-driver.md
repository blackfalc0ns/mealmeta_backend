# Screen 04.13: تعيين سائق بديل للمهمة المتعثرة (Screen 10.13 - Assign Replacement Driver)

## 🎯 الغرض من الشاشة ونظرة عامة (Overview & Purpose)
تُمثّل هذه الشاشة واجهة التدخل والإنقاذ التشغيلي الطارئ (`Emergency Replacement Driver Assignment`) المخصصة لمسؤول التوصيل (`DeliveryManager`). تُستخدم عند تعثر السائق الحالي (تعطل المركبة، حادث طارئ، أو عدم استجابة).

تُمكّن الشاشة مسؤول التوصيل من:
1. **معاينة ملخص البلاغ التشغيلي والمشكلة**: استعراض معرف المشكلة، كود البوكس، المنطقة، الأولوية، وعدد البوكسات المتأثرة مع الطابع الزمني المحدث.
2. **الاطلاع على كارت السائق الحالي المتعذر**: توثيق هويته مع حالته البرمجية الصريحة (`status: "Unavailable"`) وسبب التعذر المسجل (`unavailabilityReason`).
3. **استعراض قائمة السائقين البدلاء المرشحين المحسوبة والمفلترة بالكامل من السيرفر**:
   - السيرفر هو المسؤول الحصري عن حساب المسافة (`distanceKm`) وزمن الوصول التقديري (`estimatedArrivalMinutes`) استناداً لآخر موقع GPS معتمد (`lastLocationUpdatedAtUtc`) بدون مطالبة تطبيق الموبايل بحساب Haversine.
   - السائقون في نفس المنطقة التشغيلية (`isSameArea: true`) يأتون في الصدارة.
   - استبعاد السائق الحالي تلقائياً.
   - استبعاد أي سائق غير متاح (`isAvailable == false`) أو محذوف أو خارج النطاق التشغيلي.
   - دعم الترقيم والتصفح (`Pagination`) مع معالجة حالة القائمة الفارغة وإرجاع `HTTP 200 OK` (وليس 404).
4. **تنفيذ التعيين الذري الآمن (Atomic Transaction & Anti-Conflict)**:
   - منع تضارب تعيين سائق واحد بين أكثر من مسؤول توصيل في نفس اللحظة.
   - التحقق اللحظي داخل نفس الـ Transaction من استمرار توفر السائق وعدم حل المشكلة مسبقاً.
   - نقل المهمة وتحديث حالات الرحلة والبوكسات والسائقين فورياً وتسجيل سجل تدقيق (`Audit Log`) وبث إشعارات SignalR للسائق الجديد واللوحة.

---

## 🎨 التحليل البصري والتخطيطي (UI/UX Breakdown & Components)

```
┌────────────────────────────────────────────────────────┐
│  [<]                  تعيين سائق بديل              [ ] │
├────────────────────────────────────────────────────────┤
│  ⚠️ ملخص المشكلة                                      │
│  ┌───────────────────────────────────────────────────┐ │
│  │  #BX-10256  •  السالمية، ق 4        [مشكلة توصيل] │ │
│  │  العميل لم يستجب للاتصال بعد 3 محاولات             │ │
│  │  🕒 منذ 15 دقيقة  •  📦 1 بوكس متأثر  •  🔴 عالية   │ │
│  └───────────────────────────────────────────────────┘ │
├────────────────────────────────────────────────────────┤
│  👤 السائق الحالي المتعذر                              │
│  ┌───────────────────────────────────────────────────┐ │
│  │  [👤] أحمد السعيد (DR-1025)       [ 🔴 غير متاح ] │ │
│  │      السبب: تعطل المركبة / بلاغ طارئ              │ │
│  └───────────────────────────────────────────────────┘ │
├────────────────────────────────────────────────────────┤
│  📋 السائقون المقترحون (مرتبين حسب الأقرب والأفضل)    │
│                                                        │
│  ┌───────────────────────────────────────────────────┐ │
│  │  (•) #1 [👤] فهد الدوسري (DR-1012)   [🟢 متاح الآن]│ │
│  │      ⭐ 4.9   •   📦 0 بوكسات حالياً              │ │
│  │      📍 نفس المنطقة (السالمية)                    │ │
│  │      🚗 يبعد 1.2 كم • 5 دقائق                     │ │
│  │      🚙 هيونداي النترا • أبيض                     │ │
│  └───────────────────────────────────────────────────┘ │
│  ┌───────────────────────────────────────────────────┐ │
│  │  ( ) #2 [👤] جاسم المطيري (DR-1038)  [🟢 متاح الآن]│ │
│  │      ⭐ 4.8   •   📦 1 بوكس حالياً                │ │
│  │      📍 حولي (منطقة مجاورة)                       │ │
│  │      🚗 يبعد 3.8 كم • 11 دقيقة                    │ │
│  │      🚙 كيا سيراتو • رمادي                        │ │
│  └───────────────────────────────────────────────────┘ │
├────────────────────────────────────────────────────────┤
│         [ 🔄 تأكيد تعيين السائق البديل ]              │
└────────────────────────────────────────────────────────┘
```

---

## 🔌 عقود تكامل الخادم (API Endpoints Specification)

### 1. استعلام قائمة السائقين البدلاء المرشحين
- **الرابط (Route)**: `GET /api/v1/dispatcher/support/issues/{issueId}/candidates`
- **الصلاحية (Authorization)**: `Bearer Token` (`DeliveryManager`)
- **Query Parameters**:
  - `pageNumber` (int, default: 1): رقم الصفحة
  - `pageSize` (int, default: 20, max: 50): عدد العناصر في الصفحة

#### مثال الطلب:
```http
GET /api/v1/dispatcher/support/issues/3fa85f64-5717-4562-b3fc-2c963f66afa6/candidates?pageNumber=1&pageSize=20
Authorization: Bearer <JWT_TOKEN>
Accept-Language: ar
```

#### نموذج الاستجابة عند وجود مرشحين (`HTTP 200 OK`):
```json
{
  "summary": {
    "issueId": "3fa85f64-5717-4562-b3fc-2c963f66afa6",
    "title": "العميل لم يستجب للاتصال بعد 3 محاولات",
    "category": "DeliveryIssue",
    "categoryLabel": "مشكلة توصيل",
    "categoryColor": "#EF4444",
    "createdAtUtc": "2026-09-22T08:20:00Z",
    "reportedTimeText": "منذ 15 دقيقة",
    "boxCode": "BX-10256",
    "area": "السالمية، ق 4",
    "affectedBoxesCount": 1,
    "affectedBoxesText": "1 بوكس متأثر",
    "priority": "High",
    "priorityText": "أولوية عالية",
    "priorityColor": "#EF4444",
    "status": "Open"
  },
  "currentDriver": {
    "driverId": "1a2b3c4d-5717-4562-b3fc-2c963f66afa6",
    "driverCode": "DR-1025",
    "fullName": "أحمد السعيد",
    "avatarUrl": "https://cdn.mealmate.app/avatars/dr-1025.jpg",
    "status": "Unavailable",
    "statusText": "غير متاح",
    "statusColor": "#EF4444",
    "unavailabilityReason": "VehicleBreakdown",
    "unavailabilityReasonText": "تعطل المركبة / بلاغ طارئ"
  },
  "candidates": [
    {
      "driverId": "2b3c4d5e-5717-4562-b3fc-2c963f66afa6",
      "driverCode": "DR-1012",
      "fullName": "فهد الدوسري",
      "avatarUrl": "https://cdn.mealmate.app/avatars/dr-1012.jpg",
      "status": "Available",
      "statusText": "متاح الآن",
      "statusColor": "#10B981",
      "rating": 4.9,
      "activeOrdersCount": 0,
      "area": "السالمية",
      "isSameArea": true,
      "distanceKm": 1.2,
      "distanceText": "1.2 كم",
      "estimatedArrivalMinutes": 5,
      "estimatedArrivalText": "5 دقائق",
      "vehicleInfo": "هيونداي النترا • أبيض",
      "lastLocationUpdatedAtUtc": "2026-09-22T08:45:20Z",
      "recommendationRank": 1
    },
    {
      "driverId": "3c4d5e6f-5717-4562-b3fc-2c963f66afa6",
      "driverCode": "DR-1038",
      "fullName": "جاسم المطيري",
      "avatarUrl": "https://cdn.mealmate.app/avatars/dr-1038.jpg",
      "status": "Available",
      "statusText": "متاح الآن",
      "statusColor": "#10B981",
      "rating": 4.8,
      "activeOrdersCount": 1,
      "area": "حولي",
      "isSameArea": false,
      "distanceKm": 3.8,
      "distanceText": "3.8 كم",
      "estimatedArrivalMinutes": 11,
      "estimatedArrivalText": "11 دقيقة",
      "vehicleInfo": "كيا سيراتو • رمادي",
      "lastLocationUpdatedAtUtc": "2026-09-22T08:44:10Z",
      "recommendationRank": 2
    }
  ],
  "pagination": {
    "pageNumber": 1,
    "pageSize": 20,
    "totalItems": 2,
    "totalPages": 1,
    "hasPreviousPage": false,
    "hasNextPage": false
  }
}
```

#### نموذج الاستجابة عند عدم وجود سائقين مرشحين (`HTTP 200 OK` وليس 404):
```json
{
  "summary": { ... },
  "currentDriver": { ... },
  "candidates": [],
  "pagination": {
    "pageNumber": 1,
    "pageSize": 20,
    "totalItems": 0,
    "totalPages": 0,
    "hasPreviousPage": false,
    "hasNextPage": false
  }
}
```

#### قواعد الترتيب والاستبعاد في السيرفر (Server-Side Logic):
- **معايير الترتيب التلقائي**:
  1. `isSameArea DESC`: السائقون في نفس المنطقة الجغرافية للبلاغ أولاً.
  2. `availability`: السائقون المتاحون حالياً (`IsAvailable == true`).
  3. `distanceKm ASC`: الأقرب لموقع البوكس/المهمة.
  4. `activeOrdersCount ASC`: الأقل حملاً للمهام الحالية.
  5. `rating DESC`: الأعلى تقييماً.
  6. `driverId ASC`: لضمان الترتيب القطعي (Deterministic).
- **الاستبعادات الإلزامية**:
  - استبعاد السائق الحالي المتعثر.
  - استبعاد السائقين غير المتاحين (`IsAvailable == false`).
  - استبعاد السائقين المحذوفين أو المعطلين أو غير المرخصين في المطعم.
  - استخدام إحداثيات GPS حديثة وحساب المسافة والزمن في السيرفر.
  - تضمين `recommendationRank` تصاعدياً لكل مرشح.

---

### 2. تنفيذ التعيين وإسناد المهمة (Atomic Reassignment)
- **الرابط (Route)**: `POST /api/v1/dispatcher/support/issues/{issueId}/reassign`
- **الصلاحية (Authorization)**: `Bearer Token` (`DeliveryManager`)

#### جسم الطلب (Request Body):
```json
{
  "replacementDriverId": "2b3c4d5e-5717-4562-b3fc-2c963f66afa6",
  "notes": "تعطل مركبة السائق الحالي وعدم قدرته على استكمال الرحلة"
}
```

#### قواعد التحقق وضمان المعاملة الذرية (Validation & Concurrency Guard):
- `replacementDriverId`: إجباري وUUID صالح، ويُمنع أن يساوي السائق الحالي المتعثر (`CANNOT_REASSIGN_TO_SAME_DRIVER`).
- `notes`: اختيارية، أقصى حد 500 حرف بعد اقتطاع الفراغات (`Trim`).
- **منع تعارض التعيين المتزامن (Concurrency Transaction)**:
  1. التحقق من أن المشكلة ما زالت قابلة لإعادة التعيين ولم يتم إغلاقها أو حلها مسبقاً.
  2. فحص حالة السائق البديل في قاعدة البيانات للتأكد من استمرار توفره لحظة الضغط على زر التعيين وليس فقط وقت تحميل القائمة.
  3. إلغاء إسناد السائق السابق ووسم السائق الحالي كـ `غير متاح` لمنع تعيينه لمهام أخرى حتى حل مشكلته.
  4. إنشاء إسناد جديد نشط (`DriverAssignment`) للسائق البديل وتحديث حالة المشكلة إلى `InProgress`.
  5. تسجيل حركة تدقيق رقابية كاملة (`Audit Log`).
  6. بث إشعارات SignalR عبر الـ Hub لتحديث لوحة المراقبة وإشعار السائق الجديد.
  7. تأكيد المعاملة في عملية حفظ واحدة (`Single Transaction Commit`).

#### استجابة النجاح المتوقعة (`HTTP 200 OK`):
```json
{
  "success": true,
  "issueId": "3fa85f64-5717-4562-b3fc-2c963f66afa6",
  "previousDriverId": "1a2b3c4d-5717-4562-b3fc-2c963f66afa6",
  "issueStatus": "InProgress",
  "replacementDriver": {
    "driverId": "2b3c4d5e-5717-4562-b3fc-2c963f66afa6",
    "driverCode": "DR-1012",
    "fullName": "فهد الدوسري",
    "avatarUrl": "https://cdn.mealmate.app/avatars/dr-1012.jpg",
    "status": "OnTask",
    "statusText": "متصل على المهمة",
    "statusColor": "#10B981",
    "phoneNumber": "+965 50 123 4567",
    "vehicleInfo": "هيونداي النترا • أبيض",
    "vehicle": {
      "type": "سيارة",
      "model": "هيونداي النترا",
      "color": "أبيض",
      "plateNumber": "12-34567"
    }
  },
  "reassignedAtUtc": "2026-09-22T08:46:00Z",
  "message": "تم تعيين السائق البديل وإسناد المهمة بنجاح",
  "replacementDriverId": "2b3c4d5e-5717-4562-b3fc-2c963f66afa6",
  "replacementDriverCode": "DR-1012",
  "driverName": "فهد الدوسري"
}
```

#### 📖 القاموس التفصيلي لحقول استجابة إعادة التعيين (`Reassignment Response Dictionary`):

| الحقل | النوع البرمجي | الوصف وكيفية الاستخدام في الواجهة مباشرة |
| :--- | :--- | :--- |
| `success` | `bool` | مؤشر النجاح القطعي للعملية الذرية (`true`). |
| `issueId` | `String (UUID)` | معرّف المشكلة التي تم إسناد السائق البديل لها. |
| `previousDriverId` | `String (UUID)` | معرّف السائق السابق المتعثر لتحديث حالته في الكاش المحلي إذا لزم الأمر. |
| `issueStatus` | `String` | الحالة الجديدة للبلاغ بعد التعيين (`"InProgress"` / "قيد المعالجة"). |
| `reassignedAtUtc` | `String (ISO 8601)` | التوقيت الفعلي للتعيين بصيغة UTC. |
| `message` | `String` | رسالة النجاح المعروضة في الـ `SnackBar` ("تم تعيين السائق البديل وإسناد المهمة بنجاح"). |
| `replacementDriver` | `Object` | **كائن السائق البديل الكامل** لتحديث كارت السائق في شاشة `04.12` فورياً دون استدعاء أي API: |
| ↳ `driverId` | `String (UUID)` | المعرّف الفريد للسائق الجديد. |
| ↳ `driverCode` | `String` | كود السائق الميداني (مثل: `"DR-1012"`). |
| ↳ `fullName` | `String` | الاسم الثلاثي/الكامل للسائق البديل. |
| ↳ `avatarUrl` | `String? (URL)` | رابط صورة السائق الشخصية (يُعرض عبر `AppCachedNetworkImage`). |
| ↳ `status` | `String` | الحالة التشغيلية البرمجية الجديدة (`"OnTask"`). |
| ↳ `statusText` | `String` | النص الجاهز للعرض في شارة حالة السائق (`"متصل على المهمة"`). |
| ↳ `statusColor` | `String (Hex)` | لون شارة حالة السائق المعتمد (`"#10B981"`). |
| ↳ `phoneNumber` | `String?` | رقم هاتف السائق الجديد لتمكين زر الاتصال السريع `[ 📞 اتصال سريع ]`. |
| ↳ `vehicleInfo` | `String` | النص الملخص للمركبة (مثل: `"هيونداي النترا • أبيض"`). |
| ↳ `vehicle` | `Object?` | التفاصيل الهيكلية للمركبة: `type`, `model`, `color`, `plateNumber`. |
| `replacementDriverId` | `String (UUID)` | حقل اختياري في جذر الكائن للتوافق الرجعي المباشر. |
| `replacementDriverCode`| `String` | حقل اختياري في جذر الكائن للتوافق الرجعي المباشر. |
| `driverName` | `String` | حقل اختياري في جذر الكائن للتوافق الرجعي المباشر. |

---

#### 🛠️ نموذج Dart المقترح لاستجابة إعادة التعيين (`Flutter Model`):
```dart
class ReassignReplacementDriverResponseModel {
  final bool success;
  final String issueId;
  final String previousDriverId;
  final String issueStatus;
  final SupportProblemDriverModel replacementDriver;
  final DateTime reassignedAtUtc;
  final String message;
  final String? replacementDriverId;
  final String? replacementDriverCode;
  final String? driverName;

  ReassignReplacementDriverResponseModel({
    required this.success,
    required this.issueId,
    required this.previousDriverId,
    required this.issueStatus,
    required this.replacementDriver,
    required this.reassignedAtUtc,
    required this.message,
    this.replacementDriverId,
    this.replacementDriverCode,
    this.driverName,
  });

  factory ReassignReplacementDriverResponseModel.fromJson(Map<String, dynamic> json) {
    return ReassignReplacementDriverResponseModel(
      success: json['success'] as bool? ?? true,
      issueId: json['issueId'] as String,
      previousDriverId: json['previousDriverId'] as String,
      issueStatus: json['issueStatus'] as String? ?? 'InProgress',
      replacementDriver: SupportProblemDriverModel.fromJson(
        json['replacementDriver'] as Map<String, dynamic>,
      ),
      reassignedAtUtc: DateTime.parse(json['reassignedAtUtc'] as String),
      message: json['message'] as String? ?? 'تم تعيين السائق البديل بنجاح',
      replacementDriverId: json['replacementDriverId'] as String?,
      replacementDriverCode: json['replacementDriverCode'] as String?,
      driverName: json['driverName'] as String?,
    );
  }
}

class SupportProblemDriverModel {
  final String driverId;
  final String driverCode;
  final String fullName;
  final String? avatarUrl;
  final String status;
  final String statusText;
  final String statusColor;
  final String? phoneNumber;
  final String vehicleInfo;
  final DriverVehicleModel? vehicle;

  SupportProblemDriverModel({
    required this.driverId,
    required this.driverCode,
    required this.fullName,
    this.avatarUrl,
    required this.status,
    required this.statusText,
    required this.statusColor,
    this.phoneNumber,
    required this.vehicleInfo,
    this.vehicle,
  });

  factory SupportProblemDriverModel.fromJson(Map<String, dynamic> json) {
    return SupportProblemDriverModel(
      driverId: json['driverId'] as String,
      driverCode: json['driverCode'] as String,
      fullName: json['fullName'] as String,
      avatarUrl: json['avatarUrl'] as String?,
      status: json['status'] as String,
      statusText: json['statusText'] as String,
      statusColor: json['statusColor'] as String,
      phoneNumber: json['phoneNumber'] as String?,
      vehicleInfo: json['vehicleInfo'] as String? ?? '',
      vehicle: json['vehicle'] != null
          ? DriverVehicleModel.fromJson(json['vehicle'] as Map<String, dynamic>)
          : null,
    );
  }
}

class DriverVehicleModel {
  final String type;
  final String model;
  final String color;
  final String plateNumber;

  DriverVehicleModel({
    required this.type,
    required this.model,
    required this.color,
    required this.plateNumber,
  });

  factory DriverVehicleModel.fromJson(Map<String, dynamic> json) {
    return DriverVehicleModel(
      type: json['type'] as String? ?? '',
      model: json['model'] as String? ?? '',
      color: json['color'] as String? ?? '',
      plateNumber: json['plateNumber'] as String? ?? '',
    );
  }
}
```

---

#### 📱 دورة حياة التنقل وتمرير النتيجة بين الشاشات (`Screen 04.13 ➔ Screen 04.12`):
1. **في شاشة تعيين السائق البديل (`Screen 04.13`)**:
   - عند استلام استجابة `200 OK` الناجحة:
     ```dart
     // إغلاق الشاشة الحالية وإرجاع كائن النتيجة بالكامل للشاشة السابقة
     Navigator.of(context).pop(response);
     ```
2. **في شاشة تفاصيل البلاغ (`Screen 04.12`)**:
   - استقبال الكائن وتطبيق البيانات فوراً:
     ```dart
     final result = await Navigator.of(context).pushNamed(
       AppRoutes.assignReplacementDriver,
       arguments: {'issueId': issueId},
     );

     if (result is ReassignReplacementDriverResponseModel && result.success) {
       // تحديث كارت السائق المكلف بالبيانات الجديدة مباشرة
       emit(state.copyWith(
         driver: result.replacementDriver,
         status: result.issueStatus,
         statusLabel: 'قيد المعالجة',
       ));
       AppSnackBar.showSuccess(context, result.message);
     }
     ```
3. **النتيجة**:
   - كارت السائق يتحدث لحظياً ليعرض السائق فهد الدوسري (صورته، كوده، سيارته، ورقم هاتفه للاتصال الفوري).
   - شارة المشكلة تتحول من "مفتوحة" إلى "قيد المعالجة" باللون الأزرق.
   - لا يوجد أي استدعاء شبكة إضافي، وتجربة المستخدم سلسة وفورية.

---

## ⚠️ معالجة الأخطاء (Error Handling & HTTP Status Codes)
الصيغة الموحدة للأخطاء متوافقة تماماً مع بنية `lib/core/errors`:
```json
{
  "code": "ERROR_CODE",
  "message": "رسالة واضحة للمستخدم باللغة المحددة في Accept-Language",
  "errors": {}
}
```

| كود الحالة | كود الخطأ البرمجي (`code`) | السبب والتعامل في الواجهة |
| :--- | :--- | :--- |
| **`400 Bad Request`** | `CANNOT_REASSIGN_TO_SAME_DRIVER` أو `INVALID_REPLACEMENT_DRIVER` | محاولة اختيار نفس السائق الحالي، أو إرسال بيانات غير صالحة. يُعرض تنبيه في الواجهة. |
| **`401 Unauthorized`** | `UNAUTHORIZED` | انتهاء جلسة المستخدم أو عدم إرسال Token صالح. يتم توجيه المستخدم لشاشة تسجيل الدخول. |
| **`403 Forbidden`** | `FORBIDDEN` | المستخدم ليس لديه صلاحية `DeliveryManager`. |
| **`404 Not Found`** | `ISSUE_NOT_FOUND` أو `DRIVER_NOT_FOUND` | المشكلة أو السائق غير موجودين في النظام. |
| **`409 Conflict`** | `REPLACEMENT_DRIVER_NO_LONGER_AVAILABLE` | **تعارض التعيين**: السائق المحدد أصبح مشغولاً أو غير متاح في نفس اللحظة. يظل المحتوى والاختيار ظاهرين ويُعرض الخطأ عبر `InlineApiErrorWidget` ليختار المسؤول سائقاً آخر فوراً. |
| **`409 Conflict`** | `ISSUE_ALREADY_RESOLVED_OR_REASSIGNED` | المشكلة حُلت أو أُعيد تعيينها مسبقاً من قِبل مسؤول آخر. |
| **`422 Unprocessable`** | `NOTES_TOO_LONG` | تجاوز الملاحظات للحد الأقصى المسموح (500 حرف). |
| **`500 Internal Error`** | `INTERNAL_SERVER_ERROR` | خطأ غير متوقع بالخادم، يُعرض خيار إعادة المحاولة. |

---

## 📱 تدفق الشاشات وإرشادات التنفيذ في الموبايل (Mobile State & Navigation Flow)

1. **التحميل الأولي (Initial Loading)**:
   - يُعرض هيكل عظمي متحرك (`Shimmer loading`) يماثل كارت المشكلة وكروت السائقين.
2. **عند فشل تحميل الصفحة بدون بيانات سابقة**:
   - استخدام ويدجت الخطأ الكامل `ApiErrorWidget` مع زر "إعادة المحاولة" (`Retry`).
3. **عند نجاح التحميل مع عدم وجود مرشحين (`candidates: []`)**:
   - استخدام ويدجت الحالة الفارغة `EmptyStateWidget` مع زر "تحديث القائمة" (`Refresh`).
4. **الترشيح الافتراضي**:
   - يتم تحديد السائق الأول تلقائياً في قائمة الاختيار (`Radio Selected by default`) لأنه الأقرب والأعلى تقييماً (`recommendationRank: 1`).
5. **عند فشل عملية التعيين (مثلاً `409 Conflict - REPLACEMENT_DRIVER_NO_LONGER_AVAILABLE`)**:
   - **لا تفقد حالة الشاشة**: تظل بيانات المشكلة وقائمة السائقين كما هي دون إعادة تحميل الصفحة بالكامل.
   - يُعرض كارت الخطأ الموضعي `InlineApiErrorWidget` أعلى زر التأكيد مباشرة لتوضيح أن السائق أصبح غير متاح مع إتاحة اختيار سائق آخر فوراً.
6. **السلوك بعد نجاح العملية (`success: true`)**:
   - شاشة تعيين السائق البديل (`04.13`) تُغلق وتُمرر كائن نتيجة النجاح (`ReassignmentResult`) إلى شاشة تفاصيل البلاغ (`Screen 04.12`).
   - شاشة تفاصيل البلاغ (`04.12`) تُحدّث بيانات السائق المعروض والمهمة فورياً استناداً للبيانات المرجعة (`replacementDriverCode` و `driverName` و `issueStatus`) دون الحاجة لإعادة استدعاء API.
   - عند الرجوع إلى شاشة قائمة البلاغات (`Screen 04.11`) يتم تحديث القائمة والعدادات آلياً.
   - لا حاجة لاستخدام الاستقصاء الدوري (`Polling`) إطلاقاً.
