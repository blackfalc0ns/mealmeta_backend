# Offer direct-payment verification — 2026-10-19

Scope: initial implementation through `a35c938`, followed by correction of the six failing API tests and an HTTP flow regression. This record distinguishes automated verification from production acceptance.

## Latest follow-up verification

- The six previously reported failures are resolved. No tests were skipped or disabled. Date fixtures now use valid first-delivery dates; expired-hold preview uses the real schedule and explicitly checks Friday exclusion. Catalog tests check the current browsing contract while separately asserting profile requirements still block readiness/payment.
- **234 unit tests passed**: 222 covering offers, pricing, readiness, availability, wallets, sandbox and fulfillment, plus 12 `PayzahReceiptTests` covering provider request/receipt/status rules through a controlled HTTP handler.
- **104 API tests passed**, including all six corrected cases and three new `OfferDirectPaymentHttpFlowTests` scenarios.
- **14 PostgreSQL integration tests passed** for quote persistence, owned idempotency claims, concurrency and coupon limits, against an isolated local PostgreSQL database.
- New HTTP tests exercise Arabic and English requests through real ASP.NET routing, authentication, model binding and JSON serialization: offer options -> summary -> quote -> checkout -> same-key replay -> changed-payload conflict -> resume -> pending status -> independently verified paid status. They assert one subscription/invoice/receipt, full-period holds, and a single promotional-wallet debit. Unauthorized access, incomplete profile and expired quotes are also rejected over HTTP.
- The HTTP flow exposed an actual pricing inconsistency: exact-offer percentage discounts were computed on a gateway-inclusive total while options validated against the package subtotal. Exact-offer previews now calculate the discount on `UnroundedCorePrice`, before gateway fees and final rounding. The earlier code-only preview basis is preserved for existing callers without `offerId`. The real pricing handler is used in the HTTP test, with an explicit 20% subtotal assertion.
- Controlled boundaries in these tests: external gateway, menu/restaurant eligibility, restaurant snapshot preparation and downstream fulfillment. Pricing, quote persistence, checkout, wallet ledger and payment verification use production application services; the HTTP fixture uses an isolated EF InMemory database. PostgreSQL uniqueness/transaction behavior is covered separately by integration tests. This is not a live merchant payment or full restaurant-menu preparation/device test.

Reproduction from the backend directory:

```powershell
dotnet test tests/MealMate.UnitTests/MealMate.UnitTests.csproj --no-restore --filter "FullyQualifiedName~Offers|FullyQualifiedName~OfferSummary|FullyQualifiedName~OfferCheckoutQuote|FullyQualifiedName~PricingEngineAndClassification|FullyQualifiedName~SubscriptionCheckoutReadiness|FullyQualifiedName~SubscriptionRestaurantAvailability|FullyQualifiedName~WalletSpend|FullyQualifiedName~SandboxCheckout|FullyQualifiedName~SubscriptionFlowGap|FullyQualifiedName~FulfillmentRetryPolicy|FullyQualifiedName~FulfillmentCalendarEditability" --verbosity minimal /p:WarningLevel=0
dotnet test tests/MealMate.UnitTests/MealMate.UnitTests.csproj --no-restore --filter "FullyQualifiedName~PayzahReceiptTests" --verbosity quiet /p:WarningLevel=0
dotnet test tests/MealMate.ApiTests/MealMate.ApiTests.csproj --no-restore --filter "FullyQualifiedName~CustomerSubscriptionFlowControllerTests|FullyQualifiedName~CustomerCheckoutGateControllerTests|FullyQualifiedName~OfferPurchaseOptionsControllerTests|FullyQualifiedName~CheckoutCouponFlowTests|FullyQualifiedName~CustomerCheckoutPaymentStatusTests|FullyQualifiedName~PayzahPaymentReturnControllerTests|FullyQualifiedName~OfferDirectPaymentHttpFlowTests" --verbosity quiet /p:WarningLevel=0
```

## Verified behavior

- Options use the same pricing preview pipeline as summary/checkout, including promotional wallet and fees.
- Options use configured durations, persisted slots, valid default dates and full-period availability intersected with the offer's restaurant scope.
- Account/prerequisite, existing individual/family, unavailable period, invalid scope and unsupported zero-price selections cannot be presented as payable options.
- Direct-offer summary validates scope against the priced base, filters eligible restaurants and blocks rejected coupons. Blocked summaries do not issue payable quotes; quote storage failures propagate.
- Owned quotes bind selection, price breakdown and canonical address content; expired, changed, foreign and used quotes are rejected for new attempts.
- Durable customer/key and quote claims use existing unique PostgreSQL indexes, bind the actual subscription in the reservation transaction, and replay the saved successful checkout response before readiness/quote-expiry gates.
- Lost provider responses retain an unresolved attempt rather than automatically creating another invoice. Status uses the persisted Payzah attempt reference where possible. Resume does not replay expired links or create another invoice for unresolved quote-backed attempts.
- Checkout re-evaluates persisted offer policy before committing a reservation, even if preview amount still matches the quote.

