# 📱 الدليل الرسمي والشامل لتكامل إشعارات Firebase Cloud Messaging (FCM) والـ Device Tokens لتطبيق الموبايل

> **الجمهور المستهدف**: مطورو تطبيقات الموبايل (Flutter / React Native / Native iOS & Android) لتطبيقي **السائق (Driver App)** و **مسؤول التوصيل (Delivery Manager App)**.
> **آخر تحديث**: 2026-09-24 — تحديث عقود الـ Endpoints، دعم الـ Pre-login Registration، قنوات الأندرويد عالية الأولوية، وإلغاء تفعيل التوكنات.

---

## 🎯 1. نظرة عامة والمعمارية (Architecture Overview)

يعتمد نظام MealMate على إشعارات **FCM Push Notifications** فائقة السرعة والأولوية لتنبيه السائقين ومسؤولي التوصيل حتى في حال كان التطبيق مغلقاً بالكامل (`Terminated / Background`).

يوفر الباك إند مسارين منفصلين لحفظ توكنات الأجهزة وإدارتها:
1. **مسار السائق (Driver App)**:
   - جدول التخزين: `DriverDeviceTokens`.
   - يدعم التسجيل **قبل تسجيل الدخول** (أثناء انتظار اعتماد الحساب عبر `registrationId`) و**بعد تسجيل الدخول** (عبر JWT أو `driverId`).
2. **مسار مسؤول التوصيل / المطعم (Delivery Manager App)**:
   - جدول التخزين: `RestaurantDeviceTokens`.
   - يتطلب توثيقاً رسمياً (`Authorization: Bearer <JWT>`) لربط الجهاز بفرع المطعم الخاص بالمسؤول.

---

## 🔌 2. عقود واجهات الـ Endpoints (API Contracts)

### أ. مسار السائق (Driver App) 🚗

#### 1. تسجيل أو تحديث توكن السائق (`Register / Upsert Device Token`)
- **الرابط**: `POST /api/v1/driver/device-token`
- **التوثيق**:
  - **بعد تسجيل الدخول**: يرسل الـ Header `Authorization: Bearer <DRIVER_JWT>`
  - **قبل تسجيل الدخول (أثناء التسجيل الجديد)**: مسموح بدون توثيق بشرط تمرير `registrationId`.
- **معاملات الاستعلام (Query Parameters - اختيارية)**:
  - `registrationId`: معرف طلب التسجيل (GUID) المُستلم من خطوة `Step 3` في التسجيل.
  - `driverId`: معرف ملف السائق (GUID) اختياري.
- **جسم الطلب (Request Body - JSON)**:
  ```json
  {
    "token": "eX_sample_fcm_token_long_string_from_firebase...",
    "platform": "Android",
    "deviceId": "c2a8f8b0-4b2a-4f51-b8d4-539b98b2c123",
    "registrationId": "a7b3c2d1-0000-0000-0000-000000000001"
  }
  ```
  *(ملاحظة: يمكن إرسال `registrationId` في الرابط كـ Query Param أو داخل الـ JSON Body).*
- **استجابة النجاح**: **`204 No Content`** (أو `200 OK` بدون كائن Wrapper).

#### 2. حذف أو تعطيل توكن السائق عند تسجيل الخروج (`Unregister / Logout`)
- **الرابط المعتمد (DELETE)**:
  ```http
  DELETE /api/v1/driver/device-token?token={FCM_TOKEN}
  ```
  *(يقبل التوكن كـ Query Parameter أو داخل Body `{ "token": "..." }`)*
- **الرابط البديل (POST)** للمكتبات التي تقيّد إرسال Body مع DELETE:
  ```http
  POST /api/v1/driver/device-token/deactivate
  Content-Type: application/json

  {
    "token": "eX_sample_fcm_token_long_string_from_firebase..."
  }
  ```
- **استجابة النجاح**: **`204 No Content`**.

---

### ب. مسار مسؤول التوصيل والمطعم (Delivery Manager App) 🏬

#### 1. تسجيل أو تحديث توكن مسؤول التوصيل بالمطعم (`Register Device Token`)
- **الرابط الموحد والنهائي**: `PUT /api/v1/restaurants/device-tokens`
- **الترويسات (Headers)**:
  ```http
  Content-Type: application/json
  Authorization: Bearer <DELIVERY_MANAGER_JWT>
  ```
