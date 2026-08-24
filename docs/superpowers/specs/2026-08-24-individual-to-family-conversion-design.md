# Design: Individual → Family Conversion (Phase 1)

> **Date:** 2026-08-24  
> **Status:** proposed  
> **Depends on:** `2026-08-24-individual-subscription-exclusivity-design.md` (second individual blocked)

## 1. Problem

A customer with a current **individual** subscription cannot start family checkout cleanly:

- Individual exclusivity correctly returns `checkout.existing_subscription`.
- There is no product path to **convert** that subscription into a family where the customer becomes **manager**.
- The user also wants a chooser: family-as-manager **or** change individual plan (program/bundle/tier). The second path is **Phase 2**.

## 2. Product decisions (locked)

| Decision | Choice |
|----------|--------|
| Chooser UX | **D** — two options only: (1) become family manager, (2) change individual plan |
| Existing individual on family path | **A** — convert to manager membership; calendar continues; **no new payment for manager** |
| Delivery approach | **Phase 1** = chooser API + convert-to-family-manager + family checkout pays **new members only**. Phase 2 = change individual plan |
| Locked days | Days in `Locked72h` and later are **immutable** during conversion |

## 3. Scope

### In Phase 1

1. Surface conversion options when customer has a blocking individual subscription.
2. `POST convert-from-individual` → `FamilyGroup` Draft + link existing subscription as Manager.
3. Reuse existing family-subscription-flow (members, plans, summary, checkout).
4. Family checkout pricing: manager line = 0; charge new members only (+ family package discount).
5. Mobile docs for the chooser + convert endpoint.
6. Gate: customer with Draft family-from-conversion is not offered a second individual checkout (already blocked by individual exclusivity until `FamilyGroupId` is set — after convert, subscription has `FamilyGroupId` so exclusivity allows family path).

### Out of Phase 1

- Change individual program/bundle/tier (Phase 2) — option visible with `available: false` or omitted from enabled actions.
- Refunds / prorations beyond “manager already paid”.
- Detach member / convert member to individual (existing F19 flows).
- Flutter UI implementation (API + docs first unless requested).
- Admin override tooling.

## 4. Approaches considered

| Approach | Verdict |
|----------|---------|
| **1. Phase 1 convert + Phase 2 plan change** | **Selected** |
| 2. Both paths fully in one sprint | Rejected — accounting/calendar risk |
| 3. UI stub only | Rejected — breaks trust |

## 5. Domain model impact

### Existing entities (reuse)

- `Subscription` — set `FamilyGroupId`, `FamilyMemberId` on convert; status stays Active/Scheduled/Frozen as-is.
- `FamilyGroup` — created as `Draft`; `ManagerCustomerUserId` = customer; optional link to source subscription id in metadata or `PaymentAnchorSubscriptionId` after paid family checkout.
- `FamilyMember` — Manager row hydrated from customer profile + plan copied from individual subscription (program/bundle/tier/duration/includeFriday).

### New / optional fields (minimal)

Prefer **no migration** if possible:

- Store conversion marker on `FamilyGroup` via existing JSON/metadata if present, **or**
- Add nullable `ConvertedFromSubscriptionId` on `FamilyGroup` (preferred if no metadata column) for audit and idempotency.

**Idempotency:** second `POST convert-from-individual` while Draft exists for same manager → return existing `FamilyGroupScreenDto` (no duplicate groups).

### Blocking statuses for conversion source

Same “current individual” set as exclusivity gate, **and** `FamilyGroupId == null`:

`PendingPayment`, `Paid`, `Scheduled`, `Active`, `Frozen`, `SuspendedForReview`

**Policy:** `PendingPayment` — **not convertible** (must finish/expire payment first) → `conversion.individual_not_convertible`.  
Convertible: `Paid`, `Scheduled`, `Active`, `Frozen`, `SuspendedForReview`.

## 6. State machine

```
[Individual current, FamilyGroupId null]
        |
        | POST convert-from-individual
        v
[FamilyGroup Draft] + [Subscription linked as Manager]
        |
        | add members / set plans (existing APIs)
        v
[All required Ready + minMembers]
        |
        | POST family checkout
        v
[FamilyGroup CheckoutPending] → payment
        v
[FamilyGroup Active] + member subscriptions for NEW members only
  Manager keeps SAME Subscription id (no second create for manager)
```

### Family checkout change (critical)

Today `FamilyCheckoutService` creates a `CreateSubscriptionCommand` **per member including manager**.

Phase 1 must change that:

- **Manager:** skip create; attach existing converted subscription; ensure `FamilyGroupId` / `FamilyMemberId` set; do **not** create capacity holds that double-book manager days already held.
- **New members:** create subscriptions + holds as today.
- Payment amount = quote of **new members only** (after family discount allocation rules — document exact formula in implementation).

## 7. API contract

### 7.1 Types / options

`GET /api/v1/customer/subscription-flow/types`

When customer has convertible/blocking individual:

```json
{
  "types": [
    {
      "code": "INDIVIDUAL",
      "enabled": false,
      "disabledReason": "checkout.existing_subscription"
    },
    {
      "code": "FAMILY",
      "enabled": true,
      "requiresConversion": true,
      "conversionEndpoint": "/api/v1/customer/subscriptions/current/conversion-options"
    }
  ]
}
```

(Exact DTO field names follow existing `SubscriptionTypeOptionDto` style; extend rather than break clients.)

### 7.2 Conversion options

`GET /api/v1/customer/subscriptions/current/conversion-options`

```json
{
  "subscriptionId": "...",
  "status": "Active",
  "options": [
    {
      "code": "CONVERT_TO_FAMILY_MANAGER",
      "available": true,
      "titleAr": "أعمل عائلة وأبقى مدير",
      "titleEn": "Create a family and become manager",
      "action": "POST /api/v1/customer/family-subscription-flow/convert-from-individual"
    },
    {
      "code": "CHANGE_INDIVIDUAL_PLAN",
      "available": false,
      "disabledReason": "change_individual_plan.not_available",
      "titleAr": "أغيّر خطتي الفردية",
      "titleEn": "Change my individual plan"
    }
  ]
}
```

### 7.3 Convert

`POST /api/v1/customer/family-subscription-flow/convert-from-individual`

Body (optional):

```json
{
  "name": "عائلة أحمد",
  "familyPackageId": "<guid>"
}
```

Success: `200` + existing `FamilyGroupScreenDto` (manager member Ready or InProgress with plan copied).

Errors:

| Code | When |
|------|------|
| `conversion.no_individual_subscription` | No convertible individual |
| `conversion.already_in_family` | Already has FamilyGroupId / active family draft owned |
| `conversion.individual_not_convertible` | PendingPayment or invalid status |
| `family.package_required` | If package required by upsert rules |

### 7.4 Summary / checkout

Extend family summary DTO:

- `managerPrepaid: true`
- `managerSubscriptionId`
- `newMembersCount`
- `newMembersSubtotal`
- `familyDiscount`
- `finalPrice` (payable)

Checkout rejects `finalPrice == 0` only when `minMembers` not met; if somehow only manager and minMembers=1 package exists, product still requires package rules from admin — **do not invent zero-pay activate** unless package allows 1 member (then conversion alone may activate without payment — edge: if `minMembers == 1` and only manager Ready, allow `POST checkout` with `finalPrice=0` to flip group Active without gateway, **or** auto-activate on convert. **Decision for Phase 1:** keep existing package `minMembers` (typically ≥2); conversion leaves Draft until enough members + paid checkout.

## 8. Exclusivity interaction

After convert:

- Manager subscription has `FamilyGroupId != null` → **not** a blocking individual → exclusivity gate allows no second individual still? Wait: exclusivity checks `FamilyGroupId == null`. Converted manager no longer blocks as individual. Customer could start a **new** individual while Draft family exists — **gap**.

**Phase 1 fix:** exclusivity (or a sibling check) also blocks individual checkout if customer owns a `FamilyGroup` in `Draft` / `CheckoutPending` / `Active` / `Frozen` as manager.

Add to gate or readiness:

`checkout.family_group_in_progress` or reuse `checkout.existing_subscription` with clearer code `checkout.existing_family_or_subscription`.

**Preferred:** extend gate:

1. Blocking individual (`FamilyGroupId == null` + statuses), **or**
2. Manager of FamilyGroup in Draft/CheckoutPending/Active/Frozen.

## 9. Testing (acceptance)

| # | Case | Expected |
|---|------|----------|
| T1 | Active individual → conversion-options | Both options; family available; plan change unavailable |
| T2 | POST convert | Draft family; subscription linked; manager plan copied |
| T3 | Idempotent convert | Same group returned |
| T4 | PendingPayment individual | `conversion.individual_not_convertible` |
| T5 | Add 1 new member + checkout | Payment = new member only; manager subscription id unchanged |
| T6 | Locked72h days | Unchanged after convert |
| T7 | After convert, individual checkout | Blocked (family draft/active) |
| T8 | Family create without convert when no individual | Existing flow unchanged |

## 10. Docs

Update:

- `docs/mobile/family-subscription-api.md` — conversion section
- `docs/mobile-customer-subscription-flow.md` — types + exclusivity + conversion pointer
- Spec Phase 2 stub paragraph only

## 11. Success criteria

1. Customer with individual can choose “become family manager” and continue family flow without paying again for themselves.
2. New members are the only payable lines at family checkout.
3. No duplicate manager subscription rows.
4. Individual exclusivity remains intact and also covers in-progress family conversion.
5. Change-individual-plan clearly deferred (`available: false`).

## 12. Phase 2 (deferred detail)

`CHANGE_INDIVIDUAL_PLAN`: quote delta for program/bundle/tier/duration change on same subscription; pay delta; regenerate future open days’ meal snapshots only; never rewrite locked days.
