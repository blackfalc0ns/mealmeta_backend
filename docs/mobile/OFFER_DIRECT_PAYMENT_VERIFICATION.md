# Offer direct-payment verification — 2026-10-19

Scope: follow-up fixes on backend baseline `3205413`, extending the offer-options, summary and checkout work already in `37d7776`. Implementation began with explicitly requested Luna subagents; they reached usage limits before final review. The primary agent completed review, regression fixes and documentation. This record distinguishes automated verification from production acceptance.

## Verified behavior

- Options use the same pricing preview pipeline as summary/checkout, including promotional wallet and fees.
- Options use configured durations, persisted slots, valid default dates and full-period availability intersected with the offer's restaurant scope.
- Account/prerequisite, existing individual/family, unavailable period, invalid scope and unsupported zero-price selections cannot be presented as payable options.
- Direct-offer summary validates scope against the priced base, filters eligible restaurants and blocks rejected coupons. Blocked summaries do not issue payable quotes; quote storage failures propagate.
- Owned quotes bind selection, price breakdown and canonical address content; expired, changed, foreign and used quotes are rejected for new attempts.
- Durable customer/key and quote claims use existing unique PostgreSQL indexes, bind the actual subscription in the reservation transaction, and replay the saved successful checkout response before readiness/quote-expiry gates.
- Lost provider responses retain an unresolved attempt rather than automatically creating another invoice. Status uses the persisted Payzah attempt reference where possible. Resume does not replay expired links or create another invoice for unresolved quote-backed attempts.
- Checkout re-evaluates persisted offer policy before committing a reservation, even if preview amount still matches the quote.

## Commands and results

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

## Existing broader API failures

An additional 94-test API selection initially returned 88 passed and six failed. The same six tests were executed independently on an unmodified detached checkout of baseline `3205413`; all six also failed there. They were preserved rather than changing unrelated existing catalog/profile/date policy within this feature:

- `CustomerCheckoutGateControllerTests.ResumePreview_WhenHoldExpired_ReturnsCanPayDirectlyFalse`: suggested date default versus fallback checkout date.
- `CustomerSubscriptionFlowControllerTests.Tiers_ButtonToClassification_WithoutPrerequisites_ReturnsConflict`: test controller lacks a request-service sender for current catalog flow.
- `CustomerSubscriptionFlowControllerTests.Bundles_WhenPrerequisitesMissing_DisablesCardsWithSemanticReason`: existing catalog selectability expectation differs from current behavior.
- `CustomerSubscriptionFlowControllerTests.Summary_OmitsRestaurantsOutsideAddressCoverage`: fixture date precedes current lead-time validation.
- `CustomerSubscriptionFlowControllerTests.Summary_WhenIncomplete_ReturnsConflictWithReadiness`: fixture date fails validation before the intended profile gate.
- `CustomerSubscriptionFlowControllerTests.CatalogTiers_AttachPeriodVerdicts_WhenContextComplete`: date/context fixture fails current validation.

This is not a claim that the entire repository test suite is green. Full-suite cleanup is separate from the scoped direct-payment regression results.

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
