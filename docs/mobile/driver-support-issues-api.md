# Driver support issues API

All routes require the matching bearer-token policy. The API derives the driver's identity from the authenticated account and its persisted driver profile; callers cannot supply a driver ID.

## List

`GET /api/v1/driver/support/issues?status=All&search=&pageNumber=1&pageSize=20`

`status` accepts `All`, `UnderReview`, `AwaitingReply`, or `Resolved`. Search checks the persisted title, description, category, and a safely linked delivery order code. Page size is 1–100 and search is limited to 200 characters. The response includes overall `AllCount`, `UnderReviewCount`, `AwaitingReplyCount`, and `ResolvedCount`, plus `TotalCount` for the selected status/search and the requested page. Results sort by submission time descending and issue ID ascending.

Example response fields use the API's camelCase JSON naming:

```json
{
  "allCount": 1,
  "underReviewCount": 0,
  "awaitingReplyCount": 1,
  "resolvedCount": 0,
  "totalCount": 1,
  "pageNumber": 1,
  "pageSize": 20,
  "issues": [{
    "issueId": "00000000-0000-0000-0000-000000000000",
    "category": "VehicleBreakdown",
    "categoryLabel": "Vehicle breakdown",
    "title": "Driver issue",
    "status": "AwaitingReply",
    "statusLabel": "Awaiting reply",
    "submittedAtUtc": "2026-10-21T08:00:00Z",
    "orderCode": null
  }]
}
```

Only issues attached to the authenticated driver's profile and supported driver issue categories are returned. Reassignment requests use their dedicated category. Customer issues assigned to a driver are outside this list unless they use a supported driver issue category and profile association.

## Detail

`GET /api/v1/driver/support/issues/{issueId}`

The detail includes the original status enum, user-facing status/category labels, actual report description, evidence content URLs, safely linked order code/address/area/order creation time, and four timeline stages. The timeline marks dates only when a persisted event recorded them; legacy stages without audit records remain undated. Order fields are null when the associated stop is missing or ambiguous. Authenticated successful API responses sign private evidence URLs with a per-file `readToken` valid for 15 minutes, allowing image loading without an Authorization header. Preserve the complete URL; a fresh detail GET renews the link. Bare content URLs still require the uploader/staff session, and DTO field names remain unchanged. `CanContactSupport` is false and `ContactChannel` is null because no support chat channel is configured in the current implementation.

Status buckets group `Resolved`, `Closed`, and `Rejected` as `Resolved`. `Submitted` and legacy `AwaitingRestaurant`/`DecisionPending` are grouped under `AwaitingReply`; an explicitly audited `ContactingCustomer` stage is also `AwaitingReply`. `UnderReview` is separated unless a later explicit contact stage exists.

## Attach photos

1. Upload each image through the existing driver issue-evidence upload flow.
2. Call `POST /api/v1/driver/support/issues/{issueId}/photos` with `{ "storageKeys": ["..."] }`.

Keys must be owned by the current driver, stored in an accepted driver upload folder, use JPEG/PNG/WebP, and resolve in file storage. Repeating a key is idempotent. No more than three evidence photos may be attached to an unresolved issue. Mutations are locked and transactional; foreign or missing issues return 404, and invalid uploads, terminal issues, and photo limits return validation/conflict responses.

## Dispatcher progress

`POST /api/v1/dispatcher/support/issues/{issueId}/progress` with `{ "stage": "UnderReview", "notes": "..." }` or `{ "stage": "ContactingCustomer", "notes": "..." }`.

Delivery managers may update issues for their restaurant; admins may update any restaurant. Notes are optional and limited to 500 characters. Stages move from `Submitted` to `UnderReview` to `ContactingCustomer`; repeating the current stage is idempotent, and reversing or skipping a stage is rejected. Each change writes an audit event, a driver notification, and the existing `driver-support-issue-changed.v1` outbox event. This endpoint records progress only; it does not create a chat or claim that customer contact occurred outside the explicit dispatcher action.

The separate staff-only resolve route is `POST /api/v1/dispatcher/support/issues/{issueId}/resolve`; `resolutionNotes` is required and must contain 3–500 characters.

## Verification

The clean publication worktree passed the focused 11-test unit suite and a 101-test regression suite covering driver issue reassignment and dispatcher dashboard behavior; the API build passed with 0 warnings and 0 errors. The new HTTP tests compiled but could not run because Testcontainers could not connect to Docker (`DockerEndpointAuthConfig`). No deployment was performed.
