# Voice Calls v2 — Canonical Contract (FROZEN for T05)

**Status:** عقد v2 المُجمّد بواسطة T05. الـDTOs وحقول JSON مثبتة باختبارات ذهبية
(`tests/MealMate.UnitTests/VoiceCalls/VoiceCallV2ContractTests.cs`). أي تغيير لاحق
يتطلب تحديث الاختبارات الذهبية والموبايل معًا.

**Version:** `protocolVersion = 2`. **Legacy:** v1 يبقى للقراءة الآمنة والإنهاء وتصريف
الجلسات؛ إنشاء المكالمات عبر v1 يُغلق بمفتاح `VoiceCalls:LegacyInitiationEnabled`
(انظر §6).

## 1. Wire shapes (camelCase — golden-tested)

### CallSnapshotDto

```json
{
  "callId": "…", "tripStopId": "…", "protocolVersion": 2, "sequence": 7,
  "status": "Ringing", "endReason": null, "startedAtUtc": "…", "acceptedAtUtc": null,
  "connectedAtUtc": null, "endedAtUtc": null, "durationSeconds": null,
  "deadlineAtUtc": "…", "winningDeviceSessionId": null, "localParticipantRole": "Driver"
}
```

### CallEventEnvelope&lt;T&gt;

```json
{
  "eventId": "…", "protocolVersion": 2, "callId": "…", "sequence": 8,
  "eventType": "incoming", "occurredAtUtc": "…", "sentAtUtc": "…",
  "expiresAtUtc": "…", "payload": { …CallSnapshotDto… }
}
```

**ممنوع `data`** — إصلاح انحراف F09: الحمل ينتقل في `payload` و`sentAtUtc` دائمًا.

### RtcRelayEnvelope&lt;T&gt; (ephemeral فقط)

```json
{
  "messageId": "…", "callId": "…", "negotiationGeneration": 3,
  "senderDeviceSessionId": "…", "sentAtUtc": "…", "payload": { … }
}
```

السيرفر لا يخزن `payload` الخاص بـRTC (SDP/ICE) أبدًا.

## 2. Lifecycle event types

`incoming`, `ringing`, `answered`, `rejected`, `cancelled`, `connecting`,
`connected`, `ended`, `missed`, `failed`, `answered-elsewhere`.

- كل حدث lifecycle يحمل snapshot مطابقًا للحالة المخزنة — لا حدث يناقض `Status`.
- حدث terminal واحد محفوظ؛ إعادة تسليم نفس `eventId` ممكنة والعميل يُلغي التكرار.

## 3. REST (المسار `/api/v2/voice-calls`)

| Method/route | ملاحظات |
| --- | --- |
| POST `/devices/register` | يعيد deviceSessionId/controlProof/expiresAtUtc مرة واحدة |
| POST `/devices/{id}/revoke` | صاحب الجهاز فقط |
| GET `/eligibility/{tripStopId}` | نفس أسباب `contact.*` المعتمدة في T04 |
| POST `/initiate` | tripStopId + clientRequestId → CallSnapshotDto |
| GET `/active`, GET `/{callId}` | snapshots للمشارك، terminal by-id للتسوية |
| GET `/history/{tripStopId}` | page/pageSize، حقول public فقط |
| POST `/{callId}/ringing-ack` | إيصال عرض، بلا ادعاء ملكية |
| POST `/{callId}/answer` | أول جهاز فائز؛ غير الفائز `calls.answered_elsewhere`/409 |
| POST `/{callId}/reject` / `/cancel` / `/end` | terminal idempotent، السبب enum |
| POST `/{callId}/heartbeat` | mediaState + آخر sequence؛ يحدّث media lease |
| GET `/{callId}/ice-servers` | للمكالمة غير المنتهية وصاحب الجهاز فقط |
| GET `/{callId}/events?afterSequence=` | replay lifecycle محدود، بلا SDP/ICE |

**Headers في أوامر الأجهزة:** Bearer JWT + `X-Voice-Device-Session` +
`X-Voice-Device-Proof` (opaque 32 bytes، يُخزن hashed فقط، لا يظهر في URL أو logs).
على Hub يمر الـproof في invocation آمنة بعد اتصال JWT.

**F14 routes:** `/api/v2/delivery-contact/{tripStopId}` — `GET /case`, `POST /hold`,
`POST /resume`, `POST /reveal-phone`, `GET /timeline`. لا endpoint يزيد عدّاد
المحاولات يدويًا.

**Supervisor:** `/api/v2/supervisor/delivery-contact` — `GET /holds`,
`GET /{tripStopId}`, `POST /{tripStopId}/resolve-hold` (action enum + reason +
expectedVersion + clientRequestId).

## 4. Hub v2 — `/hubs/voice-call-v2`

`BindDeviceSession(sessionId, proof)`, `SendOffer/SendAnswer/SendIceCandidate`
(callId, generation, messageId, payload), `ReportConnecting/ReportConnected/
ReportConnectionFailed`, `MuteStateChanged`, `AcknowledgeEvent(eventId, sequence)`.

- الحدود: SDP ≤ 65536 UTF-8 bytes، ICE ≤ 4096 bytes، transport invocation ≤ 98304 bytes.
- كل الطرق محدودة بمعدل حسب الحساب/الجلسة/المكالمة (لا bypass عبر reconnect).
- توجيه RTC لجلسات الطرفين الفائزين فقط؛ أجهزة الحساب الأخرى لا تلتقط شيئًا.

## 5. Errors

`401` unauthenticated؛ `403` role/device-ownership denied؛ `404` مورد خارج النطاق؛
`409` invalid-state / busy / idempotency-mismatch / answered-elsewhere؛ `400`
validation؛ `429` مع Retry-After؛ `501` capability مصرّح بها وغير منفذة بعد؛ `503`
V2Enabled=false. لا رسائل exceptions خام؛ code + message + correlationId.

## 6. Legacy migration & downgrade guard (AC20)

| المرحلة | `VoiceCalls:V2Enabled` | `VoiceCalls:LegacyInitiationEnabled` | التزامن |
| --- | --- | --- | --- |
| الآن (بعد T05) | `false` (افتراضي) | `true` | v1 كما هو؛ v2 routes تعلن 501/503 بصدق |
| بعد T08/T09 (cohort تجريبي) | `true` | `true` | عميلان يعيشان معًا؛ v1 لا يتجاوز قواعد الجهاز/المسافة |
| بعد اكتمال الترقية | `true` | `false` | v1 initiation → `409 calls.legacy_initiation_disabled`؛ القراءة/الإنهاء يبقيان |
| بعد تصريف الجلسات | `true` | `false` | لا downgrade يتجاوز شرط الجهاز أو ٣ كم |

ممنوع إصدار شكلي RTC (v1 وv2) لنفس جلسة التفاوض. عميل قديم يبقى على v1 كاملًا.

## 7. Push payloads (حسب المنصة)

- **Android:** data-only عبر FCM v1 provider، `TTL = max(0, deadline - now)`،
  `eventId`, `callId`, `sequence`, `deadlineAtUtc`; الإلغاء/terminal data متميزان؛
  لا generic notification لكل teardown.
- **iOS:** APNs `push-type=voip` لـincoming فقط، topic صحيح، expiry قصير؛ terminal
  عبر اتصال التطبيق/push عادي مع تسوية CallKit. لا VoIP pushes لأحداث غير incoming.
- قبول الprovider (FCM message-id / APNs 200) **ليس** إثبات وصول جهاز.
