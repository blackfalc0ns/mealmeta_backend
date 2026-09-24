# Operational & Financial Policies Engine Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Build a dynamic Admin Management engine for Operational & Financial Policies, enabling admins to configure auto-lock cutoff times (24h/48h/72h or fixed daily cutoff hour), cancellation fee deductions (percentage/fixed/free), automated wallet refunds (`CustomerCashWallet` + `WalletTransaction`), and subscription freeze limits.

**Architecture:** A domain-driven policy engine backed by `SystemSettings` with in-memory caching and cache invalidation. `IOperationalPolicyService` acts as the single source of truth for cutoff deadlines in `SubscriptionFlowServices` and financial deductions/wallet credits in `SubscriptionCancellationService`. Admin endpoints provide validation, auditing, and real-time calculation previews.

**Tech Stack:** C# .NET 9, EF Core, MediatR, FluentValidation, FluentAssertions, xUnit, Angular 18, TailwindCSS.

**Spec:** [`docs/superpowers/specs/2026-09-24-operational-and-financial-policies-design.md`](file:///f:/MealMate/backend/docs/superpowers/specs/2026-09-24-operational-and-financial-policies-design.md)

## Global Constraints
- Target workspace: `f:\MealMate\backend` (Clean Architecture C# solution) and `f:\MealMate\frontend\admin-dashboard`.
- Test Framework: xUnit with FluentAssertions. Every task must follow strict TDD (failing test first, minimal code, green, commit).
- TimeZone: Kuwait Local Time (`Arabian Standard Time` / `Asia/Kuwait`, UTC+3).
- Currency: Kuwaiti Dinar (`KWD`), precision 3 decimal places.
- Storage Key: `operational_policies.settings` in `SystemSettings` table.
- Existing Test Suite: Must maintain 100% pass rate (1,126+ tests passing).

---

### Task 1: Policy Models & `IOperationalPolicyService` with In-Memory Caching

**Files:**
- Create: `src/MealMate.Application/Features/AdminDashboard/OperationalPolicies/OperationalPolicyModels.cs`
- Create: `src/MealMate.Application/Features/AdminDashboard/OperationalPolicies/IOperationalPolicyService.cs`
- Create: `src/MealMate.Application/Features/AdminDashboard/OperationalPolicies/OperationalPolicyService.cs`
- Modify: `src/MealMate.Application/DependencyInjection.cs`
- Test: `tests/MealMate.UnitTests/OperationalPolicies/OperationalPolicyServiceTests.cs`

**Interfaces:**
- Produces:
  - `enum CancellationFeeType { None = 0, Percentage = 1, FixedAmount = 2 }`
  - `record CutoffPolicySettings(int CutoffLeadHours = 72, bool EnableFixedDailyHour = false, int FixedDailyHourKuwait = 14, int KitchenConfirmSlaHours = 24)`
  - `record CancellationPolicySettings(CancellationFeeType FeeType = CancellationFeeType.Percentage, decimal FeeValue = 10.0m, bool RefundToCashWallet = true, decimal KitchenLockedRefundPercentage = 0.0m)`
  - `record FreezePolicySettings(int LeadDaysNotice = 3, int MinDays = 3, int MaxDays = 14, int MaxOccurrences = 2, bool ExtendSubscriptionEnd = true)`
  - `record OperationalPolicySettings(CutoffPolicySettings Cutoff, CancellationPolicySettings Cancellation, FreezePolicySettings Freeze)`
  - `interface IOperationalPolicyService`:
    - `Task<OperationalPolicySettings> GetSettingsAsync(CancellationToken ct = default);`
    - `Task<Result<OperationalPolicySettings>> UpdateSettingsAsync(OperationalPolicySettings newSettings, Guid adminUserId, CancellationToken ct = default);`
    - `DateTime CalculateSelectionDeadlineUtc(DateOnly serviceDate, OperationalPolicySettings? settings = null);`

- [ ] **Step 1: Write the failing unit tests for `OperationalPolicyService`**

Create `tests/MealMate.UnitTests/OperationalPolicies/OperationalPolicyServiceTests.cs` verifying:
1. Returns default settings when database has no entry.
2. Calculates deadline correctly for absolute hours (e.g. 72h before Kuwait midnight).
3. Calculates deadline correctly for fixed daily hour (e.g. 14:00 Kuwait time on cutoff day).
4. Updates settings, persists JSON in `SystemSettings`, audits the update, and returns success.

- [ ] **Step 2: Run test to verify it fails**

Run: `dotnet test tests/MealMate.UnitTests/MealMate.UnitTests.csproj --filter "FullyQualifiedName~OperationalPolicyServiceTests"`
Expected: FAIL with compilation error (types not found).

- [ ] **Step 3: Implement `OperationalPolicyModels.cs` and `IOperationalPolicyService`**

Implement strongly-typed record definitions, JSON serialization defaults, and validation guards.

- [ ] **Step 4: Implement `OperationalPolicyService.cs`**

Implement `OperationalPolicyService` using `IAppDbContext`, `IMemoryCache`, `IDateTimeProvider`, and `IAuditLogger`. Register in `DependencyInjection.cs`.

- [ ] **Step 5: Run tests and verify they pass**

Run: `dotnet test tests/MealMate.UnitTests/MealMate.UnitTests.csproj --filter "FullyQualifiedName~OperationalPolicyServiceTests"`
Expected: PASS (4/4 passed).

- [ ] **Step 6: Commit**

```bash
git add src/MealMate.Application/Features/AdminDashboard/OperationalPolicies/ tests/MealMate.UnitTests/OperationalPolicies/ src/MealMate.Application/DependencyInjection.cs
git commit -m "feat(ops): implement IOperationalPolicyService with dynamic cutoff and caching"
```

---

### Task 2: Connect Dynamic Cutoff Engine to Calendar & Lock Job

**Files:**
- Modify: `src/MealMate.Application/Features/CustomerCheckout/SubscriptionFlowServices.cs:116-125`
- Modify: `src/MealMate.Application/Features/CustomerCalendar/CustomerCalendarEngine.cs`
- Test: `tests/MealMate.UnitTests/OperationalPolicies/DynamicCutoffIntegrationTests.cs`

**Interfaces:**
- Consumes: `IOperationalPolicyService.CalculateSelectionDeadlineUtc(DateOnly serviceDate)`
- Modifies: `ISubscriptionScheduleService.GetSelectionDeadlineUtc` to call `_policyService.CalculateSelectionDeadlineUtc(serviceDate)`.

- [ ] **Step 1: Write failing integration test for dynamic cutoff calculation**

Create `tests/MealMate.UnitTests/OperationalPolicies/DynamicCutoffIntegrationTests.cs`:
1. When cutoff policy is set to 48 hours, `_schedule.GetSelectionDeadlineUtc` returns 48 hours before Kuwait midnight.
2. When cutoff policy enables fixed daily hour (14:00 Kuwait time), `_schedule.GetSelectionDeadlineUtc` returns 14:00 local time converted to UTC.

- [ ] **Step 2: Run test to verify it fails**

Run: `dotnet test tests/MealMate.UnitTests/MealMate.UnitTests.csproj --filter "FullyQualifiedName~DynamicCutoffIntegrationTests"`
Expected: FAIL (currently hardcoded to -72h).

- [ ] **Step 3: Update `SubscriptionScheduleService` in `SubscriptionFlowServices.cs`**

Inject `IOperationalPolicyService` and replace hardcoded `-72` hours with `_policyService.CalculateSelectionDeadlineUtc(serviceDate)`.

- [ ] **Step 4: Run tests to verify they pass**

Run: `dotnet test tests/MealMate.UnitTests/MealMate.UnitTests.csproj --filter "FullyQualifiedName~DynamicCutoffIntegrationTests"`
Expected: PASS.

- [ ] **Step 5: Commit**

```bash
git add src/MealMate.Application/Features/CustomerCheckout/SubscriptionFlowServices.cs tests/MealMate.UnitTests/OperationalPolicies/DynamicCutoffIntegrationTests.cs
git commit -m "feat(calendar): wire dynamic cutoff policy into SubscriptionScheduleService"
```

---

### Task 3: Cancellation Fee Calculation & Automated Wallet Credit Engine

**Files:**
- Modify: `src/MealMate.Application/Features/ProgramsSubscriptions/Cancellation/SubscriptionCancellationService.cs`
- Modify: `src/MealMate.Application/Features/ProgramsSubscriptions/Cancellation/SubscriptionCancellationContracts.cs`
- Test: `tests/MealMate.UnitTests/OperationalPolicies/SubscriptionCancellationPolicyTests.cs`

**Interfaces:**
- Consumes:
  - `IOperationalPolicyService.GetSettingsAsync()`
  - `IAppDbContext.CustomerCashWallets`
  - `IAppDbContext.WalletTransactions`
- Produces:
  - Automated `CustomerCashWallet` balance credit when `cancellation.RefundToCashWallet == true`.
  - Immutable `WalletTransaction` with `Type = "Refund"` and `ReferenceType = "SubscriptionCancellation"`.
  - `CustomerCancellationQuoteDto` containing `CancellationFeeAmount`, `NetRefundAmount`, and policy explanation message.

- [ ] **Step 1: Write failing tests for cancellation fees and wallet refund**

Create `tests/MealMate.UnitTests/OperationalPolicies/SubscriptionCancellationPolicyTests.cs`:
1. Percentage fee (e.g. 10% fee on 90 KWD refundable days = 9.000 KWD fee, 81.000 KWD net refund).
2. Fixed fee (e.g. 5.000 KWD fee on 90 KWD = 5.000 KWD fee, 85.000 KWD net refund).
3. Free cancellation (FeeType = None -> fee is 0, net refund equals gross refund).
4. Wallet credit execution: verifies `CustomerCashWallet.Balance.Amount` is increased and `WalletTransaction` row is created.
5. Kitchen-locked days protection: days inside cutoff window remain committed to kitchen (0% refund).

- [ ] **Step 2: Run test to verify it fails**

Run: `dotnet test tests/MealMate.UnitTests/MealMate.UnitTests.csproj --filter "FullyQualifiedName~SubscriptionCancellationPolicyTests"`
Expected: FAIL (currently 100% refund, no fee deduction, no wallet transaction).

- [ ] **Step 3: Update `SubscriptionCancellationService.cs`**

1. Inject `IOperationalPolicyService`.
2. Compute `GrossRefund = refundableDays.Count * dailyBoxPrice`.
3. Compute `CancellationFee` according to policy (`Percentage` vs `FixedAmount` vs `None`).
4. Compute `NetRefund = Math.Max(0, GrossRefund - fee)`.
5. In `RequestCancellationAsync` and `ProcessAdminCancellationAsync`:
   - If `NetRefund > 0` and `policy.Cancellation.RefundToCashWallet`:
     - Load or create `CustomerCashWallet` for `subscription.CustomerUserId`.
     - Add `NetRefund` to wallet balance.
     - Add `WalletTransaction` record.
6. Populate DTO fields `GrossRefundAmount`, `CancellationFeeAmount`, and `NetRefundAmount`.

- [ ] **Step 4: Run test to verify it passes**

Run: `dotnet test tests/MealMate.UnitTests/MealMate.UnitTests.csproj --filter "FullyQualifiedName~SubscriptionCancellationPolicyTests"`
Expected: PASS (all 5 tests passing).

- [ ] **Step 5: Commit**

```bash
git add src/MealMate.Application/Features/ProgramsSubscriptions/Cancellation/ tests/MealMate.UnitTests/OperationalPolicies/SubscriptionCancellationPolicyTests.cs
git commit -m "feat(cancellation): implement configurable cancellation fees and automated wallet credit"
```

---

### Task 4: Admin Policy Management Endpoints & MediatR Handlers

**Files:**
- Create: `src/MealMate.Application/Features/AdminDashboard/OperationalPolicies/GetOperationalPoliciesQuery.cs`
- Create: `src/MealMate.Application/Features/AdminDashboard/OperationalPolicies/UpdateOperationalPoliciesCommand.cs`
- Create: `src/MealMate.Application/Features/AdminDashboard/OperationalPolicies/OperationalPoliciesValidators.cs`
- Create: `src/MealMate.Api/Controllers/V1/AdminOperationalPoliciesController.cs`
- Test: `tests/MealMate.UnitTests/OperationalPolicies/AdminOperationalPoliciesEndpointsTests.cs`

**Interfaces:**
- Produces:
  - `GET /api/v1/admin/operational-policies` -> returns `OperationalPolicySettingsDto`
  - `PUT /api/v1/admin/operational-policies` -> accepts `UpdateOperationalPoliciesRequest`, validates, saves, audits, returns updated DTO.

- [ ] **Step 1: Write failing controller & handler tests**

Create `tests/MealMate.UnitTests/OperationalPolicies/AdminOperationalPoliciesEndpointsTests.cs`:
1. `GetOperationalPolicies` returns current settings with 200 OK.
2. `UpdateOperationalPolicies` successfully persists changes and logs audit entry.
3. Validation rejects invalid values (e.g. CutoffLeadHours < 12 or > 168, FeeValue < 0 or > 50%).

- [ ] **Step 2: Run test to verify it fails**

Run: `dotnet test tests/MealMate.UnitTests/MealMate.UnitTests.csproj --filter "FullyQualifiedName~AdminOperationalPoliciesEndpointsTests"`
Expected: FAIL (handler and controller do not exist).

- [ ] **Step 3: Implement MediatR Query, Command, Validators, and Controller**

Implement `GetOperationalPoliciesQueryHandler`, `UpdateOperationalPoliciesCommandHandler`, FluentValidation rules, and `AdminOperationalPoliciesController`.

- [ ] **Step 4: Run test to verify it passes**

Run: `dotnet test tests/MealMate.UnitTests/MealMate.UnitTests.csproj --filter "FullyQualifiedName~AdminOperationalPoliciesEndpointsTests"`
Expected: PASS.

- [ ] **Step 5: Run full test suite regression check**

Run: `dotnet test tests/MealMate.UnitTests/MealMate.UnitTests.csproj`
Expected: 1,135+ tests passing (0 failed).

- [ ] **Step 6: Commit**

```bash
git add src/MealMate.Application/Features/AdminDashboard/OperationalPolicies/ src/MealMate.Api/Controllers/V1/AdminOperationalPoliciesController.cs tests/MealMate.UnitTests/OperationalPolicies/AdminOperationalPoliciesEndpointsTests.cs
git commit -m "feat(api): add admin operational policies management endpoints and validation"
```

---

### Task 5: Admin Dashboard UI Component & Live Preview Calculator

**Files:**
- Create: `frontend/admin-dashboard/src/app/features/settings/operational-policies/data/operational-policies-api.service.ts`
- Create: `frontend/admin-dashboard/src/app/features/settings/operational-policies/operational-policies.component.ts`
- Create: `frontend/admin-dashboard/src/app/features/settings/operational-policies/operational-policies.component.html`
- Modify: `frontend/admin-dashboard/src/app/app.routes.ts`

- [ ] **Step 1: Implement `operational-policies-api.service.ts`**

Define TypeScript interfaces matching the backend API contract (`OperationalPolicySettingsDto`, `UpdateOperationalPoliciesRequest`) and HTTP client methods (`getPolicies`, `updatePolicies`).

- [ ] **Step 2: Implement `operational-policies.component.ts` & template**

Build modern, responsive UI with:
1. **Cutoff Policy Card:** Lead hours selector chips (24h, 48h, 72h), toggle for fixed daily hour with time input.
2. **Cancellation & Fee Card:** Radio selector (Percentage %, Fixed KWD, None), input value, toggle for wallet credit, and **Live Preview Calculator** showing dynamic calculations.
3. **Freeze Policy Card:** Days notice, min/max days, max occurrences.
4. **Save Bar:** Toast notifications, dirty state tracking, and save action.

- [ ] **Step 3: Add route and verify build**

Wire route in `app.routes.ts` under `/settings/operational-policies` and verify build succeeds.

- [ ] **Step 4: Commit**

```bash
git add frontend/admin-dashboard/src/app/features/settings/operational-policies/ frontend/admin-dashboard/src/app/app.routes.ts
git commit -m "feat(admin-ui): create operational policies management page with live calculation preview"
```
