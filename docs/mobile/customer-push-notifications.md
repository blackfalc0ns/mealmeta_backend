# الموبايل: إشعارات Push (Firebase FCM)

دليل ربط **تطبيق Flutter للعميل** بإشعارات MealMate عبر Firebase Cloud Messaging.

Base URL:

```text
https://maelmate.runasp.net/api/v1
```

Headers لطلبات العميل:

```http
Authorization: Bearer <customer-access-token>
Accept: application/json
Accept-Language: ar
```

---

## 1) مشروع Firebase

| الحقل | القيمة |
|--------|--------|
| **Project ID** | `mealmate-bd5b3` |
| **Project Number (Sender ID)** | `993522349228` |
| **Android package** | `kw.mealmate.user2` |
| **iOS bundle** | `kw.mealmate.user` |

**مهم:** مشروع Firebase **واحد** — سجّل فيه تطبيق Android + تطبيق iOS (أو استخدم Flutter wizard في الكونسول).

ملفات التكوين من Firebase Console:

- Android → `google-services.json` → `android/app/`
- iOS → `GoogleService-Info.plist` → `ios/Runner/`

---

## 2) إعداد Flutter

### 2.1 الحزم

```yaml
dependencies:
  firebase_core: ^3.13.0
  firebase_messaging: ^15.2.5
  flutter_local_notifications: ^19.0.0   # اختياري — لعرض الإشعار أثناء فتح التطبيق
```

### 2.2 توليد `firebase_options.dart`

```bash
dart pub global activate flutterfire_cli
flutterfire configure --project=mealmate-bd5b3
```

### 2.3 Android

- `applicationId` = `kw.mealmate.user2`
- فعّل Google Services plugin في `android/app/build.gradle`
- Android 13+: اطلب `POST_NOTIFICATIONS` في runtime

### 2.4 iOS

- Bundle ID = `kw.mealmate.user`
- Xcode → **Signing & Capabilities**:
  - Push Notifications
  - Background Modes → Remote notifications
- Firebase Console → Cloud Messaging → ارفع **APNs Authentication Key (.p8)** من Apple Developer

---

## 3) تهيئة FCM في التطبيق

```dart
import 'package:firebase_core/firebase_core.dart';
import 'package:firebase_messaging/firebase_messaging.dart';
import 'firebase_options.dart';

@pragma('vm:entry-point')
Future<void> firebaseMessagingBackgroundHandler(RemoteMessage message) async {
  await Firebase.initializeApp(options: DefaultFirebaseOptions.currentPlatform);
  // معالجة خفيفة فقط — لا UI هنا
}

Future<void> initPush() async {
  await Firebase.initializeApp(options: DefaultFirebaseOptions.currentPlatform);
  FirebaseMessaging.onBackgroundMessage(firebaseMessagingBackgroundHandler);

  final messaging = FirebaseMessaging.instance;
  await messaging.requestPermission(alert: true, badge: true, sound: true);

  // iOS: قد يرجع null قبل APNs — استمع لـ onTokenRefresh
  final token = await messaging.getToken();
  if (token != null) {
    await registerDeviceToken(token);
  }

  FirebaseMessaging.instance.onTokenRefresh.listen(registerDeviceToken);
}
```

**متى تسجّل التوكن؟**

1. بعد **login** ناجح (عندك access token)
2. عند **onTokenRefresh**
3. بعد **logout** → احذف التوكن من الـ backend (انظر §4.2)

---

## 4) API — تسجيل توكن الجهاز

كل الطلبات تحتاج JWT العميل.

### 4.1 تسجيل / تحديث

```http
PUT /customers/me/device-tokens
Authorization: Bearer <customer-access-token>
Content-Type: application/json
```

```json
{
  "token": "<fcm-device-token>",
  "platform": 1,
  "deviceId": "optional-stable-device-id"
}
```

