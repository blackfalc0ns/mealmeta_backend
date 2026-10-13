# Family Capacity and Manager Subscription Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development to implement task-by-task. Steps use checkbox syntax.

**Goal:** Implement reserved family seats, retained manager individual service, explicit upgrade/renewal/calendar contracts and safe purchase readiness.

**Architecture:** A shared family participation/capacity service computes persisted roster seats and funded service independently. Store manager's individual-entry exemption explicitly with an additive migration; do not repurpose financial anchors. Reuse the existing checkout, renewal and tier-upgrade reservation/payment services.

**Tech Stack:** .NET 9, C#, EF Core/PostgreSQL, MediatR, xUnit/FluentAssertions, API resx dictionaries.

**Spec:** docs/superpowers/specs/2026-10-13-family-capacity-manager-design.md

## Global Constraints

- No fake business data; production sources are persisted application data, settings and authenticated users.
- Arabic and English product copy must live in existing message resources; return semantic codes for persistent UI state.
- Preserve Kuwait business timezone and currency precision, existing API identifiers and verified payment flow.
- Preserve existing subscription IDs, plans, dates, service days, meal choices and history.
- Worktree: F:/MealMate/scratch/family-flow-20261013; do not edit F:/MealMate/backend or unrelated campaign files.
- No deployment, live database changes or remote push. Keep reviewable local commits.

### Task 1: Occupied seats, manager individual membership and package policy

**Files:** Domain/ProgramsSubscriptions/FamilyGroup.cs; Persistence family configurations and additive migration/snapshot; Application/Features/FamilyCheckout/FamilyDraftHandlers.cs, ConvertIndividualToFamilyHandler.cs, FamilyQuoteService.cs, FamilyPackagePolicy.cs, FamilyCheckoutGate.cs, FamilyCheckoutSelectionService.cs, FamilyCheckoutService.cs; new small shared policy/service in FamilyCheckout; relevant UnitTests/CustomerCheckout and ProgramsSubscriptions.

**Interfaces:** A shared asynchronous context computes occupancy, manager exemption/live individual, qualifying funded count and initial/reactivation status from persisted data. Retain ParticipantCount as funded+new qualification, add occupancy metadata separately. Later tasks consume this context rather than duplicate formulas. Keep backwards-compatible optional DTO additions.

- [ ] Add tests: deferred roster member occupies a seat but is unpriced; roster beyond chosen package rejected; manager without individual cannot defer initial purchase; two participants required even for package MinMembers=1; manager individual + one member permits first purchase, without changing any individual subscription fields/days; expired individual cannot qualify; active package and snapshot do not change when selecting only one new member; no legacy paid manager forever qualifies after expiry.
- [ ] Run tests and record expected failures before implementation. Use real policy/service behavior; fixture mocks remain test-only.

```csharp
// Behavioral invariant for the existing quote fixture, adapted to its real helpers:
quote.Value.ParticipantCount.Should().Be(2); // live manager individual + one payable member
quote.Value.Lines.Should().ContainSingle(x => x.MemberId == child.Id);
(await db.Subscriptions.FindAsync(individual.Id))!.FamilyGroupId.Should().BeNull();
// Capacity is independently asserted using the new quote/summary occupancy field.
```

- [ ] Implement shared policy, persisted exemption and guarded mutations under existing group lock; preserve legacy conversions compatibly. Apply minimum to activation/reactivation and max to seats. Update renewal policy use inside FamilyCheckoutService to snapshot discount rather than current-paid auto-package selection.
- [ ] Run scoped unit tests and migration/model validation. Commit task files only and write task-1 report with red/green commands, counts and concerns.

### Task 2: Capacity upgrades, renewal and personal calendar contracts

**Files:** Application/Features/FamilyManager contracts/handlers/resolvers; Features/CustomerCheckout/CustomerIndividualSubscriptionGate.cs and checkout services; Features/ProgramsSubscriptions/CustomerSubscriptions query/contracts; FamilyCheckout contracts/controller, new capacity-upgrade command/handler; DI registrations; API Resources.Messages.ar.resx and Resources.Messages.resx; unit/API tests.

**Interfaces:** Consume Task 1 shared capacity context. Add manager personal-service/navigation metadata independent from family payment anchor; expose enabled routes/reason codes for individual renewal/family purchase/capacity upgrade/personal tier upgrade. Capacity upgrade command accepts larger package ID and expected current package/version to avoid stale changes; use existing group lock and reject frozen payment batches. Preserve existing public routes; additive endpoint under family-subscription-flow.

- [ ] Add failing tests: manager expired but family remains administrable; main calendar returns manager current/upcoming service never another member anchor; manager individual renewal permitted when no blocking own family service, but overlap refused; larger-package upgrade changes selected package/snapshot and future pricing only, rejects same/lower or payment in progress, preserves all subscriptions/days; current renewal quote uses new package snapshot; current/lower personal tier not selectable and highest tier still permits family creation.

```csharp
var oldEnd = subscription.EndDate;
var oldPaid = subscription.PaidAmount;
// After the capacity-upgrade command:
subscription.EndDate.Should().Be(oldEnd);
subscription.PaidAmount.Should().Be(oldPaid);
group.SelectedFamilyPackageId.Should().Be(larger.Id);
group.DiscountPercentSnapshot.Should().Be(larger.DiscountPercent);
```

- [ ] Implement localized additive contract and transactional capacity upgrade. Reuse the existing personal-tier upgrade, do not create a second financial path. Ensure individual gate exceptions are manager-personal-service based, not any active family group.
- [ ] Run scoped unit/API tests, resource parity and build. Commit task files and report evidence.

### Task 3: Summary readiness, regression coverage and mobile handoff

**Files:** FamilyCheckout summary/contracts/gate/check-out readiness, small shared readiness helper as necessary; UnitTests/CustomerCheckout, ApiTests/CustomerCheckout, relevant IntegrationTests; docs/mobile/2026-10-13-family-capacity-manager-handoff.md.

**Interfaces:** Consume shared capacity/participation and navigation contracts from Tasks 1/2. Expose explicit member lifecycle and manager requirement, seat counts and deterministic block reason; existing batch/version/idempotency remain intact.

- [ ] Add failing tests for selected incomplete plan/address/start date/allergy/linked account, expired manager between summary and payment, and deferred incomplete member not blocking. Assert summary reason matches checkout for deterministic prerequisites. Preserve current restaurant eligibility and transaction reservation rechecks.
- [ ] Implement shared deterministic readiness and explicit per-member summary state. Do not invent successful unavailable restaurant checks; trace existing schedule restaurant evaluation.
- [ ] Write precise mobile handoff in Arabic with English product-key equivalents, request/response/action examples and acceptance matrix. Document that the mobile source is absent and UI runtime is unverified. Include temporary offer validity and upgrade cost model.
- [ ] Run scoped regression suite covering Family/Conversion/Individual gates/Renewal/Upgrade, API contracts, available integration tests without live DB, API build and localization/resource checks. Report skips/blockers honestly.
- [ ] Commit task files and report evidence. Coordinator performs independent diff/requirement review and final fresh verification before completion.
