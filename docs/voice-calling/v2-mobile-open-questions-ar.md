# إجابات فجوات ربط Voice-calls v2 على الموبايل — 2026-10-09

هذا الملف يجيب نقطة‑بنقطة على ملاحظات مراجعة فيتشر المكالمات (v2). كل ما هو مذكور هنا مُحقّق من الكود المرفوع على `dev` أو مُختبَر حيًّا على API المحلي (5073). المرجع الكامل للعقد: [v2-integration-handoff.md](v2-integration-handoff.md) و [v2-push-transport.md](v2-push-transport.md).

---

## 1) معرّف FCM المطلوب (`fcmDeviceTokenId`) — تم حل الـBlocker

نقطتا التسجيل الحاليتان **أصبحتا ترجعان معرّف السجل** بعد التعديل:

| الجهة | Endpoint | الاستجابة الجديدة |
| --- | --- | --- |
| العميل | `PUT /api/v1/customers/me/device-tokens` | `200` + `{"fcmDeviceTokenId":"<guid>"}` |
| السواق | `POST /api/v1/driver/device-token` | `200` + `{"fcmDeviceTokenId":"<guid>"}` |

ملاحظات تعاقدية مهمة:

- **العقد أصبح idempotent بمعرف ثابت**: إعادة تسجيل **نفس** توكن FCM ترجع **نفس** الـID دائمًا (السيرفر يبحث بالتوكن أولًا). يعني لو ضاعت استجابة أول تسجيل، أعيدوا نفس التوكن وتستعيدوا الـID بأمان — لا حاجة لـendpoint استعلام منفصل.
- سبب تغيّر الكود من 204 إلى 200: الجسم أصبح يحمل البيانات. عملاء الموبايل القديمة التي تتعامل مع أي 2xx كنجاح لن تنكسر.
- الـID هو مفتاح صف التوكن في سجل الأجهزة (`CustomerDeviceTokens` / `DriverDeviceTokens`) وهو نفسه الذي يتحقق منه السيرفر عند `POST /api/v2/voice-calls/devices/register` (يجب أن يكون التوكن مملوكًا **لنفس الحساب** وإلا رُفض التسجيل برفض عام 403).
- توكن Firebase الخام أو GUID عشوائي **غير صالحان** — السيرفر يرجّع NoRoute صامتًا وقت الإرسال لأنه لا يجد صفًا مطابقًا مملوكًا للحساب.
- على iOS يلزم الاثنان معًا: `fcmDeviceTokenId` (لأحداث الإنهاء/التنظيف عبر FCM) + `apnsVoipToken` (للمكالمات الواردة عبر PushKit) — انظر قسم 3.

**مثال حي مُختبَر (محليًا على البناء الجديد):**

```
PUT /api/v1/customers/me/device-tokens        → 200 {"fcmDeviceTokenId":"cee61460-1e85-4a5a-b185-b9d2b35750b1"}
POST /api/v2/voice-calls/devices/register     → 200 {"deviceSessionId":"81ca38ff-...","controlProof":"...","expiresAtUtc":"2026-11-08T16:24:06Z"}
     body: {"installationId":"...","platform":"android","capabilityVersion":1,"fcmDeviceTokenId":"cee61460-..."}
```

> ⚠️ شرط التفعيل على Staging: نشر البناء الجديد أولًا (الكود حاليًا غير مرفوع)، وانظر قسم 2 لعلم `V2Enabled`.

## 2) بيانات بيئة Staging

| البند | القيمة / الطريقة |
| --- | --- |
| baseUrl | `https://maelmate.runasp.net` (المستخدم في الـredirects المعتمدة؛ المحلي: `http://localhost:5073`) |
| تفعيل v2 | متغير بيئة `VoiceCalls__V2Enabled=true` (أو `"VoiceCalls": { "V2Enabled": true }`). **حاليًا غير مفعّل على النشر الحالي → كل مسارات v2 ترجع 503.** فعّلناه محليًا فقط حتى الآن |
| JWT تجريبي للعميل | `POST /api/v1/auth/customers/register` ثم `verify-phone` بالكود الثابت `123456` (بيئات dev/staging فقط) — الاستجابة تحمل `accessToken`؛ بعدها `login` مباشرة |
| JWT تجريبي للسواق | حساب سواق معتمد موجود + `POST /api/v1/auth/drivers/login` (أو مسار تسجيل السواق ثم اعتماد الأدمن) |
| `tripStopId` حقيقي | أسهل طريق: افتح تطبيق السواق على staging — `map/route` و manifest يرجعان `tripId`/`tripStopId` لأوقاف اليوم الفعلية للسواق المختبر. استعلام SQL بديل أدناه |

