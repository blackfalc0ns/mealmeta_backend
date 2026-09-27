# MealMate In-App Voice Calling — Backend Contract Specification

This document defines the authoritative backend contract, protocols, state machine, REST APIs, SignalR real-time events, WebRTC signaling conventions, and multi-device behaviors for MealMate 1-to-1 Voice Calling.

> **Target Audience:** Mobile Engineering Team (Flutter / React Native), QA, Backend Engineers.  
> **Status:** FROZEN for Part 6.

---

## 1. Architecture Overview

MealMate Voice Calling connects Customers and Drivers in the context of an active delivery stop (`TripStop`).
- **Control Plane:** ASP.NET Core 9 Web API (CQRS via MediatR, EF Core 9, PostgreSQL 16).
- **Real-Time Signaling Plane:** ASP.NET Core SignalR (`/hubs/voice-call`).
- **Push Notification Plane:** Firebase Cloud Messaging (FCM) high-priority VoIP alerts.
- **Media Plane:** Peer-to-Peer WebRTC audio encrypted via SRTP/DTLS. When direct NAT traversal fails, media is relayed through self-hosted **Coturn** TURN servers using short-lived HMAC-SHA1 credentials.
- **Media Safety:** Audio data and SDP/ICE payloads **NEVER** traverse backend databases and are **NEVER** recorded.

```
                      +-------------------+
                      |   Mobile Peer A   |
                      +--------+----------+
                               |
               1. REST Start   |  2. SignalR Signaling / FCM
                               v
                      +-------------------+
                      |  MealMate Backend |
                      +--------+----------+
                               |
               3. STUN / TURN  |  4. SignalR Incoming / FCM
                               v
+-------------------+ <-----------------> +-------------------+
|   Coturn Relay    |   Encrypted Media   |   Mobile Peer B   |
+-------------------+   (P2P or TURN)     +-------------------+
```

---

## 2. Authentication & Authorization

All REST endpoints and SignalR Hub connections require a valid JWT Bearer access token:
- **Header:** `Authorization: Bearer <JWT>`
- **SignalR Query String:** `access_token=<JWT>` (standard SignalR WebSocket handshake)
- **Roles:** The authenticated user must be a registered `Customer` or `Driver` participant belonging to the active `TripStop`.
- **Busy Guard:** A participant already engaged in an ongoing call (`Created`, `Ringing`, `Accepted`, `Connecting`, `Active`) cannot initiate or receive another concurrent call.

---

## 3. Call Lifecycle State Machine

```
[Created] --------------> (Ringing Timeout: 30s) --------------> [Missed] (Terminal)
    |
    v
[Ringing] --------------> Receiver Declines --------------------> [Rejected] (Terminal)
    |    --------------> Caller Cancels -----------------------> [Cancelled] (Terminal)
    |    --------------> (Ringing Timeout: 30s) --------------> [Missed] (Terminal)
    v
[Accepted] -------------> (Setup Timeout: 20s) ----------------> [Failed] (Terminal)
    |
    v (ReportConnecting)
[Connecting] -----------> (Media Negotiation Timeout: 30s) ----> [Failed] (Terminal)
    |        -----------> Client Setup Error ------------------> [Failed] (Terminal)
    v (ReportConnected)
 [Active] --------------> Caller or Receiver Taps End ----------> [Ended] (Terminal)
    |     --------------> (Max Call Duration: 240m) -----------> [Ended] (Terminal)
    |
    +-----> (ICE Restart via new Offer/Answer/ICE candidate stays in [Active])
```

### State Definitions & Permissions

| Status | Category | WebRTC Signaling Permitted? | Transitions Allowed To |
| :--- | :--- | :--- | :--- |
| `Created` | Ongoing | No | `Ringing`, `Cancelled`, `Missed`, `Failed` |
| `Ringing` | Ongoing | No | `Accepted`, `Rejected`, `Cancelled`, `Missed` |
| `Accepted` | Ongoing | **Yes** | `Connecting`, `Failed` |
| `Connecting` | Ongoing | **Yes** | `Active`, `Failed` |
| `Active` | Ongoing | **Yes** (Supports ICE restart) | `Ended` |
| `Rejected` | Terminal | No | None (Terminal) |
| `Cancelled` | Terminal | No | None (Terminal) |
| `Missed` | Terminal | No | None (Terminal) |
| `Failed` | Terminal | No | None (Terminal) |
| `Ended` | Terminal | No | None (Terminal) |

