# Mobile Calendar & Subscription Selection Engine Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Build the end-to-end Mobile Calendar, Meal/Restaurant Selection, Lead Time calculation, Diversification engine, Allergy-safe Auto-Selection, Restaurant confirmation SLA monitoring, and Admin Compensation workflows.

**Architecture:** 
- Backend (.NET 9 / EF Core / Postgres): Extend `CalendarMeals` domain and application services with configurable lead time settings, dynamic quota snapshotting, allergy filtering, cut-off auto-selection worker jobs, SLA escalations, and compensation endpoints.
- Frontend (Admin Dashboard / Restaurant Dashboard / Mobile App APIs): Wire UI components to reflect real-time countdowns, restaurant quota badges, nutrition macro donuts, allergen indicators, and admin escalation workspaces.

**Tech Stack:** C# .NET 9, EF Core, PostgreSQL, MediatR/CQRS, Angular/TypeScript, xUnit/FluentAssertions.

**Spec:** [`f:/MealMate/backend/docs/superpowers/specs/2026-08-19-mobile-calendar-subscription-flow-design.md`](file:///f:/MealMate/backend/docs/superpowers/specs/2026-08-19-mobile-calendar-subscription-flow-design.md)

---

## Global Constraints
- Target Framework: .NET 9.0 (`net9.0`)
- Follow TDD (Red-Green-Refactor) for each service and handler.
- Strictly respect `CustomerAllergyPreference` to prevent any food allergy incidents.
- Maintain append-only audit trail in `DiversificationLimitSnapshot` and compensation ledgers.

---

## Tasks

### Task 1: Lead Time & Calendar Generation Service
**Files:**
- Modify: `src/MealMate.Application/Features/ProgramsSubscriptions/SubscriptionCommandHandlers.cs`
- Create: `src/MealMate.Application/Features/ProgramsSubscriptions/CalendarLeadTimeService.cs`
- Test: `tests/MealMate.UnitTests/CalendarLeadTimeServiceTests.cs`

**Interfaces:**
- Consumes: `SubscriptionPaymentConfirmedEvent`, `SystemSettings` (LeadTimeHours: 48h / 72h configurable).
- Produces: `CalculateFirstDeliveryDate(DateTime paymentUtc, int leadTimeHours, DayOfWeek[] activeDays) => DateOnly`.

- [ ] **Step 1: Write failing unit tests for Lead Time calculation**
- [ ] **Step 2: Run test to verify it fails**
- [ ] **Step 3: Implement `CalendarLeadTimeService` and integrate with subscription activation handler**
- [ ] **Step 4: Run test to verify it passes**
- [ ] **Step 5: Commit**

---

### Task 2: Mathematical Restaurant Diversification & Quota Engine
**Files:**
- Create: `src/MealMate.Application/Features/ProgramsSubscriptions/RestaurantDiversificationCalculator.cs`
- Test: `tests/MealMate.UnitTests/RestaurantDiversificationCalculatorTests.cs`

**Interfaces:**
- Produces: `CalculateLimit(int durationDays, int availableRestaurantsCount) => DiversificationLimitSnapshot`.
- Formula: If $R=1 \implies D$. If $R > 1 \implies \min(D, \max(1, \lceil \frac{D}{R} \rceil + 1))$.

- [ ] **Step 1: Write unit tests for diversification formulas (1 restaurant, 5 restaurants, 30 restaurants)**
- [ ] **Step 2: Run test to verify it fails**
- [ ] **Step 3: Implement `RestaurantDiversificationCalculator` and record snapshot on checkout**
- [ ] **Step 4: Run test to verify it passes**
- [ ] **Step 5: Commit**

---

### Task 3: Allergy-Filtered Menus & Smart Auto-Selection Worker
**Files:**
- Modify: `src/MealMate.Application/Features/CustomerCheckout/CustomerCheckoutController.cs`
- Create: `src/MealMate.Application/Features/CalendarMeals/SmartAutoSelectionService.cs`
- Test: `tests/MealMate.UnitTests/SmartAutoSelectionServiceTests.cs`

**Interfaces:**
- Consumes: `CustomerAllergyPreference`, `CustomerDietaryPreference`, `SubscriptionDayMealSnapshot`.
- Produces: Safe meal selections matching calories/macros with 0 allergen collisions, setting `IsAutoSelected = true`.

- [ ] **Step 1: Write unit tests for allergen exclusion and automatic meal picking**
- [ ] **Step 2: Run test to verify it fails**
- [ ] **Step 3: Implement `SmartAutoSelectionService` and hook to the lock cut-off background job**
- [ ] **Step 4: Run test to verify it passes**
- [ ] **Step 5: Commit**

---

### Task 4: Restaurant Confirmation SLA & Admin Escalation / Compensation Flow
**Files:**
- Modify: `src/MealMate.Application/Features/AdminDashboard/AdminCheckoutOperationsController.cs`
- Create: `src/MealMate.Application/Features/CalendarMeals/AdminCompensationService.cs`
- Test: `tests/MealMate.UnitTests/AdminCompensationServiceTests.cs`

**Interfaces:**
- Endpoints:
  - `POST /api/v1/admin/calendar/orders/{dayId}/reassign`
  - `POST /api/v1/admin/calendar/orders/{dayId}/compensate-wallet`
  - `POST /api/v1/admin/calendar/orders/{dayId}/compensate-extend-subscription`

- [ ] **Step 1: Write unit tests for the 3 compensation channels and audit logging**
- [ ] **Step 2: Run test to verify it fails**
- [ ] **Step 3: Implement `AdminCompensationService` and Admin Controller actions**
- [ ] **Step 4: Run test to verify it passes**
- [ ] **Step 5: Commit**

---

### Task 5: Mobile App Calendar & Selection DTOs / API Contract
**Files:**
- Create / Update: DTOs for screens 05.10, 05.11, 05.12, 05.14
- Provide: Countdown timer calculation, Remaining Quota per restaurant, Nutrition Donut breakdown, Ingredient origins, and Allergy tags.
- Test: `tests/MealMate.IntegrationTests/CustomerCalendarFlowTests.cs`

- [ ] **Step 1: Write integration tests for Calendar view and restaurant menu endpoints**
- [ ] **Step 2: Run test to verify it fails**
- [ ] **Step 3: Expose and verify JSON responses matching Figma designs**
- [ ] **Step 4: Run test to verify all test suites pass**
- [ ] **Step 5: Commit**
