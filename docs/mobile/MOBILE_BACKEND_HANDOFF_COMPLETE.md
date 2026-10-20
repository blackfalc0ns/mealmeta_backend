# MealMate Mobile Backend Handoff — Complete Contract

**Version:** 3.2
**Audience:** Flutter/mobile and backend teams  
**Authority:** Server responses are the source of truth. The mobile client must not infer payment success, eligibility, price, coverage, or expiry locally.

For the individual offer-selection screen and direct payment, use [Offer Direct Payment Mobile Contract](OFFER_DIRECT_PAYMENT_MOBILE_CONTRACT.md). It documents `purchase-options`, exact-offer summary, `quoteId`, retry ownership, actual error envelopes and payment polling. That focused contract takes precedence over earlier individual checkout examples here. Code changes still require deployment before mobile can use them on a server.

For the dedicated campaigns tab (separate from ready packages and coupons), use [Campaign Mobile Flow Contract](CAMPAIGN_MOBILE_FLOW_CONTRACT.md): GET campaigns list/details, saved quote, quoteId-driven checkout-session, Payzah payment and polling, 404/410/409 disappearance rules and blocking reasons. Campaigns are never offered through the offers source and coupons never route into this tab.

## 1. Canonical flow routes

| Flow | Summary | Checkout | Payment status | Resume/retry |
|---|---|---|---|---|
| Individual | `GET /api/v1/customer/subscription-flow/summary` | `POST /api/v1/customer/subscriptions/checkout` | `GET /api/v1/customer/subscriptions/{subscriptionId}/payment` | `GET/POST /api/v1/customer/subscriptions/{subscriptionId}/resume` |
| Family | `GET /api/v1/customer/family-subscription-flow/summary` | `POST /api/v1/customer/family-subscription-flow/checkout` | `GET /api/v1/customer/family-subscription-flow/payment-status` | `POST /api/v1/customer/family-subscription-flow/retry-payment` |

Do not use the individual summary or resume route inside the family flow.

## 2. Catalog and offer discovery

```http
GET /api/v1/customer/subscription-flow/types
GET /api/v1/customer/subscription-flow/programs
GET /api/v1/customer/subscription-flow/programs/{programId}/bundles
GET /api/v1/customer/subscription-flow/programs/{programId}/bundles/{bundleId}/durations
GET /api/v1/customer/subscription-flow/programs/{programId}/bundles/{bundleId}/tiers
GET /api/v1/customer/family-subscription-flow/programs
GET /api/v1/customer/family-subscription-flow/programs/{programId}/bundles
GET /api/v1/customer/offers/hub
GET /api/v1/customer/offers/{offerId}/details
GET /api/v1/customer/offers/{offerId}/purchase-options

**Purchase mode on offer lists (live 2026-10-20):** every item in `GET /offers/available`, `POST /offers/available` and `GET /offers/hub` now carries `purchaseMode`: `DirectPurchase` means the id is a real active offer addressable by `GET /offers/{offerId}/purchase-options` (build the ready-offers section and open cards from purchase-options). `CouponOnly` means the offer is redeemed through its promo code in the subscription flow — and campaign projections keep their legacy campaign id and are always `CouponOnly` (purchase-options cannot address a campaign id). Do not infer the mode from `packageTargets`, `promoCode` or the offer name; use the field. To exclude campaign projections from the ready-offers list entirely, pass `includeCampaigns=false`.
```

Catalog records always expose stable IDs, `nameAr`, `nameEn`, active availability, category/tier, and server-supported durations. Never identify a program, bundle, tier, or offer by display name or image.

Offer details return `offer`, `serverNowUtc`, `currencyCode`, and `moneyScale`. Offer targets include subscription type, program/bundle IDs, tier, and allowed durations. `IsAvailable`, `AvailabilityReasonCode`, and `IneligibilityReasonCodes` explain a visible but unusable offer.

## 3. Address, preferences, eligibility, and coverage

Individual readiness uses:

```http
GET /api/v1/customers/me/addresses
GET /api/v1/customers/me/food-preferences/allergies
GET /api/v1/customers/me/food-preferences/dietary
GET /api/v1/customer/subscription-flow/readiness
GET /api/v1/customer/subscription-flow/restaurant-availability
```

Family members use an independent address and coverage scope:

```http
GET  /api/v1/customer/family-subscription-flow/members/{memberId}/addresses
POST /api/v1/customer/family-subscription-flow/members/{memberId}/addresses
POST /api/v1/customer/family-subscription-flow/members/{memberId}/addresses/preview-coverage
GET  /api/v1/customer/family-subscription-flow/members/{memberId}/addresses/{addressId}/preview-coverage
```

Checkout must be blocked when the server reports `address_required`, `allergies_required`, `dietary_preferences_required`, `no_restaurants_for_address`, or `no_restaurants_for_selection`.

## 4. Individual summary

```http
GET /api/v1/customer/subscription-flow/summary?programId={id}&bundleId={id}&tier={tier}&durationDays={days}&startDate={yyyy-MM-dd}&includeFriday=false&promoCode={code}
```

Important fields:

