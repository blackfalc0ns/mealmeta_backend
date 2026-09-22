# Screen 04.06: متابعة السائقين والمراقبة الحية على الخريطة (Screen 10.06 - Driver Map & Live Monitoring)

## 🎯 الغرض من الشاشة ونظرة عامة (Overview & Purpose)
تُمثّل هذه الشاشة مركز القيادة الجغرافي اللحظي (`Real-Time Fleet Command Center`) لمسؤول التوصيل (`Dispatcher`)، وتتيح:
1. مراقبة حركة كافة سائقي الأسطول لحظياً على الخريطة التفاعلية مع سرعاتهم وزوايا حركتهم وحالاتهم التشغيلية.
2. متابعة المؤشرات الحيوية الأربعة للأسطول بأعلى الشاشة (نشط، في التوصيل، متوقف مؤقتاً، مشاكل تتطلب انتباه).
3. التزامن الفوري والتفاعلي الثنائي بين علامات الخريطة (`Map Markers`) وقائمة البطاقات السفلية (`Bottom Cards Carousel`) بنقرة زر واحدة (`Pan & Zoom`).
4. استقبال تحديثات إحداثيات السائقين اللحظية عبر **SignalR** بدقة وبدون الحاجة لعمل Polling متكرر.
5. خلو تام من أي بيانات وهمية (Zero Mock Data)؛ كافة الإحداثيات والطلبات والمسافات مستمدة من قاعدة البيانات الحقيقية.

---

## 🎨 التحليل البصري والتخطيطي (UI/UX Breakdown & Components)

```
┌────────────────────────────────────────────────────────┐
│  [🔔]                 متابعة السائقين              [☰] │
│                 مراقبة السائقين في الوقت الفعلي         │
├────────────────────────────────────────────────────────┤
│  [🟢 32]       [🟣 18]         [🔵 7]         [🔴 3]   │
│  سائق نشط     في التوصيل     متوقف مؤقتاً   مشكلة تتطلب │
├────────────────────────────────────────────────────────┤
│                                                        │
│                    🗺️ خريطة حية تفاعلية                 │
│         - علامات السائقين (Avatar + كود البوكس + الحالة) │
│         - أدوات التحكم (بوصلة التمركز 🎯، تكبير +، تصغير -) │
│                                                        │
├────────────────────────────────────────────────────────┤
│  [بطاقة 1: الفروانية] [بطاقة 2: اليرموك] [بطاقة 3: الشعب]  │
│  (شريط أفقي تفاعلي متزامن مع علامات الخريطة Pan & Select)│
├────────────────────────────────────────────────────────┤
│  [الرئيسية]   [الطلبات]   [📍 الخريطة]   [الدعم]   [الحساب] │
└────────────────────────────────────────────────────────┘
```

### 1. الهيدر العلوي (Header Bar)
- **أيقونة التنبيهات [🔔]**: في أعلى اليمين (أو اليسار حسب اتجاه العرض) مع نقطة إشعار حمراء تنبه لوجود مشاكل تشغيلية.
- **العنوان الرئيسي**: **متابعة السائقين**
- **العنوان الفرعي**: **مراقبة السائقين في الوقت الفعلي**
- **زر القائمة الجانبية [☰]**: لفتح القائمة السريعة للتحكم.

### 2. شريط مؤشرات الأداء الحيوية الأربعة (4 KPI Metric Badges)
صف أفقي من 4 كروت إحصائية ملونة:
1. 🟢 **سائق نشط (`32`)**: إجمالي السائقين المتاحين أو العاملين بالمطعم اليوم.
2. 🟣 **في التوصيل (`18`)**: السائقون الذين يقومون حالياً بتوصيل رحلة نشطة (`InDelivery`).
3. 🔵 **متوقف مؤقتاً (`7`)**: سائقون متوقفون بدون حركة أو في استراحة (`Paused / Idle`).
4. 🔴 **مشكلة تتطلب انتباه (`3`)**: سائقون لديهم تأخير غير معتاد، أو فقدوا إشارة الـ GPS، أو واجهوا مشكلة في تسليم طلب.

### 3. الخريطة الحية التفاعلية (Interactive Map Canvas)
- خريطة تفاعلية (Google Maps / Mapbox) تغطي مناطق الكويت.
- **أدوات التحكم العائمة**:
  - 🎯 **زر إعادة التمركز (`Fit Bounds`)**: لإعادة ضبط إطار الخريطة ليشمل كافة السائقين.
  - `+` و `-`: أزرار التحكم في التكبير والتصغير.
