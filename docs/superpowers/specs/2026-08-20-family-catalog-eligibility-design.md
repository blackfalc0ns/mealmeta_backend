# Family catalog eligibility + member limits — Design

- **Date:** 2026-08-20  
- **Status:** superseded by unified shared catalog (2026-09-30)  
- **Related:** `2026-08-19-family-subscription-checkout-contract.md`, `docs/mobile/family-subscription-flow-explained.md`  
- **Decision update (2026-09-30):** The family-only catalog filtering requirement (`AvailableForFamily`) has been superseded. Both individual and family subscription flows now expose the exact same programs and meal bundles from a single shared query source (`ISubscriptionFlowQueryService`) with identical IDs, metadata, slot structures, and pricing. Per-member delivery address coverage continues to be checked individually.

> [!IMPORTANT]
> **Superseded (2026-09-30):** Family-only catalog filtering has been retired. Both individual and family flows use the unified catalog with identical programs and bundles. Existing `AvailableForFamily` column remains in the schema but does not filter customer catalog visibility or plan eligibility.

---

## 1) Problem

1. Mobile/admin need a clear explanation of how the family subscription flow works (members, plans, checkout).  
2. Admin must control **family size** (`maxMembers`).  
3. Family catalog must be **distinct from individual** in what the customer *sees*, while reusing the same programs, bundles, meals, and Basic/Platinum/Elite classification — not a parallel product catalog.

---

## 2) Product rules (v1)

| Rule | Behavior |
| --- | --- |
| Same entities | Family plans reference existing `ProgramId` + `BundleId` + shared duration catalog + `RestaurantTier` |
| Family visibility | A program/bundle pair is offered in family flow only if **both** `Program.AvailableForFamily` and `Bundle.AvailableForFamily` are true (and program/bundle otherwise active/published as today) |
| Individual unchanged | Individual `subscription-flow` ignores family flags |
| Per-member plan | Each family member still chooses program+bundle+duration+tier independently from the **family-filtered** catalog |
| Member limit | Admin sets `family.maxMembers` (and existing discount / min-for-discount). Backend enforces on add-member and checkout |
| Pricing | Same customer pricing engine + family discount % on subtotal (existing). No separate family price tables in v1 |
| Meals / classification | Unchanged restaurant meal content and tier classification; family only filters which packages are choosable |

**Default for existing data after migration:** `AvailableForFamily = false` (admin must opt-in). Safer than suddenly exposing everything as “family catalog.”

---

## 3) Domain / persistence

### 3.1 Flags on existing entities

```csharp
// Program
public bool AvailableForFamily { get; set; } // default false

// Bundle
public bool AvailableForFamily { get; set; } // default false
```

EF migration only; no new tables in v1.

### 3.2 Family pricing settings (existing, tighten)

`FamilyPricingSettings` on pricing document remains:

- `discountPercent` (0–1)  
- `maxMembers` — backend validate **2–12** (raise current admin UI clamp of 4)  
- `minMembersForDiscount` — must be in `[2 … maxMembers]`

Frontend today clamps max to 4; change to match backend (e.g. 2–12).

---

## 4) Customer APIs

### 4.1 Keep member CRUD / checkout as today

Under `/api/v1/customer/family-subscription-flow` — no change to member profile shape (no goal/activity).

### 4.2 Family catalog (new or filtered)

Do **not** reuse raw individual list endpoints for family UI after this ships. Add family-scoped catalog:

| Method | Path | Behavior |
| --- | --- | --- |
| `GET` | `/api/v1/customer/family-subscription-flow/programs` | Active programs with `AvailableForFamily` that have ≥1 family-available bundle |
| `GET` | `.../programs/{programId}/bundles` | Bundles for program with `AvailableForFamily` |
| `GET` | `.../programs/{programId}/bundles/{bundleId}/durations` | Same duration rules as individual (reuse service) |
| `GET` | `.../programs/{programId}/bundles/{bundleId}/tiers` | Same tier preview as individual |
| `GET` | `.../tiers/{tier}/restaurants` | Same restaurant pool rules |

`PUT .../members/{id}/plan` must reject program/bundle not family-eligible → `family.catalog_not_available`.

Individual paths under `/api/v1/customer/subscription-flow/*` stay for INDIVIDUAL only.

### 4.3 Metadata

`GET .../metadata` already returns `maxMembers`, `discountPercent`, `minMembersForDiscount` — keep; mobile uses these for UI limits.

---

## 5) Admin APIs / UI

### 5.1 Pricing workspace (existing card)

- Keep family discount / min / max controls.  
- Widen max members control to **2–12**.  
- Persist via existing pricing settings document `family` object.

### 5.2 Program / bundle editors

- Toggle **Available for family** on program edit and bundle edit (admin dashboard).  
- Optional later: matrix page “Family catalog” listing program×bundle with bulk toggles — **not required for v1** if toggles exist on existing drawers.

### 5.3 Admin family groups list

Unchanged (ops view of live groups).

---

## 6) Mobile flow (summary)

See `docs/mobile/family-subscription-flow-explained.md` for Arabic walkthrough.

Sequence:

1. Choose FAMILY type  
2. `GET current` → manager seeded from profile  
3. Edit members (name, relationship, gender, age, address) within `maxMembers`  
4. For each member: family catalog programs → bundles → durations → tiers → `PUT plan`  
5. `GET summary` → payment methods → `POST checkout` → one MyFatoorah invoice → N subscriptions

---

## 7) Error codes (new)

| Code | When |
| --- | --- |
| `family.catalog_not_available` | Plan save uses program/bundle not enabled for family |
| `family.max_members` | Existing — add would exceed admin max |

---

## 8) Testing

- Unit: family catalog filter (program true + bundle false → hidden).  
- API: plan reject ineligible pair; add member at max+1 → 409.  
- Admin: save `AvailableForFamily` round-trip; maxMembers 8 persists and surfaces in metadata.  
- Regression: individual catalog unchanged when flags are false/true.

---

## 9) Out of scope (v1)

- Separate family duration or tier lists  
- Pre-built “family package SKUs” with fixed multi-member plans (approach B)  
- Different commission formulas for family vs individual  
- Auto-enabling all active bundles for family  

---

## 10) Implementation order (for later plan)

1. Doc already: flow explained (mobile)  
2. Migration + flags + admin toggles + raise maxMembers clamp  
3. Family catalog endpoints + plan eligibility check  
4. Flutter guide update: switch catalog base URL to family-subscription-flow  
5. Contract freeze update  

---

## Approval

Please confirm this design before an implementation plan / code changes.
