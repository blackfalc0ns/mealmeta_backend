# Driver support and persisted custody handover contract

All routes below are under `/api/v1`. Send the bearer token and `Accept-Language: en` or `ar`. Driver identity and dispatcher restaurant scope come from authenticated claims and persisted account/profile state. `driverId` query values and client-supplied restaurant IDs never grant access. Errors are RFC 7807 Problem Details with a stable `code` extension; statuses are 400 validation, 401 unauthenticated, 403 out of scope, 404 unavailable resource, and 409 lifecycle/custody conflict.

## Lifecycle and custody

A handover moves through `Assigned → Arrived → Receiving → Completed`, or to `Cancelled` before any receipt evidence is recorded. Pending assignment does not change the original trip, active assignment, or physical custody. Arrival stores the replacement driver's coordinate pair and timestamp; no proximity threshold is asserted because none is configured. Pickup location uses one complete valid pair from the complaint or a single persisted tracking point, and includes its source and evidence time.

The replacement must record every manifest item as `Received`, `Missing`, or `Damaged` before completion. Barcode scans must match a persisted, non-revoked barcode on exactly one manifest box. `ManualCode` must exactly match one persisted manifest code. Repeating the same recorded outcome is safe; attempting to change it conflicts. Each item exposes `recordedAtUtc`; `receivedAtUtc` is set only for `Received`.

Completion transfers only `Received` boxes to a replacement trip. The original trip remains the audit record, and existing delivered/returned history is preserved. `Missing` and `Damaged` boxes remain with their original trip and receive linked active delivery holds for dispatcher follow-up. They are not deliverable on the replacement trip, and cancellation after any receipt evidence is rejected. A completed handover can have no replacement trip when there were no received boxes.

For a confirmed physical return, a dispatcher can close the linked follow-up with `POST /api/v1/dispatcher/support-handovers/{handoverId}/boxes/{tripStopId}/follow-up/return-to-kitchen` and `{ "manifestCode": "BOX-123", "resolutionNote": "Kitchen staff confirmed receipt of the sealed box" }`. This requires a completed handover, a pending `Missing` or `Damaged` follow-up, an exact unique manifest code or active persisted barcode, and a 5–500 character note. It records the resolver and audit event, resolves the hold, and marks that stop `ReturnedToKitchen`; it does not change the immutable receipt outcome. The same code and note may be replayed safely. A different payload receives `FOLLOW_UP_ALREADY_RESOLVED`. The issue cannot be resolved while any of its handover follow-ups remain open.

## Driver support reporting

All routes require a driver token and the persisted caller's active account/profile and assigned trip ownership. Optional `driverId` is accepted only if it matches the authenticated driver.

- `POST /driver/orders/{tripStopId}/report-issue` accepts `issueType`, optional `description`, `severity`, `evidencePhotoStorageKey`, and `evidencePhotoStorageKeys` (up to three distinct persisted photo keys). Stable catalog values include `AddressIssue`, `SevereDelay`, `RoadIssue`, `VehicleBreakdown`, `TrafficAccident`, and `SevereWeather`. Returns the persisted issue ID as a JSON string.
- `POST /driver/orders/{tripStopId}/report-delay` accepts `{ "delayMinutes": 25, "reason": "Road works" }`. Delay must be positive and bounded by validation; this creates a persisted `SevereDelay` support complaint visible to dispatch.
- `POST /driver/orders/{tripStopId}/request-reassignment` accepts `{ "reason": "VehicleBreakdown", "notes": "...", "latitude": 29.3, "longitude": 47.9 }`; notes and coordinates are optional (coordinates must be both present and in range). It records the request without changing current custody.

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
