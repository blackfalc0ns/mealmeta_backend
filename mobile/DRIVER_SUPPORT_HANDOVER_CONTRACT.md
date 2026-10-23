# Driver support and persisted custody handover contract

All routes below are under `/api/v1`. Send the bearer token and `Accept-Language: en` or `ar`. Driver identity and dispatcher restaurant scope come from authenticated claims and persisted account/profile state. `driverId` query values and client-supplied restaurant IDs never grant access. Errors are RFC 7807 Problem Details with a stable `code` extension; statuses are 400 validation, 401 unauthenticated, 403 out of scope, 404 unavailable resource, and 409 lifecycle/custody conflict.

## Reassignment request recovery and explicit dispatch decision (before handover)

The existing `POST /driver/orders/{boxId}/request-reassignment` persists the request and returns its `requestId`. Recover a lost POST response with `GET /driver/reassignment-requests?boxId={tripStopId}&page=1&pageSize=20`. Omit `boxId` to list all requests of the authenticated reporting driver, newest first; page size is capped at 100. The response is a bare array, including historical decisions. A duplicate send still returns the existing active-request conflict; follow it with GET rather than treating it as local success. An in-flight POST may not yet be visible to GET, so re-read after its duplicate conflict.

`GET /driver/reassignment-requests/{requestId}` reads the original reporter's request even after custody moves. A replacement driver uses the handover APIs instead. Dispatcher detail: `GET /dispatcher/reassignment-requests/{requestId}` under restaurant scope.

```json
{
  "requestId": "<guid>", "boxId": "<trip-stop-guid>", "boxCode": "<code>",
  "status": "ReassignmentRequested", "reason": "VehicleBreakdown", "notes": null,
  "requestedAtUtc": "2026-10-20T10:00:00Z", "decision": null, "decisionNotes": null,
  "decidedByUserId": null, "decidedAtUtc": null, "handoverId": null, "handoverStatus": null,
  "replacementDriverId": null, "replacementTripId": null, "latitude": null, "longitude": null
}
```

`status` is `ReassignmentRequested`, `Accepted`, `Rejected`, or `Closed`. `decision` is nullable `Accepted`/`Rejected`, separate from `handoverStatus`. Legacy rows may have null reason, notes, boxId/code or decision evidence; no missing decision is fabricated. An existing legacy handover proves acceptance but does not invent a decision actor or timestamp. `/resolve` rejects an undecided request with no handover; an accepted request may be resolved without a handover when replacement is no longer required, returning `Closed` while retaining `decision: Accepted`.

- `POST /dispatcher/reassignment-requests/{requestId}/accept`: `{ "notes": "optional, max 500" }`.
- `POST /dispatcher/reassignment-requests/{requestId}/reject`: `{ "notes": "required, 3–500 characters" }`.

Both return the request DTO. Acceptance alone preserves custody and creates no handover. A successful existing assign/reassign atomically accepts an undecided request for compatibility. Rejection closes the complaint and prevents handover creation; it does not move a box. The same decision and trimmed notes replay safely; differing evidence conflicts. `/resolve` cannot substitute for deciding a request with no handover. Decisions emit persisted driver notifications and the existing scoped issue event with `eventName` `reassignment-accepted`/`reassignment-rejected`; refresh request details after the event or reconnect.

Errors: `REASSIGNMENT_FORBIDDEN` (403), `REASSIGNMENT_NOT_FOUND` (404), `REASSIGNMENT_DECISION_INVALID` (400), `REASSIGNMENT_ALREADY_DECIDED`, `REASSIGNMENT_ALREADY_CLOSED`, `REASSIGNMENT_HANDOVER_EXISTS`, `REASSIGNMENT_STOP_UNAVAILABLE`, `REASSIGNMENT_CUSTODY_CHANGED`, `REASSIGNMENT_DECISION_REQUIRED`, `REASSIGNMENT_REJECTED` (409).

Sending reassignment requires a `PickedUp` stop, active original assignment/account, nonterminal trip, and no active handover or unresolved hold. General issue reporting requires active ownership and shared guards but has no identical PickedUp-only restriction. `RoadIssue` is a final report-issue type, not an additional reassignment-reason value. Apply `AddDriverReassignmentDecisions` before serving these APIs; a Git push is not confirmation of the deployed server version.

## Handover lifecycle and custody

A handover moves through `Assigned → Arrived → Receiving → Completed`, or to `Cancelled` before any receipt evidence is recorded. Pending assignment does not change the original trip, active assignment, or physical custody. Arrival stores the replacement driver's coordinate pair and timestamp; no proximity threshold is asserted because none is configured. Pickup location uses one complete valid pair from the complaint or a single persisted tracking point, and includes its source and evidence time.

The replacement must record every manifest item as `Received`, `Missing`, or `Damaged` before completion. Barcode scans must match a persisted, non-revoked barcode on exactly one manifest box. `ManualCode` must exactly match one persisted manifest code. Repeating the same recorded outcome is safe; attempting to change it conflicts. Each item exposes `recordedAtUtc`; `receivedAtUtc` is set only for `Received`.

