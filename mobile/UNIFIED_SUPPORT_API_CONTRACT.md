# Unified support API contract (v1)

This contract describes the implemented support API for mobile clients. All endpoints require the normal MealMate bearer session. The server derives the actor, role, restaurant, and scope from claims plus persisted account membership; clients must not send those as authorization facts. Base path: `/api/v1/support`.

## Cases

`GET /cases?pageNumber=1&pageSize=20&status=InProgress&kind=Complaint&category=delivery` returns the caller-visible page:

```json
{"items":[{"id":"<case-id>","channel":"Customer","kind":"Complaint","status":"WaitingOnParty","ownerTeam":"PlatformSupport","priority":null,"category":"delivery","subject":"Missing item","description":"One item was missing","requesterUserId":"<visible-to-authorized-parties>","restaurantId":"<visible-to-authorized-parties>","assignedToUserId":null,"version":12,"createdAtUtc":"2026-10-23T10:00:00Z","modifiedAtUtc":"2026-10-23T10:05:00Z","messages":[],"tasks":[],"links":[],"relatedResourceType":"RestaurantDailyOrder","relatedResourceId":"<resource-id>"}],"pageNumber":1,"pageSize":20,"totalCount":1}
```

The category is nullable in task-only projections, because a requester can enter private text there. Fields containing private party identity/resource details are null or omitted from task-only projections. A requester sees only their participant conversation and no private task records, outcomes, or recipient IDs. A restaurant or delivery-manager task recipient sees only the assigned task subject/question and that task's messages; the case subject/body, requester identity, links, and other channels are withheld. Internal messages are admin-only.

`POST /cases` accepts a platform request (server owns routing):

```json
{"channel":"Restaurant","kind":"GeneralRequest","category":"account","subject":"Need help","description":"Describe the issue","idempotencyKey":"client-generated-unique-key"}
```

Supported channels are `Customer`, `Driver`, `Restaurant`, and `DeliveryManager`; kinds are `Complaint`, `GeneralRequest`, and `OperationalReport`. Operational driver reports use the existing driver report route, not generic case creation. For supported customer resource types, include `relatedResourceType` and `relatedResourceId`; the server verifies ownership and derives restaurant association. A missing or unowned resource is rejected. Unlinked general support remains valid.

`GET /cases/{caseId}` returns the same case contract with messages/tasks/links filtered for the current actor. Use the current returned `version` for the next mutation.

An authorized case response may include `activeTimingCycle`. Its `firstResponseDeadlineUtc` and `resolutionDeadlineUtc` are the UTC deadlines frozen when that cycle started. `escalationDeadlineUtc` is the cycle start plus the matched rule's elapsed UTC escalation threshold; it is nullable for older cycles whose threshold was not recorded. First response is recorded only for a persisted platform operator's public case reply or public request-information message. Acknowledgments, internal notes, and private task replies do not count. Policy publication does not alter existing cycles; reopening creates a new cycle from the current policy.

Timing policy authoring is an Admin dashboard operation at `/admin/support/timing`, not a mobile endpoint. `GET /api/v1/support/timing-policies` lists history and `GET /api/v1/support/timing-policies/active` returns the active version; both require an active persisted Admin role (`SuperAdmin` or `SupportView`). `POST /api/v1/support/timing-policies/publish` requires active persisted `SuperAdmin`. The publish request uses `expectedVersion = max(history version) + 1` (version 1 only for an empty catalog) and supplies an explicit name, publication reason, and complete rule list. Mobile clients should display the server-returned cycle deadlines and must not invent duration defaults, recalculate policy values, or attempt policy publication. Current staging policy configuration and production publication are unknown and require their owners' approval.

## Conversation and actions

`POST /cases/{caseId}/messages` body:

```json
{"body":"Please check this","expectedVersion":12,"idempotencyKey":"new-key-for-this-operation","taskId":null}
```

`taskId` is omitted for participant conversation or is the server-issued task ID for an authorized task thread. Message visibility is derived by the server. Replies on Resolved/Closed cases are rejected until an explicit reopen. Replaying the same actor/key and same payload returns the persisted result; reusing the key for a different payload returns `support.idempotency_key_reused`.

Admin-only internal note: `POST /cases/{caseId}/internal-notes` with `{body, expectedVersion, idempotencyKey}`. It is never returned to customers, restaurants, drivers, or delivery managers.

`POST /cases/{caseId}/actions/{action}` uses `{expectedVersion,idempotencyKey,reason?,taskType?,recipientType?,restaurantId?,recipientUserId?,taskSubject?,taskQuestion?,dueAtUtc?,taskId?,taskResult?}`. Supported actions: `acknowledge`, `start`, `request-information`, `resolve`, `reopen`, `close`, `escalate`, `create-party-task`, `complete-party-task`, `cancel-party-task`. The URL action is authoritative: omit `action` from the body; a supplied value is overridden by the URL. Required fields depend on action; the server validates requester/manager permissions and task recipient membership. There is no generic status setter or auto-close policy. `resolve` requires required party work and linked operational work to be complete. `close` is explicit confirmation after Resolved. Requesters/admins can explicitly reopen terminal cases with a reason.

For admin task assignment, fetch `GET /cases/{caseId}/task-recipients` (requires `support.manage`). It returns current eligible delivery managers as `{userId,displayName}` records, restricted to the case restaurant. Assign restaurant review using `taskType: "RestaurantEvidence", recipientType: "Restaurant"`; assign delivery follow-up using `taskType: "DeliveryAction", recipientType: "DeliveryManager", recipientUserId`. Both can remain open independently. Cancellation is admin-only and requires `taskId` and `reason`; a canceled recipient loses access to that task and its attachments. A completed task retains readable history, permits exact replay of an already accepted message, and rejects new replies. Reassignment to another restaurant revokes the manager's old task access.