```json
{
  "basePrice": 300.000,
  "campaignDiscount": 20.000,
  "promoDiscount": 10.000,
  "walletCredit": 0.000,
  "gatewayFee": 0.000,
  "finalPrice": 270.000,
  "currencyCode": "KWD",
  "canCheckout": true,
  "checkoutBlockReason": null,
  "blockingReasons": [],
  "eligibility": { "isEligible": true, "reasons": [] },
  "availability": { "isAvailable": true, "reasons": [] },
  "summaryExpiresAtUtc": "2026-10-18T12:05:00Z",
  "serverNowUtc": "2026-10-18T12:00:00Z"
}
```

The individual summary currently serializes `basePrice`, `campaignDiscount`, and `promoDiscount`; do not require `subtotal` or `discount` aliases from this DTO. A client display subtotal is `basePrice`, and a client display discount is `campaignDiscount + promoDiscount`. Do not add coupon discount a second time. The purchase-options card has its own additive `discount` field. Send the selected `offerId` in summary and follow the focused direct-payment contract for `quoteId` and its expiry.

## 5. Family setup and summary

```http
GET /api/v1/customer/family-subscription-flow/current
GET /api/v1/customer/family-subscription-flow/packages
POST /api/v1/customer/family-subscription-flow/members
PUT /api/v1/customer/family-subscription-flow/members/{memberId}
PUT /api/v1/customer/family-subscription-flow/members/{memberId}/plan
PUT /api/v1/customer/family-subscription-flow/members/{memberId}/checkout-selection
GET /api/v1/customer/family-subscription-flow/summary?memberId={memberId}
```

Every family member has a `memberId`, display name, role, relationship, setup status, selected plan, duration, delivery address, eligibility, and availability. The package supplies `minMembers`, `maxMembers`, and `discountPercent`.

`familyDiscountPercent` uses a fractional scale: `0.100` means 10%. `durationDays` is always selected from the server catalog; values such as `26` in examples are illustrative.

Family summary includes:

```json
{
  "familyGroupId": "<uuid>",
  "members": [{
    "memberId": "<uuid>",
    "displayName": "أحمد",
    "role": "Manager",
    "setupStatus": "Ready",
    "checkoutSelected": true,
    "isReady": true,
    "programId": "<uuid>",
    "bundleId": "<uuid>",
    "selectedTier": "Basic",
    "durationDays": 26,
    "deliveryAddressId": "<uuid>",
    "eligibility": { "isEligible": true, "reasons": [] },
    "availability": { "isAvailable": true, "reasons": [] }
  }],
  "subtotal": 300.000,
  "discount": 30.000,
  "walletCredit": 0.000,
  "gatewayFee": 0.000,
  "familyDiscountPercent": 0.100,
  "familyDiscountAmount": 30.000,
  "finalPrice": 270.000,
  "currencyCode": "KWD",
  "selectedNewSubscriberCount": 2,
  "batchId": "<uuid>",
  "batchVersion": 7,
  "canCheckout": true,
  "checkoutBlockReason": null,
  "blockingReasons": [],
  "summaryExpiresAtUtc": "2026-10-18T12:05:00Z",
  "serverNowUtc": "2026-10-18T12:00:00Z"
}
```

`checkoutBlockReason` is a backward-compatible single code equal to the first `blockingReasons[].code`; use `blockingReasons` for full UI details.

## 6. Coupon contract

`summary.coupon` contains:

```json
{
  "code": "WELCOME10",
  "status": "valid",
  "offerId": "<uuid>",
  "discountAmount": 10.000,
  "messageAr": "تم تطبيق الكوبون",
  "messageEn": "Coupon applied"
}
```

Supported evaluation statuses: `valid`, `not_found`, `inactive`, `not_started`, `expired`, `ineligible`, `stacking_conflict`, `usage_exhausted`. Preserve the entered code after a failed evaluation; do not retry it indefinitely or calculate the discount on-device.

For family summary, send the code as a query parameter:

```http
GET /api/v1/customer/family-subscription-flow/summary?memberId={memberId}&promoCode={URL_ENCODED_CODE}
```

The final family checkout repeats the same `promoCode` so the server revalidates it against the locked quote. On a successful eligible evaluation, the family response uses the same `summary.coupon` shape. Rejected family coupons currently return an API validation error (`family.coupon_rejected`); missing evaluation context returns `family.coupon_context_unavailable`. Keep the entered code on the client and do not treat these errors as an undiscounted successful quote. A locked batch preserves its frozen price; refresh the flow before changing its coupon. If the existing family discount is retained instead of the coupon, `coupon` may be null.

## 7. Checkout and idempotency

Family checkout request:

```http
POST /api/v1/customer/family-subscription-flow/checkout
Content-Type: application/json
```

```json
{
  "batchId": "<uuid>",
  "batchVersion": 7,
  "idempotencyKey": "<stable-key>",
  "paymentMethodId": 3,
  "promoCode": "WELCOME10",
  "callbackUrl": "<APP_CALLBACK_URL>",
  "errorUrl": "<APP_ERROR_URL>"
}
```

