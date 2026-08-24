# Individual → Family Conversion (Phase 1) — Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Let a customer with a current individual subscription convert it into a family Draft as manager (no re-pay for manager); family checkout charges new members only.

**Architecture:** Add `ConvertedFromSubscriptionId` on `FamilyGroup`; conversion command links the existing subscription as Manager; extend exclusivity gate to block individual checkout while manager owns Draft/CheckoutPending/Active/Frozen family; change `FamilyCheckoutService` + `FamilyQuoteService` to skip manager create/holds and zero manager payable price.

**Tech Stack:** .NET 9, EF Core migrations, xUnit, NSubstitute, MediatR

**Spec:** `docs/superpowers/specs/2026-08-24-individual-to-family-conversion-design.md`

## Global Constraints

- Convertible statuses (exact): `Paid`, `Scheduled`, `Active`, `Frozen`, `SuspendedForReview` with `FamilyGroupId == null`
- `PendingPayment` → `conversion.individual_not_convertible`
- Manager keeps the **same** `Subscription.Id`; no second create for manager
- Locked calendar days remain immutable
- `CHANGE_INDIVIDUAL_PLAN` → `available: false`, reason `change_individual_plan.not_available`
- Error codes (exact): `conversion.no_individual_subscription`, `conversion.already_in_family`, `conversion.individual_not_convertible`
- Do **not** commit unless the user asks
- Preserve existing family flow for customers with **no** individual subscription

## File map

| File | Role |
|------|------|
| `Domain/.../FamilyGroup.cs` | `ConvertedFromSubscriptionId` |
| EF config + migration | Persist new column |
| `CustomerIndividualSubscriptionGate.cs` | Also block if manager of in-progress/active family |
| `FamilyCheckout/ConvertIndividualToFamily*.cs` (new) | Convert command/handler |
| `CustomerFamilySubscriptionFlowController.cs` | POST convert + wire |
| `CustomerSubscriptionManagementController.cs` or flow controller | GET conversion-options |
| `CustomerSubscriptionFlowController.cs` | Types aware of existing sub / family |
| `FamilyQuoteService.cs` | Manager prepaid → price 0 |
| `FamilyCheckoutService.cs` | Skip create/holds for converted manager |
| Unit/Api tests | Gate, convert, quote, checkout |
| `docs/mobile/family-subscription-api.md` | Conversion section |

---

### Task 1: Schema + exclusivity gate covers family groups

**Files:**
- Modify: `src/MealMate.Domain/ProgramsSubscriptions/FamilyGroup.cs`
- Modify: Persistence configuration for FamilyGroup + new migration
- Modify: `src/MealMate.Application/Features/CustomerCheckout/CustomerIndividualSubscriptionGate.cs`
- Modify: `tests/MealMate.UnitTests/CustomerCheckout/CustomerIndividualSubscriptionGateTests.cs`

**Interfaces:**
- Produces: `FamilyGroup.ConvertedFromSubscriptionId` (`Guid?`)
- Gate also returns blocking when customer is `ManagerCustomerUserId` of a group with status in `Draft`, `CheckoutPending`, `Active`, `Frozen`
- Prefer error code still usable as `checkout.existing_subscription` for individual path **or** add `checkout.existing_family_group` — **use** `checkout.existing_subscription` for readiness/checkout compatibility in Phase 1 (document in gate XML comment). Blocking record may use `SubscriptionId = Guid.Empty` when only family blocks — better: find manager's linked subscription if any, else still set HasBlocking true via new method.

**Refined gate API (backward compatible):**

```csharp
Task<BlockingIndividualSubscription?> FindBlockingAsync(...); // existing individual rule
Task<bool> HasBlockingAsync(...); // true if FindBlockingAsync != null OR HasBlockingFamilyGroupAsync
Task<bool> HasBlockingFamilyGroupAsync(Guid customerUserId, CancellationToken ct = default);
```

Readiness should call `HasBlockingAsync` / if family-only block, set `CheckoutBlockReason = checkout.existing_subscription` and leave BlockingSubscriptionId null OR set to converted subscription if found.

- [ ] **Step 1:** Add property + migration `ConvertedFromSubscriptionId` nullable FK-ish Guid (no required FK constraint to avoid cascade pain).

- [ ] **Step 2:** Extend gate tests: Active family Draft as manager → `HasBlockingAsync` true even without individual.

- [ ] **Step 3:** Implement family-group check; update readiness to use `HasBlockingAsync` path that covers family (if currently only FindBlockingAsync, switch blocking branch to also check family).

- [ ] **Step 4:** Run  
  `dotnet test tests/MealMate.UnitTests/MealMate.UnitTests.csproj --filter FullyQualifiedName~CustomerIndividualSubscriptionGateTests|FullyQualifiedName~SubscriptionCheckoutReadinessServiceTests`

---

### Task 2: Convert-from-individual command + APIs

**Files:**
- Create: `src/MealMate.Application/Features/FamilyCheckout/ConvertIndividualToFamilyCommands.cs` (command + DTOs for options if needed)
- Modify: `FamilyDraftHandlers.cs` **or** new handler class registered via MediatR assembly scan
- Modify: `CustomerFamilySubscriptionFlowController.cs`
- Modify: `CustomerSubscriptionFlowController.cs` (`Types` → async)
- Modify: management or flow for `GET .../conversion-options`
- Test: `tests/MealMate.ApiTests/CustomerCheckout/CustomerFamilySubscriptionFlowControllerTests.cs` and/or new unit tests

