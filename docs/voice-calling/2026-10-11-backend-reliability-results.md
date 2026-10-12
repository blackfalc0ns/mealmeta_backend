# Backend voice-calls reliability — implementation results (2026-10-11)

Execution of `docs/`-tracked plan "Backend Voice Calls Reliability — GLM Implementation Plan" against the backend repository only. No mobile code, hosting configuration or production data was touched. No deploy or push was performed.

## Baseline (Task 0)

- `git status` before work: pre-existing local changes were left untouched (AdminCoverage edits, `ApnsProviderTokenCache.cs`, `VoiceCallV2HangupApiTests.cs`, `v2-integration-handoff.md`, two untracked migrations, `tests/_parallel_wip/`).
- Baseline test runs (before any change): **Unit 140/140 passed**, **ApiTests (VoiceCallV2) 39/39 passed**. No pre-existing failure was observed, so no failure attribution was needed.

## Changes by task

### Task 1 — preserved, safe suppression reasons
- `VoiceCallPushResult` gained an optional `ReasonCode`; new `VoiceCallPushReasons` constants (technical codes, never localized, never user-facing).
- `DeviceVoiceCallV2PushTransport` returns a fixed reason for every non-accepted path: `realtime_only`, `session_revoked`, `session_expired`, `unsupported_platform`, `missing_deadline`, `incoming_expired`, `missing_voip_route`, `missing_fcm_reference`, `missing_owned_fcm_route`, `inactive_fcm_route`, `platform_mismatch`, `empty_fcm_token`, `provider_unregistered`. The owner-scoped registry read now includes inactive rows so `inactive_fcm_route` is distinguishable from `missing_owned_fcm_route`; an inactive route is still never sent to.
- `VoiceCallEventDelivery.ReasonCode` (nullable, max 64) persisted via `MarkSuppressed(now, reason)`; the outbox handler stores the transport reason, `hub_unbound` for unbound realtime targets, and `obsolete_lifecycle` for stale rings. Provider failures are logged as safe classifications (exception type + identifiers only — no tokens, payloads or credentials).
- Migration `20261011004452_AddVoiceCallDeliveryDiagnostics` (additive `ReasonCode` column) + idempotent SQL at `artifacts/voice-call-delivery-diagnostics.sql`. Snapshot diff inspected: only the one column; a pre-existing snapshot desync for two older untracked migrations was already present before this work and was not included in (or altered by) the new migration.

### Task 2 — FCM refresh rebinds the same installation
- New `VoiceCallPushRouteRebinder`: after an authenticated token registration, live sessions matching owner + `installationId` == `deviceId` + platform (not revoked, not expired) are repointed to the registry row in the same atomic save — in both the existing-token and new-token branches of the customer handler, and symmetrically in the driver handler. Session id, control proof and the winning device of any active call are untouched. Missing/blank `deviceId` rebinds nothing.
- Token claimed by a new account: the previous owner's sessions lose their route reference (session never moves between accounts).
- Task 2 needed no schema change (the rebind moves an existing reference). Migration `20261011010814_AddVoiceCallTerminalRequesterAudit` belongs to Task 4 below.

## Review fixes (same day, R1–R4)

The independent review of this implementation reproduced two race conditions (R1, R2) and two contract/scope deviations (R3, R4). All four are fixed with deterministic failing-first tests.

### R1 — interleaved registrations could bind a session to an inactive route
Two racing registrations for one installation could interleave read→commit so the session's `FcmDeviceTokenId` landed on a row the other writer had already deactivated. Fix, layered like the device-session rotation:
- **In-process gate**: `VoiceCallPushRouteRebinder.RegistrationGate(userId, installationId)` serializes the whole read-modify-write of concurrent same-installation registrations on one node.
- **Cross-instance backstop**: `CustomerDeviceTokens` and `DriverDeviceTokens` carry the PostgreSQL `xmin` shadow concurrency token (same pattern as Trips) — migration `AddPushTokenConcurrencyTokens` is intentionally **empty** (xmin is a system column; Npgsql emits no SQL for it). Both handlers retry from a fresh read on `DbUpdateConcurrencyException` (up to 3 attempts), detaching exactly the rows they touched.
- `PostgreSqlVoiceCallConcurrencyTests.Stale_interleaved_rebind_commit_is_rejected_and_recovery_lands_an_active_route` replays the review's exact interleaving deterministically: the stale commit now throws instead of silently winning, and the real handler's retry lands an active, owned, matching route.