- **جسم الطلب (Request Body - JSON)**:
  ```json
  {
    "token": "eX_sample_fcm_token_long_string_from_firebase...",
    "platform": "Android",
    "deviceId": "c2a8f8b0-4b2a-4f51-b8d4-539b98b2c123"
  }
  ```
- **استجابة النجاح**: **`204 No Content`**.

#### 2. حذف أو تعطيل توكن مسؤول التوصيل عند تسجيل الخروج (`Unregister Device Token`)
- **الرابط المعتمد (DELETE)**:
  ```http
  DELETE /api/v1/restaurants/device-tokens?token={FCM_TOKEN}
  Authorization: Bearer <DELIVERY_MANAGER_JWT>
  ```
  *(يقبل التوكن في الـ Query أو داخل Request Body `{ "token": "..." }`)*
- **الرابط البديل (POST)**:
  ```http
  POST /api/v1/restaurants/device-tokens/deactivate
  Authorization: Bearer <DELIVERY_MANAGER_JWT>
  Content-Type: application/json

  {
    "token": "eX_sample_fcm_token_to_remove..."
  }
  ```
- **استجابة النجاح**: **`204 No Content`**.

#### 3. إرسال فحص تجريبي في بيئة التطوير (`Development Test Push`)
- **الرابط**: `POST /api/v1/restaurants/device-tokens/test`
- **جسم الطلب**:
  ```json
  {
    "token": "eX_sample_fcm_token_to_test..."
  }
  ```
- **ملاحظة أمنية**: هذا الـ Endpoint متاح في بيئات **Development & Staging فقط** للتأكد من وصول الإشعارات الفورية، ويتم تعطيله تلقائياً في بيئة الـ Production عبر إعدادات السيرفر.

---

## 🛡️ 3. شكل الاستجابة ونظام الأخطاء الموحد (Response & Error Schemas)

### أ. استجابة النجاح
- نظام MealMate **لا يستخدم** نمط الـ Custom Wrappers مثل `{ "data": ..., "isSuccess": true }`.
- العمليات الإجرائية (Commands) مثل تسجيل وحذف التوكنات ترجع **`HTTP 204 No Content`** (Body فارغ).
- العمليات الاستعلامية (Queries) ترجع **`HTTP 200 OK`** بالبيانات المباشرة كـ JSON.

### ب. استجابة الخطأ القياسية (RFC 7807 ProblemDetails)
في حال حدوث أي خطأ، يرجع السيرفر كائن ProblemDetails بالصيغة الموحدة:
```json
{
  "type": "https://tools.ietf.org/html/rfc7231#section-6.5.1",
  "title": "بيانات غير صالحة",
  "status": 400,
  "detail": "رمز الجهاز (Token) مطلوب.",
  "instance": "/api/v1/driver/device-token",
  "code": "validation.required"
}
```

### ج. جدول رموز الحالات (HTTP Status Codes) المتوقعة:
| كود الحالة | المعنى | سبب الحدوث المحتمل في التطبيق |
|---|---|---|
| **204 No Content** | نجاح تام | تم تسجيل التوكن أو إلغاء تفعيله بنجاح. |
| **400 Bad Request** | خطأ في المدخلات | الـ `token` فارغ، أو يتجاوز 512 حرفاً، أو الـ `platform` غير مدعومة. |
| **401 Unauthorized** | غير مسجل الدخول | عدم إرسال Bearer Token في مسار المطعم أو إرسال JWT منتهي الصلاحية. |
| **403 Forbidden** | صلاحيات غير كافية | مستخدم مسجل كـ Customer أو Driver يحاول استدعاء مسار مسؤول التوصيل بالمطعم. |
| **404 Not Found** | غير موجود | رقم الـ `registrationId` المرسل غير موجود في قاعدة البيانات. |
| **429 Too Many Requests** | تجاوز سقف المحاولات | محاولات تسجيل توكن متكررة جداً تفوق سقف الـ Rate Limiter المسموح. |
| **500 Internal Server Error** | خطأ في السيرفر | مشكلة داخلية غير متوقعة في الخادم. |

---

## ⚙️ 4. قواعد العمل وسلوك النظام (System Behaviors & Rules)