| `platform` | المنصة |
|------------|--------|
| `1` | Android |
| `2` | iOS |

**Flutter — مثال:**

```dart
Future<void> registerDeviceToken(String fcmToken) async {
  final platform = Platform.isIOS ? 2 : 1;
  await api.put('/customers/me/device-tokens', body: {
    'token': fcmToken,
    'platform': platform,
    'deviceId': await deviceId, // اختياري
  });
}
```

### 4.2 إلغاء التسجيل (logout)

```http
DELETE /customers/me/device-tokens
Authorization: Bearer <customer-access-token>
Content-Type: application/json
```

```json
{
  "token": "<fcm-device-token>"
}
```

---

## 5) الأحداث اللي بتوصل push (الشريحة الأولى)

| الحدث | `eventKey` في data | متى يُرسَل |
|--------|-------------------|------------|
| تأكيد الاشتراك/الدفع (ترحيبي) | `subscription.payment_confirmed` | فور تأكيد الدفع، حتى لو الـ fulfillment لسه بيكتمل |
| قفل الطلب (−72س) | `order.locked_72h` | عند إرسال اليوم للمطعم |
| التحضير النهائي (−24س) | `delivery.prep_24h` | عند فتح نافذة التحضير |
| تم التسليم | `delivery.delivered` | عند تأكيد التسليم من المطعم |

العنوان والنص **بلغة العميل** (`PreferredLanguage` في البروفايل — `ar` افتراضي).

---

## 6) شكل الـ push على الجهاز

### Notification (يظهر في شريط الإشعارات)

- `title` — عنوان مختصر
- `body` — نص الإشعار

### Data payload (للتنقل داخل التطبيق)

```json
{
  "eventKey": "order.locked_72h",
  "subjectId": "3fa85f64-5717-4562-b3fc-2c963f66afa6",
  "deepLink": "mealmate://calendar"
}
```

| `deepLink` | الوجهة المقترحة |
|------------|-----------------|
| `mealmate://calendar` | التقويم |
| `mealmate://orders` | الطلبات |

**Flutter — استقبال:**

```dart
FirebaseMessaging.onMessage.listen((message) {
  // التطبيق مفتوح — اعرض banner محلي أو SnackBar
  final data = message.data;
  final eventKey = data['eventKey'];
  final subjectId = data['subjectId'];
});

FirebaseMessaging.onMessageOpenedApp.listen((message) {
  navigateFromPush(message.data);
});

// cold start
final initial = await FirebaseMessaging.instance.getInitialMessage();
if (initial != null) navigateFromPush(initial.data);
```

---

## 7) ترتيب التكامل في التطبيق

```text
1) Firebase init + firebase_options.dart
2) طلب إذن الإشعارات
3) login → access token
4) getToken() → PUT /customers/me/device-tokens
5) onTokenRefresh → إعادة PUT
6) onMessage / onMessageOpenedApp / getInitialMessage → deep link
7) logout → DELETE /customers/me/device-tokens
```

---

## 8) اختبار

### 8.1 من Firebase Console (بدون backend)

