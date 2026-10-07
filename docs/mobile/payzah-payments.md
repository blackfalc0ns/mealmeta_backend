# Payzah mobile payment contract — 2026-10-06

This contract replaces all earlier payment-provider sections in mobile guides. The active provider is Payzah. Historical payment records and applied migrations retain their original provider IDs and labels.

## Checkout

Use payment method ID `3` (`PAYZAH`) returned by the payment-method endpoint. The app opens `paymentUrl` supplied by the API in Payzah hosted checkout. KNET/card selection occurs there; available methods and fees come from the merchant account. API method fee/total fields are nullable when the provider does not supply them. Never assume a zero provider fee.

The initialization response explicitly returns the provider's `PaymentID`; the backend persists it separately from the redirect URL. All application payment processing uses the immutable `pz-<TrackId>` handle; it is unique across sandbox/live even if provider PaymentID values overlap. Existing app-facing checkout response schemas retain `paymentUrl`; internal callback/status and subscription payment IDs use the application handle.

## Authoritative confirmation

Payzah browser return: `GET` or `POST /api/v1/payments/payzah/return?trackId=<persisted-track-id>`. The backend sends `trackid` and the original provider `payment_id` to the correct environment's server status API. It requires exact persisted track/reference binding; amount/currency are immutable expected initialization values, with provider response money checked whenever present. Callback `paymentStatus` is never sufficient to activate anything. A payment from an older attempt cannot release newer holds or activate a later quote.

Return deep links are fixed application destinations: `mealmate://checkout/success`, `/error`, `/pending`, `/review`, with application `paymentId` handle. Success requires both authoritative captured payment and accepted application processing. Review means do not start another payment until support reconciles the captured transaction. Pending means check application subscription/payment status. Arbitrary callback `to` destinations are ignored.

An optional JSON notification endpoint `/api/v1/webhooks/payzah` accepts the documented `trackId` field and re-verifies against the provider. There is no invented HMAC signature, webhook secret or old-provider event schema. The merchant/browser return is sufficient for normal processing. Operations with no bound Payzah attempt require review; historical IDs are never sent to Payzah.

## Merchant configuration

Admin endpoint `/api/v1/settings/payment_gateways/payzah`: GET configuration and PUT `{ "isSandbox": true, "rawPrivateKey": "merchant-provided-private-key" }`. Stored keys are encrypted and API responses contain only masking. Switching environments requires the new environment key. Configure `Payments:PublicBaseUrl` as the merchant's absolute HTTPS API URL, and optionally bootstrap `Payments:BootstrapPayzah:PrivateKey` and `IsSandbox` from the secret store. Never reuse previous gateway credentials. Configuration readiness is not a successful live connection test; Payzah does not document a read-only credential test.

Sandbox initialization: `https://development.payzah.net/ws/paymentgateway/index`; production: `https://payzah.net/production770/ws/paymentgateway/index`. Status uses corresponding `/get-payment-details`. Authorization is the base64-encoded private key, without Bearer. Currency is KWD numeric `414`, amount has three decimals, hosted `payment_type` is `3`.

## Refunds

Refund submission uses the server-verified original `payzahRefrenceCode`, original track ID and documented `/refund` payload (`refrence_code`, `trackid`, `amount`, `refund_type`, `message`). The gateway documents acceptance only; no remote receipt IDs or refund-status endpoint are invented. Accepted/uncertain submissions require merchant review, reserve the amount, never automatically resend and do not mark funds returned. Previous-provider refunds remain review-only and are not submitted to Payzah.

## Verification and deployment

Apply the forward gateway migration after reviewing its SQL; it creates separate Payzah settings/attempt tables and removes old credential configuration, while retaining transaction history and applied migrations. Supply a real merchant sandbox key and perform an authorized sandbox transaction before going live. No purchases, refunds, database migration or deployment were run during implementation.

Sources: [Payzah documentation](https://docs.payzah.site/docs), [vendor-maintained API blueprint](https://jsapi.apiary.io/apis/payzahpaymentgatewaydocumentationupdated), [official Payzah WooCommerce SDK](https://plugins.svn.wordpress.org/payzah-payment-gateway-for-woocmmerce/trunk/includes/SDK/Client.php), [official gateway refund implementation](https://plugins.svn.wordpress.org/payzah-payment-gateway-for-woocmmerce/trunk/includes/Classes/Gateway.php).