**Command:**

```csharp
public sealed record ConvertIndividualToFamilyCommand(
    string? Name = null,
    Guid? FamilyPackageId = null) : ICommand<FamilyGroupScreenDto>;

public sealed record GetSubscriptionConversionOptionsQuery : IQuery<SubscriptionConversionOptionsDto>;

public sealed record SubscriptionConversionOptionsDto(
    Guid SubscriptionId,
    string Status,
    IReadOnlyList<SubscriptionConversionOptionDto> Options);

public sealed record SubscriptionConversionOptionDto(
    string Code,
    bool Available,
    string TitleAr,
    string TitleEn,
    string? Action,
    string? DisabledReason);
```

**Convert handler logic:**

1. Resolve customer id.
2. If editable Draft family already exists for manager with `ConvertedFromSubscriptionId` set → return MapScreen (idempotent).
3. If any Active family → `conversion.already_in_family`.
4. Find convertible individual subscription (statuses above, `FamilyGroupId == null`); if none → `conversion.no_individual_subscription`; if only PendingPayment → `conversion.individual_not_convertible`.
5. Create Draft `FamilyGroup` (name default from profile or `"عائلتي"`), set `ConvertedFromSubscriptionId = sub.Id`, assign package if provided.
6. Create Manager `FamilyMember` with plan copied from subscription (ProgramId, BundleId, SelectedTier, DurationDays, IncludeFriday); hydrate name/address from profile; SetupStatus via rules.
7. Set `sub.FamilyGroupId`, `sub.FamilyMemberId`; set `member.MemberSubscriptionId = sub.Id` if that property exists.
8. Save; return screen DTO.

Check `FamilyMember` for `MemberSubscriptionId` / plan fields before coding.

**Endpoints:**

- `POST /api/v1/customer/family-subscription-flow/convert-from-individual`
- `GET /api/v1/customer/subscriptions/current/conversion-options` (management controller)
- `GET types` async: if HasBlockingAsync → INDIVIDUAL `IsAvailable=false`; FAMILY stays available with note in description or leave as-is and rely on conversion-options when opening FAMILY.

Extend `SubscriptionTypeOptionDto` only if needed; prefer not breaking binary shape — keep record, set `IsAvailable` false for INDIVIDUAL when blocked.

- [ ] **Step 1:** Failing API/unit tests for convert + options + types.
- [ ] **Step 2:** Implement handler + endpoints.
- [ ] **Step 3:** Run targeted ApiTests + UnitTests.

---

### Task 3: Quote + checkout skip converted manager

**Files:**
- Modify: `FamilyQuoteService.cs`
- Modify: `FamilyCheckoutService.cs`
- Modify: `tests/MealMate.UnitTests/ProgramsSubscriptions/FamilyQuoteServiceTests.cs`
- Modify: `tests/MealMate.UnitTests/CustomerCheckout/FamilyCheckoutServiceTests.cs`

**Quote:**

If `group.ConvertedFromSubscriptionId is Guid convertedId`:
- For manager member (Role == Manager), set `MemberPrice = 0` (and discounts 0) in the line **or** exclude from payable subtotal.
- Recompute `subtotal` / family discount / `finalPrice` on **payable** lines only (non-zero / non-manager).
- Still include manager line in `Lines` with `MemberPrice = 0` for UI transparency.

**Checkout `StartAsync`:**

When `group.ConvertedFromSubscriptionId` is set:
- For manager member: do **not** call `CreateSubscriptionCommand`; load existing subscription by id; verify ownership; set FamilyGroupId/FamilyMemberId; **skip** new CapacityHolds for manager service dates already covered.
- For other members: existing create + holds path.
- `PaymentAnchorSubscriptionId` = converted manager subscription id.
- `QuotedFinalPrice` / payment amount = quote.FinalPrice (new members only).
- If FinalPrice == 0 but minMembers not satisfied → still blocked by gate; do not special-case.

- [ ] **Step 1:** Unit test quote zeros manager when ConvertedFromSubscriptionId set.
- [ ] **Step 2:** Implement quote + checkout changes.
- [ ] **Step 3:** Run FamilyQuote + FamilyCheckout tests.

---

### Task 4: Docs

**Files:**
- Modify: `docs/mobile/family-subscription-api.md`
- Modify: `docs/mobile-customer-subscription-flow.md`

Add conversion section: options, convert POST, manager prepaid summary fields, exclusivity interaction.

- [ ] Update docs only; no code.

---

## Spec coverage

| Spec item | Task |
|-----------|------|
| ConvertedFromSubscriptionId | 1 |
| Gate blocks family in progress | 1 |
| conversion-options + convert API | 2 |
| Types INDIVIDUAL disabled | 2 |
| Manager no re-create / no re-pay | 3 |
| Docs | 4 |
| CHANGE_INDIVIDUAL_PLAN unavailable | 2 |
| PendingPayment not convertible | 2 |

## Self-review

- No TBD left for Phase 1 pricing rule (manager line = 0).
- Idempotent convert specified.
- Existing non-converted family checkout unchanged when `ConvertedFromSubscriptionId` is null.