1. افتح [Firebase Console](https://console.firebase.google.com/) → **MealMate** → **Messaging** → **Create campaign** → **Firebase Notification messages**
2. اكتب عنوان/نص تجريبي
3. **Send test message** → الصق **FCM token** من التطبيق (`FirebaseMessaging.instance.getToken()`)
4. Send

> مناسب لاختبار Android/iOS/APNs قبل ما الـ backend يكون جاهز.

### 8.2 من الـ backend (Development)

**المتطلبات:**

1. Service Account JSON في `backend/secrets/firebase-service-account.json`
2. تسجيل توكن الجهاز: `PUT /customers/me/device-tokens`
3. الـ API يعمل في **Development**

```http
POST /customers/me/device-tokens/test
Authorization: Bearer <customer-access-token>
```

**Response مثال:**

```json
{
  "fcmConfigured": true,
  "activeTokenCount": 1,
  "sentCount": 1,
  "failedCount": 0,
  "errors": []
}
```

**PowerShell:**

```powershell
$token = "<customer-jwt>"
Invoke-RestMethod -Method Post `
  -Uri "http://localhost:5000/api/v1/customers/me/device-tokens/test" `
  -Headers @{ Authorization = "Bearer $token"; Accept = "application/json" }
```

### 8.3 حدث حقيقي

نفّذ login → سجّل token → نفّذ حدث (دفع، قفل يوم، تسليم).

---

## 9) أخطاء شائعة

| المشكلة | السبب المحتمل |
|---------|----------------|
| `getToken()` يرجع null على iOS | APNs مش مضبوط أو التطبيق على Simulator |
| Push مش بيوصل | Token مش مسجّل، أو JWT منتهي وقت التسجيل |
| `401` على device-tokens | access token غلط أو منتهي |
| Package name mismatch | `kw.mealmate.user2` / `kw.mealmate.user` لازم يطابق Firebase بالظبط |

---

## 10) إرسال من الـ Backend — خطوات سريعة

### A) إعداد مرة واحدة

1. Firebase Console → ⚙️ **Project settings** → **Service accounts**
2. **Generate new private key** → ينزل ملف JSON
3. احفظه (محلي):

```text
backend/secrets/firebase-service-account.json
```

> **`google-services.json` / `GoogleService-Info.plist` للموبايل فقط — مش للـ backend.**

4. على السيرفر (Production): ارفع نفس الملف واضبط:

```text
FIREBASE_PROJECT_ID=mealmate-bd5b3
GOOGLE_APPLICATION_CREDENTIALS=/path/to/firebase-service-account.json
```

5. طبّق migration (مرة واحدة):

```powershell
dotnet ef database update --project src/MealMate.Persistence --startup-project src/MealMate.Api
```

### B) سجّل توكن الجهاز

```http
PUT /api/v1/customers/me/device-tokens
Authorization: Bearer <customer-jwt>
Content-Type: application/json

{
  "token": "<fcm-device-token>",
  "platform": 1
}
```

### C) ابعت إشعار تجريبي (Development)

```http
POST /api/v1/customers/me/device-tokens/test
Authorization: Bearer <customer-jwt>
```

**PowerShell:**

```powershell
$base = "http://localhost:5000/api/v1"
$jwt  = "<customer-access-token>"

# 1) سجّل التوكن
Invoke-RestMethod -Method Put -Uri "$base/customers/me/device-tokens" `
  -Headers @{ Authorization = "Bearer $jwt"; "Content-Type" = "application/json" } `
  -Body (@{ token = "<fcm-token>"; platform = 1 } | ConvertTo-Json)

# 2) ابعت test push
Invoke-RestMethod -Method Post -Uri "$base/customers/me/device-tokens/test" `
  -Headers @{ Authorization = "Bearer $jwt" }
```

### D) إرسال تلقائي (Production)

بعد إعداد الـ JSON على السيرفر، الـ backend يبعت تلقائيًا عند:

- تأكيد الدفع / ترحيب الاشتراك (فور التحقق من الدفع)
- قفل الطلب −72س
- −24س (background job)
- تم التسليم

---

## 11) ملاحظات

- **صندوق إشعارات داخل التطبيق** مش جزء من الشريحة الأولى — push فقط. الـ `eventKey` و`subjectId` جاهزين لما تضيف inbox لاحقًا.
- الـ backend **ما يوقف** الدفع أو الطلب لو FCM فشل.
- التوكنات الغلط/المنتهية تُعطَّل تلقائيًا على السيرفر.

---

## مراجع

- [Firebase Flutter setup](https://firebase.google.com/docs/flutter/setup)
- [Firebase Cloud Messaging — Flutter](https://firebase.google.com/docs/cloud-messaging/flutter/client)