`POST /cases/{caseId}/links` accepts `{relation,resourceType,resourceId,expectedVersion,idempotencyKey}`. Currently supported source is `DriverComplaint`; the link does not grant access to the complaint. A source still in progress prevents case resolution.

## Existing driver operational issues

`GET /operational-issues?pageNumber=1&pageSize=20`, `GET /operational-issues/{issueId}`, `POST /operational-issues/{issueId}/escalate` (`{reason,idempotencyKey}`), `/progress` (`{stage,notes}`), and `/resolve` (`{resolutionNotes}`) are separate from general cases. They project the existing Complaint and retain its current dispatcher authorization, custody, reassignment, and handover checks. The delivery manager is the first owner where the existing persisted membership permits it; escalation records an event and does not duplicate the Complaint or alter its lifecycle. Driver operational progress/resolve commands remain authoritative.

Operational issue DTOs include `evidence: [{id,readPath}]`. Fetch `GET /operational-issues/{issueId}/evidence/{evidenceId}` with bearer authentication. It enforces the same current issue scope, verifies an owned upload record and stored image signature/size, and returns image bytes with `no-store` and `nosniff`; no storage key or public URL is exposed. Supported existing keys are relative driver issue-evidence/delivery uploads or HTTPS `ik.imagekit.io` paths. Legacy evidence without a valid upload record or another CDN host is unavailable through this proxy (404), never fetched from an arbitrary URL. This does not change the legacy upload visibility policy.

The older handover workflow remains documented in [DRIVER_SUPPORT_HANDOVER_CONTRACT.md](DRIVER_SUPPORT_HANDOVER_CONTRACT.md); support links do not replace its custody protocol.

## Errors, concurrency, reconnect

Use HTTP status plus the stable Problem Details `code` value. Relevant codes include `support.authentication_required` (401), `support.access_denied` (403), `support.case_not_found` (404), `support.request_invalid` (400), `support.transition_invalid` (409), `support.version_conflict` (409), `support.idempotency_key_reused` (409), `support.link_target_unsupported` (400), and `support.operational_route_required` (400). A hidden out-of-scope record can return 404.

Keep the exact request body and idempotency key after a timeout, disconnect, or 5xx, and replay it unchanged. For a definitive validation/authorization response, correct the request and use a new key. On version conflict, reload the case and ask the user to reconcile their draft before sending with a fresh key and version. Refresh after reconnect; the server's list/detail is authoritative. Do not infer success from a local optimistic status.

## Private attachments and financial truth

Upload each file before sending its message using authenticated multipart `POST /api/v1/support/attachments` with the file field named `file`. The server accepts up to 25 MiB per file, PNG/JPEG/WebP images, and PDF only, and checks extension, declared media type, and file signature. It returns `{ "storageKey": "documents/support/evidence/<opaque-name>", "fileName": "evidence.pdf", "contentType": "application/pdf", "sizeBytes": 1234 }`. This is an opaque upload key, not a read URL. The client may attach up to five keys to `POST /cases/{id}/messages` or `/internal-notes` using `attachmentKeys`; every key must belong to the authenticated uploader and use the support evidence scope. The message idempotency hash includes the ordered key list.

Persist attachment keys until the message succeeds or a definitive error is returned. After success, use the returned message `attachments` metadata; it includes safe name/type/size and a relative `readPath`, but no storage key. Fetch each `readPath` with bearer authentication. The endpoint checks current case and message channel visibility for every read; a canceled task or hidden/internal message is not readable to that party. Responses are non-cacheable and add `nosniff`; clients should render only returned image/PDF media types. A link copied from the generic upload endpoint is not a substitute. Support evidence is stored under the encrypted `documents/` path, and deployments must configure `Storage:DocumentEncryptionKey` as a base64 256-bit key.

Support status is not evidence that a refund, replacement day, or other financial action executed. Existing compensation records remain the source of truthful selection and execution states; this support API does not create or execute compensation.


## Notifications

The same transaction persists generic bilingual inbox notifications for public admin replies/information requests/resolution and a durable customer/driver support push request. Exact replay adds no duplicate inbox or push-outbox row. Internal text, customer descriptions, and private task content are never embedded in notification bodies. At dispatch, customer and driver support pushes recheck the active account, current requester/channel, current driver profile where applicable, and active nondeleted recipient device tokens. Missing tokens produce no provider call; invalid tokens are deactivated, while provider configuration/transient failures remain retryable through the outbox. Delivery is at-least-once, so clients should deduplicate by stable `eventId` (outbox message ID) and `notificationId` (inbox row ID). `caseId` is the support case route target; `subjectId` remains the support event ID. Push content uses the existing generic bilingual inbox copy and safe deep link, without case text or attachment details. This source behavior does not establish Firebase configuration, successful staging delivery, or mobile-client acceptance. Restaurant inbox records remain durable; delivery-manager notifications continue using the established push outbox. The platform operational escalation queue reads persisted scoped audit events.

Mobile case notifications use `/support/cases/{caseId}`; restaurant links use `/restaurant/support?caseId={caseId}`. Clients must load current authorized detail after opening a notification and handle revoked access. Integrate these routes in the authoritative mobile app repository before a mobile release; none is available in this checkout.