---

## 4. REST Endpoints (`api/v1/voice-calls`)

Base URL: `/api/v1/voice-calls` (Alternative alias: `/api/v1/calls`)

### 4.1 Initiate Call
`POST /api/v1/voice-calls/initiate`
- **Rate Limit:** 5 requests / minute per user (`voice-calls-start`).
- **Request Body:**
  ```json
  {
    "tripStopId": "3fa85f64-5717-4562-b3fc-2c963f66afa6",
    "clientRequestId": "7b8e1f54-8e34-4b51-9a72-7b0b3e6d8a11"
  }
  ```
- **Response `200 OK`:** `CallSessionDto`
- **Errors:**
  - `400 Bad Request` (`calls.delivery_not_active`, `calls.caller_busy`, `calls.receiver_busy`)
  - `403 Forbidden` (`calls.unauthorized_participant`)
  - `409 Conflict` (idempotent duplicate client request ID returns original session)
  - `429 Too Many Requests` (Rate limit exceeded)

### 4.2 Get Active Call
`GET /api/v1/voice-calls/active`
- Returns the user's current ongoing session, or `200 OK` with `null` if idle.
- Deterministically resolves ongoing session ordered by `StartedAtUtc DESC, Id DESC`.

### 4.3 Get Call By ID
`GET /api/v1/voice-calls/{id}`
- Returns `CallSessionDto`. Authorized only for caller or receiver.

### 4.4 Answer Call
`POST /api/v1/voice-calls/{id}/answer`
- Invoked by the receiver. Transitions call `Ringing -> Accepted`.
- Idempotent: repeated calls by receiver return `200 OK` with current accepted state.
- Dispatches `voice-call:answered` to caller AND to all receiver devices to dismiss ringing UI.

### 4.5 Reject Call
`POST /api/v1/voice-calls/{id}/reject`
- Invoked by receiver. Transitions `Ringing -> Rejected`.
- Payload: `{"reason": "Busy"}` (optional).

### 4.6 Cancel Call
`POST /api/v1/voice-calls/{id}/cancel`
- Invoked by initiating caller before answer. Transitions `Created/Ringing -> Cancelled`.
- Payload: `{"reason": "ChangedMind"}` (optional).

### 4.7 End Call
`POST /api/v1/voice-calls/{id}/end`
- Invoked by caller or receiver during `Active`. Transitions `Active -> Ended`.
- Calculates `DurationSeconds = EndedAtUtc - ConnectedAtUtc`.
- Payload: `{"reason": "NormalHangup"}` (optional).

### 4.8 Get Call History for Delivery Stop
`GET /api/v1/voice-calls/history/{tripStopId}?page=1&pageSize=20`
- Returns `PagedResult<CallSessionDto>`.
- Paginated, ordered by `CreatedAtUtc DESC, Id DESC`.

### 4.9 Get ICE Servers (STUN/TURN)
`GET /api/v1/voice-calls/ice-servers`
- **Rate Limit:** 30 requests / minute per user (`voice-calls-ice`).
- Returns ephemeral HMAC-SHA1 credentials for Coturn:
  ```json
  {
    "iceServers": [
      { "urls": ["stun:turn.mealmate.com:3478"] },
      {
        "urls": [
          "turn:turn.mealmate.com:3478?transport=udp",
          "turn:turn.mealmate.com:3478?transport=tcp"
        ],
        "username": "1758632400:user-guid",
        "credential": "base64-hmac-sha1-token"
      },
      {
        "urls": ["turns:turn.mealmate.com:5349?transport=tcp"],
        "username": "1758632400:user-guid",
        "credential": "base64-hmac-sha1-token"
      }
    ],
    "expiresAtUtc": "2026-09-23T14:41:43Z",
    "ttlSeconds": 86400
  }
  ```

---

## 5. SignalR Hub Contract (`/hubs/voice-call`)

