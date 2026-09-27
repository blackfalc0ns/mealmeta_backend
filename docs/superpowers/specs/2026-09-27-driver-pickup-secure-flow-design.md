# Secure Driver Pickup Flow Design

**Date:** 2026-09-27  
**Scope:** Driver mobile screens 06.03, 06.04, and 06.05 and their backend contracts

## Goal

Make pickup a secure, resumable, and idempotent flow in which barcode validation, the uploaded condition photo, pickup confirmation, the pickup summary, and trip start all refer to the same authenticated driver, trip, and box.

## Current-State Findings

- Barcode validation is read-only and correctly leaves an uncollected box as `PendingScan`, but it returns no proof that the later confirmation relates to that validation.
- The existing pickup command accepts a new barcode value and a caller-provided storage key, so the confirmation is not cryptographically or persistently tied to the earlier validation or upload.
- The first box pickup currently changes the trip to `InProgress`; trip start must happen only through the explicit start action after all boxes are received.
- The generic multipart upload API can persist files through `IFileStorage`, but it does not establish ownership of a pickup photo by a particular driver, trip, box, or validation.
- The manifest can expose pickup progress, but there is no purpose-built resumable summary contract for screen 06.05.
- Driver pickup endpoints currently permit anonymous access and accept an optional `driverId`; the production flow must derive the driver from the authenticated user.

## Chosen Architecture

Use a database-backed pickup workflow and the existing server-side `IFileStorage` abstraction.

1. Barcode validation creates a durable audit record and issues an opaque, cryptographically random token. Only a SHA-256 hash of the token is stored. The token expires after five minutes and is scoped to one authenticated driver, one trip, and one box.
2. A box-specific multipart endpoint accepts the condition image and validation token. The backend validates the token, assignment, image type, image signature, and size before saving the image. It records the resulting real storage key as belonging to that validation and box.
3. Pickup confirmation accepts the same token and recorded storage key. In one database transaction, it validates all bindings, marks the token consumed, inserts the final `PickupScan`, updates the box to `PickedUp`, and returns authoritative trip counters and navigation guidance.
4. Pickup summary is rebuilt from persisted trip, stop, validation, scan, and customer data so closing and reopening the app does not lose progress.
5. Trip start is a separate trip-scoped, idempotent operation. It is the only pickup-flow operation that transitions the trip to `InProgress`.

Presigned upload is intentionally not introduced in this iteration because the storage abstraction supports server-side streaming consistently across its configured providers but has no provider-neutral presigned-write contract. The public API contract isolates upload from confirmation so the transport can later change to presigned upload without changing pickup semantics.

## State Model

### Box state

- Before successful confirmation: the persisted `TripStop.Status` remains `Pending`.
- After successful confirmation: `TripStop.Status` becomes `PickedUp`.
- Barcode validation and photo upload are workflow milestones, not `TripStopStatus` values.

The API projects a pickup status for presentation:

- `PendingScan`: no successful validation exists.
- `BarcodeValidated`: a successful validation exists, but no associated uploaded condition photo exists.
- `PhotoUploaded`: an associated photo exists, but pickup has not been confirmed.
- `PickedUp`: the box was confirmed and a final `PickupScan` exists.

Validation history remains available for audit and `validatedBoxesCount`; token expiry affects authorization to continue, not the historical fact that validation occurred.

### Trip state

- Confirming one or more boxes does not start the trip.
- The trip may start only when every assigned stop is `PickedUp` and the trip is in an allowed pre-start state.
- A repeated successful start request returns the original successful result and does not mutate counts or timestamps.
- Normal trip start has no `forceStartWithUnscannedBoxes` field. Any future force-start process must be a separate privileged workflow with a required reason and audit trail.

## Persistence Model

### `PickupValidation`

- `Id: Guid`
- `TripId: Guid`
- `BoxId: Guid`
- `DriverProfileId: Guid`
- `BarcodeValue: string`
- `TokenHash: string` — unique SHA-256 digest; the raw token is never persisted
- `ValidatedAtUtc: DateTime`
- `ExpiresAtUtc: DateTime`
- `ConsumedAtUtc: DateTime?`
- `IsManualEntry: bool`
- `DriverAssignmentId: Guid` — the exact active assignment present when validation succeeded; a different active assignment invalidates the token

