# Voice-call v2 integration handoff — 2026-10-07

This contract describes the repaired backend. Physical customer/driver clients, provider credentials and staging media were not available in this workspace. A reported `Active` state does not prove audible media.

> **2026-10-11 reliability update** (details in `2026-10-11-backend-reliability-results.md`): every suppressed or rejected delivery target now records a stable technical `reasonCode`; FCM token re-registration rebinds the same installation's live voice session without rotating it; and the first terminal `/end` request is audited with its authenticated actor. The sections below retain the original contract; the update notes are inline.

## Delivery suppression reason codes

Per-device `VoiceCallEventDelivery` rows carry an optional `reasonCode` whenever a target ends `Suppressed` or (with provider classification) is deactivated. Codes are technical identifiers — never localized copy — and never contain tokens, proofs or payloads:

| Code | Meaning |
| --- | --- |
| `realtime_only` | `connecting`/`connected`/`ringing` events ride SignalR only, never push |
| `session_revoked` | device session revoked before dispatch |
| `session_expired` | device session expired before dispatch |
| `unsupported_platform` | session platform is neither android nor ios |
| `missing_deadline` | incoming event without any usable ring deadline |
| `incoming_expired` | ring deadline already passed at dispatch time |
| `missing_voip_route` | iOS incoming session has no APNs VoIP token |
| `missing_fcm_reference` | session never bound an FCM registry row |
| `missing_owned_fcm_route` | bound FCM row does not exist under this account |
| `inactive_fcm_route` | bound FCM row exists but is deactivated (e.g. after logout or provider `Unregistered`) |
| `platform_mismatch` | bound FCM row's platform disagrees with the session platform |
| `empty_fcm_token` | bound FCM row has a blank token value |
| `provider_unregistered` | provider confirmed the route invalid (FCM `Unregistered`, APNs `Unregistered`/`BadDeviceToken`); the registry row is deactivated |
| `obsolete_lifecycle` | the event was superseded — the call's committed sequence already moved past it, or the ring is no longer valid; the newer staged event owns delivery |
| `hub_unbound` | no SignalR connection bound to the device session at dispatch |
| `transport_rejected` | defensive fallback when a transport returns no code |

An `AcceptedByProvider` row is never marked `Suppressed` afterwards, and ordinary retries skip accepted targets. FCM `Unavailable`/quota/credential failures remain exceptions to the outbox retry — they never disable a route. The three route-staleness codes (`inactive_fcm_route`, `missing_owned_fcm_route`, `missing_fcm_reference`) are additionally **recoverable** for a still-ringing incoming: when the same committed event is replayed after a route refresh, those deliveries are retried once against the refreshed route instead of being dropped for good.

## Push-route refresh (FCM token rotation)

Registering a new FCM token (`PUT /api/v1/customers/me/device-tokens`, driver equivalent) rebinds the live v2 voice sessions of the **same installation**: sessions matching owner + `installationId` == `deviceId` + platform, not revoked and not expired, move to the new registry row in the same atomic save. The rebind runs only when the request carries an explicit, non-blank installation id — a value remembered on the registry row is never enough. Session id and control proof never change, so an active call is not disturbed. If no explicit installation match exists, nothing is rebound — the backend never guesses "the most recent device". When a token row is claimed by a new account, the previous owner's sessions have their route reference cleared instead of following the token. Concurrent registrations for one installation are serialized per node and, across instances, by the token rows' PostgreSQL `xmin` optimistic-concurrency token with a reread-and-retry — a stale commit can never point a session at a route the other writer deactivated.

The dispatch side is refresh-safe too: before every push dispatch the outbox re-reads the session's committed route (bypassing its loaded copy), and a push delivery that was suppressed purely for route staleness is recovered on replay while the incoming ring is still valid — so a refresh landing mid-dispatch cannot permanently drop a ring. Obsolete/terminal events, expired rings and provider-confirmed invalid routes are never re-sent.

## Terminal requester audit

