# Family Subscription Flow

This document describes the family subscription flow implemented by the API. It is intended for the mobile client, QA, and operations teams. The examples use placeholders such as `<memberId>` and do not represent real customer data.

## State model

```text
Draft -> CheckoutPending -> Paid
  |           |              |
  |           +------------> ReviewRequired
  |                          |
  +------------------------> Expired / Failed
```

- `Draft`: the family roster and member plans can be edited.
- `CheckoutPending`: the quote, selected members, dates, prices, and payment attempt are frozen. Editing selections or plans is rejected.
- `Paid`: the gateway callback is verified and the batch is eligible for fulfillment.
- `ReviewRequired`: payment or gateway state is uncertain; a new payment must not be initialized automatically.
- `Expired` / `Failed`: the reservation is closed. `retry-payment` may create a new draft batch from the selected members when policy allows it.

## 1. Read catalog and family metadata

```http
GET /api/v1/customer/family-subscription-flow/metadata
GET /api/v1/customer/family-subscription-flow/programs
GET /api/v1/customer/family-subscription-flow/programs/{programId}/bundles
GET /api/v1/customer/family-subscription-flow/programs/{programId}/bundles/{bundleId}/durations?includeFriday=false
GET /api/v1/customer/family-subscription-flow/programs/{programId}/bundles/{bundleId}/tiers?durationDays=6&includeFriday=false&familyMemberId={memberId}
GET /api/v1/customer/family-subscription-flow/packages
```

The family type limit is read from the active family package settings. The client must use the returned `minMembers` and `maxMembers`; it must not hardcode 4 or 8.

## 2. Create or resume the family draft

```http
PUT /api/v1/customer/family-subscription-flow/group
Content-Type: application/json
```

```json
{
  "name": "Family plan",
  "familyPackageId": "<familyPackageId>"
}
```

Resume with:

```http
GET /api/v1/customer/family-subscription-flow/current
```

When the response has `status = "CheckoutPending"`, show the payment screen and use `pendingCheckout.retryPaymentEndpoint`. Do not reopen the editable draft screen.

## 3. Add members and save member setup

```http
POST /api/v1/customer/family-subscription-flow/members
Content-Type: application/json
```

```json
{
  "displayName": "Member name",
  "relationshipCode": "spouse",
  "gender": "Female",
  "ageYears": 30,
  "phone": "+965XXXXXXXX"
}
```

Save address, coverage, allergies, disliked ingredients, and dietary preferences using the member-specific endpoints. Explicit empty choices are represented by the stable codes `NO_ALLERGIES` and `NO_DIETARY_RESTRICTIONS`.

```http
POST /api/v1/customer/family-subscription-flow/members/{memberId}/addresses
POST /api/v1/customer/family-subscription-flow/members/{memberId}/addresses/{addressId}/preview-coverage
PUT  /api/v1/customer/family-subscription-flow/members/{memberId}/allergies
PUT  /api/v1/customer/family-subscription-flow/members/{memberId}/disliked-ingredients
PUT  /api/v1/customer/family-subscription-flow/members/{memberId}/dietary
```

## 4. Save a member plan and date

```http
PUT /api/v1/customer/family-subscription-flow/members/{memberId}/plan
Content-Type: application/json
```

```json
{
  "programId": "<programId>",
  "bundleId": "<bundleId>",
  "tier": "Basic",
  "durationDays": 26,
  "includeFriday": false,
  "startDate": "2026-10-21"
}
```

Rules enforced by the API:

- A start date before the Kuwait business date is rejected with `checkout.start_date_invalid`.
- If both `tier` and `selectedTier` are sent, they must have the same value. The same applies to `bundleId` and `selectedBundleId`.
- The selected date is normalized to the first delivery date by the schedule service. The service end date is calculated from actual service dates, including Friday additions.

The dedicated date endpoint is:

```http
PUT /api/v1/customer/family-subscription-flow/members/{memberId}/start-date
```

```json
{
  "startDate": "2026-10-21"
}
```

## 5. Select who will be charged

```http
PUT /api/v1/customer/family-subscription-flow/members/{memberId}/checkout-selection
Content-Type: application/json
```

```json
{
  "subscribeNow": true
}
```

The selection is stored on the open checkout batch. It does not create a subscription by itself. A member with `subscribeNow = false` remains deferred and is not included in the quote.

## 6. Read the quote and availability

