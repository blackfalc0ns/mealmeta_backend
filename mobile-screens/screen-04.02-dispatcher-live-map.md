# Screen 04.02: خريطة السائقين الموسعة والتتبع الحي في الوقت الفعلي (Dispatcher Live Map & Fleet Tracking)

## 🎯 الغرض من الشاشة ونظرة عامة
تُمثّل هذه الشاشة واجهة المراقبة الجغرافية الحية والموسعة على كامل الشاشة (`Full-Screen Live Map`) لمسؤول التوصيل / المرسل (`Dispatcher / Delivery Manager`).
تُتيح الشاشة لمسؤول التوصيل:
1. تتبع حركة السائقين ومركبات الأسطول مباشرة على خريطة تفاعلية (Google Maps / Mapbox) في الوقت الفعلي.
2. فلترة السائقين على الخريطة حسب حالتهم التشغيلية (`الكل`، `في الطريق للعميل`، `في التوصيل`، `في استراحة`، `متاح`).
3. النقر على أيقونة أي سائق (Marker) لعرض بطاقة سفلية منبثقة (`Driver Detail Card`) تعرض: الاسم، رقم اللوحة، الهاتف، الطلب النشط، السرعة، وزر الاتصال أو إسناد طلب.
4. رسم مسار التوصيل والاتجاهات (`Directions Polyline`) بين موقع السائق وموقع تسليم العميل أو المطعم بنقرة واحدة.
5. الاستماع لأحداث تحديث المواقع الفورية عبر **SignalR Hub** (`driver-location-updated`) دون الحاجة لعمل Polling دوري.

---

## 🎨 التحليل البصري والتخطيطي (UI/UX Breakdown & Components)

### 1. الهيدر العلوي الشفاف المعلق (Floating Map Top Bar)
- **زر الرجوع (Back Button)**: دائري بخلفية بيضاء وظل ناعم للعودة إلى لوحة التحكم الرئيسية (`04.01`).
- **شريط البحث السريع**: إدخال اسم السائق أو رقم اللوحة للتركيز المباشر عليه على الخريطة (`Pan & Zoom`).
- **زر إعادة التركيز وضبط النطاق (Fit All Bounds)**: أيقونة بوصلة لإعادة توسيط الخريطة لتشمل كافة السائقين النشطين في الكويت.

### 2. شريط فلاتر حالات السائقين المعلق (Status Filter Chips)
شريط أفقي يطفو أسفل الهيدر:
- 🟣 **الكل** (إجمالي السائقين المتصلين).
- 🔵 **في الطريق للعميل (`EnRouteToCustomer`)**: أيقونة سيارة متحركة.
- 🟢 **في التوصيل (`InDelivery`)**: أيقونة تسليم نشط.
- 🟡 **في استراحة (`OnBreak`)**: أيقونة قهوة / استراحة.
- ⚪ **متاح (`Available`)**: أيقونة جاهز لاستلام طلبات.

### 3. علامات الخريطة المخصصة (Custom Animated Map Markers)
- علامة كل سائق تظهر دائرة تحتوي على:
  - صورة السائق الرمزية أو أيقونة المركبة.
  - حلقة ملونة حسب الحالة التشغيلية (أزرق: في الطريق، أخضر: في التوصيل، أصفر: استراحة، رمادي: متاح).
  - مؤشر اتجاه الحركة (`Heading Rotation`) يدور مع زاوية حركة السيارة.
  - شارة صغيرة بالسرعة الحالية (مثل `42 كم/س`).

### 4. البطاقة السفلية للسائق المحدد (Selected Driver Bottom Sheet / Card)
عند النقر على أي سائق على الخريطة تظهر بطاقة سفلية أنيقة تحتوي على:
- الصورة الشخصية، الاسم، ورقم لوحة المركبة (مثل `أحمد السعيد` - `DX-439612`).
- شارة الحالة والسرعة الحالية (مثل `في الطريق للعميل • 42 كم/س`).
- الطلب الحالي وموقع التسليم (مثل `#BX-1256` - `السالمية، ق 4، ش 12`).
- أزرار الإجراء السريع:
  - 📞 **اتصال مباشر**: يفتح تطبيق الهاتف برقم السائق.
  - 🗺️ **رسم المسار**: يرسم Polyline على الخريطة بين السائق وموقع العميل.
  - 📦 **إسناد بوكس**: يفتح قائمة البوكسات المعلقة لإسنادها له.

---

## 🔌 عقود تكامل الخادم (API Contracts)

### 1. جلب المواقع الحية لكافة السائقين
- **الرابط**: `GET /api/v1/dispatcher/drivers/live-locations`
- **الترويسات (Headers)**:
  ```http
  Accept-Language: ar  # أو en
  Authorization: Bearer {token}
  ```