Indexes cover the unique token hash and `(TripId, BoxId, DriverProfileId, ValidatedAtUtc)`. Issuing a new token does not delete validation history. A box is counted as validated when at least one successful `PickupValidation` exists for the current trip assignment.

### `PickupConditionPhoto`

- `Id: Guid`
- `PickupValidationId: Guid`
- `TripId: Guid`
- `BoxId: Guid`
- `DriverProfileId: Guid`
- `StorageKey: string` — unique and returned by the real storage provider
- `ContentType: string`
- `SizeBytes: long`
- `Checksum: string?`
- `UploadedAtUtc: DateTime`
- `ConfirmedAtUtc: DateTime?`
- `DeletedAtUtc: DateTime?`

Only one active photo is allowed per validation. Replacing a pre-confirmation photo deletes the previous object only after the replacement has been stored and recorded successfully. An unconfirmed photo older than 24 hours is an orphan; a scheduled cleanup deletes its storage object and marks its record deleted. Cleanup failures remain observable and retryable.

### `IdempotencyRecord`

- `Id: Guid`
- `DriverProfileId: Guid`
- `Operation: string` — `PickupConfirm` or `TripStart`
- `IdempotencyKey: string`
- `RequestHash: string`
- `HttpStatusCode: int`
- `ResponseJson: string`
- `CreatedAtUtc: DateTime`
- `ExpiresAtUtc: DateTime`

A unique index on `(DriverProfileId, Operation, IdempotencyKey)` serializes retries. Reusing a key with a different request hash returns `IDEMPOTENCY_KEY_REUSED`. Successful responses are retained for 24 hours. The handler returns the stored response for a matching retry.

## API Contracts

All endpoints below require an authenticated driver. Production handlers resolve `DriverProfileId` from `ICurrentUser`; they do not accept `driverId` from the request. Development and automated tests provide an authenticated test identity rather than bypassing authorization.

### 1. Validate barcode

`POST /api/v1/driver/pickup/validate-barcode`

Request:

```json
{
  "barcodeValue": "480K-1234",
  "isManualEntry": false
}
```

Response:

```json
{
  "isValid": true,
  "validationToken": "opaque-random-token",
  "expiresAtUtc": "2026-09-27T08:05:00Z",
  "box": {
    "boxId": "00000000-0000-0000-0000-000000000000",
    "boxCode": "480K-1234",
    "customerName": "محمد علي",
    "deliveryZone": "حي الروضة",
    "mealsCount": 3
  },
  "scanStatus": "BarcodeValidated",
  "nextAction": "CaptureConditionPhoto"
}
```

If the box is already picked up, the endpoint returns `BOX_ALREADY_PICKED_UP` rather than issuing another token.

### 2. Upload condition photo

`POST /api/v1/driver/pickup/boxes/{boxId}/condition-photo`

Content type: `multipart/form-data`

- `file`: required image
- `validationToken`: required opaque token

Rules:

- Maximum encoded file size: 10 MiB.
- Allowed formats: JPEG, PNG, and WebP.
- The server checks both declared content type and file signature; changing an extension is insufficient.
- The mobile client should compress before upload. The server does not recompress because silent transformation would alter evidence and increase CPU/memory risk.
- Storage folder: `uploads/drivers/delivery/pickup-condition/{tripId}/{boxId}`.

Response:

```json
{
  "boxId": "00000000-0000-0000-0000-000000000000",
  "storageKey": "uploads/drivers/delivery/pickup-condition/...",
  "contentType": "image/jpeg",
  "sizeBytes": 245812,
  "uploadedAtUtc": "2026-09-27T08:02:00Z",
  "scanStatus": "PhotoUploaded",
  "nextAction": "ConfirmPickup"
}
```

### 3. Confirm pickup

`POST /api/v1/driver/pickup/boxes/{boxId}/confirm`

Required header: `Idempotency-Key: <UUID>`

Request:

```json
{
  "validationToken": "opaque-random-token",
  "conditionPhotoStorageKey": "uploads/drivers/delivery/pickup-condition/...",
  "latitude": 29.3375,
  "longitude": 48.0280
}
```