`batchId`, `batchVersion`, `idempotencyKey`, and `promoCode` are optional for older clients but should be sent by current clients. `callbackUrl` and `errorUrl` must use an allowed app scheme/host. `paymentMethodId` defaults to 3 when omitted. `promoCode` is optional; omit it when no coupon was selected. `correlationId` (optional string) and `clientContractVersion` (optional integer) are also supported; send the invoice contract version supported by the mobile release. Do not send undocumented fields.

Individual checkout accepts `idempotencyKey` in the request body. Family checkout accepts `idempotencyKey`, `batchId`, and `batchVersion` in the request body. Use the same key for the same logical attempt and a new key after changing selection, price, or quote version.

Successful individual checkout returns `subscriptionId`; successful family checkout returns `familyGroupId` and `paymentAnchorSubscriptionId`. Both return `paymentUrl`, `holdExpiresAtUtc`, `finalPrice`, and `currencyCode` where applicable. Creating a payment URL is not proof of payment.

## 8. Family payment status and retry

```http
GET /api/v1/customer/family-subscription-flow/payment-status
```

This endpoint takes no identifier query parameters. It resolves the authenticated customer's current family group from the access token, then reads its latest initial batch. Do not send `familyGroupId`, `batchId`, or `paymentAnchorSubscriptionId` to select another attempt.

Response fields:

`familyGroupId`, `batchId`, `batchVersion`, `paymentAnchorSubscriptionId`, `status`, `paymentStatus`, `finalPrice`, `currencyCode`, `holdExpiresAtUtc`, `isPaid`, `isFailed`, `isExpired`, `canRetry`, `retryPaymentEndpoint`, and `pendingCheckout`.

`pendingCheckout` is returned from the family current/status surfaces when a valid payment attempt exists. It is not created by payment-status. Retry uses:

```http
POST /api/v1/customer/family-subscription-flow/retry-payment
```

If no retryable attempt exists, refresh summary and start a new family checkout. Never call individual `/subscriptions/{id}/resume` for a family group.

## 9. Payment statuses

Official payment values are `Pending`, `Paid`, `Failed`, `Expired`, `Cancelled`, `Refunded`, and `PartiallyRefunded`. `PaidPreparingCalendar` and `ReviewRequired` are lifecycle/review states, not payment success. Unknown values must be rendered as pending/review and must not unlock the calendar.

## 10. Advertising and analytics

```http
GET /api/v1/customer/advertising/placements?placementKey={placementKey}
POST /api/v1/customer/advertising/events
```

Placements include `displayStartsAtUtc` and `displayEndsAtUtc`; show an ad only inside `[displayStartsAtUtc, displayEndsAtUtc)`. `eventType` is `impression` or `click`. Events are idempotent on `(placementId, eventId)`; resending the same event is ignored and never counted twice.

```json
{ "placementId": "<uuid>", "eventId": "<client-id>", "eventType": "impression" }
```

## 11. Hold expiry

After `holdExpiresAtUtc`, the server returns a conflict such as `checkout.capacity_hold_expired`, with retry/refresh advice. The client must refresh summary and availability, then create a new checkout/retry attempt with a new idempotency key. It must not reuse an expired payment URL.

## 12. HTTP errors

| Status | Meaning | Client action |
|---|---|---|
| `400` | Invalid field, duration, or redirect URL | Fix the request |
| `404` | Missing or unavailable resource | Refresh catalog/screen |
| `409` | State, quote, hold, or idempotency conflict | Refresh summary/status according to advice |
| `410` | Explicitly expired resource | Start a new flow |
| `422` | Commercial eligibility/coverage rejection | Show blocking reasons; do not pay |
| `502` | Payment gateway failure | Retry only when advice allows |

Every mobile-facing error uses a stable `code`, Arabic/English messages, optional `field`, and optional `advice` with `retryable`, `refreshSummaryRequired`, and `refreshAvailabilityRequired`.

## 13. IDs and polling

- Individual `subscriptionId`: payment status, individual resume, and individual polling.
- Family `familyGroupId`: family summary/current/payment-status and family retry routing.
- Family `paymentAnchorSubscriptionId`: gateway/payment attempt correlation; it is not a replacement for `familyGroupId` and is not the ID for every member subscription.

After returning from WebView, poll the server status endpoint. A redirect callback alone never proves payment.

## 14. Money, dates, and localization

Currency is `KWD`; monetary values use three decimal places and server-side rounding. All timestamps ending in `Utc` are ISO-8601 UTC. `serverNowUtc` is authoritative for countdowns and expiry. Use `nameAr/nameEn` according to the active locale and preserve API codes/enums unchanged.

## 15. Compatibility

The individual summary retains `basePrice`, `campaignDiscount`, `promoDiscount`, `walletCredit`, `gatewayFee`, and `finalPrice`. Its `quoteId` field is additive. Legacy checkout requests without a quote retain their existing behavior; they do not gain the quote-backed idempotency guarantee by sending a correlation ID. Clients using the direct offer-payment flow must follow the focused contract and send the quote plus stable idempotency key. Family contracts retain their separately documented batch/version requirements.