### 1. سلوك التكرار وتغيير المستخدم (Upsert & Idempotency)
- عملية تسجيل التوكن **Idempotent بنسبة 100%**:
  - إذا كان التوكن مسجلاً مسبقاً: يُحدث السيرفر الحساب المربوط به (`UserId`) وتاريخ آخر ظهور (`LastSeenAtUtc`)، ويضبط `IsActive = true`.
  - **تبديل الحسابات على نفس الجهاز**: إذا سجل سائق خروجه ودخل سائق آخر بنفس الجهاز، ينقل السيرفر ملكية التوكن تلقائياً للسائق الجديد ويلغي ربطه بالسابق **دون إرجاع خطأ 409 Conflict**.
  - إذا قام نفس المستخدم بالحصول على توكن جديد من نفس الـ `deviceId`، يقوم السيرفر بتعطيل التوكن القديم وتفعيل الجديد.

### 2. آلية الحذف (Soft Deactivate)
- عند استدعاء `DELETE` أو `/deactivate`:
  - السيرفر **لا يحذف السجل فيزيائياً** من قاعدة البيانات للحفاظ على سجلات التتبع والتدقيق (Audit Trail).
  - يتم ضبط `IsActive = false`.
  - عند إعادة تسجيل الدخول لاحقاً بنفس التوكن، يعاد تفعيله تلقائياً `IsActive = true`.

### 3. القيم المقبولة للـ Platform
- القيم المعتمدة رسمياً في النظام:
  - `"Android"`
  - `"iOS"`
  - `"Web"`
*(الباك إند يقبلها دون حساسية لحالة الأحرف مثل `android` أو `ios`، لكن يُنصح بالالتزام بالصيغة الرسمية أعلاه).*

### 4. التعامل التلقائي مع التوكنات المنتهية وغير الصالحة (Invalid FCM Tokens)
- عندما يرسل السيرفر إشعاراً ويستجيب Firebase بخطأ:
  - `MessagingErrorCode.Unregistered` (تم حذف التطبيق أو انتهاء صلاحية التوكن).
  - `MessagingErrorCode.InvalidArgument`
  - `MessagingErrorCode.SenderIdMismatch`
- يقوم السيرفر **تلقائياً وبشكل فوري** بتعطيل التوكن في قاعدة البيانات (`IsActive = false`) لمنع محاولة الإرسال إليه مجدداً.

---

## 🔔 5. إعدادات FCM المعتمدة لوضع الخلفية (Background / Terminated Mode)

لضمان إيقاظ التطبيق حتى وهو مغلق في Android و iOS، يرسل الباك إند الإعدادات التالية:

### أ. إعدادات Android
```json
"android": {
  "priority": "high",
  "notification": {
    "channel_id": "mealmate_alerts_channel",
    "sound": "default",
    "default_sound": true,
    "default_vibrate_timings": true
  }
}
```
> [!IMPORTANT]
> **مهم لمطوري Android**: يجب على تطبيق الموبايل إنشاء Notification Channel بالاسم المذكور بالضبط:
> - **Channel ID**: `mealmate_alerts_channel`
> - **Name**: تنبيهات الطلبات والرحلات
> - **Importance**: `NotificationManager.IMPORTANCE_HIGH`

### ب. إعدادات iOS (APNs)
```json
"apns": {
  "headers": {
    "apns-priority": "10",
    "apns-push-type": "alert"
  },
  "aps": {
    "sound": "default",
    "content-available": 1
  }
}
```

---

## 📑 6. الجدول الكامل لعقود وبيانات جميع الإشعارات (Complete Event Keys & Payloads)

يستقبل تطبيق الموبايل كائن الإشعار دائماً بوجود كائن `notification` (للعرض التلقائي في النظام) وكائن `data` (للتحكم البرمجي والتوجيه):