- **العلامات المخصصة على الخريطة (`Custom Animated Markers`)**:
  - صورة السائق دائرية مع إطار ملون.
  - شارة الأيقونة الصغيرة بجانب الصورة:
    - 🟢 شاحنة خضراء: في التوصيل.
    - 🟠 شاحنة برتقالية: في الطريق للتحميل.
    - ⚪ شاحنة رمادية: متوقف.
  - كبسولة معلقة أسفل الماركر:
    - كود البوكس النشط: مثل `BX-458622`.
    - شريط الحالة: `في التوصيل` أو `في الطريق للتحميل` أو `متوقف`.

### 4. شريط البطاقات السفلي المتزامن (Bottom Drivers Carousel)
شريط كروت أفقي قابل للتمرير:
- **التفاعل الثنائي**:
  - النقر على كارت السائق يقوم بتحريك الكاميرا (`Animate Camera`) للتركيز على موقعه في الخريطة وتحديده.
  - النقر على أي ماركر على الخريطة يقوم بتمرير الـ Carousel وتحديد الكارت المقابل له (يظهر بإطار بنفسجي محدد).
- **تشريح الكارت**:
  - نقطة الحالة العلوية بالزاوية: 🔴 (مشكلة)، 🟢 (توصيل عادي)، ⚪ (متوقف).
  - صورة السائق واسمه (مثل: `أحمد فيصل`).
  - كود البوكس باللون البنفسجي: `BX-452311`.
  - كبسولة الحالة: `في التوصيل ●`، `في الطريق للتحميل`، `متوقف ●`.
  - حقل الموقع: `الموقع` ⬅️ `اليرموك` / `الفروانية` / `الشعب`.
  - حقل المسافة المتبقية: `المسافة المتبقية` ⬅️ `1.8 كم 📍` (أو `—` في حال التوقف).

### 5. شريط التنقل السفلي (Bottom Navigation Bar)
- 5 عناصر مع تفعيل تبويب **`الخريطة 📍`** بخلفية بنفسجية دائرية مميزة.

---

## 🔌 عقود تكامل الخادم (API Contracts)

### 1. جلب المؤشرات الحيوية وبيانات الخريطة والبطاقات السفلية
- **الرابط**: `GET /api/v1/dispatcher/drivers/live-monitoring`
- **المعاملات (Query Parameters)**:
  - `restaurantId` (اختياري، يتم جلبه تلقائياً من توكن المستخدم).
  - `status` (اختياري للفلترة: `InDelivery`, `Loading`, `Paused`, `AttentionRequired`).
- **الترويسات (Headers)**:
  ```http
  Accept-Language: ar  # أو en
  Authorization: Bearer <token>
  ```
- **استجابة الخادم الناجحة (200 OK)**:
```json
{
  "kpis": {
    "activeDriversCount": 32,
    "inDeliveryCount": 18,
    "pausedCount": 7,
    "attentionRequiredCount": 3
  },
  "drivers": [
    {
      "driverId": "11111111-1111-1111-1111-111111111111",
      "driverCode": "ID:D-1025",
      "driverName": "أحمد فيصل",
      "phone": "+96551234001",
      "plateNumber": "DX-439612",
      "avatarUrl": "https://cdn.mealmate.app/avatars/ahmed-f.jpg",
      "boxCode": "BX-452311",
      "latitude": 29.3245,
      "longitude": 47.9621,
      "heading": 85.0,
      "speedKmh": 28.5,
      "statusCategory": "InDelivery",
      "statusText": "في التوصيل",
      "statusColor": "Green",
      "topIndicatorColor": "Green",
      "locationZone": "اليرموك",
      "remainingDistanceKm": 1.8,
      "remainingDistanceText": "1.8 كم",
      "hasIssue": false,
      "issueDescription": null,
      "updatedAtUtc": "2026-09-22T04:54:00Z"
    },
    {
      "driverId": "22222222-2222-2222-2222-222222222222",
      "driverCode": "ID:D-1026",
      "driverName": "أحمد فيصل",
      "phone": "+96551234002",
      "plateNumber": "DX-A58932",
      "avatarUrl": "https://cdn.mealmate.app/avatars/ahmed-f2.jpg",
      "boxCode": "BX-452311",
      "latitude": 29.2789,
      "longitude": 47.9532,
      "heading": 120.0,
      "speedKmh": 0.0,
      "statusCategory": "AttentionRequired",
      "statusText": "مشكلة تتطلب انتباه",
      "statusColor": "Red",
      "topIndicatorColor": "Red",
      "locationZone": "الفروانية",
      "remainingDistanceKm": 1.8,
      "remainingDistanceText": "1.8 كم",
      "hasIssue": true,
      "issueDescription": "تأخر في التحميل عن الموعد المحدد",
      "updatedAtUtc": "2026-09-22T04:50:00Z"
    },
    {
      "driverId": "33333333-3333-3333-3333-333333333333",
      "driverCode": "ID:D-1027",
      "driverName": "سعد خالد",
      "phone": "+96551234003",
      "plateNumber": "DX-438922",
      "avatarUrl": "https://cdn.mealmate.app/avatars/saad-k.jpg",
      "boxCode": "BX-452311",
      "latitude": 29.3512,
      "longitude": 48.0123,
      "heading": 0.0,
      "speedKmh": 0.0,
      "statusCategory": "Paused",
      "statusText": "متوقف",
      "statusColor": "Grey",
      "topIndicatorColor": "Grey",
      "locationZone": "الشعب",
      "remainingDistanceKm": null,
      "remainingDistanceText": "—",
      "hasIssue": false,
      "issueDescription": null,
      "updatedAtUtc": "2026-09-22T04:42:00Z"
    }
  ]
}
```