```sql
-- وقاف اليوم المرتبط برحلة نشطة لسواق محدد (PostgreSQL، أسماء الأعمدة PascalCase)
SELECT ts."Id" AS trip_stop_id, ts."OrderCode", ts."Status", t."Status" AS trip_status
FROM "TripStops" ts JOIN "Trips" t ON t."Id" = ts."TripId"
WHERE t."Status" = 2 /* InProgress */ AND ts."Status" = 1 /* PickedUp */
ORDER BY ts."SequenceNumber" LIMIT 5;
```

شرط بدء المكالمة (يقرره السيرفر ولا مجال لتجاوزه): استعلام `GET /api/v2/voice-calls/eligibility/{tripStopId}` قبل إظهار زر الاتصال، ويجب أن يكون الوقاف `PickedUp` داخل رحلة `InProgress` مكلّفًا للسواق نفسه.

## 3) Push Configuration

**FCM (Android + iOS):**
- الإعداد موجود ومفعّل في بيئة التطوير: `Firebase:ProjectId = mealmate-bd5b3` و `Firebase:CredentialPath = secrets/firebase-service-account.json` (service account JSON).
- كلا التطبيقين (عميل/سواق) يجب أن يكونا على **نفس مشروع Firebase** `mealmate-bd5b3` — المزود ينشئ FirebaseApp واحدًا من نفس الاعتمادات.
- رسائل المكالمات data-only بأولوية عالية وTTL مربوط بموعد انتهاء المكالمة — التطبيق يعالج `data.envelope` وليس notification.

**APNs VoIP (iOS):**
- القسم: `VoiceCalls:Apns` → `TeamId`, `KeyId`, `BundleId` (تطبيق العميل)، `DriverBundleId` (تطبيق السواق)، `PrivateKeyPem` أو `PrivateKeyPath` (مفتاح p8)، `UseSandbox` (true لبيئة التطوير من Apple).
- الإرسال الوارد يتم مباشرة عبر HTTP/2 API التابع لـApple (`apns-push-type: voip`، الأولوية 10، الـtopic هو `<bundleId>.voip`) وليس عبر FCM.
- **تسجيل توكن PushKit يحدث من نفس الـendpoint**: حقل `apnsVoipToken` داخل `POST /api/v2/voice-calls/devices/register` — **لا يوجد مسار منفصل**. لو رجع الـroute غير صالحًا يمسح السيرفر التوكن من الجلسة تلقائيًا.
- iOS يحتاج أيضًا تسجيل FCM عادي (`platform:"ios"` + `fcmDeviceTokenId`) لأن أحداث ما بعد الوارد/الإنهاء تُرسل عبر FCM كما في Android.

## 4) SignalR — مؤكَّد من الكود

| البند | القيمة المؤكدة |
| --- | --- |
| عنوان الـHub | `/hubs/voice-call-v2` (مسجّل في `Program.cs`) |
| تمرير JWT | `?access_token=<JWT>` في query string عند الاتصال فقط (`OnMessageReceived` يقبل `access_token`/`accessToken`/`token` على مسار `/hubs`). الإثبات يُرسل داخل الاستدعاء وليس في URL |
| استدعاءات العميل | `BindDeviceSession(deviceSessionId, controlProof)` ثم `SendOffer(callId, {type:"offer",sdp}, messageId, negotiationGeneration)` / `SendAnswer(...)` / `SendIceCandidate(callId, {candidate}, messageId, negotiationGeneration)` |
| أحداث السيرفر | `bound` → `{deviceSessionId}` عند نجاح الربط، ثم `call:event`, `rtc:offer`, `rtc:answer`, `rtc:ice-candidate` |
| الظرف (envelope) | يحمل `messageId`, `callId`, `negotiationGeneration`, `senderDeviceSessionId`, `sentAtUtc`, `payload` |
| الحدود | SDP ≤ 65,536 بايت UTF‑8، مرشح ICE ≤ 4,096 بايت، حد استقبال الرسالة 400KB |