```http
GET /api/v1/customer/family-subscription-flow/summary?memberId=<memberId>
```

Important response fields:

```json
{
  "status": "Draft",
  "canCheckout": true,
  "participantCount": 2,
  "occupiedSeatCount": 2,
  "availableSeatCount": 1,
  "maxSeats": 3,
  "currentPurchaseCount": 2,
  "batchVersion": 18,
  "lockedQuote": false,
  "memberSchedules": [
    {
      "memberId": "<memberId>",
      "paymentDate": "2026-10-14",
      "firstDeliveryDate": "2026-10-21",
      "endDate": "2026-11-19",
      "deliveryDaysCount": 26,
      "restaurantRepetition": {
        "limit": 13,
        "restaurantCount": 2,
        "durationDays": 26
      },
      "availability": {
        "status": "available",
        "eligibleRestaurantCount": 2
      }
    }
  ]
}
```

Restaurant repetition is calculated from service days and the number of eligible restaurants. For 26 service days and two eligible restaurants, the average is rounded up to 13 occurrences per restaurant, giving enough capacity for the full period.

When payment is pending, the response is locked and the capacity fields come from the frozen batch:

```json
{
  "status": "CheckoutPending",
  "lockedQuote": true,
  "participantCount": 2,
  "occupiedSeatCount": 2,
  "availableSeatCount": 1,
  "currentPurchaseCount": 2,
  "maxSeats": 3
}
```

## 7. Start checkout

```http
POST /api/v1/customer/family-subscription-flow/checkout
Content-Type: application/json
```

```json
{
  "batchId": "<batchId>",
  "batchVersion": 18,
  "idempotencyKey": "family-checkout-<client-generated-key>",
  "callbackUrl": "<approved-callback-url>",
  "errorUrl": "<approved-error-url>",
  "clientContractVersion": 1
}
```

The server validates the batch version, coverage, allergies, dates, capacity, and payment method before creating pending subscriptions and capacity holds. A stale `batchVersion` returns `409 family.quote_changed`.

Successful response:

```json
{
  "familyGroupId": "<familyGroupId>",
  "paymentAnchorSubscriptionId": "<subscriptionId>",
  "paymentUrl": "<gateway-url>",
  "holdExpiresAtUtc": "2026-10-14T12:30:00Z",
  "finalPrice": 77.28,
  "batchId": "<batchId>",
  "batchVersion": 19
}
```

The payment URL alone does not activate the subscription. Activation requires a verified gateway callback.

## 8. Retry or resume payment

```http
POST /api/v1/customer/family-subscription-flow/retry-payment
Content-Type: application/json
```

```json
{
  "batchId": "<batchId>",
  "batchVersion": 19,
  "idempotencyKey": "<same-or-client-retry-key>",
  "callbackUrl": "<approved-callback-url>",
  "errorUrl": "<approved-error-url>",
  "clientContractVersion": 1
}
```

For an active `PaymentPending` batch, retry returns the existing payment response. It does not create another batch, subscription, or payment attempt. This is protected by the family-group lock and the database unique index on `(BatchId, AttemptKey)`.

The regression test for this guarantee is `RetryPayment_PendingAttemptReusesSameUrlAndInvoice`.

## 9. Payment completion and post-payment behavior

After the gateway callback is verified, the batch transitions to `Paid`, the pending subscriptions become eligible for fulfillment, and the calendar becomes available. Before verified fulfillment, the calendar endpoint intentionally returns:

```json
{
  "code": "calendar.fulfillment_not_ready"
}
```

The production readiness test still needs a real merchant-live payment and a verified callback. Sandbox redirects must not be treated as a successful live payment.

## Error handling contract

- `409 family.quote_changed`: refresh the summary and submit the latest `batchVersion`.
- `409 family.checkout_pending`: resume or retry the existing payment; do not edit the family.
- `409 family.batch_review_required`: support must resolve the uncertain payment state.
- `400 checkout.start_date_invalid`: choose today or a future Kuwait business date.
- `400 family.plan_conflicting_tier` / `family.plan_conflicting_bundle`: send one alias or send matching values.
- `502 payment.initialization_failed`: keep the draft and let the customer retry after the gateway issue is resolved.

## Current release boundary

The backend implementation and automated tests cover the draft, quote, locking, retry idempotency, and response contracts. A final release requires a live merchant configuration, a real authorized payment, callback verification, fulfillment activation, calendar verification, and mobile Arabic/English RTL testing.
