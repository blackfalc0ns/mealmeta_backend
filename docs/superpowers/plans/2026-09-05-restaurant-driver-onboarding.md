# Restaurant Driver Onboarding & Mobile First-Time Setup Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Enable restaurant managers to register drivers with 8 required fields and 6 documents (with expiry dates and front/back images), enable drivers to activate via phone-only OTP + password creation on mobile, and provide admin verification.

**Architecture:** 
- Backend CQRS commands for restaurant driver registration, document tracking with expiries, phone lookup, and OTP-based first-time password setup.
- Restaurant dashboard Step Wizard modal (3 steps) for driver onboarding with file upload previews.
- Admin dashboard pending driver verification screen enhancements to review all 6 documents and expiries.
- Clear mobile contract documentation for the driver mobile app.

**Tech Stack:** .NET 9 C#, ASP.NET Core Identity, MediatR, FluentValidation, Entity Framework Core, Angular 19 Standalone Components, TailwindCSS / CSS tokens.

**Spec:** [`f:\MealMate\backend\docs\superpowers\specs\2026-09-05-restaurant-driver-onboarding-design.md`](file:///f:/MealMate/backend/docs/superpowers/specs/2026-09-05-restaurant-driver-onboarding-design.md)

## Global Constraints
- Phone number must be unique across all active/registered users in the platform.
- National ID must be unique across all driver registrations and profiles.
- All 6 documents are mandatory: National ID (Front & Back), Driving License (Front & Back), Vehicle Registration, Employment Contract.
- All expiry dates must be future dates (`> DateTime.UtcNow.Date`).
- Passwords must meet minimum complexity (min 8 characters).

---

### Task 1: Domain Entities & Enums Extensions

**Files:**
- Modify: `f:\MealMate\backend\src\MealMate.Domain\Enums\Enums.cs:178-199`
- Modify: `f:\MealMate\backend\src\MealMate.Domain\IdentityAccess\DriverRegistration.cs:1-46`
- Modify: `f:\MealMate\backend\src\MealMate.Domain\IdentityAccess\DriverProfile.cs:1-38`
- Modify: `f:\MealMate\backend\src\MealMate.Persistence\Configurations\IdentityAccess\IdentityAccessConfigurations.cs`

**Interfaces:**
- Produces:
  - `DocumentType.DrivingLicenseFront = 19`, `DocumentType.DrivingLicenseBack = 20`
  - `DriverRegistration.VehicleModel`, `DriverRegistration.NationalIdExpiryUtc`, `DriverRegistration.LicenseExpiryUtc`, `DriverRegistration.VehicleRegistrationExpiryUtc`, `DriverRegistration.ContractExpiryUtc`
  - `DriverProfile.VehicleModel`

- [ ] **Step 1: Write tests for domain entity properties**
Create `tests/MealMate.Domain.Tests/IdentityAccess/DriverRegistrationTests.cs`:
```csharp
using System;
using MealMate.Domain.Enums;
using MealMate.Domain.IdentityAccess;
using Xunit;

namespace MealMate.Domain.Tests.IdentityAccess;

public class DriverRegistrationTests
{
    [Fact]
    public void DriverRegistration_ShouldSupportVehicleModelAndExpiries()
    {
        var reg = new DriverRegistration
        {
            VehicleModel = "Toyota Corolla 2022",
            NationalIdExpiryUtc = DateTime.UtcNow.AddYears(2),
            LicenseExpiryUtc = DateTime.UtcNow.AddYears(3),
            VehicleRegistrationExpiryUtc = DateTime.UtcNow.AddYears(1),
            ContractExpiryUtc = DateTime.UtcNow.AddYears(1)
        };

        Assert.Equal("Toyota Corolla 2022", reg.VehicleModel);
        Assert.NotNull(reg.NationalIdExpiryUtc);
    }
}
```

- [ ] **Step 2: Run test to verify it fails**
Run: `dotnet test tests/MealMate.Domain.Tests --filter FullyQualifiedName~DriverRegistrationTests`
Expected: Compile failure on missing `VehicleModel` or expiries.

- [ ] **Step 3: Update `Enums.cs`, `DriverRegistration.cs`, and `DriverProfile.cs`**
Add `DrivingLicenseFront = 19` and `DrivingLicenseBack = 20` in `MealMate.Domain.Enums.DocumentType`.
Add properties to `DriverRegistration`:
```csharp
public string? VehicleModel { get; set; }
public DateTime? NationalIdExpiryUtc { get; set; }
public DateTime? LicenseExpiryUtc { get; set; }
public DateTime? VehicleRegistrationExpiryUtc { get; set; }
public DateTime? ContractExpiryUtc { get; set; }
```
Add property to `DriverProfile`:
```csharp
public string? VehicleModel { get; set; }
```

- [ ] **Step 4: Update EF Core Configuration in `IdentityAccessConfigurations.cs`**
Add property max length configuration:
```csharp
builder.Property(x => x.VehicleModel).HasMaxLength(100);
```

- [ ] **Step 5: Run tests and verify they pass**
Run: `dotnet test tests/MealMate.Domain.Tests --filter FullyQualifiedName~DriverRegistrationTests`
Expected: PASS.

- [ ] **Step 6: Commit**
```bash
git add src/MealMate.Domain src/MealMate.Persistence tests/MealMate.Domain.Tests
git commit -m "feat(domain): add vehicle model and expiries to driver registration"
```

---

### Task 2: Restaurant Driver Registration Command & Endpoint

**Files:**
- Create: `f:\MealMate\backend\src\MealMate.Application\Features\IdentityAccess\DriverRegistration\RegisterRestaurantDriverCommand.cs`
- Modify: `f:\MealMate\backend\src\MealMate.Api\Controllers\V1\RestaurantDriversController.cs`
- Test: `f:\MealMate\backend\tests\MealMate.Application.Tests\IdentityAccess\RegisterRestaurantDriverCommandTests.cs`

**Interfaces:**
- Consumes: `IUserAccountService`, `IAppDbContext`, `ICurrentUser`, `IDateTimeProvider`
- Produces: `POST /api/v1/restaurants/drivers/register` endpoint returning `DriverRegistrationDto`.

- [ ] **Step 1: Write failing validation test for RegisterRestaurantDriverCommand**
```csharp
[Fact]
public void ShouldFail_WhenPhoneOrNationalIdIsMissing()
{
    var command = new RegisterRestaurantDriverCommand(
        FullNameAr: "أحمد", FullFullNameEn: "Ahmed", Phone: "", NationalId: "", ...);
    var validator = new RegisterRestaurantDriverCommandValidator();
    var result = validator.Validate(command);
    Assert.False(result.IsValid);
}
```

- [ ] **Step 2: Run test to verify failure**
Run: `dotnet test tests/MealMate.Application.Tests --filter FullyQualifiedName~RegisterRestaurantDriverCommandTests`
Expected: FAIL (types not found).

- [ ] **Step 3: Implement Command and Handler**
Implement `RegisterRestaurantDriverCommand`:
- Validates:
  - Phone format and uniqueness against `_users.FindByPhoneAsync()`.
  - NationalId uniqueness against `_db.DriverRegistrations` and `_db.DriverProfiles`.
  - Expiry dates (`NationalIdExpiry`, `LicenseExpiry`, `VehicleLicenseExpiry`, `ContractExpiry`) > UtcNow.
  - All 6 storage keys present.
- Execution:
  - Creates driver user via `_users.CreateAsync(...)` with status `Registered`.
  - Assigns role `AppRoles.Driver`.
  - Creates `DriverRegistration` entity with status `Submitted` and assigns `RestaurantId`.
  - Inserts 6 `Document` entities for:
    - `DocumentType.NationalIdFront`
    - `DocumentType.NationalIdBack`
    - `DocumentType.DrivingLicenseFront`
    - `DocumentType.DrivingLicenseBack`
    - `DocumentType.VehicleRegistration`
    - `DocumentType.EmploymentContract`
  - Saves changes and logs audit entry.

- [ ] **Step 4: Expose `POST /api/v1/restaurants/drivers/register` in `RestaurantDriversController.cs`**
```csharp
[HttpPost("register")]
public async Task<IActionResult> RegisterDriver(
    [FromBody] RegisterRestaurantDriverCommand body,
    CancellationToken cancellationToken)
{
    var result = await Sender.Send(body, cancellationToken);
    return HandleCreated(result, $"/api/v1/restaurants/drivers");
}
```

- [ ] **Step 5: Run tests and verify PASS**
Run: `dotnet test tests/MealMate.Application.Tests --filter FullyQualifiedName~RegisterRestaurantDriverCommandTests`
Expected: PASS.

- [ ] **Step 6: Commit**
```bash
git add src/MealMate.Application src/MealMate.Api tests/MealMate.Application.Tests
git commit -m "feat(api): add restaurant driver registration endpoint"
```

---

### Task 3: Mobile Driver Phone Lookup & First-Time Activation Endpoints

**Files:**
- Create: `f:\MealMate\backend\src\MealMate.Application\Features\IdentityAccess\Auth\DriverPhoneLookupQuery.cs`
- Create: `f:\MealMate\backend\src\MealMate.Application\Features\IdentityAccess\Auth\DriverFirstTimeSetupCommand.cs`
- Modify: `f:\MealMate\backend\src\MealMate.Api\Controllers\V1\DriverAuthController.cs`
- Test: `f:\MealMate\backend\tests\MealMate.Application.Tests\IdentityAccess\DriverPhoneLookupAndSetupTests.cs`

**Interfaces:**
- Produces:
  - `POST /api/v1/auth/drivers/lookup-phone`
  - `POST /api/v1/auth/drivers/first-time-setup`

- [ ] **Step 1: Write failing test for lookup & first-time setup**
Create tests for:
1. Lookup unregistered phone -> returns NotFound.
2. Lookup driver requiring first-time setup -> returns `isFirstTimeSetup: true` and dispatches OTP.
3. Lookup driver with existing password -> returns `isFirstTimeSetup: false`.
4. First-time setup with valid OTP -> sets password, returns JWT token.

- [ ] **Step 2: Run test to verify failure**
Run: `dotnet test tests/MealMate.Application.Tests --filter FullyQualifiedName~DriverPhoneLookupAndSetupTests`
Expected: FAIL.

- [ ] **Step 3: Implement `DriverPhoneLookupQuery` and `DriverFirstTimeSetupCommand`**
- `DriverPhoneLookupQuery`:
  - Finds user by phone with `UserType == Driver`.
  - Checks if user has a password hash. If null or empty -> `isFirstTimeSetup = true`.
  - If first-time setup: triggers `_otp.SendAsync` with purpose `VerifyPhone`.
- `DriverFirstTimeSetupCommand`:
  - Validates OTP via `_otp.VerifyAsync`.
  - Sets password via `_users.SetPasswordAsync` / `UserManager.AddPasswordAsync`.
  - Generates JWT token and Refresh token via `ITokenService`.
  - Returns `DriverAuthResult` with `Status = "PendingApproval"`.

- [ ] **Step 4: Expose endpoints in `DriverAuthController.cs`**
Add:
```csharp
[HttpPost("lookup-phone")]
[AllowAnonymous]
public async Task<IActionResult> LookupPhone([FromBody] DriverLookupPhoneRequest body, CancellationToken ct)
    => HandleResult(await Sender.Send(new DriverPhoneLookupQuery(body.Phone), ct));

[HttpPost("first-time-setup")]
[AllowAnonymous]
public async Task<IActionResult> FirstTimeSetup([FromBody] DriverFirstTimeSetupRequest body, CancellationToken ct)
    => HandleResult(await Sender.Send(new DriverFirstTimeSetupCommand(body.Phone, body.OtpCode, body.Password), ct));
```

- [ ] **Step 5: Run tests and verify PASS**
Run: `dotnet test tests/MealMate.Application.Tests --filter FullyQualifiedName~DriverPhoneLookupAndSetupTests`
Expected: PASS.

- [ ] **Step 6: Commit**
```bash
git add src/MealMate.Application src/MealMate.Api tests/MealMate.Application.Tests
git commit -m "feat(auth): add driver phone lookup and first-time activation flow"
```

---

### Task 4: Admin Verification Screen & Documents Query Enhancement

**Files:**
- Modify: `f:\MealMate\backend\src\MealMate.Application\Features\IdentityAccess\DriverRegistration\DriverRegistrationCommands.cs`
- Modify: `f:\MealMate\backend\src\MealMate.Application\Features\IdentityAccess\Documents\DocumentQueries.cs`
- Modify: `f:\MealMate\backend\src\MealMate.Api\Controllers\V1\DriverRegistrationsController.cs`

**Interfaces:**
- Produces:
  - Document DTO containing `expiresAtUtc` and updated `DocumentType`.
  - `DriverRegistrationDto` containing `vehicleModel` and expiries.

- [ ] **Step 1: Write test verifying Admin gets complete driver details and document expiries**
Verify `GetDriverRegistrationQuery` returns `VehicleModel` and document records contain `ExpiresAtUtc`.

- [ ] **Step 2: Update DTOs and Mappings**
Update `DriverRegistrationDto` to include `VehicleModel`, `NationalIdExpiryUtc`, `LicenseExpiryUtc`, `VehicleRegistrationExpiryUtc`, `ContractExpiryUtc`.
Update `DocumentDto` to ensure `ExpiresAtUtc` is included.

- [ ] **Step 3: Update `AdminDriverDecisionCommandHandler`**
When approved, ensure `profile.VehicleModel = registration.VehicleModel`.

- [ ] **Step 4: Run backend tests**
Run: `dotnet test`
Expected: All backend tests pass.

- [ ] **Step 5: Commit**
```bash
git add src/MealMate.Application src/MealMate.Api tests/
git commit -m "feat(admin): map vehicle model and document expiries in admin driver queries"
```

---

### Task 5: Restaurant Dashboard - 3-Step Driver Onboarding Wizard

**Files:**
- Create: `f:\MealMate\frontend\restaurant-dashboard\src\app\features\restaurant\drivers\components\add-driver-wizard-dialog\add-driver-wizard-dialog.component.ts`
- Create: `f:\MealMate\frontend\restaurant-dashboard\src\app\features\restaurant\drivers\components\add-driver-wizard-dialog\add-driver-wizard-dialog.component.html`
- Modify: `f:\MealMate\frontend\restaurant-dashboard\src\app\features\restaurant\drivers\data\restaurant-drivers-api.service.ts`
- Modify: `f:\MealMate\frontend\restaurant-dashboard\src\app\features\restaurant\drivers\drivers.page.ts`
- Modify: `f:\MealMate\frontend\restaurant-dashboard\src\app\features\restaurant\drivers\drivers.page.html`

**Interfaces:**
- Consumes: `POST /api/v1/restaurants/drivers/register`, `POST /api/v1/documents/upload`
- Produces: Interactive modal with 3 steps:
  - Step 1: Personal Info & National ID (Front/Back) + Expiry.
  - Step 2: Vehicle (Model, Plate, Type) & Driver License (Front/Back) + Expiry & Vehicle License + Expiry.
  - Step 3: Restaurant Contract + Expiry & Final Submission.

- [ ] **Step 1: Update `restaurant-drivers-api.service.ts`**
Add `registerDriver(payload)` method and file upload helper.

- [ ] **Step 2: Build `AddDriverWizardDialogComponent`**
- Signal-driven state:
  - `currentStep = signal<1 | 2 | 3>(1)`
  - Form validation for each step before advancing.
  - File drag & drop and click upload with preview thumbnails.
  - Expiry date inputs with min date validation (`min="today"`).
  - Clean RTL styling consistent with MealMate's design tokens.

- [ ] **Step 3: Add button and connect dialog in `drivers.page.ts` & `.html`**
- Add primary button: `+ إضافة سائق جديد` (Add New Driver).
- On click, open `mm-add-driver-wizard-dialog`.
- On successful submission, refresh list and show success toast notification.

- [ ] **Step 4: Verify Angular Build in `restaurant-dashboard`**
Run: `npm run build` in `f:\MealMate\frontend\restaurant-dashboard`
Expected: Build succeeds with 0 errors.

- [ ] **Step 5: Commit**
```bash
git add src/app/features/restaurant/drivers
git commit -m "feat(restaurant-ui): add 3-step driver onboarding wizard"
```

---

### Task 6: Admin Dashboard - Enhanced Pending Driver Verification View

**Files:**
- Modify: `f:\MealMate\frontend\admin-dashboard\src\app\features\admin\accounts\models\restaurant-admin.model.ts`
- Modify: `f:\MealMate\frontend\admin-dashboard\src\app\features\admin\accounts\pages\pending-driver-detail-page\pending-driver-detail-page.component.ts`
- Modify: `f:\MealMate\frontend\admin-dashboard\src\app\features\admin\accounts\pages\pending-driver-detail-page\pending-driver-detail-page.component.html`

**Interfaces:**
- Consumes: `GET /api/v1/admin/drivers/registrations/{id}`, `GET /api/v1/admin/drivers/registrations/{id}/documents`
- Produces: Enhanced review UI displaying all 6 documents, expiration status badges, and vehicle model.

- [ ] **Step 1: Update `restaurant-admin.model.ts`**
Add `vehicleModel`, `nationalIdExpiryUtc`, `licenseExpiryUtc`, `vehicleRegistrationExpiryUtc`, `contractExpiryUtc`, and `expiresAtUtc` to document DTOs.

- [ ] **Step 2: Update `pending-driver-detail-page.component.html`**
- Add Vehicle Model & Type card in the vehicle info section.
- Display documents grid with:
  - Categorized slots: الهوية الوطنية (وجه أمامي / وجه خلفي)، رخصة القيادة (وجه أمامي / وجه خلفي)، رخصة المركبة، عقد المطعم.
  - Expiration badge (الأخضر: ساري، الأصفر: ينتهي قريباً، الأحمر: منتهي الصلاحية).
  - Quick document preview and zoom modal.

- [ ] **Step 3: Verify Angular Build in `admin-dashboard`**
Run: `npm run build` in `f:\MealMate\frontend\admin-dashboard`
Expected: Build succeeds with 0 errors.

- [ ] **Step 4: Commit**
```bash
git add src/app/features/admin/accounts
git commit -m "feat(admin-ui): display 6 driver documents with expiries and vehicle model"
```

---

### Task 7: Mobile Driver App Contract Documentation & Final Verification

**Files:**
- Create: `f:\MealMate\backend\docs\mobile\driver-first-time-login-api.md`

- [ ] **Step 1: Write detailed API quickguide for mobile developers**
Document exact requests, responses, HTTP error codes, and sample payloads for:
- `POST /api/v1/auth/drivers/lookup-phone`
- `POST /api/v1/auth/drivers/first-time-setup`
- Handling the `PendingApproval` state on the driver home screen.

- [ ] **Step 2: Run end-to-end backend tests**
Run: `dotnet test` in `f:\MealMate\backend`
Expected: All tests pass.

- [ ] **Step 3: Commit**
```bash
git add docs/mobile/driver-first-time-login-api.md
git commit -m "docs(mobile): add driver first-time login API specification"
```
