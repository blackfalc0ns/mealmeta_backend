# Voice calls: token expiry and refresh

## New initiation response

`POST /api/v2/voice-calls/initiate` can return HTTP **409** with ProblemDetails
`code: "calls.auth_refresh_required"`. The validated access token is still
accepted by authentication, but has too little lifetime left for a new call's
ring/setup window. No new call, busy slots, push, operation receipt, or contact
attempt is created for this rejection.

The required lifetime is configured ring timeout + accepted setup timeout +
connecting timeout + 30 seconds safety (110 seconds with current defaults).
The API reads expiry from the signed JWT, never from mobile-supplied fields.
Already-expired tokens continue returning HTTP **401** through authentication.
This change does not accept expired tokens or change token lifetimes.

## Mobile changes required

1. Before initiating, refresh proactively if the access token is nearing expiry.
2. On `409 calls.auth_refresh_required`, or the first `401`, pause dependent
   requests and use a single shared refresh operation:
   `POST /api/v1/auth/refresh` with JSON `{ "refreshToken": "<stored refresh token>" }`.
   This endpoint permits an expired access token; use a separate client/interceptor
   path so refresh cannot recursively trigger itself.
3. Persist both replacement tokens atomically. Concurrent API calls must await
   the same refresh instead of rotating the refresh token independently.
4. Retry initiation with **the same clientRequestId** and the same owned voice
   device session/proof. Do not register a replacement voice session just because
   the access token was refreshed. Re-registration revokes the old session.
5. If initiation previously succeeded, or its response was lost, reconcile using
   `GET /api/v2/voice-calls/active` or replay the same initiation key. Resume the
   existing call instead of generating a new request ID. A valid-token replay of
   an existing operation returns its current snapshot without starting a new call.
6. SignalR reconnects must obtain the latest token and bind the existing owned
   voice device session again. Keep the ongoing call state during refresh.
7. Do not clear login on the first `401`, a refresh timeout, a network failure,
   HTTP 429, or a server error. Only a definitive invalid/expired/revoked refresh
   credential should prompt reauthentication. Bound retries: no infinite loop.
8. If abandoning a call after successful refresh, use the authenticated cancel
   endpoint while Ringing or the end endpoint after answer; do not leave a live
   call running in the background while returning to login.

Never print Authorization, refresh tokens, or X-Voice-Device-Proof in logs.

## Counting and limits

For v2 missed calls, qualification now additionally requires the bound caller
device session to belong to the caller and be unexpired/unrevoked at call end.
A failed check is recorded as a technical attempt, without increasing
qualifiedMissedCount. A session revoked or expired only after call end does not
invalidate a legitimate earlier miss. Technical calls can remain visible in
history; a history row is not a qualifying no-answer attempt.

JWT expiry and voice device-session expiry are separate. The backend cannot
infer local logout, app closure, or a disconnected caller merely from a still
valid device session. This patch prevents starting a new call with insufficient
JWT lifetime and excludes known invalid device sessions; completing the refresh
flow above is necessary to stop the mobile app returning to login. Existing
historical counts are not edited automatically.

## Acceptance checks

- Near-expiry initiation: 409 code above, no new call/attempt or incoming push.
- Refresh success: retained login, replacement tokens saved, one call for one key.
- Concurrent 401s: one refresh operation, no repeated token rotation.
- Lost initiation response: replay returns the original call ID.
- Expired JWT: 401 followed by refresh/reconciliation, no immediate forced logout.
- Definitive refresh failure: login prompt; no locally invented call/missed count.
- Caller device invalid before call end: technical attempt, qualified count unchanged.
- Normal unanswered call with valid devices: original qualification rules apply.