Completion transfers only `Received` boxes to a replacement trip. The original trip remains the audit record, and existing delivered/returned history is preserved. `Missing` and `Damaged` boxes remain with their original trip and receive linked active delivery holds for dispatcher follow-up. They are not deliverable on the replacement trip, and cancellation after any receipt evidence is rejected. A completed handover can have no replacement trip when there were no received boxes.

For a confirmed physical return, a dispatcher can close the linked follow-up with `POST /api/v1/dispatcher/support-handovers/{handoverId}/boxes/{tripStopId}/follow-up/return-to-kitchen` and `{ "manifestCode": "BOX-123", "resolutionNote": "Kitchen staff confirmed receipt of the sealed box" }`. This requires a completed handover, a pending `Missing` or `Damaged` follow-up, an exact unique manifest code or active persisted barcode, and a 5–500 character note. It records the resolver and audit event, resolves the hold, and marks that stop `ReturnedToKitchen`; it does not change the immutable receipt outcome. The same code and note may be replayed safely. A different payload receives `FOLLOW_UP_ALREADY_RESOLVED`. The issue cannot be resolved while any of its handover follow-ups remain open.

## Driver support reporting

All routes require a driver token and the persisted caller's active account/profile and assigned trip ownership. Optional `driverId` is accepted only if it matches the authenticated driver.

- `POST /driver/orders/{tripStopId}/report-issue` accepts `issueType`, optional `description`, `severity`, `evidencePhotoStorageKey`, `evidencePhotoStorageKeys` (up to three distinct persisted photo keys), and optional `idempotencyKey`. Both keyed and unkeyed success responses remain `200` with the persisted issue ID as a bare JSON GUID. A supplied key is trimmed, must contain 1–100 characters, and is scoped to the authenticated driver and this operation. Exact replay returns the original complaint ID without another complaint or event; reusing the key with a changed normalized payload or a different stop returns 409. Empty/overlength keys return 400. Use a key for retry recovery after an uncertain result; an unkeyed request cannot safely recover that result. A keyed replay requires the same active owning driver but can recover a committed result after custody changes. New reports still require active account/profile, assigned stop ownership, and all delivery custody checks. A missing customer subscription link returns 404 without saving the report; a valid legacy `DailyOrderId` → daily order → subscription day → subscription link is supported when the stop has no day ID.
- `POST /driver/orders/{tripStopId}/report-delay` accepts `{ "delayMinutes": 25, "reason": "Road works" }`. Delay must be positive and bounded by validation; this creates a persisted `SevereDelay` support complaint visible to dispatch.
- `POST /driver/orders/{tripStopId}/request-reassignment` accepts `{ "reason": "VehicleBreakdown", "notes": "...", "latitude": 29.3, "longitude": 47.9 }`; notes and coordinates are optional (coordinates must be both present and in range). It records the request without changing current custody. A missing customer subscription link returns 404 without writes; the valid legacy daily-order link described above is also accepted.

## Dispatcher support issue routes

These routes require `DispatcherPolicy` and enforce the authenticated user's restaurant scope. A global admin may omit restaurant scope only where its policy permits it.

- `GET /dispatcher/support/issues?restaurantId={id}&status=Open&area={stable-area-key}&search={text}&datePreset={preset}&fromDateUtc={utc}&toDateUtc={utc}&pageNumber=1&pageSize=20` returns counters, localized area chips, issue cards, and pagination. Omit filters you do not use.
- `GET /dispatcher/support/issues/{issueId}` returns the scoped issue details and evidence.
- `GET /dispatcher/support/issues/{issueId}/candidates?pageNumber=1&pageSize=20` returns eligible persisted driver profiles and actual available metrics; unavailable distance/rating/location values are `null`.
- `POST /dispatcher/support/issues/{issueId}/reassign` accepts `{ "replacementDriverId": "<driver-profile-guid>", "notes": "optional" }`. The legacy route returns its compatible response plus `handoverId` and `handoverStatus`; it starts a handover and does not transfer custody immediately.
- `POST /dispatcher/support/issues/{issueId}/resolve` accepts `{ "resolutionNotes": "..." }`. It rejects unresolved active handovers and returns the persisted resolver display name or `null`.

## Handover routes

Dispatcher routes require `DispatcherPolicy`; driver routes require `DriverPolicy`. A driver can read a handover only as its original or replacement participant. Dispatchers can read only within their authenticated restaurant scope.