## Initial delivery commands and results (historical)

Run from the backend repository:

```powershell
dotnet test tests/MealMate.UnitTests/MealMate.UnitTests.csproj --no-restore --filter "FullyQualifiedName~OfferPurchaseOptionsTests|FullyQualifiedName~OfferSummaryTests|FullyQualifiedName~OfferCheckoutQuoteServiceTests|FullyQualifiedName~OfferEligibilityEngineTests|FullyQualifiedName~OfferQueriesTests" --verbosity quiet /p:WarningLevel=0
```

50 passed, 0 failed, 0 skipped.

```powershell
dotnet test tests/MealMate.ApiTests/MealMate.ApiTests.csproj --no-restore --filter "FullyQualifiedName~OfferPurchaseOptionsControllerTests|FullyQualifiedName~CheckoutCouponFlowTests|FullyQualifiedName~CustomerCheckoutPaymentStatusTests" --verbosity quiet /p:WarningLevel=0
```

20 passed, 0 failed, 0 skipped. API-project tests include direct service/controller calls; this is not a deployed HTTP or device test.

```powershell
dotnet test tests/MealMate.IntegrationTests/MealMate.IntegrationTests.csproj --no-restore --filter "FullyQualifiedName~OfferCheckoutQuotePersistenceTests|FullyQualifiedName~IndividualCheckoutIdempotencyPostgresTests|FullyQualifiedName~SubscriptionFlowCouponConcurrencyPostgresTests" --verbosity quiet /p:WarningLevel=0
```

14 passed, 0 failed, 0 skipped on isolated local PostgreSQL 18, using the shared fixture's command-local `MEALMATE_TEST_POSTGRES_ADMIN` setting. Each test creates/migrates a fresh fixture-owned database. No production/application database was migrated. Docker was unavailable; the existing coupon concurrency fixture was adapted to the same real PostgreSQL helper used by the new tests.

TDD evidence: duration/meal fallback, blocked-family card and persisted-offer policy regressions failed before their fixes; quote storage failure and blocked-summary tests were added before summary fixes. The policy regression demonstrated both expired and raised minimum-order conditions could reach payment when a matching preview result was returned; both now reject before subscription/gateway creation.

Documentation checks: eight JSON examples parse; both resource XML files contain exactly one nonempty value for each of twelve affected reason/error keys; both mobile contract copies match byte-for-byte. Scoped `git diff --check` passes. The root `scripts/i18n-audit.cjs` script is absent, so it was not run. No Flutter Arabic/English or RTL runtime checks were performed.

## Previously failing broader API tests — resolved

The initial 94-test API selection returned 88 passed and six failed, reproduced on baseline `3205413`. The follow-up fixes below resolve all six while retaining production lead-time and checkout prerequisite rules:

- `ResumePreview_WhenHoldExpired_ReturnsCanPayDirectlyFalse`: uses the real schedule and verifies the next valid Saturday when Friday is excluded.
- Old tier prerequisite test replaced by `Tiers_RequiresAddress_ButAllowsBrowsingBeforeProfileCompletion`: supplies a pricing-catalog sender and proves incomplete allergies/dietary do not block browsing while readiness remains blocked.
- Old bundle prerequisite test replaced by `Bundles_RequiresAddress_AndReportsProfileStepsWithoutBlockingBrowsing`: asserts address blocking, navigable covered cards, profile flags and actionable `requiredProfileStep` metadata.
- `Summary_OmitsRestaurantsOutsideAddressCoverage`: uses the actual earliest first-delivery date and retains coverage assertions.
- `Summary_WhenIncomplete_ReturnsConflictWithReadiness`: supplies a valid date so the test reaches the intended profile gate.
- `CatalogTiers_AttachPeriodVerdicts_WhenContextComplete`: derives the selected date from the real Kuwait schedule rather than a UTC offset that can violate business lead time.

All selected follow-up tests pass with zero failures/skips. This is not a claim that every unrelated repository test was executed.

## Remaining release checks and limits

- Actual Payzah merchant sandbox payment, callbacks and server-confirmed activation have not been exercised here.
- Server deployment, production migration and real mobile integration are not proved by passing tests or a Git push.
- An unresolved provider creation may need operational reconciliation. If PaymentID was never persisted, the current provider API implementation cannot always determine the remote outcome from TrackId alone. Never automatically create another invoice merely to escape `checkout.in_progress`.
- The crash window between reservation commit and provider initiation is safely blocked from duplicate creation, but is not automatically recovered by a durable background worker in this change.
- No catalog-scale load/performance benchmark was run. Period eligibility/pricing are evaluated per distinct candidate; assess production catalog size and latency before rollout to a large catalog.
- Legacy checkout without `quoteId` retains prior behavior and does not gain quote-backed replay guarantees. Existing old-client rename behavior for `paymentDate` is documented separately; this change does not promise the removed `chosenStartDate` alias.

Mock Data Check:
- No mock business data introduced.
- No fake fallback responses introduced.
- No hardcoded production business values introduced.
- All runtime data comes from real application sources.