---

## ⚡ البث اللحظي عبر SignalR (Real-time Live GPS & Dispatcher Hub)

### 1. الاتصال بـ Hub المرسل (Dispatcher Hub)
- **المسار الرسمي المعتمد**: `/hubs/dispatcher` (مسار موحد ورسمي لكافة اتصالات الموزعين).
- **التوثيق عبر WebSocket**: يتم تمرير التوكن كـ Query Parameter صريح في عنوان الاتصال:
  `wss://api.mealmate.app/hubs/dispatcher?access_token=<JWT_TOKEN>`
- **الانضمام التلقائي للمجموعة عند الاتصال وإعادة الاتصال**:
  - بمجرد نجاح المصادقة، يقرأ السيرفر معرّف المطعم (`mm_restaurant_id` أو `restaurant_id`) من الـ Token ويضم الاتصال تلقائياً إلى المجموعة:
    `restaurant:{restaurantId}:dispatcher`
  - **ملاحظة للموبايل**: لا تحتاج الواجهة لاستدعاء `JoinRestaurantDispatcherGroup` يدوياً؛ السيرفر يتولى الضم التلقائي عند الاتصال الأول وعند كل إعادة اتصال (`OnConnectedAsync`).

### 2. قائمة الأحداث اللحظية المبثوثة (Real-time Events Contract)

#### أ. حدث تحديث الموقع الوحيد (`driver-location-updated`)
تم اعتماد حدث **واحد فقط** لتجنب التكرار والازدواجية (تم إلغاء حدث `driver-monitoring-updated` نهائياً):
```json
{
  "driverId": "3fa85f64-5717-4562-b3fc-2c963f66afa6",
  "tripId": "1a2b3c4d-5717-4562-b3fc-2c963f66afa6",
  "latitude": 29.3255,
  "longitude": 47.9635,
  "heading": 88.0,
  "speedKmh": 32.0,
  "locationZone": "اليرموك",
  "remainingDistanceKm": 1.8,
  "remainingDistanceText": "1.8 كم",
  "updatedAtUtc": "2026-09-22T08:55:12Z"
}
```
> [!NOTE]
> **قاعدة الموبايل عند غياب المنطقة أو المسافة**: إذا كانت `locationZone` أو `remainingDistanceKm` غير متاحة لحظياً (`null`)، يحتفظ تطبيق الموبايل بقيم آخر استجابة REST ناجحة.

#### ب. حدث تغير حالة السائق التشغيلية (`driver-status-updated`)
يبث فور تغير حالة السائق أو إسناد مهمة جديدة أو حدوث مشكلة، ومُرفق به كائن الـ KPIs المحدث تلقائياً لمنع الحاجة لطلب REST إضافي:
```json
{
  "driverId": "3fa85f64-5717-4562-b3fc-2c963f66afa6",
  "statusCategory": "InDelivery",
  "statusText": "في التوصيل",
  "statusColor": "Green",
  "topIndicatorColor": "Green",
  "hasIssue": false,
  "issueDescription": null,
  "kpis": {
    "activeDriversCount": 32,
    "inDeliveryCount": 18,
    "pausedCount": 7,
    "attentionRequiredCount": 3
  },
  "timestampUtc": "2026-09-22T08:55:12Z"
}
```