### R2 — dispatcher used a stale route and permanently dropped a live ring
The outbox loaded device sessions once, so a token refresh completing during the awaited hub send left the push dispatch reading the superseded (now inactive) route; the delivery was recorded `Suppressed` and replay skipped it forever. Fix:
- Before every push dispatch, the handler re-reads the session with `AsNoTracking` (bypassing the change tracker) and dispatches with the committed route — ownership/expiry re-checked; the revision + deadline guards still run first, so obsolete/expired incoming and provider-confirmed invalid routes are never re-sent.
- A `Suppressed` push delivery whose reason is route staleness (`inactive_fcm_route`, `missing_owned_fcm_route`, `missing_fcm_reference`) is recoverable: on replay of the same committed event while the incoming is still ringing, it is reset to `Pending` and dispatched once to the refreshed route. Hub suppressions, `obsolete_lifecycle` and everything terminal stay non-recoverable.
- Deterministic unit tests with a shared in-memory store: barrier inside the hub send + refresh on a second context (fresh route delivered once, stale route never); suppressed-then-refresh replay recovery; and replay-after-terminal proving no revival.

### R3 — migration SQL artifact contained out-of-scope subscription changes
`artifacts/voice-call-terminal-requester-audit.sql` had been scripted to the last migration in the chain, dragging in `AddSubscriptionDayReplacementWindow`. Both artifacts are now generated with **explicit from/to migration ids** and verified to contain only the voice-call column adds (18 lines each, zero `SubscriptionDays`/`ReplacementWindow` content):
- `voice-call-delivery-diagnostics.sql`: `AddSubscriptionDayFailureReasonCode → AddVoiceCallDeliveryDiagnostics`.
- `voice-call-terminal-requester-audit.sql`: `AddVoiceCallDeliveryDiagnostics → AddVoiceCallTerminalRequesterAudit`.

### R4 — driver rebind used a remembered deviceId
The driver handler passed the registry row's stored `DeviceId` to the rebinder, so a request without an explicit `deviceId` could still rebind sessions. The rebind now uses only the normalized, non-blank `deviceId` from the request (registry-row persistence policy unchanged). The driver fresh-token branch also gained the same same-installation supersession the customer branch already had (previous routes of the installation deactivated in the same commit). Driver unit tests cover: null/blank deviceId never rebinds, explicit deviceId rebinds in both branches, fresh-token route move, and account-claim detachment.

### Task 3 — envelope revision consistency
- `VoiceCallOutboxHandler.HandleV2Async` now compares the reloaded call `Sequence` against the event's sequence. A superseded event (`current.Sequence > event sequence`) is recorded `Suppressed`/`obsolete_lifecycle` per (event, channel, device) and not dispatched; the newer staged event owns delivery. Same-sequence replays stay eligible — the crash-after-send window remains at-least-once and still requires client dedupe by event ID. Missed-call qualification (`RecordAttemptForMissedCallAsync`) keeps running before the device loop, so qualification stays committed even when the send is suppressed.
- Envelope golden guarantees now asserted by tests: `envelope.CallId == payload.CallId`, `envelope.Sequence == payload.Sequence`, `eventType` consistent with snapshot status, `occurredAtUtc <= sentAtUtc`, incoming never outlives its ring deadline.

### Task 4 — terminal requester audit
- `CallSession.RecordTerminalRequester(userId, deviceSessionId)`: records the authenticated HTTP actor of the first terminal transition (including `Failed` + `EndedBeforeConnect`); idempotent — retries and the other side's later `/end` never re-attribute. Throws before a terminal state. Recovery-worker terminations never call it, so timeouts stay unattributed (`EndedByUserId`/`EndedByDeviceSessionId` null).
- `EndVoiceCallV2CommandHandler` records inside the participant lock after the transition, before save, with a structured audit log (callId, deviceSessionId, role, statusBefore→statusAfter, sequence, server UTC). Pre-connect (`EndedBeforeConnect`) and post-active (`CallerEnded`/`ReceiverEnded`) semantics and `ConnectedAtUtc`-only duration are unchanged.
- Migration `AddVoiceCallTerminalRequesterAudit` (additive `EndedByDeviceSessionId` column) + incremental idempotent SQL at `artifacts/voice-call-terminal-requester-audit.sql`. `EndedByUserId` had existed but was never written anywhere; it is now populated by this path only.

### Task 5 — guard tests (protection, not behavior change)
Already-covered behaviors kept as-is and documented: one-winner multi-device answer + non-owner denial, both-side connected → Active exactly once, one-sided connected → still Connecting, one-peer heartbeat cannot extend the silent peer's lease, same `clientRequestId` idempotency, stale conditional expiry never kills a progressing call, priority/data-only `BuildMessage`, APNs deletes only confirmed-invalid routes.
New coverage added: answer just before the ring deadline stays `Accepted` and refuses the stale expiry; late connected report after terminal is refused (no revival, sequence frozen); `/active` returns null after terminal; new `clientRequestId` after terminal starts a fresh operation; TURN credentials go only to the scoped participant (username embeds the requester id), foreign account 404, terminal call 409; FCM `Unavailable` retries without disabling the route; TTL never outlives the ring deadline; every transport reason code has a dedicated assertion.
Concurrency: `PostgreSqlVoiceCallConcurrencyTests` gained a real-PostgreSQL race where two simultaneous registrations of the same installation converge to a final route that is active, owned and installation-matched (the commit that lands last owns the binding; the test asserts committed state, not capture order).

## Verification results

Final runs after all tasks **and** the R1–R4 review fixes (same machine, source rebuilt each run):

