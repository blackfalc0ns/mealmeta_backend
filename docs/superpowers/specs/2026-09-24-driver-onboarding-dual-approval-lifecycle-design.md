# Driver Onboarding & Dual Approval Lifecycle Design Spec

**Date:** 2026-09-24  
**Author:** Antigravity & Architecture Team  
**Status:** Approved  
**Target Projects:** `f:\MealMate\backend`, `f:\MealMate\frontend\admin-dashboard`, `f:\MealMate\frontend\restaurant-dashboard`  

---

## 1. Problem Statement & Motivation

The driver registration, review, and dual approval lifecycle in MealMate has accumulated several critical architectural, operational, and security gaps:
1. **Missing Password & Verification at Registration:** In `StaffDriverSelfRegistrationCommand`, the driver does not specify a password; instead, a random temporary GUID password is automatically generated. The driver has no password to log in with, cannot track their application status securely, and anyone typing their phone number can inspect sensitive application notes anonymously.
2. **Platform Fleet / Independent Driver Dead-End:** `RestaurantId` is currently required during registration. Drivers wishing to join the general MealMate fleet cannot register without selecting a specific restaurant. If registered with `RestaurantId = null`, the admin approval logic blocks approval because `RestaurantApprovalStatus != Approved`.
3. **Restaurant Direct Registration Paradox:** When a restaurant registers a driver via `RegisterRestaurantDriverCommand`, `RestaurantApprovalStatus` is left as `Submitted` and `RegistrationSource` defaults to `MobileApp`. When the platform Admin tries to approve the driver, the system errors saying the restaurant must approve first, even though the restaurant created the driver!
4. **Document Type Mismatch in Submission:** `SubmitDriverRegistrationCommand` verifies `DocumentType.NationalId`, but documents are stored as `DocumentType.NationalIdFront` and `NationalIdBack`, causing submission verification errors.
5. **Reset Loop on Resubmission:** When the Admin requests changes (e.g. re-upload a clear driving license), the driver resubmits, but the code resets `RestaurantApprovalStatus` back to `Submitted`, forcing the restaurant to re-approve an application they already approved.
6. **Data Loss upon Driver Profile Creation:** Key regulatory and operational fields (`NationalIdExpiryUtc`, `LicenseExpiryUtc`, `VehicleRegistrationExpiryUtc`, `ContractExpiryUtc`, `VehicleYear`, `VehicleColor`, `IsVehicleOwned`, `Nationality`, `DateOfBirthUtc`) exist on `DriverRegistration` but are absent on `DriverProfile`. They are dropped when converting an approved registration into an active `DriverProfile`.
7. **Orphan Identity on Rejection:** When the Admin rejects a driver registration, the associated `ApplicationUser` account remains in `AccountStatus.Registered` instead of transitioning to `AccountStatus.Rejected`.
8. **Blind Restaurant Rejection:** When a restaurant rejects an applicant (e.g., branch has no capacity), the registration is permanently killed (`VerificationStatus.Rejected`) rather than being released back to the platform fleet or made available for other restaurants.

---

## 2. Target Architecture & Design Principles

### 2.1 Driver Registration Types
1. **Restaurant-Bound Driver:** Registered to a specific restaurant partner. Requires dual approval:
   - Stage 1: Restaurant Review (`RestaurantApprovalStatus`: `Submitted` -> `Approved` / `Rejected` / `NeedsChanges`).
   - Stage 2: Admin Final Review (`AdminApprovalStatus`: `Submitted` -> `Approved` / `Rejected` / `NeedsChanges`).
2. **Platform Fleet / Independent Driver:** Registered without a restaurant (`RestaurantId = null`).
   - Bypasses Stage 1 (`RestaurantApprovalStatus = NotApplicable` or handled seamlessly).
   - Goes directly to Admin Final Review (`AdminApprovalStatus`: `Submitted` -> `Approved` / `Rejected` / `NeedsChanges`).
3. **Restaurant-Created Driver:** Registered by the restaurant manager from the restaurant portal.
   - Stage 1 is automatically `Approved` (with audit trail noting created by restaurant manager).
   - Moves directly to Admin Final Review.

### 2.2 Secure Account Creation & Credentialing
- `StaffDriverSelfRegistrationCommand` accepts `Password` (minimum 8 characters, validated against password rules).
- The user account in `IUserAccountService` is created with the driver's chosen password.
- Account status is `AccountStatus.Registered` until approved.
- Driver can log in or check status with their verified phone and password or secure phone lookup.

