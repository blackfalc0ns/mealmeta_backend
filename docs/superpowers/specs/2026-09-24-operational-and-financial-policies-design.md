# Operational & Financial Policies Engine (Cutoff, Cancellation Fees, Wallet Refund & Freeze)
## Design Specification

**Document:** `docs/superpowers/specs/2026-09-24-operational-and-financial-policies-design.md`  
**Date:** September 24, 2026  
**Status:** Validated Design Spec  
**Target Solution:** `f:\MealMate\backend` & `f:\MealMate\frontend\admin-dashboard`

---

## 1. Executive Summary & Objectives

Currently, critical operational deadlines and cancellation refund calculations are hardcoded across multiple services:
- **Lock Cutoff Time:** Hardcoded at `-72 hours` in [`SubscriptionFlowServices.cs`](file:///f:/MealMate/backend/src/MealMate.Application/Features/CustomerCheckout/SubscriptionFlowServices.cs#L120).
- **Cancellation Refunds:** Refunds 100% of non-kitchen days without any administrative or operational fee deduction in [`SubscriptionCancellationService.cs`](file:///f:/MealMate/backend/src/MealMate.Application/Features/ProgramsSubscriptions/Cancellation/SubscriptionCancellationService.cs), and only records JSON metadata without actually crediting the customer's cash wallet.
- **Freeze Controls:** Minimum and maximum limits are managed via separate defaults without an accessible admin interface.

### Core Objectives
1. **Dynamic Auto-Lock Cutoff Control:** Allow admins to configure cutoff lead time (e.g. 72h, 48h, 24h) with an optional fixed daily cutoff hour (e.g. 14:00 Kuwait time).
2. **Configurable Cancellation Fee Policy:** Allow admins to choose between Percentage fee (e.g., 10%), Fixed amount fee (e.g., 5.000 KWD), or None (100% refund).
3. **Automated Wallet Credit:** When a subscription is cancelled, immediately credit the net refund to the customer's [`CustomerCashWallet`](file:///f:/MealMate/backend/src/MealMate.Domain/CustomerFinance/CustomerCashWallet.cs) and log an immutable [`WalletTransaction`](file:///f:/MealMate/backend/src/MealMate.Domain/CustomerFinance/WalletTransaction.cs).
4. **Kitchen Commitment Protection:** Days inside the cutoff window remain committed to the kitchen (0% refund, cooked and delivered), preserving partner restaurant trust and preventing food waste.
5. **Centralized Admin Management:** Provide unified APIs and a dedicated Admin Dashboard UI for live preview, configuration, and audit logging.

---

## 2. Architecture & Data Model

### 2.1 Persistence Layer (`SystemSettings`)
The configuration is stored in the existing `SystemSettings` table with a stable unique key:
`operational_policies.settings`.

```json
{
  "cutoff": {
    "cutoffLeadHours": 72,
    "enableFixedDailyHour": false,
    "fixedDailyHourKuwait": 14,
    "kitchenConfirmSlaHours": 24
  },
  "cancellation": {
    "feeType": "Percentage",
    "feeValue": 10.0,
    "refundToCashWallet": true,
    "kitchenLockedRefundPercentage": 0.0
  },
  "freeze": {
    "leadDaysNotice": 3,
    "minDays": 3,
    "maxDays": 14,
    "maxOccurrences": 2,
    "extendSubscriptionEnd": true
  }
}
```

### 2.2 Strongly-Typed Domain Records & Policy Models

```csharp
namespace MealMate.Application.Features.AdminDashboard.OperationalPolicies;

public enum CancellationFeeType
{
    None = 0,
    Percentage = 1,
    FixedAmount = 2
}

public sealed record CutoffPolicySettings(
    int CutoffLeadHours = 72,
    bool EnableFixedDailyHour = false,
    int FixedDailyHourKuwait = 14,
    int KitchenConfirmSlaHours = 24
);

public sealed record CancellationPolicySettings(
    CancellationFeeType FeeType = CancellationFeeType.Percentage,
    decimal FeeValue = 10.0m,
    bool RefundToCashWallet = true,
    decimal KitchenLockedRefundPercentage = 0.0m
);

public sealed record FreezePolicySettings(
    int LeadDaysNotice = 3,
    int MinDays = 3,
    int MaxDays = 14,
    int MaxOccurrences = 2,
    bool ExtendSubscriptionEnd = true
);

public sealed record OperationalPolicySettings(
    CutoffPolicySettings Cutoff,
    CancellationPolicySettings Cancellation,
    FreezePolicySettings Freeze
)
{
    public static OperationalPolicySettings Default => new(
        new CutoffPolicySettings(),
        new CancellationPolicySettings(),
        new FreezePolicySettings()
    );
}
```

### 2.3 Caching & Service Layer (`IOperationalPolicyService`)
To ensure high-throughput execution in calendar queries and checkout:
- Policies are cached in `IMemoryCache` with a 15-minute sliding expiration.
- Updates from the Admin API immediately invalidate the cache key.
- Fallback to safe defaults if the database entry is missing.

```csharp
public interface IOperationalPolicyService
{
    Task<OperationalPolicySettings> GetSettingsAsync(CancellationToken ct = default);
    Task<Result<OperationalPolicySettings>> UpdateSettingsAsync(OperationalPolicySettings newSettings, Guid adminUserId, CancellationToken ct = default);
    DateTime CalculateSelectionDeadlineUtc(DateOnly serviceDate, OperationalPolicySettings? settings = null);
}
```

---

## 3. Dynamic Cutoff Calculation Logic

The Kuwait time zone (`Asia/Kuwait` / `Arabian Standard Time`, UTC+3) is used as the operational baseline.

### 3.1 Standard Cutoff Hours (e.g. 72h, 48h, 24h)
```csharp
// When EnableFixedDailyHour is false:
var localMidnight = DateTime.SpecifyKind(serviceDate.ToDateTime(TimeOnly.MinValue), DateTimeKind.Unspecified);
var utcMidnight = TimeZoneInfo.ConvertTimeToUtc(localMidnight, KuwaitTimeZone);
return utcMidnight.AddHours(-policy.CutoffLeadHours);
```

### 3.2 Fixed Daily Cutoff Hour (e.g. 14:00 Kuwait Time)
```csharp
// When EnableFixedDailyHour is true:
// Example: If CutoffLeadHours is 72h (3 days), service on Sunday locks at 14:00 on Thursday.
var cutoffDate = serviceDate.AddDays(-(policy.CutoffLeadHours / 24));
var localCutoff = DateTime.SpecifyKind(cutoffDate.ToDateTime(new TimeOnly(policy.FixedDailyHourKuwait, 0)), DateTimeKind.Unspecified);
return TimeZoneInfo.ConvertTimeToUtc(localCutoff, KuwaitTimeZone);
```

### 3.3 Integration Points
- **`SubscriptionFlowServices.GetSelectionDeadlineUtc(serviceDate)`**: Calls `IOperationalPolicyService.CalculateSelectionDeadlineUtc`.
- **`SubscriptionDayLockJob`**: Compares `SelectionDeadlineUtc <= now` (now dynamically controlled by policy).

---

## 4. Cancellation & Financial Wallet Refund Engine

### 4.1 Financial Calculation Specification

Given a subscription with:
- Total Days: $D_{total}$
- Paid Amount: $A_{paid}$
- Daily Box Price: $P_{day} = \text{Round}(A_{paid} / D_{total}, 3)$
- Delivered Days: $D_{delivered}$
- Locked Kitchen Days: $D_{locked}$ (inside cutoff window)
- Refundable Days: $D_{refundable} = \text{Days beyond cutoff}$

1. **Gross Refund:**
   $$\text{GrossRefund} = D_{refundable} \times P_{day}$$

2. **Cancellation Fee Deduction:**
   - If $\text{FeeType} == \text{Percentage}$:
     $$\text{Fee} = \text{Round}\left(\text{GrossRefund} \times \frac{\text{FeeValue}}{100}, 3\right)$$
   - If $\text{FeeType} == \text{FixedAmount}$:
     $$\text{Fee} = \min(\text{GrossRefund}, \text{FeeValue})$$
   - If $\text{FeeType} == \text{None}$:
     $$\text{Fee} = 0$$

3. **Net Refund to Customer:**
   $$\text{NetRefund} = \max(0, \text{GrossRefund} - \text{Fee})$$

### 4.2 Cash Wallet Automatic Credit Flow

When `cancellation.RefundToCashWallet == true` and `NetRefund > 0`:
1. Find or create [`CustomerCashWallet`](file:///f:/MealMate/backend/src/MealMate.Domain/CustomerFinance/CustomerCashWallet.cs) for `subscription.CustomerUserId`.
2. Increment `wallet.Balance.Amount += NetRefund`.
3. Add a [`WalletTransaction`](file:///f:/MealMate/backend/src/MealMate.Domain/CustomerFinance/WalletTransaction.cs):
   - `TransactionType = WalletTransactionType.Credit`
   - `Amount = NetRefund`
   - `CurrencyCode = subscription.PaidAmount.CurrencyCode`
   - `ReferenceType = "SubscriptionCancellation"`
   - `ReferenceId = subscription.Id`
   - `Notes = $"Refund for cancelled subscription {subscription.Id}. Gross: {GrossRefund:0.000}, Fee: {Fee:0.000}, Net: {NetRefund:0.000}"`
4. Publish push notification to customer informing them of the wallet credit.

---

## 5. API Surface & Contracts

### 5.1 Admin Endpoints (`/api/v1/admin/operational-policies`)
- `GET /api/v1/admin/operational-policies`: Returns `OperationalPolicySettingsDto`
- `PUT /api/v1/admin/operational-policies`: Accepts `UpdateOperationalPoliciesRequest`, validates input, updates `SystemSettings`, audits the action, and invalidates cache.

### 5.2 Validation Rules
- `CutoffLeadHours`: Must be between 12 and 168 hours (12h to 7 days).
- `FixedDailyHourKuwait`: Must be between 0 and 23.
- `FeeValue`: If Percentage, must be between 0.0% and 50.0%. If FixedAmount, must be $\ge 0$.
- `FreezePolicy`: `MinDays` $\ge 1$, `MaxDays` $\le 30$, `LeadDaysNotice` $\ge 0$.

---

## 6. Admin Dashboard Frontend (`admin-dashboard`)

A new view under **Settings > Operational Policies** (`/settings/operational-policies`):
- **Cutoff Policy Card:**
  - Slider/Chips for Lead Hours (24h, 48h, 72h).
  - Toggle for Fixed Daily Cutoff Hour with Time Picker.
- **Cancellation & Refunds Card:**
  - Fee Type Selector (None / Percentage % / Fixed KWD).
  - Fee Input field with real-time **Calculator Preview** (e.g., *"On a 100 KWD refundable balance, fee is 10 KWD, customer receives 90 KWD"*).
  - Toggle: "Refund directly to customer Cash Wallet".
- **Freeze Policy Card:**
  - Lead Notice days, Min days, Max days, Max occurrences.
- **Audit History & Save:**
  - Shows when policies were last modified and by whom.
  - "Save Policies" button with confirmation modal.

---

## 7. Testing & Verification Strategy

Following **Test-Driven Development (TDD)**:
1. **Unit Tests (`OperationalPolicyServiceTests.cs`):**
   - Test default policy fallback when database has no entry.
   - Test cutoff deadline calculation for absolute hours.
   - Test cutoff deadline calculation for fixed daily hour (14:00 Kuwait time).
2. **Cancellation & Wallet Tests (`SubscriptionCancellationPolicyTests.cs`):**
   - Test percentage fee deduction (e.g. 10% deduction).
   - Test fixed fee deduction (e.g. 5 KWD deduction).
   - Test that `CustomerCashWallet` is credited and `WalletTransaction` is created.
   - Test that kitchen-locked days remain locked with 0% refund.
3. **Admin Controller & Security Tests (`OperationalPoliciesControllerTests.cs`):**
   - Verify `Admin` authorization gate (Customer/Restaurant cannot access).
   - Verify input validation guards (invalid percentages or out-of-range hours).