- **الاستجابة (200 OK)**:
```json
[
  {
    "driverId": "11111111-1111-1111-1111-111111111111",
    "fullName": "أحمد السعيد",
    "phone": "+96551234001",
    "plateNumber": "DX-439612",
    "avatarUrl": "https://cdn.mealmate.app/avatars/ahmed.jpg",
    "status": "EnRouteToCustomer",
    "statusLabel": "في الطريق للعميل",
    "statusLabelAr": "في الطريق للعميل",
    "statusLabelEn": "En Route to Customer",
    "latitude": 29.3759,
    "longitude": 47.9774,
    "heading": 140.5,
    "speedKmh": 42.0,
    "activeOrderId": "ord-1025",
    "customerAddress": "السالمية، ق 4، ش 12",
    "updatedAtUtc": "2026-09-21T18:10:00Z"
  },
  {
    "driverId": "22222222-2222-2222-2222-222222222222",
    "fullName": "محمد العنزي",
    "phone": "+96551234002",
    "plateNumber": "DX-A58932",
    "avatarUrl": "https://cdn.mealmate.app/avatars/mohammed.jpg",
    "status": "InDelivery",
    "statusLabel": "في التوصيل",
    "statusLabelAr": "في التوصيل",
    "statusLabelEn": "In Delivery",
    "latitude": 29.3375,
    "longitude": 48.0281,
    "heading": 90.0,
    "speedKmh": 35.0,
    "activeOrderId": "ord-1026",
    "customerAddress": "حولي، ق 1، ش بيروت",
    "updatedAtUtc": "2026-09-21T18:07:00Z"
  },
  {
    "driverId": "33333333-3333-3333-3333-333333333333",
    "fullName": "يوسف خالد",
    "phone": "+96551234003",
    "plateNumber": "DX-438922",
    "avatarUrl": "https://cdn.mealmate.app/avatars/yousef.jpg",
    "status": "OnBreak",
    "statusLabel": "في استراحة",
    "statusLabelAr": "في استراحة",
    "statusLabelEn": "On Break",
    "latitude": 29.3450,
    "longitude": 47.9890,
    "heading": 0.0,
    "speedKmh": 0.0,
    "activeOrderId": null,
    "customerAddress": null,
    "updatedAtUtc": "2026-09-21T18:00:00Z"
  }
]
```

---

### 2. حساب المسار والزمن التقديري بين نقطتين (Route Calculation)
- **الرابط**: `POST /api/v1/dispatcher/routes/calculate`
- **جسم الطلب (Request Body)**:
```json
{
  "originLatitude": 29.3759,
  "originLongitude": 47.9774,
  "destinationLatitude": 29.3375,
  "destinationLongitude": 48.0281
}
```
- **الاستجابة (200 OK)**:
```json
{
  "distanceKm": 8.4,
  "durationMinutes": 14.5,
  "distanceText": "8.4 كم",
  "durationText": "15 دقيقة"
}
```

---

### 3. جلب خط المسار (Polyline) لرسمه على الخريطة
- **الرابط**: `POST /api/v1/dispatcher/routes/directions`
- **جسم الطلب (Request Body)**:
```json
{
  "originLatitude": 29.3759,
  "originLongitude": 47.9774,
  "destinationLatitude": 29.3375,
  "destinationLongitude": 48.0281
}
```
- **الاستجابة (200 OK)**:
```json
{
  "encodedPolyline": "g}k~FhnfxOf@aA|@eB...",
  "distanceKm": 8.4,
  "durationMinutes": 14.5,
  "steps": [
    {
      "instruction": "اتجه جنوباً نحو شارع سالم المبارك",
      "distanceMeters": 450,
      "durationSeconds": 60
    }
  ]
}
```

---

### 4. التحديث اللحظي للمواقع عبر البث المباشر (SignalR Realtime Updates)
- **رابط الـ Hub**: `http://localhost:5073/hubs/dispatcher`
- **اسم الحدث (Event Name)**: `driver-location-updated`
- **حمولة البيانات المستقبلة (Payload)**:
```json
{
  "driverId": "11111111-1111-1111-1111-111111111111",
  "tripId": "7eac2561-4503-40dc-bf2f-58f38c065ed1",
  "latitude": 29.3765,
  "longitude": 47.9780,
  "heading": 145.0,
  "speedKmh": 45.0,
  "updatedAtUtc": "2026-09-21T18:15:30Z"
}
```
> **توجيه لمطور الفلاتر (Flutter Implementation Tip)**:
> عند استلام حدث `driver-location-updated`، لا تقم بإعادة بناء كامل الخريطة (`setState`); بل قم بتحديث موضع الـ Marker المحدد بسلاسة مع تحريك ناعم (`Marker Animation / LatLngTween`) وتحديث زاوية التدوير `heading`.

---

## 🏷️ ملخص قيم الحالات التشغيلية (Driver Status Dictionary)

| القيمة البرمجية (`status`) | النص العربي (`statusLabelAr`) | النص الإنجليزي (`statusLabelEn`) | لون الـ Marker |
| :--- | :--- | :--- | :--- |
| `EnRouteToCustomer` | في الطريق للعميل | En Route to Customer | أزرق نابض (`#3B82F6`) |
| `InDelivery` | في التوصيل | In Delivery | أخضر زمردي (`#10B981`) |
| `OnBreak` | في استراحة | On Break | برتقالي دافئ (`#F59E0B`) |
| `Available` | متاح | Available | بنفسجي / رمادي (`#8B5CF6`) |