### 2.3 Resubmission Workflow (No Restaurant Reset on Admin Changes)
- If `AdminApprovalStatus == NeedsChanges` and `RestaurantApprovalStatus == Approved`:
  - When the driver resubmits, `RestaurantApprovalStatus` **remains** `Approved`.
  - Only `AdminApprovalStatus` and overall `Status` are reset to `Submitted` / `UnderReview`.
  - The Admin is notified via `IDriverRegistrationNotificationService.NotifyAdminsDriverApplicationReadyForConfirmationAsync`.
- Only if the *Restaurant* requested changes does the restaurant re-review.

### 2.4 Non-Fatal Restaurant Rejection
- When a restaurant rejects a driver with reason:
  - If rejected specifically for restaurant capacity ("No vacancy", "Fully staffed"), the driver's `RestaurantId` can be cleared and converted into an Independent/Fleet candidate so the platform admin can link them to another restaurant or platform fleet.

### 2.5 Complete Regulatory Profile (`DriverProfile`)
- Expand `DriverProfile` to persist:
  - `NationalIdExpiryUtc` (DateTime?)
  - `LicenseExpiryUtc` (DateTime?)
  - `VehicleRegistrationExpiryUtc` (DateTime?)
  - `ContractExpiryUtc` (DateTime?)
  - `VehicleYear` (int?)
  - `VehicleColor` (string?)
  - `IsVehicleOwned` (bool?)
  - `Nationality` (string?)
  - `DateOfBirthUtc` (DateTime?)
- Map these fields when creating `DriverProfile` in `AdminDriverDecisionCommandHandler`.
- Update EF Core configuration and migration.

### 2.6 Clean User Account State Transition
- When Admin Approves: `UpdateAccountStatusAsync(userId, AccountStatus.Active)`.
- When Admin Rejects: `UpdateAccountStatusAsync(userId, AccountStatus.Rejected)`.

---

## 3. Data Contracts & Interfaces

### 3.1 `StaffDriverSelfRegistrationCommand`
```csharp
public sealed record StaffDriverSelfRegistrationCommand(
    Guid? RestaurantId,
    string FullNameAr,
    string FullNameEn,
    string Phone,
    string? Email,
    string Password, // Added!
    string NationalId,
    DateTime NationalIdExpiry,
    DateTime? DateOfBirth,
    string? Nationality,
    string VehicleType,
    string VehicleModel,
    string VehiclePlate,
    int? VehicleYear,
    string? VehicleColor,
    bool? IsVehicleOwned,
    string LicenseNumber,
    DateTime LicenseExpiry,
    DateTime VehicleLicenseExpiry,
    DateTime? ContractExpiry,
    string NationalIdFrontStorageKey,
    string NationalIdBackStorageKey,
    string DrivingLicenseFrontStorageKey,
    string DrivingLicenseBackStorageKey,
    string VehicleRegistrationStorageKey,
    string? ProfileImageStorageKey = null,
    string? VehiclePhotoStorageKey = null,
    string? ContractStorageKey = null) : ICommand<StaffDriverRegistrationResultDto>;
```

### 3.2 Enhanced Status Query DTO
```csharp
public sealed record DriverApplicationStatusDto(
    Guid RegistrationId,
    string Phone,
    string FullName,
    string? RestaurantName,
    Guid? RestaurantId,
    string Status, // Submitted, UnderReview, NeedsChanges, Approved, Rejected
    int Stage,     // 1: PendingRestaurant, 2: PendingAdmin, 3: NeedsChanges, 4: Approved, 5: Rejected
    string Badge,
    string Title,
    string Subtitle,
    string? Notice,
    string? ChangeRequestNotes,
    string? RejectionReason,
    bool CanResubmit,
    bool IsApproved,
    string? RequestedByRole, // "Restaurant" or "Admin"
    DateTime CreatedAtUtc,
    DateTime? ReviewedAtUtc);
```

---

## 4. Verification & Testing Strategy
- Unit tests for:
  1. `StaffDriverSelfRegistrationCommand`: verifies password usage, optional `RestaurantId`, and duplicate identity checks.
  2. `RegisterRestaurantDriverCommand`: verifies `RestaurantApprovalStatus = Approved` and `RegistrationSource = RestaurantPortal`.
  3. `SubmitDriverRegistrationCommand`: verifies document presence checking `NationalIdFront` & `DrivingLicenseFront`.
  4. `ResubmitDriverRegistrationCommand`: verifies that admin-requested changes preserve restaurant approval and notify admin.
  5. `AdminDriverDecisionCommand`: verifies independent driver approval, `DriverProfile` expiry field mapping, and user account status set to `Rejected` on rejection.
- Full regression run of `dotnet test` (all 1,140+ unit tests passing).