| الحدث (`eventKey` / `type`) | المستلم | الحقول الإلزامية في `data` | الحقول الاختيارية | شاشة التطبيق المستهدفة |
|---|---|---|---|---|
| `driver.registration.submitted` | المطعم / الإدارة | `eventKey`, `registrationId`, `restaurantId`, `driverName`, `phone` | `deepLink` | شاشة طلبات التسجيل المعلقة بالمطعم |
| `driver.registration.restaurant_approved` | السائق | `eventKey`, `registrationId`, `stage` ("2"), `status` ("Approved") | - | شاشة متابعة حالة التسجيل المرحلة 2 (Stage 2) |
| `driver.registration.restaurant_changes_requested` | السائق | `eventKey`, `registrationId`, `stage` ("1"), `status` ("NeedsChanges"), `notes` | - | شاشة تعديل مستندات السائق (`Screen 03.02`) |
| `driver.registration.admin_confirmed` | السائق | `eventKey`, `registrationId`, `stage` ("3"), `status` ("Approved") | - | شاشة اكتمال التسجيل والبدء (`Screen 03.04`) |
| `driver.registration.admin_rejected` | السائق | `eventKey`, `registrationId`, `stage` ("3"), `status` ("Rejected"), `reason` | - | شاشة رفض الطلب (`Screen 03.03`) |
| `driver.box.assigned` *(أو `box-assigned`)* | السائق | `eventKey`, `type`, `boxId`, `boxCode`, `zoneName`, `action` | - | شاشة تفاصيل البوكس (`Screen 04.04 / 05.04`) |
| `driver.trip.assigned` *(أو `trip-assigned`)* | السائق | `eventKey`, `type`, `tripId`, `tripCode`, `boxesCount`, `action` | - | شاشة مانيفست الرحلة (`Screen 06.02`) |
| `driver.trip.kitchen_ready` *(أو `kitchen-ready`)* | السائق | `eventKey`, `type`, `tripId`, `tripCode`, `action` | - | شاشة استلام البوكس ومسح الباركود (`Screen 06.01 / 06.03`) |
| `dispatcher.batch.ready` | مسؤول التوصيل | `eventKey`, `restaurantId`, `readyCount`, `action` | - | شاشة طابور البوكسات والإسناد (`Screen 04.03`) |
| `incoming_call` | السائق / العميل | `callId`, `channelName`, `token`, `callerName`, `callerRole` | `boxId` | شاشة المكالمة الصوتية الواردة (`Screen 08.07`) |

---

## 📦 7. أمثلة الـ Payloads الواقعية (Sample Payloads)

### 1. إشعار إسناد بوكس جديد للسائق (`box-assigned`)
```json
{
  "notification": {
    "title": "تم إسناد طلب جديد 📦",
    "body": "تم إسناد الطلب BX-458722 في منطقة السالمية إليك للتوصيل."
  },
  "data": {
    "eventKey": "driver.box.assigned",
    "type": "box-assigned",
    "boxId": "a1111111-1111-1111-1111-111111111111",
    "boxCode": "BX-458722",
    "zoneName": "منطقة السالمية",
    "action": "/driver/orders/BX-458722"
  }
}
```

### 2. إشعار جاهزية الشحنة بالمطبخ للسائق (`kitchen-ready`)
```json
{
  "notification": {
    "title": "الشحنة جاهزة للاستلام بالمطبخ 🍳",
    "body": "أنهى المطبخ تجهيز وجبات الرحلة TRIP-104. يمكنك التوجه للاستلام ومسح الباركود الآن."
  },
  "data": {
    "eventKey": "driver.trip.kitchen_ready",
    "type": "kitchen-ready",
    "tripId": "b2222222-2222-2222-2222-222222222222",
    "tripCode": "TRIP-104",
    "action": "/driver/pickup/manifest"
  }
}
```

### 3. إشعار دفعة طلبات جاهزة لمسؤول التوصيل (`dispatcher.batch.ready`)
```json
{
  "notification": {
    "title": "📦 دفعة طلبات جاهزة للتوزيع",
    "body": "يتوفر الآن 5 طلبات جاهزة بالمطبخ بانتظار الإسناد للسائقين."
  },
  "data": {
    "eventKey": "dispatcher.batch.ready",
    "restaurantId": "0a40e4ff-72c7-4754-94e3-50b5f505b730",
    "readyCount": "5",
    "action": "/dispatcher/orders/queue"
  }
}
```

---

## 💻 8. كود Flutter / Dart المحدث والجاهز للاستخدام المباشر