- **URL:** `/hubs/voice-call`
- **Authentication:** `access_token` query parameter with JWT.
- **Client Auto-Groups:** On connect, client connection is automatically added to `user:{userId}`.
- **Signaling Rate Limit:** Max 60 messages/sec per connection (sliding window) to accommodate trickle ICE candidates.

### 5.1 Client -> Hub Methods

#### `SendOffer(Guid callId, RtcSessionDescriptionDto offer)`
- Relays WebRTC SDP Offer to peer.
- Permitted States: `Accepted`, `Connecting`, `Active` (ICE restart renegotiation).
- Max SDP Size: 64 KB.

#### `SendAnswer(Guid callId, RtcSessionDescriptionDto answer)`
- Relays WebRTC SDP Answer to peer.
- Permitted States: `Accepted`, `Connecting`, `Active` (ICE restart renegotiation).
- Max SDP Size: 64 KB.

#### `SendIceCandidate(Guid callId, RtcIceCandidateDto candidate)`
- Relays trickle ICE candidate to peer.
- Permitted States: `Accepted`, `Connecting`, `Active`.
- Max Candidate Size: 4 KB.

#### `ReportConnecting(Guid callId)`
- Client reports media peer connection started (`Accepted -> Connecting`).
- Idempotent: repeated reports or reports when already `Active` succeed safely.

#### `ReportConnected(Guid callId)`
- Client reports media established and audio flowing (`Connecting -> Active`).
- Sets server-authoritative `ConnectedAtUtc` (set once, never overwritten on retries).

#### `ReportConnectionFailed(Guid callId, string? reason)`
- Client reports unrecoverable negotiation failure (`Connecting -> Failed`).
- Valid Reason Values: `IceFailed`, `PeerConnectionFailed`, `SignalingTimeout`, `MediaUnavailable`, `PermissionDenied`, `Unknown`.

#### `MuteStateChanged(Guid callId, bool isMuted)`
- Relays mute toggle notification to peer (`peer-mute-state-changed`).

---

### 5.2 Server -> Client Events

| Event Name | Recipient Group | Description | Payload |
| :--- | :--- | :--- | :--- |
| `connection-established` | Caller | Confirms SignalR Hub connection | `{ connectionId, userId, connectedAtUtc, status }` |
| `voice-call:incoming` | Receiver user group | Incoming ringing call notification | `{ callId, callerUserId, callerType, tripStopId, startedAtUtc, ringingAtUtc, sentAtUtc }` |
| `voice-call:ringing` | Caller user group | Receiver device is ringing | `{ callId, ringingAtUtc, sentAtUtc }` |
| `voice-call:answered` | Caller & Receiver | Receiver answered (dismisses ringing UI on other devices) | `{ callId, answeredAtUtc, sentAtUtc }` |
| `voice-call:rejected` | Caller user group | Receiver declined call | `{ callId, reason, rejectedAtUtc, sentAtUtc }` |
| `voice-call:cancelled` | Receiver user group | Caller cancelled before answer | `{ callId, reason, cancelledAtUtc, sentAtUtc }` |
| `voice-call:connecting` | Both participants | WebRTC negotiation started | `{ callId, timestampUtc, sentAtUtc }` |
| `voice-call:connected` | Both participants | WebRTC audio connected (Active) | `{ callId, connectedAtUtc, sentAtUtc }` |
| `voice-call:ended` | Both participants | Call ended normally or by timeout | `{ callId, reason, durationSeconds, endedAtUtc, sentAtUtc }` |
| `voice-call:missed` | Both participants | Call timed out with no answer | `{ callId, missedAtUtc, sentAtUtc }` |
| `voice-call:failed` | Both participants | Call negotiation failed | `{ callId, reason, failedAtUtc, sentAtUtc }` |
| `rtc:offer` | Peer user group | Relayed SDP Offer envelope | `{ callId, senderUserId, data: { type, sdp }, timestampUtc }` |
| `rtc:answer` | Peer user group | Relayed SDP Answer envelope | `{ callId, senderUserId, data: { type, sdp }, timestampUtc }` |
| `rtc:ice-candidate` | Peer user group | Relayed ICE candidate envelope | `{ callId, senderUserId, data: { candidate, sdpMid, sdpMLineIndex }, timestampUtc }` |
| `peer-mute-state-changed`| Peer user group | Remote peer muted/unmuted | `{ callId, senderUserId, isMuted, updatedAtUtc }` |

