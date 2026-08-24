# Design: Individual Subscription Exclusivity Gate

> **Date:** 2026-08-24  
> **Status:** proposed  
> **Scope:** Block a second **individual** subscription for the same customer account  
> **Out of scope:** Individual → family manager / program-bundle upgrade conversion UI & flows (later sprint)

## 1. Problem

A customer with an existing individual subscription can still enter individual checkout and create another `Subscription` row. There is no exclusivity check in:

- `SubscriptionCheckoutReadinessService`
- `CustomerCheckoutController` / subscription flow
- `CreateSubscriptionCommand` handler

This risks duplicate calendars, capacity holds, payments, and fulfillment.

## 2. Product decisions (approved)

| Decision | Choice |
|----------|--------|
| Blocking statuses | Same as management “current”: `PendingPayment`, `Paid`, `Scheduled`, `Active`, `Frozen`, `SuspendedForReview` |
| Scope now | Gate only — no family conversion / upgrade screens |
| Family later | From individual: choose “become family manager” **or** “upgrade program/bundle” (separate design) |

## 3. Rule (precise)

A customer may have **at most one current individual subscription**.

**Blocking subscription** = row where:

```
CustomerUserId == customerId
AND FamilyGroupId IS NULL
AND Status IN (
  PendingPayment, Paid, Scheduled, Active, Frozen, SuspendedForReview
)
```

If such a row exists → **reject** any attempt to start / create another **individual** subscription.

**Allowed after:** `Completed`, `Cancelled`, `Expired`, `PaymentFailed`, `Upgraded` (and any non-blocking status).  
`CancellationPending` is **not** in the management current set → **does not block** (align with `IsCurrentStatus`).

### Why `FamilyGroupId IS NULL` matters

Family checkout creates **multiple** subscriptions under the **manager** `CustomerUserId`, then sets `FamilyGroupId` on each entity. A naive “any current subscription blocks create” would break the second family member create.

Exclusivity is therefore **individual-only**. Family batch create remains allowed when there is no blocking **individual** row.

### Dead `PendingPayment`

Existing hold-expiry jobs already release / expire abandoned checkouts. No new resume logic in this sprint. After the pending individual is no longer in a blocking status, a new individual checkout is allowed.

## 4. Approach

**Chosen: Approach 2 — central gate service + early UX + hard server enforce.**

| Approach | Verdict |
|----------|---------|
| 1. Check only in `CreateSubscriptionCommand` | Rejected — late failure after full funnel |
| **2. `ICustomerIndividualSubscriptionGate` + readiness + checkout + create** | **Selected** |
| 3. DB unique index only | Rejected — poor errors; family multi-row under same user |

## 5. Architecture

### 5.1 New service

```csharp
public interface ICustomerIndividualSubscriptionGate
{
    /// <summary>Returns the blocking individual subscription if any.</summary>
    Task<BlockingIndividualSubscription?> FindBlockingAsync(
        Guid customerUserId,
        CancellationToken ct = default);

    Task<bool> HasBlockingAsync(Guid customerUserId, CancellationToken ct = default);
}

public sealed record BlockingIndividualSubscription(
    Guid SubscriptionId,
    SubscriptionStatus Status,
    DateOnly? StartDate,
    DateOnly? EndDate);
```

Shared status set (single source of truth):

```csharp
public static class CurrentIndividualSubscriptionStatuses
{
    public static readonly SubscriptionStatus[] Blocking =
    [
        SubscriptionStatus.PendingPayment,
        SubscriptionStatus.Paid,
        SubscriptionStatus.Scheduled,
        SubscriptionStatus.Active,
        SubscriptionStatus.Frozen,
        SubscriptionStatus.SuspendedForReview,
    ];
}
```

Prefer extracting/sharing with `CustomerSubscriptionManagementController.IsCurrentStatus` to avoid drift (same set of status **names**).

### 5.2 Call sites (must all use the gate)

| Layer | Behavior |
|-------|----------|
| `SubscriptionCheckoutReadinessService.EvaluateAsync` | If blocking → `CanCheckout = false`, `CanOpenSummary = false`, `CheckoutBlockReason = checkout.existing_subscription`, include blocking subscription id in DTO (optional field) |
| `CustomerCheckoutController.Checkout` | Before reservation → `409 Conflict` with `error = checkout.existing_subscription` |
| `CustomerSubscriptionFlowController` readiness / summary / paths that lead to checkout | Same conflict / readiness block |
| `CreateSubscriptionCommand` handler | At start of handle: if blocking → `Error.Conflict("checkout.existing_subscription", ...)` — last line of defense for individual admin/API create |

