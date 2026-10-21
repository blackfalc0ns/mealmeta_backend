# Phase 03 — Pricing, Profitability, and Risk

## Decision record

Use the existing Finance Governance `RuleVersion` maker/checker workflow as the source of approved pricing policy. Resolve the policy for the actual bundle scope across active legal entities; fail closed when no unique approved version exists. Store the resolved policy identity, canonical content hash, rule version, and decision inputs in an immutable checkout quote snapshot. Do not copy mutable `SystemSetting` values or application defaults into a governed quote.

Treat these values as separate financial facts: restaurant reference price, contracted supply price, customer list price, customer net price, external payment capture, expected gateway cost, and observed gateway cost. Customer list price is rounded upward before a single selected discount is applied. Gateway cost is an estimate on positive external capture and never increases customer list or net price. Promotional wallet credit is a discount source; cash wallet tender is rejected until an authorized settlement ledger exists.

## Scope and flow

The governed quote service will be the only source of customer amounts for individual, family, and campaign checkout. Clients submit offer/selection identifiers, not amounts or quote snapshots. Each flow resolves current contracts and approved policy server-side, creates a canonical immutable quote, then rechecks quote expiry, selection, policy, capacity, and funding constraints under the existing database transaction and relevant bundle lock before creating holds or payment capture. Existing paid subscriptions and frozen family batches remain historical snapshots; this phase does not rewrite their paid amounts.

Restaurant reference-price changes go through the same publication service and cause a new cohort classification because the cohort input changed. Supply-price changes also go through that service, but only recheck profitability and quote source freshness; a supply-only change does not create a classification run or alter cohort membership. The service holds the shared bundle lock, checks the expected row version, verifies a distinct checker against the persisted maker, and commits the active-version transition and any required classification provenance atomically. The old lock command cannot bypass this path.

Classification uses observed cohort mean and population standard deviation: below `mean - 0.5 SD` is Basic, above `mean + 0.5 SD` is Elite, and both bounds belong to Plus (the existing `Platinum` enum). A zero deviation cohort is all Plus, including one-item cohorts. Outlier reporting remains a separate two-standard-deviation measure. Statistical calculations retain decimal precision; no single-item business thresholds are introduced.

Profitability results are assessments, not automatic approvals. A low-level calculator may return `NotEvaluated` where the approved policy supplies no minimum contribution. Governed policy must explicitly define applicable routes, margin basis, gateway estimate, eligible campaign/influencer costs, funding and loss limits. Missing contract commission is not replaced by a default commission. Campaign discounts are never represented as family commission.

Acquisition loss limits use persisted budget reservations keyed uniquely to the quote, with database locking, atomic amount recheck, expiry, release, and idempotency. An exception requires a different checker and a reservation at least equal to the required deficit; an absolute binding cap always blocks publication or checkout.

## Implementation sequence

1. Stage A: correct calculator, decimal math, selected-offer semantics, and classification boundaries with unit regressions.
2. Stage B: add typed approved policy and immutable quote/risk/budget records, database constraints, policy resolution, and persistence guards.
3. Stage C: route publication and individual/family/campaign checkout through the trusted quote and policy services; keep historical paid data immutable.
4. Stage D: secure API and localized admin/restaurant screens, including policy status, quote source, captured price breakdown, review, and unavailable states when real values or policy are absent.
5. Verify with isolated PostgreSQL concurrency/rollback tests, actual HTTP authorization and checkout tests, affected unit regressions, and Angular typecheck/build/i18n/browser checks. No live database migration is run.

## Authorized implementation surface

Stage A is limited to the pricing calculators, decimal math, and named pricing/classification unit tests. Stage B uses the existing governance validator/resolver, typed pricing and quote records, application services, shared application/persistence abstractions, additive pricing configuration/migration, and the named finance pricing API and tests. Stage C may change only the explicitly reviewed individual, family, campaign, and restaurant pricing call sites; Stage D may add the finance pricing page and narrowly update the existing restaurant pricing workspace. Phase 01/02 migrations and paid checkout snapshots are preserved. No other checkout controller or persisted flow is changed without separately authorizing its exact path.

## Acceptance checklist

- [ ] No customer amount or policy decision comes from a client-supplied price, mutable setting, invented commission, or fabricated tier threshold.
- [ ] Quote snapshots pin all four prices, selected offer/discount source, source contracts, policy version/hash, expiry, and canonical fingerprint.
- [ ] Quote confirmation rechecks freshness, offer and capacity under transaction/lock before holds; repeated requests are idempotent.
- [ ] Campaign loss-budget reservation is durable, race-safe, uniquely tied to a quote, and safely released/expired.
- [ ] Publication is atomic, row-versioned, maker/checker protected, and cannot be bypassed through the legacy lock endpoint.
- [ ] Existing paid subscriptions and frozen quotes remain unchanged.
- [ ] Tests prove rejection paths and successful paths against actual services and isolated PostgreSQL; no mock-only completion claims.
- [ ] UI uses real API data, offers Arabic and English with active-locale updates, and shows a localized unavailable state when policy or source data is absent.
- [ ] Cash-wallet tender remains explicitly unsupported until a separately authorized settlement flow exists.

## Boundaries

Observed payment-provider fees remain later-phase actual costs; this phase estimates expected cost only. Operational campaign, subscription, and family data are not converted into synthetic source documents. A route without an approved policy remains unavailable rather than silently falling back. External pricing policy, rates, contribution targets, account codes, or campaign budgets are not invented here.