| Suite | Filter | Result |
| --- | --- | --- |
| UnitTests | `VoiceCalls\|CustomerDeviceTokenRegistrationTests\|DriverDeviceTokenTests` | **183/183 passed** (baseline 140/140) |
| ApiTests | `FullyQualifiedName~VoiceCall` (full voice surface incl. delivery-contact workflows) | **123/123 passed** (baseline VoiceCallV2 filter: 39/39) |
| IntegrationTests | `FullyQualifiedName~VoiceCall` | **17/17 passed** (incl. the new PostgreSQL interleaving-rejection test) |
| Solution build | `dotnet build MealMate.sln` | 0 errors |
| `git diff --check` (voice scope) | exit 0 (LF/CRLF warnings only) | clean |

Migrations were generated with `dotnet ef` and reviewed; **neither was applied to any hosted/production database** (the generated artifacts live under `backend/artifacts/`). `git diff --check` and `git diff --stat` outputs were reviewed source-only.

## Configuration requirements
- None new. All added behavior is schema + code; no new options, connection strings or secrets. `VoiceCalls:V2Enabled` must remain explicitly enabled in staging (unchanged requirement).

## Localization check
- No new product-owned user-facing copy was introduced. Added codes (`VoiceCallPushReasons`) are technical identifiers and logs are developer-facing — per the workspace i18n rule these stay untranslated, and no `.resx` changes were required. Existing localized API messages were not modified.

## What was NOT tested (honest limits)
- No physical-device, audio/RTP, CallKit/PushKit, coturn relay, or APNs/Firebase production-credential testing. REST/SignalR-level tests do not prove audible voice.
- Live E2E against the deployed environment was not performed (out of plan scope; no deploy).
- The two concurrent-registration race semantics were verified against disposable PostgreSQL testcontainers only, not against the hosted dev database.
- The idempotent SQL scripts were reviewed but not executed against any database.

## Mobile still owns (backend cannot fix these one-sided)
The mobile clients still need: `callId`/epoch/sequence guards around every async operation and cleanup, suppression of stale native ended events, background deadline handling that does not trust the device clock, and ICE failure recovery/requeue. The backend changes here reduce stale-event arrival and make suppression auditable, but cannot correct client-side state machines.

## Files touched (source)
- `src/MealMate.Application/Features/VoiceCalls/Abstractions/IVoiceCallV2Transport.cs` — `ReasonCode`, `VoiceCallPushReasons`
- `src/MealMate.Infrastructure/VoiceCalls/DeviceVoiceCallV2PushTransport.cs` — per-path reasons, inactive-vs-missing distinction
- `src/MealMate.Domain/VoiceCalls/VoiceCallEventDelivery.cs` — `ReasonCode` + `MarkSuppressed(reason)`
- `src/MealMate.Persistence/Configurations/VoiceCalls/VoiceCallV2SchemaConfigurations.cs` — column length
- `src/MealMate.Application/Features/VoiceCalls/Services/VoiceCallOutboxHandler.cs` — revision guard, reason persistence, safe failure logs
- `src/MealMate.Application/Features/VoiceCalls/Services/VoiceCallPushRouteRebinder.cs` (new; includes the registration gate)
- `src/MealMate.Application/Features/CustomerPush/CustomerDeviceTokenHandlers.cs`, `src/MealMate.Application/Features/DriverDelivery/DriverApp/DriverNotificationCommands.cs` — atomic rebind with per-installation gate, xmin retry, explicit-request-deviceId-only rebinding, driver same-installation supersession
- `src/MealMate.Persistence/Configurations/IdentityAccess/IdentityAccessConfigurations.cs` — xmin shadow concurrency tokens for both token registries
- `src/MealMate.Domain/VoiceCalls/CallSession.cs` — `RecordTerminalRequester`, `EndedByDeviceSessionId`
- `src/MealMate.Application/Features/VoiceCalls/V2/Commands/VoiceCallV2LifecycleCommands.cs` — requester recording + audit log
- Migrations: `20261011004452_AddVoiceCallDeliveryDiagnostics`, `20261011010814_AddVoiceCallTerminalRequesterAudit`, `AddPushTokenConcurrencyTokens` (intentionally empty — system column), (+ Designer/snapshot)
- Tests: `VoiceCallV2PushTransportTests`, `VoiceCallV2DeliveryTests`, `CustomerDeviceTokenRegistrationTests`, `DriverDeviceTokenTests`, `CallSessionDomainTests`, `VoiceCallDeviceSessionApiTests`, `VoiceCallV2HangupApiTests`, `VoiceCallV2LifecycleApiTests`, `VoiceCallV2ExpiryApiTests`, `PostgreSqlVoiceCallConcurrencyTests`
- Docs: `docs/voice-calling/v2-integration-handoff.md` (extended, nothing removed), this file

Mock Data Check:
- No mock business data introduced.
- No fake fallback responses introduced.
- No hardcoded production business values introduced.
- All runtime data comes from real application sources.