قاعدة صارمة: **لا إشارة قبل الربط** — أي `Send*` قبل `BindDeviceSession` يفشل، والإشارة مسموحة فقط بين جهازَي الفوز الفعليين بالمكالمة (`CallerDeviceSessionId` / `ReceiverDeviceSessionId` بعد أول answer).

## 5) WebRTC / TURN

- `GET /api/v2/voice-calls/{callId}/ice-servers` (بـ`X-Voice-Device-Session` + `X-Voice-Device-Proof`) يرجّع STUN + `turn:...udp` + `turn:...tcp` + `turns:...tcp` مع **credentials مؤقتة HMAC‑SHA1** (صيغة TURN REST API القياسية، افتراضيًا TTL 3600 ثانية) — لا credentials اختراقية لمكالمة غير موجودة أو طرف غير رابح.
- إعدادات السيرفر: قسم `Coturn` → `ServerHost`, `SharedSecret` (**يجب أن يطابق سر coturn المنشور**), `TurnPort 3478`, `TurnsPort 5349`, `TtlSeconds 3600`.
- نشر coturn: `backend/docker/coturn/docker-compose.coturn.yml` + `turnserver.conf` جاهزان ويفشل التشغيل عند غياب القيم الإلزامية — المطلوب على staging: سيرفر coturn منشور بعنوان عام + نفس الـSharedSecret في متغيرات بيئة الـAPI.
- **تنبيه TLS**: القيمة `Coturn:TlsCertConfigured` افتراضيها `true` في الكود الحالي، أي أن `turns:` قد يُعلَن قبل توثيق شهادة فعلية. حتى توثيق الشهادة على staging اضبطوا `Coturn:TlsCertConfigured=false` كي لا يستلم العملاء عناوين TLS لن تستخدم.
- اختبار الشبكتين المختلفتين مسؤولية تطبيق الموبايل عبر `getStats()`: يعتبر الاتصال relay‑مؤكدًا عندما يظهر مرشح بنوع `relay` محليًا و`selected-candidate-pair` عبر TURN، وليس فقط نجاح المكالمة على نفس الـWi‑Fi.

## 6) الاختبار على أجهزة حقيقية

لا يوجد أي blocker متبقٍ من جهة الباكند بعد نشر البناء الجديد وتفعيل `V2Enabled`. سيناريوهات القبول المطلوبة (نفس قائمة [v2-integration-handoff.md §Deployment](v2-integration-handoff.md)):

- Android: وارد في foreground / background / terminated (data-only + أولوية عالية).
- iOS: PushKit/CallKit مع `apns-push-type: voip` وreport عند الرفض من CallKit.
- جهازان لنفس حساب العميل: أول answer يفوز بالـmedia والجهاز الآخر يستلم حدث إنهاء نظيفًا.
- انقطاع الشبكة وإعادة الاتصال: إعادة bind + مزامنة عبر `GET /active` و`GET /{callId}/events?afterSequence=...` مع إزالة تكرار بـ`eventId`.
- صوت حقيقي باتجاهين مع relay مؤكد من إحصاءات ICE.
- تدوير الجلسة: إعادة `devices/register` لنفس `installationId` تُبطل الجلسة القديمة، والـ`controlProof` يُرجَع مرة واحدة فقط ويُخزَّن في الـkeychain/keystore.

---

### ملخص المطلوب من إدارة البيئة (ليس من الموبايل)

1. دفع/نشر البناء الجديد (يرجع `fcmDeviceTokenId`) ثم ضبط `VoiceCalls__V2Enabled=true`.
2. نشر coturn بمفتاح مشترك مطابق وقيم إلزامية، وتعطيل `TlsCertConfigured` حتى توثيق الشهادة.
3. ضبط `VoiceCalls:Apns` (TeamId/KeyId/BundleId/p8/UseSandbox) لبيئة staging.
