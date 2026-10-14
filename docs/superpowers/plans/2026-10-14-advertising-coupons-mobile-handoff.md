# Advertising and coupons mobile handoff implementation plan

> Execute each change with a failing regression test, then review the final contracts and handoff together. Use superpowers:subagent-driven-development for the documentation task.

**Goal:** Complete gaps in the existing customer advertising/offers/coupon flow and deliver one Markdown integration file per mobile screen.

**Architecture:** Keep the current advertising and subscription checkout services. Extend offer DTOs additively with navigation scope; retain authoritative server pricing and persisted sources. The mobile application itself is outside this repository.

**Tech Stack:** .NET 9, EF Core, PostgreSQL, xUnit; Markdown handoff for the mobile client.

**Spec:** User-approved scope in this chat: complete missing functionality and prepare mobile integration files. Existing business rules: `../specs/2026-10-09-advertising-flows-design.md`.

## Constraints

- Preserve existing user changes; no wholesale staging, reset, seed, production writes or deployment.
- Arabic and English product copy belongs in existing API resources; mobile instructions cover reactive switching and both bilingual data fields.
- Prices and eligibility come from server-side services; retain KWD and business timezone from application contracts.
- Distinguish paid advertising, campaign checkout, and coupon-based individual subscription checkout.

## Tasks

- [x] Add regression tests: disabled advertising space must not serve placements; creative route auction ID overrides body; campaign projections must have stable IDs; offer navigation scopes reach mobile consumers; targeted customer offer details cannot disclose inactive/ineligible offers.
- [x] Fix customer advertising space filter and route ID binding, without changing auction/payment policy. Also reproduce/fix empty restaurant destination IDs from the existing UI and whitespace event-ID replays.
- [x] Extend `OfferDto` with eligible program, bundle and restaurant IDs and a semantic source; map persisted relations and campaign-derived data. Supply a customer detail query/route using customer eligibility and server clock. Use campaign IDs for campaign projections and preserve persisted offer IDs. The new envelope route is `GET /api/v1/customer/offers/{offerId}/details`; the existing authenticated flat `GET /{id}` remains compatible and now customer-safe.
- [x] Reproduce and fix coupon-code normalization under non-English process culture, retaining the code contract. Verify summary evaluation is read-only and checkout holds are unchanged.
- [x] Create `docs/mobile/advertising-coupons-screens/README.md` and separate screen files for home ads, offers hub, offer details, program, bundle/duration/tier, prerequisites, coupon/summary, payment, payment result, and campaign checkout. Include actual methods/routes, fields, auth, transitions, Arabic/English switching, and recovery states. Link existing family documentation where a separate family flow applies.
- [x] Run scoped unit and API regressions, coupon checkout regressions, backend build, localization audit/self-test and resource parity. Review the final diff and verify all Markdown links. Fix advertising navigation copy by referencing its existing dictionary and classify technical API identifiers with narrow exceptions; never expand the legacy baseline.

## Verification commands

```powershell
dotnet test tests/MealMate.UnitTests/MealMate.UnitTests.csproj --filter "FullyQualifiedName~AdvertisingLifecycleTests|FullyQualifiedName~OfferQueriesTests|FullyQualifiedName~CampaignOfferSyncTests|FullyQualifiedName~SubscriptionFlowCouponServiceTests"
dotnet test tests/MealMate.ApiTests/MealMate.ApiTests.csproj --filter "FullyQualifiedName~AdvertisingMobileBoundaryTests|FullyQualifiedName~CheckoutCouponFlowTests|FullyQualifiedName~OffersControllerTests"
dotnet build MealMate.sln --no-restore
node scripts/i18n-audit.cjs --self-test
node scripts/i18n-audit.cjs
git diff --check
```

Runtime mobile acceptance remains for the mobile repository/device: ar → en → ar with an open coupon error, persisted locale, RTL/LTR, expired ad refresh, payment failure/resume and success confirmed from the backend.

The campaign reference file records a separate existing gap: campaign session creation reserves capacity but does not return a provider payment URL or expose customer payment status. This task does not claim that campaign payment initiation is implemented. Execution evidence: `../reports/2026-10-14-advertising-coupons-mobile-handoff.md`.
