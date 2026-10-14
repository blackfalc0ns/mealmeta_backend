# Campaign checkout payment implementation plan

**Goal:** Complete the user-specified mobile flow: create capacity hold → start real Payzah payment → return to app → verify authoritative payment status and fulfill exactly once.

**Approved contract:** `POST` and `GET /api/v1/customer/campaigns/{sessionId}/payment`, customer ownership, durable idempotency, five public statuses, server-only webhook authentication, sealed price and unchanged 15-minute hold.

## Design and boundaries

- The existing checkout session receives a durable initiation journal (key/hash, provider handle/URL, initialization error, callback destinations and one-time capacity release marker). Existing idempotency records cache the start response; unique database indexes and PostgreSQL locks protect sessions and customer keys.
- External provider initiation runs after a committed reservation of the attempt, outside EF retry transactions. Unknown initiation outcomes never silently generate another charge. A saved provider attempt allows recovery/reconciliation.
- A shared campaign payment service serves status polling and the authenticated webhook. It checks provider reference, amount, currency and attempt binding before settlement, uses existing callback/late-payment/purchase handlers, and keeps legacy callbacks compatible.
- Successful payment protects capacity and creates the existing subscription exactly once. Internal Completed maps to public Paid. Failure/expiry releases the hold once; verified late captures follow the existing late-payment policy.
- Return destinations use configured normal-checkout defaults or validated request URLs. Browser redirects are navigation; the provider status response is payment evidence.
- New copy belongs to both API .resx resources. No production configuration/seeding/deployment is part of implementation.

## Tasks

- [ ] Add failing service tests for start/replay/conflict/owner scope; verification success/failure/cancellation/expiry/late payment and no duplicate provider request/subscription/capacity release.
- [ ] Implement service contracts and durable fields, uniqueness/locks, and generate a narrow EF migration.
- [ ] Add customer routes and integrate server webhook; adapt worker expiry/reconciliation to the same locks and release marker.
- [ ] Add Arabic/English error resources and controller boundary tests; verify resource parity and status/request contracts.
- [ ] Exercise real PostgreSQL contention/constraints/migration with a disposable test container. Run campaign unit/API regressions and full backend build.
- [ ] Review final behavior and write Arabic mobile contracts with requests, responses, errors, retries, retention and provider callback requirements. Report exact checks and limits.

Primary files: `CampaignPaymentService.cs`, `ICampaignPaymentService.cs`, checkout session domain/configuration, `IApplicationLockManager` and PostgreSQL implementation, customer/webhook controllers, reservation expiry/reconciliation services, API resources, tests, generated migration, and `docs/mobile/campaign-checkout-payment/`.
