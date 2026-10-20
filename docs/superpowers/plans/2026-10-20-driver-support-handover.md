# Driver support and persisted handover implementation plan

## Scope
Implement persisted handover workflows for dispatcher-reported driver issues in MealMate backend. Preserve complaint reporter identity and trip custody until a replacement driver arrives and records receipt of real manifest boxes. Remove fabricated support candidate data and enforce real identity and restaurant scope.

## Sequence
1. Establish regression coverage: run the supplied handover unit test red; add focused behavior and contract tests for realistic support, ownership, cancellation, missing/damaged boxes, and concurrency.
2. Add handover/custody domain model, EF configuration and migration; expose DbSets and PostgreSQL lock methods for trip, handover, driver, and box mutation coordination.
3. Implement transactional application handlers for creating/listing/detailing handovers, arrival, exact manifest barcode/manual-code receipts, safe cancellation, completion, and support issue reporting categories. Integrate old reassignment and resolve paths so reassign creates a handover, unresolved handovers block resolve, and resolved-by data comes from authenticated persisted users.
4. Enforce scope and identity in support reads, candidate selection, and mutation APIs. Replace synthetic candidate values with persisted profile, assignment, registration, and authenticated complaint/tracking location data. Add controllers, localized errors, durable notifications, and realtime updates following existing infrastructure.
5. Publish complete mobile request/response/error/event/recovery contract and verify unit/API/PostgreSQL flows, existing pickup compatibility, migration/model consistency, changed-file policy scan, and diff scope.

## Acceptance criteria
- Support AddressIssue, SevereDelay, TrafficAccident, and vehicle/road issues with coherent localized categories.
- A handover persists complaint, trip, original and replacement drivers, pickup location from complaint or authentic tracking, and custody boxes.
- Assignment alone never transfers assignment or marks a stop picked up. Only Received manifest boxes become eligible for the replacement assignment after arrival and completion; Missing/Damaged remain explicit follow-up and are never silently deliverable.
- Every operation validates authenticated role, driver ownership, and restaurant scope. Duplicate commands/scans are safe. PostgreSQL locks and constraints serialize handovers and trip/driver/box mutation.
- Delivered/ReturnedToKitchen and other terminal box states are protected. Pending handovers prevent support resolution and competing reassignment.
- Cancellation preserves scan evidence and does not create fake success. Missing real pickup location blocks handover creation.
- Support queries return database-backed candidates with nullable unavailable data and no fabricated identities, locations, distances, ratings, areas, orders, or timestamps.
- Tests exercise real PostgreSQL locks and transaction behavior; InMemory is not used to claim concurrency safety.
- No unrelated checkout-test or outputs changes are modified or included.
