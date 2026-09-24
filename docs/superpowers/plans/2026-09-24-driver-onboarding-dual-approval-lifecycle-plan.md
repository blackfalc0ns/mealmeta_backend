# Driver Onboarding & Dual Approval Lifecycle Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Fix all architectural, security, and operational gaps in driver onboarding, credentialing (driver-chosen password), independent fleet registration, restaurant direct registration, dual approval state machine, and `DriverProfile` regulatory data retention.

**Architecture:** A robust dual-approval state machine supporting both restaurant-bound and independent fleet drivers. Drivers establish their credentials upon registration. Approved registrations faithfully persist all identity and vehicle metadata into `DriverProfile`. The resubmission loop preserves restaurant approval when changes are requested by admin, and rejections properly update user account statuses.

**Tech Stack:** C# .NET 9, EF Core, MediatR, FluentValidation, FluentAssertions, xUnit.

**Spec:** [`docs/superpowers/specs/2026-09-24-driver-onboarding-dual-approval-lifecycle-design.md`](file:///f:/MealMate/backend/docs/superpowers/specs/2026-09-24-driver-onboarding-dual-approval-lifecycle-design.md)

## Global Constraints
- Target workspace: `f:\MealMate\backend`.
- Test Framework: xUnit with FluentAssertions. Every task must follow strict TDD (failing test first, minimal code, green, commit).
- Solution-wide test suite: Must maintain 100% pass rate (1,140+ tests passing, 0 regressions).
- Target files:
  - `src/MealMate.Domain/IdentityAccess/DriverProfile.cs`
  - `src/MealMate.Persistence/Configurations/IdentityAccess/IdentityAccessConfigurations.cs`
  - `src/MealMate.Application/Features/IdentityAccess/StaffAuth/StaffDriverRegistration.cs`
  - `src/MealMate.Application/Features/IdentityAccess/StaffAuth/StaffPhoneLookup.cs`
  - `src/MealMate.Application/Features/IdentityAccess/DriverRegistration/DriverRegistrationCommands.cs`
  - `src/MealMate.Application/Features/IdentityAccess/DriverRegistration/RegisterRestaurantDriverCommand.cs`
  - `src/MealMate.Application/Features/IdentityAccess/DriverRegistration/RestaurantDriverRegistrations.cs`
  - `src/MealMate.Api/Controllers/V1/StaffDriverRegistrationController.cs`

---

### Task 1: Domain & Persistence Enhancement for `DriverProfile`

**Files:**
- Modify: `src/MealMate.Domain/IdentityAccess/DriverProfile.cs`
- Modify: `src/MealMate.Persistence/Configurations/IdentityAccess/IdentityAccessConfigurations.cs:135-153`
- Create: `src/MealMate.Persistence/Migrations/20260924140000_AddDriverProfileRegulatoryFields.cs`
- Modify: `src/MealMate.Persistence/Migrations/AppDbContextModelSnapshot.cs`
- Test: `tests/MealMate.UnitTests/IdentityAccess/DriverProfileRegulatoryFieldsTests.cs`

**Interfaces:**
- Produces:
  - Properties on `DriverProfile`:
    - `DateTime? NationalIdExpiryUtc { get; set; }`
    - `DateTime? LicenseExpiryUtc { get; set; }`
    - `DateTime? VehicleRegistrationExpiryUtc { get; set; }`
    - `DateTime? ContractExpiryUtc { get; set; }`
    - `int? VehicleYear { get; set; }`
    - `string? VehicleColor { get; set; }`
    - `bool? IsVehicleOwned { get; set; }`
    - `string? Nationality { get; set; }`
    - `DateTime? DateOfBirthUtc { get; set; }`

- [x] **Step 1: Write failing test for `DriverProfile` regulatory and vehicle properties**

Create `tests/MealMate.UnitTests/IdentityAccess/DriverProfileRegulatoryFieldsTests.cs` verifying properties can be assigned, read, and retain regulatory data.

- [x] **Step 2: Run test to verify it fails**

Run: `dotnet test tests/MealMate.UnitTests/MealMate.UnitTests.csproj --filter "FullyQualifiedName~DriverProfileRegulatoryFieldsTests"`
Expected: FAIL (properties not yet defined on `DriverProfile`).

- [x] **Step 3: Update `DriverProfile.cs` and `IdentityAccessConfigurations.cs`**

Add properties to `DriverProfile.cs` and configure lengths / columns in `IdentityAccessConfigurations.cs`.

- [x] **Step 4: Create migration and update model snapshot**

Add `20260924140000_AddDriverProfileRegulatoryFields.cs` and update `AppDbContextModelSnapshot.cs`.

- [x] **Step 5: Run tests to verify they pass**

Run: `dotnet test tests/MealMate.UnitTests/MealMate.UnitTests.csproj --filter "FullyQualifiedName~DriverProfileRegulatoryFieldsTests"`
Expected: PASS.

- [x] **Step 6: Commit**

```bash
git add src/MealMate.Domain/ src/MealMate.Persistence/ tests/MealMate.UnitTests/IdentityAccess/DriverProfileRegulatoryFieldsTests.cs
git commit -m "feat(domain): add regulatory expiry and vehicle fields to DriverProfile"
```

---

### Task 2: Driver Password Credentialing & Registration Ingestion Fixes

**Files:**
- Modify: `src/MealMate.Application/Features/IdentityAccess/StaffAuth/StaffDriverRegistration.cs`
- Modify: `src/MealMate.Application/Features/IdentityAccess/DriverRegistration/DriverRegistrationCommands.cs:275-285`
- Modify: `src/MealMate.Application/Features/IdentityAccess/DriverRegistration/RegisterRestaurantDriverCommand.cs`
- Test: `tests/MealMate.UnitTests/IdentityAccess/StaffDriverRegistrationCommandTests.cs`

**Interfaces:**
- Modifies: `StaffDriverSelfRegistrationCommand`:
  - `Guid? RestaurantId = null` (optional for independent platform drivers)
  - `string Password` (required, min 8 chars)
- Modifies: `SubmitDriverRegistrationCommandHandler`:
  - Checks for `DocumentType.NationalIdFront` OR `DocumentType.NationalId`
  - Checks for `DocumentType.DrivingLicenseFront` OR `DocumentType.DrivingLicense`
- Modifies: `RegisterRestaurantDriverCommandHandler`:
  - Sets `RegistrationSource = "RestaurantPortal"`
  - Sets `RestaurantApprovalStatus = VerificationStatus.Approved`
  - Sets `RestaurantReviewedByUserId = _currentUser.UserId`
  - Sets `RestaurantReviewedAtUtc = _clock.UtcNow`
  - Sets `Status = VerificationStatus.UnderReview` (ready for admin)

- [x] **Step 1: Write failing tests for registration ingestion fixes**

Create `tests/MealMate.UnitTests/IdentityAccess/StaffDriverRegistrationCommandTests.cs`:
1. Registration succeeds with driver-provided password and persists password hash.
2. Registration succeeds with `RestaurantId = null` (Independent Fleet driver).
3. `SubmitDriverRegistrationCommand` passes when documents are `NationalIdFront` and `DrivingLicenseFront`.
4. `RegisterRestaurantDriverCommand` marks restaurant approval as `Approved` and source as `RestaurantPortal`.

- [x] **Step 2: Run test to verify it fails**

Run: `dotnet test tests/MealMate.UnitTests/MealMate.UnitTests.csproj --filter "FullyQualifiedName~StaffDriverRegistrationCommandTests"`
Expected: FAIL.

- [x] **Step 3: Implement fixes in `StaffDriverRegistration.cs`, `DriverRegistrationCommands.cs`, and `RegisterRestaurantDriverCommand.cs`**

Implement password parameter, validator, handler user creation with provided password, nullable `RestaurantId`, document type matching, and restaurant portal pre-approval.

- [x] **Step 4: Run tests to verify they pass**

Run: `dotnet test tests/MealMate.UnitTests/MealMate.UnitTests.csproj --filter "FullyQualifiedName~StaffDriverRegistrationCommandTests"`
Expected: PASS.

- [x] **Step 5: Commit**

```bash
git add src/MealMate.Application/Features/IdentityAccess/ tests/MealMate.UnitTests/IdentityAccess/StaffDriverRegistrationCommandTests.cs
git commit -m "feat(auth): enable driver-chosen password, fleet registration, and restaurant pre-approval"
```

---

### Task 3: Dual Approval State Machine & Profile Conversion

**Files:**
- Modify: `src/MealMate.Application/Features/IdentityAccess/DriverRegistration/DriverRegistrationCommands.cs:340-425`
- Modify: `src/MealMate.Application/Features/IdentityAccess/DriverRegistration/RestaurantDriverRegistrations.cs`
- Modify: `src/MealMate.Application/Features/IdentityAccess/StaffAuth/StaffDriverRegistration.cs:570-640`
- Test: `tests/MealMate.UnitTests/IdentityAccess/DriverDualApprovalLifecycleTests.cs`

**Interfaces:**
- Modifies:
  - `AdminDriverDecisionCommandHandler`:
    - Allows `approve` when `RestaurantId == null` without restaurant approval.
    - Copies all regulatory dates and vehicle details from `DriverRegistration` to `DriverProfile`.
    - In `case "reject"`: updates `AccountStatus` to `AccountStatus.Rejected`.
  - `ResubmitDriverRegistrationCommandHandler`:
    - If `AdminApprovalStatus == NeedsChanges` and `RestaurantApprovalStatus == Approved`, preserve restaurant approval and notify admin.
  - `RestaurantDriverDecisionCommandHandler`:
    - Clean state management when restaurant rejects vs approves.

- [x] **Step 1: Write failing unit tests for dual approval lifecycle**

Create `tests/MealMate.UnitTests/IdentityAccess/DriverDualApprovalLifecycleTests.cs`:
1. Admin can approve independent driver (`RestaurantId = null`) directly.
2. When Admin approves, all regulatory expiry dates and vehicle attributes are copied to `DriverProfile`.
3. When Admin rejects, `ApplicationUser` account status is set to `AccountStatus.Rejected`.
4. When driver resubmits after Admin change request, restaurant approval status remains `Approved` and admin is notified.

- [x] **Step 2: Run test to verify it fails**

Run: `dotnet test tests/MealMate.UnitTests/MealMate.UnitTests.csproj --filter "FullyQualifiedName~DriverDualApprovalLifecycleTests"`
Expected: FAIL.

- [x] **Step 3: Implement lifecycle logic in handlers**

Update `AdminDriverDecisionCommandHandler` and `ResubmitDriverRegistrationCommandHandler`.

- [x] **Step 4: Run tests to verify they pass**

Run: `dotnet test tests/MealMate.UnitTests/MealMate.UnitTests.csproj --filter "FullyQualifiedName~DriverDualApprovalLifecycleTests"`
Expected: PASS.

- [x] **Step 5: Commit**

```bash
git add src/MealMate.Application/Features/IdentityAccess/ tests/MealMate.UnitTests/IdentityAccess/DriverDualApprovalLifecycleTests.cs
git commit -m "feat(approval): enforce dual approval state machine and complete DriverProfile conversion"
```

---

### Task 4: Staged Application Status & Secure Phone Lookup

**Files:**
- Modify: `src/MealMate.Application/Features/IdentityAccess/StaffAuth/StaffDriverRegistration.cs:645-720`
- Modify: `src/MealMate.Application/Features/IdentityAccess/StaffAuth/StaffPhoneLookup.cs:70-95`
- Test: `tests/MealMate.UnitTests/IdentityAccess/DriverApplicationStatusQueryTests.cs`

**Interfaces:**
- Modifies: `DriverApplicationStatusDto`:
  - `int Stage`:
    - 1: `PendingRestaurantReview`
    - 2: `PendingAdminReview`
    - 3: `NeedsChanges`
    - 4: `Approved`
    - 5: `Rejected`
  - `string? RequestedByRole`: indicates whether "Restaurant" or "Admin" requested changes or made decision.
  - `bool CanResubmit`: true if `NeedsChanges`.

- [x] **Step 1: Write failing test for staged application status mapper**

Create `tests/MealMate.UnitTests/IdentityAccess/DriverApplicationStatusQueryTests.cs`:
1. Stage 1 returned when pending restaurant.
2. Stage 2 returned when restaurant approved and pending admin.
3. Stage 3 returned when changes requested, identifying requesting party.
4. Stage 4 returned when fully approved.

- [x] **Step 2: Run test to verify it fails**

Run: `dotnet test tests/MealMate.UnitTests/MealMate.UnitTests.csproj --filter "FullyQualifiedName~DriverApplicationStatusQueryTests"`
Expected: FAIL.

- [x] **Step 3: Implement staged mapping in `DriverStatusMapper` and `StaffPhoneLookup`**

Update `DriverStatusMapper.Map` and DTOs.

- [x] **Step 4: Run tests to verify they pass**

Run: `dotnet test tests/MealMate.UnitTests/MealMate.UnitTests.csproj --filter "FullyQualifiedName~DriverApplicationStatusQueryTests"`
Expected: PASS.

- [x] **Step 5: Commit**

```bash
git add src/MealMate.Application/Features/IdentityAccess/StaffAuth/ tests/MealMate.UnitTests/IdentityAccess/DriverApplicationStatusQueryTests.cs
git commit -m "feat(status): provide granular staged status for driver mobile screens"
```

---

### Task 5: Full Solution Verification & Zero-Regression Check

**Files:**
- Solution-wide test runner

- [x] **Step 1: Run complete unit test suite**

Run: `dotnet test tests/MealMate.UnitTests/MealMate.UnitTests.csproj`
Expected: 1,145+ tests passing, 0 failed.

- [x] **Step 2: Run compilation check on API and Persistence**

Run: `dotnet build src/MealMate.Api/MealMate.Api.csproj`
Expected: 0 errors.

- [x] **Step 3: Update plan checkboxes and final commit**

Mark all steps as completed and commit plan.