#### جـ. حدث تحديث البلاغات والمشكلات التشغيلية (`driver-issue-updated`)
يبث عند فتح مشكلة تشغيلية أو حلها أو تعيين سائق بديل، ويحتوي صراحةً على `driverId` وكائن الـ KPIs:
```json
{
  "issueId": "3fa85f64-5717-4562-b3fc-2c963f66afa6",
  "driverId": "11111111-1111-1111-1111-111111111111",
  "resolved": false,
  "status": "Open",
  "issueDescription": "تأخر في التحميل عن الموعد المحدد",
  "kpis": {
    "activeDriversCount": 32,
    "inDeliveryCount": 18,
    "pausedCount": 7,
    "attentionRequiredCount": 3
  },
  "timestampUtc": "2026-09-22T08:55:12Z"
}
```

#### د. حدث إسناد بوكس جديد (`box-assigned`)
```json
{
  "boxId": "3fa85f64-5717-4562-b3fc-2c963f66afa6",
  "boxCode": "BX-10256",
  "driverId": "1a2b3c4d-5717-4562-b3fc-2c963f66afa6",
  "driverName": "أحمد السعيد",
  "assignedAtUtc": "2026-09-22T08:55:12Z"
}
```

---

### 3. استراتيجية إعادة الاتصال وتزامن البيانات (Reconnection Strategy)
- **أحداث عابرة**: أحداث SignalR عابرة (`Ephemeral`) ولا يتم تخزينها في طوابير للمستخدم أثناء انقطاع الإنترنت.
- **إعادة التزامن**: عند إعادة الاتصال (`onreconnected`)، يقوم الموبايل فوراً باستدعاء `GET /api/v1/dispatcher/drivers/live-monitoring` لجلب الحالة الكاملة كـ Source of Truth.
- **إعادة الانضمام التلقائي**: السيرفر يعيد ضم الاتصال لمجموعة المطعم تلقائياً دون أي تدخل يدوي من الموبايل.
- **التحكم بالترتيب الزمني**: جميع أحداث الخادم تحمل توقيت UTC موثوق (`updatedAtUtc` / `timestampUtc`). على تطبيق الموبايل تجاهل أي حدث توقيته أقدم من آخر حدث تم تطبيقه لمنع Out-of-Order Glitches.
- **عدم استخدام Polling دوري**: لا يوجد داعٍ لأي Polling دوري في وجود SignalR واستدعاء الـ REST عند الـ Reconnect.

---

### 4. محاكاة واختبار الأحداث في بيئة Staging (Simulation Endpoints)
تم توفير 3 نقاط نهاية للمطورين لإرسال أحداث تجريبية مباشرة إلى تطبيق الموبايل عبر SignalR:

1. **محاكاة حركة السائق (GPS)**:
   - `POST /api/v1/dispatcher/test/simulate-location`
   - Payload:
   ```json
   {
     "restaurantId": "uuid-optional",
     "driverId": "uuid",
     "tripId": "uuid-optional",
     "latitude": 29.3255,
     "longitude": 47.9635,
     "heading": 88.0,
     "speedKmh": 32.0,
     "locationZone": "اليرموك",
     "remainingDistanceKm": 1.8,
     "remainingDistanceText": "1.8 كم"
   }
   ```
2. **محاكاة تغير الحالة والـ KPIs**:
   - `POST /api/v1/dispatcher/test/simulate-status`
   - Payload:
   ```json
   {
     "restaurantId": "uuid-optional",
     "driverId": "uuid",
     "statusCategory": "InDelivery",
     "statusText": "في التوصيل",
     "hasIssue": false
   }
   ```
3. **محاكاة حدوث أو حل مشكلة تشغيلية**:
   - `POST /api/v1/dispatcher/test/simulate-issue`
   - Payload:
   ```json
   {
     "restaurantId": "uuid-optional",
     "issueId": "uuid-optional",
     "driverId": "uuid",
     "resolved": false,
     "status": "Open",
     "issueDescription": "تأخر في التحميل"
   }
   ```

---

