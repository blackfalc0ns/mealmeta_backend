# F03 discovery evidence

Discovery was read-only before Stage A authorization. The working branch already contains F01/F02 and concurrent changes; this evidence records active source paths and relevant boundaries, not a clean-baseline claim.

## Pricing calculator and classification

- `src/MealMate.Application/Features/ProgramsSubscriptions/Pricing/PricingEngine.cs`: `PricingEngine.Calculate` calculated gateway gross-up into the customer amount, applied campaign, promo, and promotional wallet credits sequentially, rounded only after those deductions, and embedded a hard-coded 5% margin alert. Stage A changes now round list price before the single discount, estimate gateway cost separately, and accept an optional governed minimum-contribution threshold.
- `src/MealMate.Application/Features/ProgramsSubscriptions/Pricing/SubscriptionPackagePricingCalculator.cs`: the former single-restaurant path used fixed 4.5/6 daily-price thresholds; missing tiers fell back to the overall cohort average; average daily prices were rounded before later pricing. Stage A now classifies from observed cohort mean/SD, leaves both half-SD boundaries in the middle enum (`Platinum`), returns middle for zero-SD cohorts, rejects missing-tier pricing, and retains decimal precision.
- `src/MealMate.Application/Features/ProgramsSubscriptions/Pricing/RestaurantTierClassificationService.cs`: prior result bounds represented 2 SD even though tier assignment uses 0.5 SD. Stage A separates the returned tier bounds from the two-SD outlier calculation.
- `src/MealMate.Application/Features/ProgramsSubscriptions/Pricing/RestaurantReferencePriceGuard.cs` and new `DecimalPricingMath.cs`: the former guard used binary floating-point square root and floor/ceiling conversions. Stage A uses bounded decimal helpers; fractional curve exponents are supported up to three decimal places and higher precision is rejected instead of silently approximated.

## Existing pricing authority and source snapshots

- `src/MealMate.Application/Features/ProgramsSubscriptions/PricingAndClassificationHandlers.cs` serves pricing preview/catalog and includes the old `LockRestaurantPricingCommand` handler. Its pricing flow reads mutable subscription pricing settings even when a pricing rule version is present; the rule version is not yet a single typed approved policy source.
- `src/MealMate.Application/Features/ProgramsSubscriptions/SubscriptionCommandHandlers.cs` independently resolves price and creates subscriptions. It stores a pricing-rule reference but recalculates rather than consuming an immutable trusted quote.
- `src/MealMate.Domain/ProgramsSubscriptions/OfferCheckoutQuote.cs` and `src/MealMate.Application/Features/CustomerCheckout/OfferCheckoutQuoteService.cs` persist offer-checkout quote amounts, but they do not yet provide one shared authority for all individual, family, and campaign checkout flows.
- `src/MealMate.Application/Features/CustomerCheckout/CustomerCheckoutService.cs` validates an optional quote and independently obtains checkout pricing. `src/MealMate.Application/Features/FamilyCheckout/FamilyQuoteService.cs` composes member pricing; `FamilyCheckoutService.cs` freezes family checkout batches. These are distinct paths that must be explicitly routed through the approved pricing/quote authority.
- `src/MealMate.Domain/CollaborativeCampaigns/Services/CampaignPricingCalculator.cs`, `src/MealMate.Application/Features/CollaborativeCampaigns/Services/CustomerCampaignEvaluationService.cs`, and `.../Queries/CustomerCampaignOptionProjector.cs` implement a separate campaign-pricing path. Campaign budgets are not yet a durable quote-reservation aggregate.
- `src/MealMate.Application/Features/ProgramsSubscriptions/Pricing/RestaurantPricingWorkspaceService.cs` manages restaurant price proposals. `RestaurantBundlePricingController.cs` and `RestaurantPricingWorkspaceController.cs` expose restaurant pricing APIs. Publication must be atomic and must not be bypassed through the older lock command.

## Frontend surfaces

- Admin pricing surfaces include `src/app/features/admin/subscriptions/pages/pricing-workspace-page/` and `src/app/features/admin/marketing/components/campaign-customer-simulator/`. Add the governed review surface under `src/app/features/admin/finance/` rather than treating the simulator as a source of policy.
- Restaurant pricing surface is `frontend/restaurant-dashboard/src/app/features/restaurant/pricing/`. `data/pricing-api.service.ts` already uses the real pricing API. `data/pricing.facade.ts` and `data/pricing.mock.ts` still derive tier/commission display values from defaults and hard-coded thresholds, so F03 must display real values or a localized unavailable state.
- The project has typed bilingual UI localization instructions. New UI must render translations from the active locale and avoid caching translated strings in state.

## Test evidence and limits

- Stage A TDD behavior regressions were observed red before production changes: stacked discount sources were accepted; gateway fee assumptions changed the customer price; the exact half-SD boundary and one-item cohort were not Plus; and missing-tier pricing silently used another tier's average.
- Focused Stage A run passed 27/27 tests after the calculator changes.
- The wider affected unit filter was attempted after Stage A and exposed stale single-item threshold expectations plus a checkout test fixture whose intended Basic tier no longer had a supporting cohort. The two affected test files were authorized for fixture/assertion updates; the broad filter must be rerun after the concurrent compile issue below is cleared.
- A later broad unit run was blocked before tests by concurrent capacity source at `src/MealMate.Application/Features/DriverDelivery/Dispatcher/UpdateDriverSupportProgressCommand.cs(38,106)` (`DateTime?` passed where `DateTime` is required). That file is outside this phase's allowlist and was not modified.
- No live database migration or real checkout/payment-provider claim is made by this discovery. Stage B+ acceptance requires isolated PostgreSQL and actual HTTP/flow coverage.