Response:

```json
{
  "tripId": "00000000-0000-0000-0000-000000000000",
  "boxId": "00000000-0000-0000-0000-000000000000",
  "boxCode": "480K-1234",
  "status": "PickedUp",
  "pickedUpAtUtc": "2026-09-27T08:03:00Z",
  "assignedBoxesCount": 8,
  "validatedBoxesCount": 8,
  "receivedBoxesCount": 8,
  "remainingBoxesCount": 0,
  "allBoxesReceived": true,
  "nextAction": "ShowPickupSummary"
}
```

For a non-final box, `allBoxesReceived` is false and `nextAction` is `ShowBoxSuccess`.

Confirmation requires coordinates. It verifies token hash, expiry, non-consumption, authenticated driver, trip, box, unchanged active assignment, uploaded-photo record, storage key equality, and unconfirmed box state. It does not accept a barcode value.

### 4. Get pickup summary

`GET /api/v1/driver/trips/{tripId}/pickup-summary`

Response:

```json
{
  "tripId": "00000000-0000-0000-0000-000000000000",
  "tripCode": "48-8752",
  "tripStatus": "Assigned",
  "pickupStatus": "ReadyToStart",
  "assignedBoxesCount": 8,
  "validatedBoxesCount": 8,
  "receivedBoxesCount": 8,
  "remainingBoxesCount": 0,
  "allBoxesReceived": true,
  "canStartTrip": true,
  "boxes": [
    {
      "boxId": "00000000-0000-0000-0000-000000000000",
      "boxCode": "480K-1234",
      "customerName": "محمد علي",
      "status": "PickedUp",
      "validatedAtUtc": "2026-09-27T08:00:00Z",
      "pickedUpAtUtc": "2026-09-27T08:03:00Z",
      "conditionPhotoUrl": "/api/v1/uploads/content?key=..."
    }
  ]
}
```

`conditionPhotoUrl` is derived from the real stored key through the configured storage provider or the existing guarded content endpoint. No sample values or fabricated fallbacks are returned.

### 5. Start trip

`POST /api/v1/driver/trips/{tripId}/start`

Required header: `Idempotency-Key: <UUID>`

Request:

```json
{
  "latitude": 29.3375,
  "longitude": 48.0280
}
```

The backend verifies ownership, active assignment, allowed trip state, required location, every assigned box being `PickedUp`, and absence of a conflicting start. On success it sets `Trip.Status = InProgress` and writes `StartedAtUtc` exactly once.

The legacy actions `POST /api/v1/driver/orders/{boxId}/pickup` and `POST /api/v1/driver/pickup/start-trip` are removed from their controllers in the same release as the new mobile contract. There is no compatibility route that can bypass the token, photo ownership, or idempotency rules.

## Error Contract

Errors use the existing API problem envelope while exposing a stable uppercase `code`, localized `message`, and optional structured `details`.

Barcode validation:

- `BARCODE_REQUIRED`
- `BARCODE_NOT_FOUND`
- `BOX_NOT_ASSIGNED_TO_DRIVER`
- `BOX_NOT_IN_ACTIVE_TRIP`
- `BOX_ALREADY_PICKED_UP`
- `TRIP_NOT_AT_KITCHEN`
- `VALIDATION_RATE_LIMITED`

Photo upload:

- `VALIDATION_TOKEN_INVALID`
- `VALIDATION_TOKEN_EXPIRED`
- `BOX_ASSIGNMENT_CHANGED`
- `PHOTO_REQUIRED`
- `PHOTO_TOO_LARGE`
- `PHOTO_INVALID_TYPE`
- `PHOTO_INVALID_SIGNATURE`
- `PHOTO_STORAGE_FAILED`

Pickup confirmation:

- `IDEMPOTENCY_KEY_REQUIRED`
- `IDEMPOTENCY_KEY_REUSED`
- `VALIDATION_TOKEN_INVALID`
- `VALIDATION_TOKEN_EXPIRED`
- `VALIDATION_TOKEN_CONSUMED`
- `PHOTO_REQUIRED`
- `PHOTO_NOT_UPLOADED`
- `PHOTO_INVALID`
- `LOCATION_REQUIRED`
- `BOX_ALREADY_PICKED_UP`
- `BOX_ASSIGNMENT_CHANGED`