The first transition that ends a call records the authenticated HTTP actor (account + device session) via `RecordTerminalRequester`, including ended-before-connect (`Failed` + `EndedBeforeConnect`). Retries and the other side's later `/end` never re-attribute it. Recovery-worker terminations (missed/setup/media timeouts) record **no** requester — a timeout belongs to no participant. The audit stores requester identity only; the backend cannot and does not claim whether a human pressed a button versus a native callback fired, and no additional identity detail is exposed to the peer.

## Driver delivery flow

1. Validate the pickup barcode, upload the required condition photos and confirm pickup. Barcode results identify boxes using subscription-day IDs; use the returned `boxId` on the photo/confirmation endpoints.
2. Start delivery. Calling requires the assigned driver, an `InProgress` trip and a `PickedUp` stop, together with the existing eligibility rules. Before showing/enabling the call button, query `GET /api/v2/voice-calls/eligibility/{tripStopId}` and use its server decision. Loading an order alone does not authorize initiation.
3. Register/bind the device, initiate the call, and let the customer app acknowledge actual ringing using `POST /api/v2/voice-calls/{callId}/ringing-ack`. Provider acceptance or hub enqueue alone does not qualify a missed attempt. A persisted `Missed`/`NoAnswer` call needs at least the observed ringing policy duration (20 seconds).
4. Fetch `GET /api/v2/delivery-contact/{tripStopId}/case`. After the first qualifying miss, create `POST .../{tripStopId}/hold` with `notes` and `firstAttemptCallId`. Keep the undelivered stop `PickedUp`; other stops can still be delivered.
5. Return to the stop and explicitly `POST .../{tripStopId}/resume`. The existing policy requires the retry cooldown (default 120 seconds) and a fresh, accurate, driver-owned location within the contact radius (3 km). Passing time alone does not resume the hold.
6. Initiate the second call after resume. A second qualifying miss must occur after resume; two earlier misses cannot unlock the phone.
7. When the case permits it, `POST .../{tripStopId}/reveal-phone` with `clientRequestId`, `reason` and `acknowledgedPrivacyWarning: true`. Only the assigned driver can reveal the persisted customer phone; responses use `Cache-Control: no-store` and a short-lived grant. An absent phone returns a domain error, including on grant replay. The native client opens the platform dialer using this returned number.

Technical failures, missing receipts, expired or insufficient ringing and cancelled/rejected calls must not count as qualifying no-answer attempts. The backend cannot prove physical audibility of a client receipt; native code must acknowledge when ringing actually starts.

## Authentication and signaling

Enable `VoiceCalls:V2Enabled` explicitly on staging. JWT authenticates the account. `POST /api/v2/voice-calls/devices/register` takes `installationId`, `platform`, `capabilityVersion` and owned push-route identifiers. Persist the returned `deviceSessionId` and one-time `controlProof` securely. Rotation/revocation serializes against call control and invalidates older sessions.

Initiation and control requests send `X-Voice-Device-Session` and `X-Voice-Device-Proof`. Device identity is derived from those headers and JWT, never trusted from a request body. `GET /{callId}/ice-servers` also requires these headers and an active winning device on an answered call. Snapshot/history/events reads require participant account authorization.

Connect JWT-authenticated SignalR to `/hubs/voice-call-v2`, then invoke `BindDeviceSession(deviceSessionId, controlProof)`. Send proof only in this invocation, never in a URL. Rebinding replaces the former device group. Reconnect requires a fresh bind and REST reconciliation.

| Invocation | Arguments | Peer event |
| --- | --- | --- |
| `SendOffer` | call ID, `{type:"offer",sdp}`, nonempty message UUID, nonnegative negotiation generation | `rtc:offer` |
| `SendAnswer` | call ID, `{type:"answer",sdp}`, nonempty message UUID, nonnegative negotiation generation | `rtc:answer` |
| `SendIceCandidate` | call ID, candidate DTO, nonempty message UUID, nonnegative negotiation generation | `rtc:ice-candidate` |

