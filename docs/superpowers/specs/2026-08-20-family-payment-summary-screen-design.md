# Family payment summary screen fields — Design

- **Date:** 2026-08-20  
- **Status:** implemented (2026-08-20)  
- **Decision:** Enhance existing `GET .../family-subscription-flow/summary` (option A). No new endpoint. No VAT/tax line (mock-only).

## Goal

One response drives the payment UI: member strip, selected-member subscription summary on chip switch, family totals, and meal overview table.

## Contract

```http
GET /api/v1/customer/family-subscription-flow/summary
GET /api/v1/customer/family-subscription-flow/summary?memberId={guid}
```

Switching chips → same GET with new `memberId`. Totals + `mealOverview` unchanged; `selectedMember` / `selectedMemberId` change.

### Additions

**selectedMember:** `memberId`, `role`, `isManager`, `setupStatus`, `isReady`, `selectedTier`, `tierNameAr/En`, `durationLabelAr/En`, `programId`, `bundleId`, meal slot flags (`breakfast`…), `mealsPerDay` (keep existing name/price/count fields).

**mealOverview[]:** `setupStatus`, `isReady`, `isManager` (keep breakfast/lunch/snack/dinner/`mealsPerDay`).

**Pricing:** unchanged `subtotal`, `familyDiscountPercent`, `familyDiscountAmount`, `finalPrice`, `currencyCode`.

Payment methods remain `GET /payment-methods?amount={finalPrice}`.