- `GET /dispatcher/support-handovers?status={status}&restaurantId={id}&page=1&pageSize=20` lists scoped handovers. `status` is optional.
- `GET /dispatcher/support-handovers/{handoverId}` returns detail.
- `POST /dispatcher/support-handovers/issues/{issueId}/assign` accepts `{ "replacementDriverId": "<driver-profile-guid>", "idempotencyKey": "stable-client-key", "notes": "optional" }` and returns the created/current handover.
- `POST /dispatcher/support-handovers/{handoverId}/cancel` accepts `{ "reason": "optional, maximum 500 characters" }`; cancellation succeeds only before receipt evidence.
- `POST /dispatcher/support-handovers/{handoverId}/boxes/{tripStopId}/follow-up/return-to-kitchen` accepts `{ "manifestCode": "BOX-123", "resolutionNote": "Kitchen staff confirmed receipt of the sealed box" }`. Use the original manifest code or the box's active persisted barcode. Resolution is available after handover completion for a `Missing` or `Damaged` box with an open persisted follow-up; it resolves the hold and records `ReturnedToKitchen` while preserving the receipt outcome and audit history. Repeating the same code and note is safe.
- `GET /driver/support-handovers?status=active` returns the authenticated driver's active handovers as original or replacement participant. Use it on cold start and reconnect; `status` may be omitted.
- `GET /driver/support-handovers/{handoverId}` returns handover detail, issue summary, trip code, actual participant names/phones when present, pickup source, and box evidence.
- `POST /driver/support-handovers/{handoverId}/arrival` accepts `{ "latitude": 29.3, "longitude": 47.9 }`.
- `POST /driver/support-handovers/{handoverId}/boxes/scan` accepts `{ "code": "BOX-123", "status": "Received", "method": "Barcode", "followUpNote": null }`. `status` is `Received`, `Missing`, or `Damaged`; `method` is `Barcode` or `ManualCode`. `followUpNote` is optional and limited to 500 characters for `Received`; `Missing` and `Damaged` require a non-empty note.
- `POST /driver/support-handovers/{handoverId}/complete` has no body and returns the final summary.
- `POST /driver/support-handovers/{handoverId}/cancel` has the same optional reason body and evidence restriction as dispatcher cancellation.

A handover response has this shape (nullable fields are shown as `null` when no persisted value exists):

```json
{
  "handoverId": "<guid>", "issueId": "<guid>", "tripId": "<original-trip-guid>",
  "originalDriverId": "<guid>", "replacementDriverId": "<guid>", "restaurantId": "<guid>",
  "status": "Receiving", "pickupLatitude": 29.3, "pickupLongitude": 47.9,
  "assignedAtUtc": "2026-10-20T10:00:00Z", "arrivedAtUtc": "2026-10-20T10:10:00Z",
  "completedAtUtc": null, "replacementTripId": null,
  "receivedCount": 1, "missingCount": 0, "damagedCount": 0, "pendingCount": 1,
  "pickupLocationSource": "Complaint", "pickupLocationRecordedAtUtc": "2026-10-20T09:55:00Z",
  "issueTitle": "Vehicle issue", "issueCategory": "VehicleBreakdown", "issueDescription": "...",
  "tripCode": "TRIP-123", "originalDriverName": "...", "originalDriverPhone": null,
  "replacementDriverName": "...", "replacementDriverPhone": null,
  "boxes": [
    { "tripStopId": "<guid>", "manifestCode": "BOX-123", "status": "Received",
      "receivedAtUtc": "2026-10-20T10:12:00Z", "recordedAtUtc": "2026-10-20T10:12:00Z",
      "followUpNote": null, "followUpId": null, "followUpStatus": null }
  ]
}
```

Example conflict response:

```json
{
  "type": "about:blank", "title": "Conflict", "status": 409,
  "detail": "The box already has a recorded outcome.",
  "instance": "/api/v1/driver/support-handovers/<guid>/boxes/scan",
  "code": "BOX_ALREADY_RECORDED"
}
```

Other stable failures include `DISPATCHER_SCOPE_REQUIRED`, `DISPATCHER_SCOPE_FORBIDDEN`, `HANDOVER_FORBIDDEN`, `HANDOVER_NOT_FOUND`, `HANDOVER_ALREADY_ACTIVE`, `REPLACEMENT_DRIVER_UNAVAILABLE`, `PICKUP_LOCATION_UNAVAILABLE`, `HANDOVER_ARRIVAL_REQUIRED`, `MANIFEST_BOX_NOT_FOUND`, `MANIFEST_CODE_AMBIGUOUS`, `BARCODE_INVALID`, `HANDOVER_RECEIPTS_INCOMPLETE`, `TERMINAL_BOX_CANNOT_TRANSFER`, `HANDOVER_RECEIPT_EXISTS`, `HANDOVER_PENDING`, and `IDEMPOTENCY_KEY_REUSED`. On a conflict or reconnect, refresh from the driver's authenticated list/detail endpoint before retrying.

## Reconnect events and recovery

Persisted outbox notifications publish scoped SignalR events to participating driver groups and the restaurant dispatcher group. Handover lifecycle/receipt event names are `support-handover-updated` (driver) and `driver-support-handover-updated` (dispatcher); support issue events are `driver-support-issue-updated` (driver) and `driver-issue-updated` (dispatcher). Payloads include event and handover/issue IDs, restaurant and participant IDs, status, and event time where applicable. Events are hints; after reconnect or a missed event, call `GET /driver/support-handovers?status=active` and then fetch detail. Dispatcher hub joins derive restaurant membership from authenticated claims; arbitrary joins are rejected.
