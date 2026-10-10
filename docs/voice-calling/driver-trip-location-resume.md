# Driver trip and call-screen resume

Backend changes (2026-10-10):

- `POST /api/v1/driver/location` now forwards the body `tripId` to the existing active-assignment ownership check. `driverId` from the body is ignored; driver identity comes from JWT.
- Both existing DriverHub GPS methods now persist against the connection's `tripId` query parameter. Invalid/empty/repeated trip selectors fail before persistence. A trip owned by another driver fails the existing handler check. Connections without a trip selector retain the existing latest-active-assignment behavior.
- Keep the existing six-argument `UpdateLocationV2(latitude, longitude, accuracyMeters, capturedAtUtc, heading, speedKmh)` signature. Connect using `/hubs/driver-hub?tripId=<currentTripId>`; the client must reconnect with the new selector when switching trips. REST is also available for an explicit trip update before contact eligibility.
- Starting an already InProgress trip with an active assignment returns HTTP 200 with the persisted start timestamp. It does not reset the trip or work-session count. A missing stored start timestamp still returns a conflict. Idempotency-key reuse with different request parameters remains a conflict. Cached success copy is rendered in the current request language.

Required mobile sequence:

1. On returning to the app or call screen, load the trip and contact case from the server. Preserve the server's missed-call count, hold and call state; do not reset these when rebuilding the screen.
2. Obtain a fresh GPS fix with the device's measured accuracy and real UTC capture time. Resume the GPS stream and ensure its trip selector matches the current box's trip.
3. Submit this fix through `UpdateLocationV2`, or `POST /api/v1/driver/location` with `tripId`, `latitude`, `longitude`, `accuracyMeters` and `capturedAtUtc`. Wait for success and verify the ACK `tripId` before checking eligibility.
4. Fetch `/api/v2/voice-calls/eligibility/<tripStopId>` and `/api/v2/delivery-contact/<tripStopId>/case`. These routes use **trip stop ID**, while GPS and trip start use **trip ID**.
5. If eligibility says `contact.location_stale`, request and submit a new measured fix before retrying. If it says `contact.location_accuracy_low`, wait for improved GPS. Handle GPS permission/errors and always clear loading state.
6. Start uses `POST /api/v1/driver/trips/<tripId>/start`, a JSON body containing `latitude` and `longitude`, and an `Idempotency-Key` header. Reuse a key only for the exact same logical request/body. Navigate using the returned InProgress state.

The server still rejects stale or inaccurate locations. Legacy `UpdateLocation` has no measured accuracy, so an ACK from that method cannot establish call eligibility. Do not invent accuracy values or re-stamp old coordinates as a fresh fix.

Verification: scoped API boundary, real EF command/eligibility, pickup/start and resource-language tests run locally. Flutter UI and deployed-host behavior require mobile changes and deployment; they are not verified by these backend tests.