### 5. إجابات وتأكيدات الأسئلة الجوهرية (Essential Clarifications)
1. **هل `AttentionRequired` حالة مستقلة عن `InDelivery`؟**
   - **نعم**. `statusCategory` هي قيمة إشغال وأولوية تشغيلية واحدة وحصرية: إما `InDelivery` أو `Loading` أو `Paused` أو `AttentionRequired`.
   - إذا كان السائق في مهمة توصيل ونشأت مشكلة (انقطاع GPS، أو تأخر تسليم، أو بلاغ شكوى مفتوح)، فإن تصنيفه يتحول فوراً إلى `AttentionRequired` مع `hasIssue: true` ويتحول لونه إلى الأحمر لجذب انتباه المنسق.
2. **هل السيرفر يرسل `driver-status-updated` تلقائياً بعد انقطاع GPS لمدة 25 دقيقة؟**
   - نعم، في استعلام الـ REST يُصنف السائق مباشرة كـ `AttentionRequired` إذا مضى أكثر من 25 دقيقة على آخر إحداثية. وفي بيئة الاختبار Staging يمكن إرسال حدث التنبيه فوراً عبر `POST /test/simulate-status`.
3. **هل الأحداث تحتوي على توقيت UTC موثوق لمنع تطبيق تحديث قديم؟**
   - نعم، كافة الأحداث تحمل حقلاً زمنياً موحداً بصيغة ISO 8601 UTC (`updatedAtUtc` و `timestampUtc`) مثل `2026-09-22T08:55:12Z`.
4. **توفير REST URL و Hub URL**:
   - REST API: `GET https://api-staging.mealmate.app/api/v1/dispatcher/drivers/live-monitoring`
   - SignalR Hub: `wss://api-staging.mealmate.app/hubs/dispatcher?access_token=<TOKEN>`

---

### 5. إعدادات خرائط Google Maps في تطبيق الموبايل (Mobile Setup)

#### أ. إعدادات أندرويد (`android/app/src/main/AndroidManifest.xml`)
```xml
<manifest ...>
    <uses-permission android:name="android.permission.ACCESS_FINE_LOCATION" />
    <uses-permission android:name="android.permission.ACCESS_COARSE_LOCATION" />

    <application ...>
        <meta-data
            android:name="com.google.android.geo.API_KEY"
            android:value="YOUR_GOOGLE_MAPS_ANDROID_API_KEY" />
    </application>
</manifest>
```

#### ب. إعدادات آيفون (`ios/Runner/AppDelegate.swift` و `Info.plist`)
1. في `AppDelegate.swift`:
```swift
import UIKit
import Flutter
import GoogleMaps

@UIApplicationMain
@objc class AppDelegate: FlutterAppDelegate {
  override func application(
    _ application: UIApplication,
    didFinishLaunchingWithOptions launchOptions: [UIApplication.LaunchOptionsKey: Any]?
  ) -> Bool {
    GMSServices.provideAPIKey("YOUR_GOOGLE_MAPS_IOS_API_KEY")
    GeneratedPluginRegistrant.register(with: self)
    return super.application(application, didFinishLaunchingWithOptions: launchOptions)
  }
}
```
2. في `Info.plist`:
```xml
<key>NSLocationWhenInUseUsageDescription</key>
<string>يحتاج التطبيق لصلاحية الموقع لعرض مسارات السائقين والتتبع المباشر للأسطول.</string>
```

---

## 🔄 مسار تدفق المستخدم والتنقل (User Flow Diagram)

```mermaid
flowchart TD
    Dashboard["لوحة التحكم أو شاشة السائقين"] -->|نقر 'عرض السائقين على الخريطة'| LiveMap["10.06 متابعة السائقين"]
    
    LiveMap -->|استدعاء API| FetchMonitoring["GET /drivers/live-monitoring"]
    FetchMonitoring -->|200 OK مباشرة| RenderView["رسم الماركرز + عدادات الـ KPIs + شريط الكروت السفلي"]
    
    LiveMap -->|اتصال Hub| ConnectSignalR["الاتصال بـ /hubs/dispatcher?access_token=..."]
    ConnectSignalR -->|حدث driver-location-updated| MoveMarker["تحريك الماركر بسلاسة + تحديث الكارت"]
    ConnectSignalR -->|حدث driver-status-updated| UpdateStatus["تحديث حالة السائق وعدادات KPIs"]
    ConnectSignalR -->|حدث driver-issue-updated| UpdateAlerts["تحديث شارات المشكلات والتنبيهات"]
    
    RenderView -->|نقر ماركر على الخريطة| FocusCard["تمرير الـ Carousel وتحديد كارت السائق"]
    RenderView -->|نقر كارت في الـ Carousel| CenterMap["تحريك الكاميرا لموقع السائق على الخريطة"]
```
