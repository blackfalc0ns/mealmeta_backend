# Explicit sandbox checkout without collecting funds

When the persisted system setting `checkout.sandbox_completion` is active with
`{"enabled":true,"publicBaseUrl":"https://your-api-host"}`, and the MyFatoorah
gateway is configured as sandbox at `https://apitest.myfatoorah.com`, the individual
subscription flow offers `SANDBOX_WITHOUT_PAYMENT` (method id `-1`, integration
type `SANDBOX_HOSTED`). Missing, malformed or disabled settings keep the provider
flow. Live gateway configuration disables sandbox completion immediately.

The existing `POST /api/v1/customer/subscriptions/checkout` and resume endpoint
return a hosted `paymentUrl`. Open this URL as usual. Its GET only displays the
persisted amount and a clearly labelled test confirmation button. Pressing the
button posts the short-lived encrypted capability, confirms the customer's own
reservation and runs normal fulfillment. Poll the existing subscription payment
endpoint when returning to the app. The existing status contract reports the
completed test subscription as Paid/Scheduled/Active; no real charge occurs.

The ticket binds the customer, subscription, callback, expiry and the persisted
quote/reservation fingerprint. Resuming with replacement holds invalidates older
links. Issuance, resume and confirmation share the subscription lock. It must also
match an issued checkout operation in the database. Expired reservations,
modified tickets, family subscriptions and live payments cannot be confirmed.
Confirmation is idempotent, and only POST changes the subscription.

Audit operations are `SANDBOX_CHECKOUT_STARTED` and
`SANDBOX_CHECKOUT_CONFIRMED`. No MyFatoorah receipt, invoice, payment id or provider
verification timestamp is created. Existing gateway credentials are retained.

To restore provider checkout, deactivate this system setting or set `enabled` to
false. Reconfigure live mode only when ready for actual collection. Family
checkout and tier upgrades continue using their existing payment flows.