**Family checkout:** does **not** need a bypass flag if the rule is `FamilyGroupId IS NULL` only. After the first family member subscription gets `FamilyGroupId`, it no longer counts as blocking individual. Before any family member exists, manager with an active **individual** is still blocked — correct for this sprint (conversion is later).

### 5.3 Error contract

| Code | HTTP | Meaning |
|------|------|---------|
| `checkout.existing_subscription` | 409 Conflict | Customer already has a current individual subscription |

Response body (checkout / flow):

```json
{
  "error": "checkout.existing_subscription",
  "subscriptionId": "<guid>",
  "status": "Active"
}
```

Readiness DTO:

- `CheckoutBlockReason = "checkout.existing_subscription"`
- Optional: `BlockingSubscriptionId`, `BlockingSubscriptionStatus` for mobile to deep-link to current subscription screen

### 5.4 Concurrency

Between readiness and create, two parallel checkouts could race. Mitigate by:

1. Acquiring an existing customer/payment lock if available (`ICapacityConcurrencyLock.AcquireSubscriptionAsync` pattern or a dedicated `AcquireCustomerIndividualCheckoutAsync(customerUserId)`), **and**
2. Re-checking `FindBlockingAsync` inside the checkout transaction **after** the lock, **before** `CreateSubscriptionCommand`.

### 5.5 Mobile / admin UX (minimal)

- Mobile: if readiness returns `checkout.existing_subscription` → hide/disable “اشتراك فردي” CTA; show current subscription card (existing home/management APIs).
- Admin create-subscription API: return same conflict (no silent second individual).
- **Not in this sprint:** family manager / upgrade choice screen.

## 6. Data / migrations

No schema change required. Query uses existing `Subscriptions` indexes (`CustomerUserId`, `Status`). Optionally add composite filtered index later if needed:

```sql
-- optional follow-up, not required for MVP
-- filtered unique on CustomerUserId WHERE FamilyGroupId IS NULL AND Status IN (...)
```

MVP does **not** add a unique filtered index (family/status edge cases + CancellationPending). Application gate + lock is enough for v1.

## 7. Testing

| # | Case | Expected |
|---|------|----------|
| T1 | Customer with `Active` individual | Checkout → 409 `checkout.existing_subscription` |
| T2 | Customer with `Frozen` individual | Same block |
| T3 | Customer with `PendingPayment` individual | Same block |
| T4 | Customer with only `Completed` / `Cancelled` / `PaymentFailed` | Checkout allowed |
| T5 | Readiness with Active individual | `CanCheckout=false`, block reason set |
| T6 | CreateSubscription with Active individual | Conflict error |
| T7 | Family checkout creating 2nd member after 1st has `FamilyGroupId` | **Not** blocked by this gate |
| T8 | Manager with Active individual, no FamilyGroupId | Family create also blocked until conversion sprint |

## 8. Files to touch (implementation plan input)

- `MealMate.Application/Features/CustomerCheckout/CustomerIndividualSubscriptionGate.cs` (new)
- `SubscriptionCheckoutReadinessService.cs` (+ DTO fields if needed)
- `SubscriptionCommandHandlers.cs` (`CreateSubscriptionCommand`)
- `CustomerCheckoutController.cs`
- `CustomerSubscriptionFlowController.cs` (readiness/summary entry points)
- `DependencyInjection.cs` (register gate)
- Unit/integration tests under Application or Api tests
- Optional: align `IsCurrentStatus` with shared helper
- Docs: short note in `docs/mobile-customer-subscription-flow.md` / checkout flow

## 9. Success criteria

1. Impossible to create a second current **individual** subscription via mobile checkout or create API.
2. Readiness surfaces the block before payment UI.
3. Family multi-member create for a manager **without** an individual current subscription still works.
4. Error code stable: `checkout.existing_subscription`.

## 10. Follow-ups (explicitly deferred)

1. Individual → family: “become manager only” vs “upgrade program/bundle” chooser.
2. Rules for converting/cancelling/freezing the existing individual during that conversion.
3. Whether a family **member** (linked user) may open an individual subscription.
4. Filtered unique DB constraint.
