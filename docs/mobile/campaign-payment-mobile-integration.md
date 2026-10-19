# Campaign payment mobile integration

Use the campaign checkout session id returned by `POST /api/v1/customer/campaigns/checkout-session`.

## Screens and files

| Screen | Client file | API calls |
| --- | --- | --- |
| Campaign checkout | `campaign_checkout_screen.dart` | Create checkout session |
| Campaign payment | `campaign_payment_screen.dart` | Start payment |
| Payment return/result | `campaign_payment_result_screen.dart` | Read payment status |

The client must open the returned `paymentUrl` in the system browser or an in-app browser. It must never call the webhook.

## Start payment

`POST /api/v1/customer/campaigns/{sessionId}/payment`

```json
{
  "idempotencyKey": "mobile-generated-stable-key",
  "callbackUrl": "mealmate://campaign-payment/callback",
  "errorUrl": "mealmate://campaign-payment/error",
  "paymentMethodId": 3
}
```

The same key and the same payload safely return the original response. Reusing a key with different URLs or payment method returns `409 Conflict`.

```json
{
  "sessionId": "00000000-0000-0000-0000-000000000000",
  "paymentId": "pz-...",
  "paymentUrl": "https://...",
  "amount": 12.500,
  "currency": "KWD",
  "status": "Pending",
  "expiresAtUtc": "2026-10-14T12:15:00Z"
}
```

## Return and polling

After the browser returns to the app, parse `sessionId`, `paymentId`, and `status` if supplied, then always call:

`GET /api/v1/customer/campaigns/{sessionId}/payment`

```json
{
  "sessionId": "...",
  "paymentId": "pz-...",
  "status": "Paid",
  "isPaid": true,
  "isFailed": false,
  "amount": 12.500,
  "currency": "KWD",
  "expiresAtUtc": "...",
  "subscriptionId": "..."
}
```

Display `Pending`, `Paid`, `Failed`, `Expired`, or `Cancelled`. Poll `Pending` with backoff; stop on a terminal state. A `Paid` response is authoritative only when returned by this API.

Common errors: `404` session ownership/not found, `409` expired or idempotency conflict, `422` invalid quote/URL/contact, and `503` provider initialization/status unavailable. Keep the server error code for localized Arabic/English copy.

The provider callback is server-to-server and protected by `X-Webhook-Secret`; the mobile app does not receive or forward it.
