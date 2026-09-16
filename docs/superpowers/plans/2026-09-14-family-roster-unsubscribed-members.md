# Family Roster & Unsubscribed Members Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Enable adding family members to the family roster without requiring an immediate subscription, ensuring unsubscribed members do not block checkout, do not generate pricing charges, and are cleanly presented in Family Manager.

**Architecture:** Decouple family roster membership from subscription eligibility. `FamilyCheckoutGate` and `FamilyQuoteService` strictly evaluate payable members (members with `HasPricablePlan`). Post-payment checkout provisions subscriptions only for payable members. `FamilyManager` queries surface all members in the roster while clearly reflecting subscription readiness.

**Tech Stack:** .NET 9, C#, Entity Framework Core, MediatR, xUnit, FluentAssertions.

**Spec:** `docs/superpowers/specs/2026-09-14-family-roster-unsubscribed-members-design.md`

## Global Constraints

- Never block checkout if there are unconfigured members, as long as at least one member is payable and ready.
- Subscriptions must only be generated for payable members with complete plans.
- Family manager list/detail queries must return all members (subscribed and unsubscribed) without throwing null references.

---

### Task 1: Verify and Ensure Gate & Quote Isolation for Unsubscribed Members

**Files:**
- Modify: `src/MealMate.Application/Features/FamilyCheckout/FamilyCheckoutGate.cs`
- Modify: `src/MealMate.Application/Features/FamilyCheckout/FamilyQuoteService.cs`
- Test: `tests/MealMate.UnitTests/CustomerCheckout/FamilyCheckoutServiceTests.cs`
- Test: `tests/MealMate.UnitTests/ProgramsSubscriptions/FamilyQuoteServiceTests.cs`

**Interfaces:**
- Consumes: `FamilyMemberPlanRules.HasPricablePlan(FamilyMember)`
- Produces: `FamilyCheckoutGate.Evaluate(...)`, `FamilyQuoteService.QuoteAsync(...)`

- [ ] **Step 1: Write unit tests for gate and quote with mixed members**
  Add unit tests verifying that a family with 1 ready member and 2 unconfigured members passes `FamilyCheckoutGate.Evaluate` and quotes only the ready member.

- [ ] **Step 2: Run tests to verify existing behavior / any edge cases**
  Run dotnet test on `FamilyCheckoutServiceTests` and `FamilyQuoteServiceTests`.

- [ ] **Step 3: Refine gate and quote logic if needed**
  Ensure edge cases (e.g. discount calculation, minimum members check) properly handle unsubscribed members.

- [ ] **Step 4: Verify tests pass**
  Run dotnet test.

---

### Task 2: Ensure Family Manager and Home Queries Handle Unsubscribed Members Gracefully

**Files:**
- Modify: `src/MealMate.Application/Features/FamilyManager/FamilyManagerHandlers.cs`
- Modify: `src/MealMate.Application/Features/FamilyManager/FamilyManagerContracts.cs`
- Test: `tests/MealMate.UnitTests/FamilyManager/FamilyManagerHandlerTests.cs`

**Interfaces:**
- Consumes: `GetFamilyMembersQuery`, `GetFamilyHomeQuery`
- Produces: `FamilyMemberListItemDto`, `FamilyHomeDto`

- [ ] **Step 1: Write tests for Family Manager with unsubscribed members**
  Verify that when an unsubscribed member is in the group (`MemberSubscriptionId == null`, `ProgramId == null`), `GetFamilyMembersQuery` and `GetFamilyHomeQuery` return clean DTOs with `RemainingDays = 0`, null program names, and proper setup status without runtime errors.

- [ ] **Step 2: Run test to confirm behavior**
  Run dotnet test on `FamilyManagerHandlerTests`.

- [ ] **Step 3: Update mapping logic if needed**
  Ensure null-safety and proper badge attributes for unsubscribed members.

- [ ] **Step 4: Run tests to verify all pass**
  Run dotnet test.

---

### Task 3: Full End-to-End Test & Verification

**Files:**
- Test: `tests/MealMate.UnitTests/CustomerCheckout/FamilyCheckoutServiceTests.cs`

- [ ] **Step 1: Write full flow test**
  Draft group with 1 manager (ready) and 1 dependent (no plan). Checkout succeeds, creates 1 subscription for manager, leaves dependent with `MemberSubscriptionId == null`.

- [ ] **Step 2: Run all backend tests**
  Execute `dotnet test` across the solution.