---

## 6. Server Recovery & Timeout Rules

The backend runs `VoiceCallRecoveryJob` across all API instances coordinated via PostgreSQL transaction-level advisory locks (`pg_try_advisory_xact_lock`).

1. **Ringing Timeout (`30s`):**
   - Condition: `Status == Ringing` AND `UtcNow - RingingAtUtc >= 30s`
   - Transition: `Ringing -> Missed` (`EndReason = NoAnswer`)
   - Actions: Dispatches `voice-call:missed` SignalR event + FCM teardown push to receiver.
2. **Accepted Setup Timeout (`20s`):**
   - Condition: `Status == Accepted` AND `UtcNow - AcceptedAtUtc >= 20s`
   - Transition: `Accepted -> Failed` (`EndReason = ConnectionFailed`)
   - Protects against receiver crash immediately after tapping Accept.
3. **Connecting Negotiation Timeout (`30s`):**
   - Condition: `Status == Connecting` AND `UtcNow - ConnectingAtUtc >= 30s`
   - Transition: `Connecting -> Failed` (`EndReason = ConnectionFailed`)
   - Protects against ICE/DTLS stalls, TURN unreachable, or network drops.
4. **Zombie Active Call Safeguard (`240m` / 4 hours):**
   - Condition: `Status == Active` AND `UtcNow - ConnectedAtUtc >= 240m`
   - Transition: `Active -> Ended` (`EndReason = Timeout`)
   - Protects against zombie active calls when both devices lose power or vanish without calling `EndCall`.

---

## 7. Multi-Device & Concurrency Guarantees

1. **Multiple Receiver Devices:**
   - When a call is initiated, `voice-call:incoming` SignalR event and FCM push are sent to all active devices registered to the receiver.
   - **First Device Wins:** When Device A calls `/answer`, the database transitions `Status = Accepted` protected by PostgreSQL `xmin` concurrency.
   - Device B answering milliseconds later receives safe idempotent success (if already accepted) or conflict without state corruption.
   - `voice-call:answered` is broadcast to the receiver's user group, causing Device B to immediately dismiss its incoming call screen.
2. **SignalR Disconnection Independence:**
   - A temporary drop in WebSocket connection does **NOT** terminate media or mark the call failed.
   - WebRTC media flows peer-to-peer or via TURN; clients re-establish SignalR in the background.
3. **WebRTC Active ICE Restart:**
   - When mobile devices switch networks (e.g. Wi-Fi to Cellular), mobile WebRTC creates a new SDP Offer with `iceRestart: true`.
   - The Hub permits `SendOffer` and `SendAnswer` in `Active` status.
   - `ConnectedAtUtc` and duration timing do **NOT** reset during ICE restart.
4. **Perfect Negotiation Recommendation:**
   - Mobile clients should implement the W3C WebRTC **Perfect Negotiation** pattern:
     - Caller role is the **polite** peer (or Callee is polite, agreed by role).
     - The polite peer rolls back local offers if glare occurs (both peers sending offers simultaneously).

---

## 8. Stable Error Codes

| Error Code | HTTP Status | Meaning |
| :--- | :--- | :--- |
| `calls.unauthorized` | 401 | Missing or invalid authentication token |
| `calls.forbidden` | 403 | User is not a participant in this call session |
| `calls.not_found` | 404 | Call session or delivery stop not found |
| `calls.invalid_state` | 400 | Requested transition is invalid from current call status |
| `calls.caller_busy` | 400 | Initiating caller is already in another ongoing call |
| `calls.receiver_busy` | 400 | Target receiver is already in another ongoing call |
| `calls.delivery_not_active` | 400 | Trip stop delivery window is not active or has expired |
| `calls.already_cancelled` | 400 | Call has already been cancelled |
| `calls.already_ended` | 400 | Call has already ended |
| `calls.concurrency_conflict` | 409 | Concurrent state mutation detected |
| `calls.rate_limited` | 429 | Rate limit exceeded (call initiation or ICE token request) |