Trip start:

- `IDEMPOTENCY_KEY_REQUIRED`
- `IDEMPOTENCY_KEY_REUSED`
- `UNSCANNED_BOXES_EXIST`
- `TRIP_ALREADY_STARTED`
- `TRIP_NOT_READY`
- `NO_ACTIVE_TRIP`
- `LOCATION_REQUIRED`
- `DRIVER_NOT_AUTHORIZED`

The 15-second scan timeout remains a mobile-only concern and has no API error.

## Consistency and Concurrency

- Validation token generation uses a cryptographically secure random source. Token comparison uses the SHA-256 digest and constant-time comparison where raw digest bytes are compared.
- Confirmation runs inside a database transaction. A uniqueness constraint prevents more than one final pickup scan for the same trip box.
- The validation and stop rows participate in optimistic or explicit concurrency checks so two simultaneous confirmations cannot increment counts twice.
- Counters are queried from persisted rows after mutation; they are not incremented from client input.
- Idempotency responses are written in the same transaction as the business mutation.
- Realtime notifications are emitted only after the transaction succeeds. A retry returns the saved response without emitting a duplicate business notification.

## Security

- Replace `[AllowAnonymous]` on pickup endpoints with driver authorization.
- Ignore and remove public `driverId` query parameters in production contracts.
- Every operation verifies the trip belongs to the authenticated driver's active assignment.
- Storage keys are never trusted by prefix alone; the corresponding `PickupConditionPhoto` ownership row must exist and match the validation, box, trip, and driver.
- Rate limit barcode validation by authenticated driver and route. The initial policy is 30 attempts per minute, with standard `Retry-After` behavior.
- Logs and audit records must never contain raw validation tokens.

## Orphan Photo Handling

If upload succeeds but confirmation fails, the uploaded photo remains attached to its validation for retry until token expiry. The driver may revalidate the same box and upload a replacement. A scheduled cleanup processes unconfirmed photos older than 24 hours:

1. Attempt deletion through `IFileStorage.DeleteAsync`.
2. On success, set `DeletedAtUtc`.
3. On failure, retain the row, log the dependency failure, and retry on a later run.

Confirmed evidence photos are not deleted by this cleanup.

## Testing Strategy

### Unit tests

- Validation issues a five-minute token, stores only its hash, and records the correct driver/trip/box.
- Invalid, foreign, already-picked-up, and unassigned barcodes return stable codes.
- Upload rejects an invalid/expired/foreign token, oversized files, wrong MIME types, and mismatched file signatures.
- Confirm rejects token/box/photo/assignment mismatches and missing location.
- Confirm consumes a token once, creates one `PickupScan`, leaves the trip unstarted, and returns correct counters and `nextAction`.
- Repeating confirm or start with the same idempotency key and request returns the exact prior response; a changed request with the same key fails.
- Summary derives all counts and box rows from persisted data.
- Start refuses incomplete pickup and changes trip state/timestamp only once after completion.
- Orphan cleanup deletes only expired, unconfirmed photos and retries storage failures.

### Integration tests

- Authenticated end-to-end flow: validate → upload multipart image → confirm all boxes → fetch summary → start trip.
- Authorization isolation between two drivers.
- Concurrent duplicate confirmation produces one scan and one received count.
- Database constraints and migration apply successfully against PostgreSQL.
- Real configured test storage is used; no in-memory production fallback is introduced.

## Compatibility and Documentation

- Update mobile screen contracts 06.03, 06.04, and 06.05 to the new routes, request bodies, response bodies, state meanings, and error codes.
- Update screen 06.02/06.06 manifest terminology so `Scanned` is not used as a synonym for `PickedUp`.
- Update OpenAPI annotations and endpoint examples.
- Existing unrelated driver delivery endpoints remain unchanged.

## Non-Goals

- Implementing a privileged force-start workflow.
- Adding presigned upload support to all storage providers.
- Changing customer delivery confirmation or proof-of-delivery flows.
- Fabricating pickup, customer, photo, or summary data when persistence is empty or unavailable.