```dart
import 'dart:convert';
import 'dart:io';
import 'package:device_info_plus/device_info_plus.dart';
import 'package:firebase_messaging/firebase_messaging.dart';
import 'package:flutter_local_notifications/flutter_local_notifications.dart';
import 'package:http/http.dart' as http;

class PushNotificationService {
  final FirebaseMessaging _fcm = FirebaseMessaging.instance;
  final FlutterLocalNotificationsPlugin _localNotifications = FlutterLocalNotificationsPlugin();
  final String _baseUrl = 'http://maelmate.runasp.net'; // أو عنوان السيرفر المعتمد

  static const String notificationChannelId = 'mealmate_alerts_channel';
  static const String notificationChannelName = 'تنبيهات الطلبات والرحلات';

  /// 1. تهيئة الإشعارات، القنوات، والصلاحيات
  Future<void> initialize({
    required String userRole, // 'Driver' أو 'DeliveryManager'
    String? authToken,
    String? registrationId,
  }) async {
    // إنشاء قناة الإشعارات في Android
    if (Platform.isAndroid) {
      const androidChannel = AndroidNotificationChannel(
        notificationChannelId,
        notificationChannelName,
        importance: Importance.high,
        playSound: true,
      );

      await _localNotifications
          .resolvePlatformSpecificImplementation<AndroidFlutterLocalNotificationsPlugin>()
          ?.createNotificationChannel(androidChannel);
    }

    // طلب الصلاحيات
    NotificationSettings settings = await _fcm.requestPermission(
      alert: true,
      badge: true,
      sound: true,
      provisional: false,
    );

    if (settings.authorizationStatus == AuthorizationStatus.authorized) {
      // مزامنة التوكن الحالي
      String? token = await _fcm.getToken();
      if (token != null) {
        await syncTokenWithBackend(
          fcmToken: token,
          userRole: userRole,
          authToken: authToken,
          registrationId: registrationId,
        );
      }

      // الاستماع لتجدد التوكن تلقائياً
      _fcm.onTokenRefresh.listen((newToken) async {
        await syncTokenWithBackend(
          fcmToken: newToken,
          userRole: userRole,
          authToken: authToken,
          registrationId: registrationId,
        );
      });

      // استقبال الإشعار أثناء استخدام التطبيق (Foreground)
      FirebaseMessaging.onMessage.listen((RemoteMessage message) {
        handleNotificationRouting(message.data);
      });

      // استقبال النقر على الإشعار وفتح التطبيق (Background / Terminated)
      FirebaseMessaging.onMessageOpenedApp.listen((RemoteMessage message) {
        handleNotificationRouting(message.data);
      });
    }
  }

  /// 2. إرسال التوكن للباك إند (مع دعم Pre-login بالسائق و JWT)
  Future<bool> syncTokenWithBackend({
    required String fcmToken,
    required String userRole,
    String? authToken,
    String? registrationId,
  }) async {
    try {
      final deviceId = await _getUniqueDeviceId();
      final platform = Platform.isIOS ? 'iOS' : 'Android';

      Uri uri;
      String method;

      if (userRole.toLowerCase() == 'driver') {
        final queryParams = registrationId != null ? '?registrationId=$registrationId' : '';
        uri = Uri.parse('$_baseUrl/api/v1/driver/device-token$queryParams');
        method = 'POST';
      } else {
        uri = Uri.parse('$_baseUrl/api/v1/restaurants/device-tokens');
        method = 'PUT';
      }

      final headers = {
        'Content-Type': 'application/json',
        if (authToken != null && authToken.isNotEmpty) 'Authorization': 'Bearer $authToken',
      };

      final body = jsonEncode({
        'token': fcmToken,
        'platform': platform,
        'deviceId': deviceId,
        if (registrationId != null) 'registrationId': registrationId,
      });

      final response = method == 'POST'
          ? await http.post(uri, headers: headers, body: body)
          : await http.put(uri, headers: headers, body: body);

      // الباك يرجع 204 No Content أو 200 OK عند النجاح
      if (response.statusCode == 204 || response.statusCode == 200) {
        print('✅ FCM Token synced successfully for $userRole');
        return true;
      } else {
        print('⚠️ Sync Token failed: ${response.statusCode} - ${response.body}');
        return false;
      }
    } catch (e) {
      print('❌ Sync Token Error: $e');
      return false;
    }
  }

  /// 3. إلغاء تفعيل التوكن عند تسجيل الخروج (Logout)
  Future<void> unregisterToken({
    required String fcmToken,
    required String userRole,
    String? authToken,
  }) async {
    try {
      final isDriver = userRole.toLowerCase() == 'driver';
      final path = isDriver ? '/api/v1/driver/device-token' : '/api/v1/restaurants/device-tokens';
      final uri = Uri.parse('$_baseUrl$path?token=${Uri.encodeComponent(fcmToken)}');

      final headers = {
        if (authToken != null && authToken.isNotEmpty) 'Authorization': 'Bearer $authToken',
      };

      final response = await http.delete(uri, headers: headers);
      if (response.statusCode == 204 || response.statusCode == 200) {
        print('✅ FCM Token deactivated successfully on logout');
      }
    } catch (e) {
      print('❌ Unregister Token Error: $e');
    }
  }

  /// 4. المعرف الفريد للجهاز
  Future<String> _getUniqueDeviceId() async {
    final deviceInfo = DeviceInfoPlugin();
    if (Platform.isAndroid) {
      final androidInfo = await deviceInfo.androidInfo;
      return androidInfo.id;
    } else if (Platform.isIOS) {
      final iosInfo = await deviceInfo.iosInfo;
      return iosInfo.identifierForVendor ?? 'ios-uuid';
    }
    return 'unknown-uuid';
  }

  /// 5. التوجيه للشاشات حسب بيانات الإشعار
  void handleNotificationRouting(Map<String, dynamic> data) {
    final eventKey = data['eventKey'] ?? data['type'];
    final tripId = data['tripId'];
    final boxId = data['boxId'];
    final tripCode = data['tripCode'];
    final boxCode = data['boxCode'];

    switch (eventKey) {
      // إسناد بوكس
      case 'driver.box.assigned':
      case 'box-assigned':
        // فتح شاشة تفاصيل البوكس وتوصيله (Screen 05.04) باستخدام boxId أو boxCode
        break;

      // جاهزية الشحنة بالمطبخ
      case 'driver.trip.kitchen_ready':
      case 'kitchen-ready':
        // فتح شاشة استلام الرحلة ومسح الباركود (Screen 06.01) باستخدام tripId أو tripCode
        break;

      // إسناد رحلة
      case 'driver.trip.assigned':
      case 'trip-assigned':
        // فتح شاشة قائمة البوكسات في الرحلة (Screen 06.02)
        break;

      // دفعة طلبات جاهزة لمسؤول التوصيل
      case 'dispatcher.batch.ready':
        // فتح شاشة طابور الطلبات والإسناد (Screen 04.03)
        break;

      // حالة طلب تسجيل السائق
      case 'driver.registration.restaurant_changes_requested':
        // فتح شاشة تعديل المستندات (Screen 03.02)
        break;

      case 'driver.registration.admin_confirmed':
        // فتح شاشة بدء العمل وتسجيل الدخول (Screen 03.04)
        break;

      case 'incoming_call':
        // فتح شاشة الاتصال الصوتي WebRTC (Screen 08.07)
        break;
    }
  }
}
```

