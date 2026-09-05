# Restaurant Delivery Manager Onboarding & Mobile Login Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Implement restaurant onboarding of delivery managers with 5 fields and 3 documents (civil ID front/back + contract), first-time phone OTP lookup and password setup via mobile, and platform admin approval.

**Architecture:** A domain registration entity (`DeliveryManagerRegistration`) tracks the vetting lifecycle (`Submitted`, `UnderReview`, `Approved`, `Rejected`). Restaurants submit documents and expiry dates; delivery managers log in via mobile by phone lookup and OTP challenge; platform admins verify credentials and approve the account to `Active`.

**Tech Stack:** .NET 9, EF Core, Angular 19, TailwindCSS, TypeScript, MediatR, FluentValidation.

**Spec:** [`docs/superpowers/specs/2026-09-05-delivery-manager-onboarding-design.md`](file:///f:/MealMate/backend/docs/superpowers/specs/2026-09-05-delivery-manager-onboarding-design.md)

## Global Constraints
- Delivery manager phone, email, and national ID must be globally unique across users and pending registrations.
- All expiry dates (`NationalIdExpiryUtc`, `ContractExpiryUtc`) must be strictly in the future.
- The 3 documents uploaded are: `NationalIdFront` (10), `NationalIdBack` (11), `EmploymentContract` (12).
- First-time setup issues an immediate JWT session via `_sessions.IssueSessionAsync` returning `LoginResultDto`.

---

### Task 1: Domain Entities & Storage for Delivery Manager Registration

**Files:**
- Create: `f:\MealMate\backend\src\MealMate.Domain\Entities\IdentityAccess\DeliveryManagerRegistration.cs`
- Modify: `f:\MealMate\backend\src\MealMate.Application\Common\Abstractions\IAppDbContext.cs`
- Modify: `f:\MealMate\backend\src\MealMate.Persistence\AppDbContext.cs`
- Modify: `f:\MealMate\backend\src\MealMate.Infrastructure\Persistence\Configurations\IdentityAccessConfigurations.cs`
- Test: `f:\MealMate\backend\tests\MealMate.UnitTests\IdentityAccess\DeliveryManagerRegistrationModelTests.cs`

- [ ] **Step 1: Write unit test verifying DeliveryManagerRegistration entity creation and initial state**
- [ ] **Step 2: Run test and verify failure**
- [ ] **Step 3: Create DeliveryManagerRegistration entity and EF Core configuration**
- [ ] **Step 4: Run test and verify pass**
- [ ] **Step 5: Commit changes**

---

### Task 2: Restaurant Delivery Manager Registration Command & Endpoint

**Files:**
- Create: `f:\MealMate\backend\src\MealMate.Application\Features\IdentityAccess\DeliveryManagerRegistration\RegisterRestaurantDeliveryManagerCommand.cs`
- Modify: `f:\MealMate\backend\src\MealMate.Api\Controllers\V1\RestaurantDeliveryManagersController.cs`
- Test: `f:\MealMate\backend\tests\MealMate.UnitTests\IdentityAccess\RegisterRestaurantDeliveryManagerCommandTests.cs`

- [ ] **Step 1: Write unit test verifying validation logic (uniqueness, future expiries, 3 documents)**
- [ ] **Step 2: Run test and verify failure**
- [ ] **Step 3: Implement RegisterRestaurantDeliveryManagerCommand, validator, handler, and endpoint**
- [ ] **Step 4: Run test and verify pass**
- [ ] **Step 5: Commit changes**

---

### Task 3: Delivery Manager Mobile Phone Lookup & First-Time Activation

**Files:**
- Create: `f:\MealMate\backend\src\MealMate.Application\Features\IdentityAccess\DeliveryManagerRegistration\DeliveryManagerPhoneLookupAndSetup.cs`
- Modify: `f:\MealMate\backend\src\MealMate.Api\Controllers\V1\DeliveryManagerAuthController.cs`
- Test: `f:\MealMate\backend\tests\MealMate.UnitTests\IdentityAccess\DeliveryManagerPhoneLookupAndSetupTests.cs`

- [ ] **Step 1: Write unit test for lookup-phone and first-time-setup flows**
- [ ] **Step 2: Run test and verify failure**
- [ ] **Step 3: Implement phone lookup, OTP verification, password setup, and endpoints**
- [ ] **Step 4: Run test and verify pass**
- [ ] **Step 5: Commit changes**

---

### Task 4: Admin Delivery Manager Approval & Decision Endpoints

**Files:**
- Create: `f:\MealMate\backend\src\MealMate.Application\Features\IdentityAccess\DeliveryManagerRegistration\AdminDeliveryManagerRegistrations.cs`
- Create: `f:\MealMate\backend\src\MealMate.Api\Controllers\V1\AdminDeliveryManagerRegistrationsController.cs`
- Test: `f:\MealMate\backend\tests\MealMate.UnitTests\IdentityAccess\AdminDeliveryManagerRegistrationsTests.cs`

- [ ] **Step 1: Write unit test verifying admin approval transitions user to Active and links documents**
- [ ] **Step 2: Run test and verify failure**
- [ ] **Step 3: Implement admin decision commands and controller endpoints**
- [ ] **Step 4: Run test and verify pass**
- [ ] **Step 5: Commit changes**

---

### Task 5: Restaurant Dashboard - Enhanced Delivery Manager Modal with Documents

**Files:**
- Modify: `f:\MealMate\frontend\restaurant-dashboard\src\app\features\restaurant\dispatch\data\delivery-managers-api.service.ts`
- Modify: `f:\MealMate\frontend\restaurant-dashboard\src\app\features\restaurant\dispatch\data\dispatch-officers.facade.ts`
- Modify: `f:\MealMate\frontend\restaurant-dashboard\src\app\features\restaurant\dispatch\dispatch-officer-create-modal.component.ts`
- Modify: `f:\MealMate\frontend\restaurant-dashboard\src\app\features\restaurant\dispatch\dispatch-officer-create-modal.component.html`

- [ ] **Step 1: Add document upload API calls and request DTOs**
- [ ] **Step 2: Update modal with tabs/steps for info + document uploads + previews**
- [ ] **Step 3: Verify Angular build via `npm.cmd run build`**
- [ ] **Step 4: Commit changes**

---

### Task 6: Admin Dashboard - Delivery Manager Verification View

**Files:**
- Modify: `f:\MealMate\frontend\admin-dashboard\src\app\features\admin\accounts\models\restaurant-workspace.model.ts`
- Modify: `f:\MealMate\frontend\admin-dashboard\src\app\features\admin\accounts\pages\delivery-manager-detail-page\delivery-manager-detail-page.component.ts`
- Modify: `f:\MealMate\frontend\admin-dashboard\src\app\features\admin\accounts\pages\delivery-manager-detail-page\delivery-manager-detail-page.component.html`

- [ ] **Step 1: Update models and view component to load documents and registration details**
- [ ] **Step 2: Update template to display National ID, expiries, document viewer with validity badges, and approval actions**
- [ ] **Step 3: Verify Angular build via `npm.cmd run build`**
- [ ] **Step 4: Commit changes**

---

### Task 7: Delivery Manager Mobile Quickguide & End-to-End Verification

**Files:**
- Create: `f:\MealMate\backend\docs\mobile\delivery-manager-first-time-login-api.md`

- [ ] **Step 1: Create mobile integration guide with sequence diagram, payloads, and error codes**
- [ ] **Step 2: Run full backend unit test suite (`dotnet test`)**
- [ ] **Step 3: Update `walkthrough.md`**
- [ ] **Step 4: Commit changes**