RTC envelopes preserve `messageId`, `callId`, `negotiationGeneration`, `senderDeviceSessionId`, `sentAtUtc` and typed `payload`. Relay goes only to the winning peer device. Sender/recipient sessions must remain owned, unrevoked and unexpired. SDP limits are 65,536 UTF-8 bytes; ICE candidate limits are 4,096 UTF-8 bytes. The hub receive limit allows JSON escaping and invocation metadata.

## Lifecycle, retries and recovery

Subscribe to `call:event`. The same committed event ID is used for realtime, push and REST replay. `localParticipantRole` consistently means `Driver` or `Customer`. `incoming` targets receiver devices (the customer in the driver-to-customer flow); later lifecycle events clean up both parties, including other receiver devices after answer.

`envelope.sequence` is the immutable committed event sequence, and `payload.sequence` with its state is the authoritative snapshot that was current at dispatch (since 2026-10-11 the two always agree — see below). Deduplicate by event ID and apply state using snapshot sequence. A delayed `incoming` whose snapshot is terminal must never open ringing UI. Since 2026-10-11 the outbox additionally refuses to dispatch superseded events at all: an event whose committed revision was already advanced is recorded `Suppressed` with `obsolete_lifecycle` instead of being sent under an old sequence, so an envelope's `sequence` and its snapshot revision always agree. Crash/lease expiry retains an at-least-once send window for same-sequence replays, so client deduplication by event ID remains mandatory. Recover after reconnect through `GET /active`, `GET /{callId}` and `GET /{callId}/events?afterSequence=...`; replay includes committed events even if transport retry has not finished. The events endpoint returns lifecycle metadata without a snapshot payload; fetch the call snapshot or active call for authoritative state.

Per-device delivery records distinguish `AcceptedByProvider`, `DispatchedToHub`, `Suppressed` and `Failed`. Provider receipt is not device acknowledgement; hub enqueue is not proof of an online client. Successful targets are persisted before later failures and skipped on ordinary retries. The shared outbox dispatcher schedules retry. Crash/lease expiry retains an at-least-once send window, so client deduplication remains mandatory.

Both peers report connecting/connected and may send heartbeats after answer in Accepted, Connecting or Active states. Setup heartbeats preserve the setup deadline; they do not postpone negotiation expiry. Recovery enforces setup deadlines, independent peer liveness and maximum duration. A heartbeat from one peer cannot keep the missing peer alive. Terminal recovery releases busy reservations together with the committed lifecycle event. Clients stop media/native UI when an authoritative terminal snapshot arrives.

Device groups use the SignalR lifetime manager. Deploying multiple API processes additionally requires a real shared SignalR backplane/service; this change does not configure or validate one.

## Deployment and device acceptance

See [v2 push configuration](v2-push-transport.md). Android receives call-specific high-priority data; iOS incoming uses direct APNs VoIP, with app-specific bundle topics. Use actual Firebase/APNs credentials and correct production/sandbox routing.

Coturn requires an explicitly selected verified image tag/digest, real public address/realm, certificate directory and matching API/server shared secret. `backend/docker/coturn/docker-compose.coturn.yml` passes these settings to the actual server command and fails when mandatory values are absent. `Coturn:TlsCertConfigured` defaults to false; enable it only after validating the deployed certificate/listener. Production rejects the checked-in development secret. Compose syntax validation does not prove network relay or a working certificate. Reference: [coturn server options](https://github.com/coturn/coturn/blob/master/README.turnserver).

Before release, demonstrate on physical Android/iOS devices: foreground/background/terminated incoming calls, correct native call UI, Arabic and English including language switching, two-way audio, forced TURN relay across separate networks, token rotation/revocation, answer on one of two customer devices, either party hanging up, network loss/reconnect and stale-event cleanup. Repeat the exact pickup/start → first miss → Hold → deliver another stop → return/resume → second miss → external dial path. Record actual provider receipts and device/audio evidence without logging proofs, phone numbers or secrets.