---

## 🛠️ 9. أوامر cURL لاختبار الـ Endpoints (Terminal / Postman)

### 1. ربط توكن السائق فور التسجيل (بدون JWT):
```bash
curl -X POST "http://maelmate.runasp.net/api/v1/driver/device-token?registrationId=a7b3c2d1-0000-0000-0000-000000000001" \
  -H "Content-Type: application/json" \
  -d '{
    "token": "c7J_FCM_DRIVER_PRELOGIN_TOKEN...",
    "platform": "Android",
    "deviceId": "samsung-sm-g998b"
  }'
```

### 2. حذف/تعطيل توكن السائق عند تسجيل الخروج:
```bash
curl -X DELETE "http://maelmate.runasp.net/api/v1/driver/device-token?token=c7J_FCM_DRIVER_PRELOGIN_TOKEN..."
```

### 3. تسجيل توكن مسؤول التوصيل بالمطعم (مع JWT):
```bash
curl -X PUT "http://maelmate.runasp.net/api/v1/restaurants/device-tokens" \
  -H "Content-Type: application/json" \
  -H "Authorization: Bearer <DELIVERY_MANAGER_JWT>" \
  -d '{
    "token": "dK4_FCM_RESTAURANT_TOKEN...",
    "platform": "Android",
    "deviceId": "pixel-7-pro"
  }'
```

### 4. حذف توكن مسؤول التوصيل عند تسجيل الخروج:
```bash
curl -X DELETE "http://maelmate.runasp.net/api/v1/restaurants/device-tokens?token=dK4_FCM_RESTAURANT_TOKEN..." \
  -H "Authorization: Bearer <DELIVERY_MANAGER_JWT>"
```
