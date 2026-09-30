using System;
using System.Collections.Generic;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using FluentAssertions;
using MealMate.Application.Common.Abstractions;
using MealMate.Application.Common.Localization;
using MealMate.Application.Common.Models;
using MealMate.Application.Common.Security;
using MealMate.Application.Features.IdentityAccess.DriverRegistration;
using MealMate.Application.Features.IdentityAccess.StaffAuth;
using MealMate.Domain.Enums;
using MealMate.Domain.Foundation;
using MealMate.Domain.IdentityAccess;
using MealMate.Domain.RestaurantOperations;
using MealMate.Infrastructure.Push;
using MealMate.Persistence;
using Microsoft.EntityFrameworkCore;
using NSubstitute;
using Xunit;

namespace MealMate.UnitTests.IdentityAccess;

public sealed class DriverRegistrationLifecycleFlowTests
{
    private static AppDbContext CreateDb()
    {
        var options = new DbContextOptionsBuilder<AppDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString())
            .Options;
        return new AppDbContext(options, Substitute.For<IDomainEventDispatcher>());
    }

    private static (
        IUserAccountService Users,
        IAuthSessionService Sessions,
        IAuditLogger Audit,
        IDateTimeProvider Clock,
        ILocalizedMessageProvider Messages,
        IDriverRegistrationNotificationService Notifier,
        ICurrentUser CurrentUser) CreateServices()
    {
        var users = Substitute.For<IUserAccountService>();
        var sessions = Substitute.For<IAuthSessionService>();
        var audit = Substitute.For<IAuditLogger>();
        var clock = Substitute.For<IDateTimeProvider>();
        var messages = Substitute.For<ILocalizedMessageProvider>();
        var notifier = Substitute.For<IDriverRegistrationNotificationService>();
        var currentUser = Substitute.For<ICurrentUser>();
        currentUser.IsAuthenticated.Returns(true);
        currentUser.Roles.Returns(new[] { AppRoles.SuperAdmin });
        currentUser.IsInRole(Arg.Any<string>()).Returns(true);
        currentUser.HasPermission(Arg.Any<string>()).Returns(true);

        clock.UtcNow.Returns(new DateTime(2026, 9, 27, 10, 0, 0, DateTimeKind.Utc));
        messages.CurrentCulture.Returns(new System.Globalization.CultureInfo("ar"));
        messages.GetMessage(Arg.Any<string>(), Arg.Any<string?>(), Arg.Any<object[]>())
            .Returns(call => (string?)call[1] ?? (string)call[0]);

        users.FindByIdAsync(Arg.Any<Guid>(), Arg.Any<CancellationToken>())
            .Returns(call => new UserAccountDto((Guid)call[0], "driver@mealmate.local", "+96550000000", "Driver", UserType.Driver, AccountStatus.Registered, null, true, true, DateTime.UtcNow, null, false));

        return (users, sessions, audit, clock, messages, notifier, currentUser);
    }

    [Fact]
    public async Task Scenario1_CompleteDualApprovalLifecycle_FromAppRegistration_ToRestaurant_ToAdminApproval_ToLogin()
    {
        // =========================================================================
        // Step 1: Arrange Infrastructure & Seed Active Approved Restaurant
        // =========================================================================
        await using var db = CreateDb();
        var (users, sessions, audit, clock, messages, notifier, currentUser) = CreateServices();

        var restaurant = new RestaurantProfile
        {
            UserId = Guid.NewGuid(),
            LegalNameAr = "مطعم فيت أند لايت",
            LegalNameEn = "Fit & Light Restaurant",
            TradeNameAr = "فيت أند لايت",
            TradeNameEn = "Fit & Light",
            VerificationStatus = VerificationStatus.Approved,
            IsOperationallyActive = true,
            IsArchived = false
        };
        db.RestaurantProfiles.Add(restaurant);
        await db.SaveChangesAsync();

        var driverUserId = Guid.NewGuid();
        var driverPhone = "+96555123456";
        var driverPassword = "SuperDriverPassword2026!";

        // Mock UserAccountService to create user and check password realistically
        var createdUserAccount = new UserAccountDto(
            driverUserId,
            "driver1@example.com",
            driverPhone,
            "Fahad Al-Mutairi",
            UserType.Driver,
            AccountStatus.Registered,
            restaurant.Id,
            EmailConfirmed: true,
            PhoneNumberConfirmed: true,
            CreatedAtUtc: clock.UtcNow,
            LastLoginAtUtc: null,
            HasSetPassword: true);

        users.FindByPhoneAsync(driverPhone, Arg.Any<CancellationToken>()).Returns((UserAccountDto?)null);
        users.CreateAsync(Arg.Any<CreateUserRequest>(), Arg.Any<CancellationToken>())
            .Returns(Result.Success(createdUserAccount));

        // =========================================================================
        // Step 2: Driver Self-Registers From Mobile App (Screen 02.01 - 02.03)
        // =========================================================================
        var registerHandler = new StaffDriverSelfRegistrationCommandHandler(
            users, db, audit, clock, messages, notifier, DriverRegistrationTestStorage.Create());

        var regCommand = new StaffDriverSelfRegistrationCommand(
            RestaurantId: restaurant.Id,
            FullNameAr: "فهد المطيري",
            FullNameEn: "Fahad Al-Mutairi",
            Phone: driverPhone,
            Email: "driver1@example.com",
            Password: driverPassword,
            NationalId: "296051512345",
            NationalIdExpiry: DateTime.UtcNow.AddYears(3),
            DateOfBirth: new DateTime(1996, 5, 15),
            Nationality: "كويتي",
            VehicleType: "Car",
            VehicleModel: "Toyota Camry 2023",
            VehiclePlate: "KW-88210",
            VehicleYear: 2023,
            VehicleColor: "White",
            IsVehicleOwned: true,
            LicenseNumber: "DL-882100",
            LicenseExpiry: DateTime.UtcNow.AddYears(2),
            VehicleLicenseExpiry: DateTime.UtcNow.AddYears(1),
            ContractExpiry: null,
            NationalIdFrontStorageKey: "docs/nid_front.jpg",
            NationalIdBackStorageKey: "docs/nid_back.jpg",
            DrivingLicenseFrontStorageKey: "docs/lic_front.jpg",
            DrivingLicenseBackStorageKey: "docs/lic_back.jpg",
            VehicleRegistrationStorageKey: "docs/veh_reg.jpg",
            ProfileImageStorageKey: "docs/profile.jpg",
            VehiclePhotoStorageKey: "docs/vehicle.jpg");

        var regResult = await registerHandler.Handle(regCommand, CancellationToken.None);
        regResult.IsSuccess.Should().BeTrue();
        regResult.Value.Status.Should().Be("Submitted");

        var regEntity = await db.DriverRegistrations.FirstOrDefaultAsync(r => r.Phone == driverPhone);
        regEntity.Should().NotBeNull();
        regEntity!.RestaurantApprovalStatus.Should().Be(VerificationStatus.Submitted);
        regEntity.AdminApprovalStatus.Should().Be(VerificationStatus.Submitted);
        regEntity.RegistrationSource.Should().Be("MobileApp");

        // Verify Document entities were automatically attached
        var attachedDocs = await db.Documents.Where(d => d.OwnerId == regEntity.Id).ToListAsync();
        attachedDocs.Should().HaveCount(7);

        // =========================================================================
        // Step 3: Check Initial Status (Screen 03.01 - Under Review)
        // =========================================================================
        var statusHandler = new GetDriverApplicationStatusQueryHandler(db, messages);
        var statusStage1 = await statusHandler.Handle(new GetDriverApplicationStatusQuery(driverPhone, null), CancellationToken.None);
        statusStage1.IsSuccess.Should().BeTrue();
        statusStage1.Value.Stage.Should().Be(1);
        statusStage1.Value.Badge.Should().Be("قيد المراجعة");
        statusStage1.Value.CanResubmit.Should().BeFalse();
        statusStage1.Value.IsApproved.Should().BeFalse();
        statusStage1.Value.RestaurantName.Should().Be("فيت أند لايت");

        // =========================================================================
        // Step 3.1: Guardrail - Driver CANNOT Log In While Account is Pending Approval
        // =========================================================================
        var unitOfWork = Substitute.For<IUnitOfWork>();
        var loginHandler = new StaffLoginCommandHandler(
            users, sessions, audit, unitOfWork, messages);

        users.FindByPhoneAsync(driverPhone, Arg.Any<CancellationToken>()).Returns(createdUserAccount);
        users.CheckPasswordAsync(driverUserId, driverPassword, Arg.Any<CancellationToken>()).Returns(true);

        var prematureLogin = await loginHandler.Handle(
            new StaffLoginCommand(driverPhone, StaffRoles.Driver, driverPassword),
            CancellationToken.None);

        prematureLogin.IsSuccess.Should().BeFalse();
        prematureLogin.Error.Code.Should().Be(MessageCodes.Auth.AccountPendingApproval);

        // =========================================================================
        // Step 4: Guardrail - Admin CANNOT Approve Before Restaurant Approval
        // =========================================================================
        currentUser.UserId.Returns(Guid.NewGuid());
        var adminHandler = new AdminDriverDecisionCommandHandler(
            db, users, currentUser, audit, clock, notifier, null, DriverRegistrationTestStorage.Create());

        var prematureAdminApproval = await adminHandler.Handle(
            new AdminDriverDecisionCommand(regEntity.Id, "approve", "Approved by admin prematurely"),
            CancellationToken.None);

        prematureAdminApproval.IsSuccess.Should().BeFalse();
        prematureAdminApproval.Error.Code.Should().Be(MessageCodes.Driver.RestaurantApprovalRequired);

        // =========================================================================
        // Step 5: Restaurant Reviews - Requests Modification (Screen 03.02 - Needs Changes)
        // =========================================================================
        currentUser.RestaurantId.Returns(restaurant.Id);
        var restaurantReviewHandler = new RestaurantDriverDecisionCommandHandler(
            db, currentUser, audit, clock, notifier);

        var requestChangesResult = await restaurantReviewHandler.Handle(
            new RestaurantDriverDecisionCommand(regEntity.Id, "request-changes", "يرجى رفع صورة أوضح لرخصة القيادة"),
            CancellationToken.None);

        requestChangesResult.IsSuccess.Should().BeTrue();
        requestChangesResult.Value.Status.Should().Be(VerificationStatus.NeedsChanges);

        // Status Query should now show Stage 2 (Needs Changes) with canResubmit = true
        var statusStage2 = await statusHandler.Handle(new GetDriverApplicationStatusQuery(driverPhone, null), CancellationToken.None);
        statusStage2.IsSuccess.Should().BeTrue();
        statusStage2.Value.Stage.Should().Be(2);
        statusStage2.Value.CanResubmit.Should().BeTrue();
        statusStage2.Value.ChangeRequestNotes.Should().Be("يرجى رفع صورة أوضح لرخصة القيادة");
        statusStage2.Value.RequestedByRole.Should().Be("Restaurant");

        // =========================================================================
        // Step 6: Driver Resubmits Updated License Photo
        // =========================================================================
        var resubmitHandler = new ResubmitDriverRegistrationCommandHandler(
            db, audit, clock, messages, notifier, null, null, DriverRegistrationTestStorage.Create());

        var resubmitResult = await resubmitHandler.Handle(
            new ResubmitDriverRegistrationCommand(
                RegistrationId: regEntity.Id,
                DrivingLicenseFrontStorageKey: "docs/lic_front_clear_hd.jpg"),
            CancellationToken.None);

        resubmitResult.IsSuccess.Should().BeTrue();
        resubmitResult.Value.Stage.Should().Be(1); // Returns back to UnderReview
        resubmitResult.Value.CanResubmit.Should().BeFalse();

        // =========================================================================
        // Step 7: Restaurant Approves Registration
        // =========================================================================
        var restaurantApprovalResult = await restaurantReviewHandler.Handle(
            new RestaurantDriverDecisionCommand(regEntity.Id, "approve", "تمت مراجعة رخصة القيادة بنجاح"),
            CancellationToken.None);

        restaurantApprovalResult.IsSuccess.Should().BeTrue();
        restaurantApprovalResult.Value.RestaurantApprovalStatus.Should().Be(VerificationStatus.Approved);
        restaurantApprovalResult.Value.Status.Should().Be(VerificationStatus.UnderReview);

        // Status query now reflects restaurant approval, waiting for admin
        var statusStageRestaurantApproved = await statusHandler.Handle(new GetDriverApplicationStatusQuery(driverPhone, null), CancellationToken.None);
        statusStageRestaurantApproved.Value.Stage.Should().Be(1);
        statusStageRestaurantApproved.Value.Subtitle.Should().Contain("تمت موافقة المطعم");

        // =========================================================================
        // Step 8: Admin Final Approval (Screen 03.04 - Account Approved)
        // =========================================================================
        var adminFinalApproval = await adminHandler.Handle(
            new AdminDriverDecisionCommand(regEntity.Id, "approve", "اعتماد نهائي للسائق بعد اكتمال الفحص والمطابقة"),
            CancellationToken.None);

        adminFinalApproval.IsSuccess.Should().BeTrue();
        adminFinalApproval.Value.Status.Should().Be(VerificationStatus.Approved);
        adminFinalApproval.Value.AdminApprovalStatus.Should().Be(VerificationStatus.Approved);

        // 1. Verify DriverProfile was created in DB with exact driver data
        var createdProfile = await db.DriverProfiles.FirstOrDefaultAsync(p => p.UserId == driverUserId);
        createdProfile.Should().NotBeNull();
        createdProfile!.FullNameAr.Should().Be("فهد المطيري");
        createdProfile.RestaurantId.Should().Be(restaurant.Id);
        createdProfile.NationalId.Should().Be("296051512345");
        createdProfile.LicenseNumber.Should().Be("DL-882100");
        createdProfile.VehiclePlate.Should().Be("KW-88210");
        createdProfile.VerificationStatus.Should().Be(VerificationStatus.Approved);

        // 2. Verify RestaurantDriverLink was established
        var link = await db.RestaurantDriverLinks.FirstOrDefaultAsync(
            l => l.RestaurantId == restaurant.Id && l.DriverProfileId == createdProfile.Id);
        link.Should().NotBeNull();
        link!.IsActive.Should().BeTrue();

        // 3. Verify user account status updated to Active
        await users.Received(1).UpdateAccountStatusAsync(driverUserId, AccountStatus.Active, Arg.Any<CancellationToken>());

        // 4. Verify status query returns Stage 4 (Approved)
        var statusStage4 = await statusHandler.Handle(new GetDriverApplicationStatusQuery(driverPhone, null), CancellationToken.None);
        statusStage4.Value.Stage.Should().Be(4);
        statusStage4.Value.Badge.Should().Be("تم قبول الحساب");
        statusStage4.Value.IsApproved.Should().BeTrue();

        // =========================================================================
        // Step 9: Driver Logs In With Phone and Password (Screen 01.05)
        // =========================================================================
        var activeUserAccount = createdUserAccount with { AccountStatus = AccountStatus.Active };
        users.FindByPhoneAsync(driverPhone, Arg.Any<CancellationToken>()).Returns(activeUserAccount);
        users.CheckPasswordAsync(driverUserId, driverPassword, Arg.Any<CancellationToken>()).Returns(true);

        var authTokens = new AuthTokensDto(
            AccessToken: "session_token_123",
            AccessTokenExpiresAtUtc: clock.UtcNow.AddHours(2),
            RefreshToken: "refresh_token_abc",
            RefreshTokenExpiresAtUtc: clock.UtcNow.AddDays(7));

        var authSessionDto = new AuthSessionDto(
            driverUserId,
            activeUserAccount.Email,
            activeUserAccount.PhoneNumber,
            activeUserAccount.FullName,
            activeUserAccount.UserType,
            activeUserAccount.AccountStatus,
            activeUserAccount.RestaurantId,
            new List<string> { AppRoles.Driver },
            new List<string>(),
            authTokens);

        sessions.IssueSessionAsync(driverUserId, Arg.Any<string?>(), Arg.Any<string?>(), Arg.Any<CancellationToken>())
            .Returns(Result.Success(authSessionDto));

        var loginResult = await loginHandler.Handle(
            new StaffLoginCommand(driverPhone, StaffRoles.Driver, driverPassword),
            CancellationToken.None);

        loginResult.IsSuccess.Should().BeTrue();
        loginResult.Value.AccessToken.Should().Be("session_token_123");
    }

    [Fact]
    public async Task Scenario2_FreelancePlatformDriver_BypassesRestaurantStep_DirectToAdminApproval()
    {
        // Arrange
        await using var db = CreateDb();
        var (users, sessions, audit, clock, messages, notifier, currentUser) = CreateServices();

        var driverUserId = Guid.NewGuid();
        var driverPhone = "+96555998877";
        var driverPassword = "FreelanceSecure2026!";

        var userAccount = new UserAccountDto(
            driverUserId,
            "freelance@mealmate.com",
            driverPhone,
            "Khaled Al-Otaibi",
            UserType.Driver,
            AccountStatus.Registered,
            RestaurantId: null,
            EmailConfirmed: true,
            PhoneNumberConfirmed: true,
            CreatedAtUtc: clock.UtcNow,
            LastLoginAtUtc: null,
            HasSetPassword: true);

        users.FindByPhoneAsync(driverPhone, Arg.Any<CancellationToken>()).Returns((UserAccountDto?)null);
        users.CreateAsync(Arg.Any<CreateUserRequest>(), Arg.Any<CancellationToken>())
            .Returns(Result.Success(userAccount));

        var registerHandler = new StaffDriverSelfRegistrationCommandHandler(
            users, db, audit, clock, messages, notifier, DriverRegistrationTestStorage.Create());

        // Self-register with RestaurantId = null (Independent platform fleet)
        var regCommand = new StaffDriverSelfRegistrationCommand(
            RestaurantId: null,
            FullNameAr: "خالد العتيبي",
            FullNameEn: "Khaled Al-Otaibi",
            Phone: driverPhone,
            Email: "freelance@mealmate.com",
            Password: driverPassword,
            NationalId: "295010199988",
            NationalIdExpiry: DateTime.UtcNow.AddYears(2),
            DateOfBirth: new DateTime(1995, 1, 1),
            Nationality: "كويتي",
            VehicleType: "Motorcycle",
            VehicleModel: "Honda 2022",
            VehiclePlate: "KW-M-102",
            VehicleYear: 2022,
            VehicleColor: "Black",
            IsVehicleOwned: true,
            LicenseNumber: "DL-M-1020",
            LicenseExpiry: DateTime.UtcNow.AddYears(2),
            VehicleLicenseExpiry: DateTime.UtcNow.AddYears(1),
            ContractExpiry: null,
            NationalIdFrontStorageKey: "docs/nid_front.jpg",
            NationalIdBackStorageKey: "docs/nid_back.jpg",
            DrivingLicenseFrontStorageKey: "docs/lic_front.jpg",
            DrivingLicenseBackStorageKey: "docs/lic_back.jpg",
            VehicleRegistrationStorageKey: "docs/veh_reg.jpg",
            ProfileImageStorageKey: "docs/profile.jpg");

        var regResult = await registerHandler.Handle(regCommand, CancellationToken.None);
        regResult.IsSuccess.Should().BeTrue();

        var regEntity = await db.DriverRegistrations.FirstOrDefaultAsync(r => r.Phone == driverPhone);
        regEntity.Should().NotBeNull();
        regEntity!.RestaurantApprovalStatus.Should().Be(VerificationStatus.Approved); // Auto-approved for platform fleet
        regEntity.Status.Should().Be(VerificationStatus.UnderReview); // Straight to admin

        // Admin approves directly without any restaurant approval requirement
        currentUser.UserId.Returns(Guid.NewGuid());
        var adminHandler = new AdminDriverDecisionCommandHandler(
            db, users, currentUser, audit, clock, notifier, null, DriverRegistrationTestStorage.Create());

        var adminApproval = await adminHandler.Handle(
            new AdminDriverDecisionCommand(regEntity.Id, "approve", "Approved directly for MealMate platform fleet"),
            CancellationToken.None);

        adminApproval.IsSuccess.Should().BeTrue();
        adminApproval.Value.Status.Should().Be(VerificationStatus.Approved);

        var driverProfile = await db.DriverProfiles.FirstOrDefaultAsync(p => p.UserId == driverUserId);
        driverProfile.Should().NotBeNull();
        driverProfile!.RestaurantId.Should().BeNull(); // Independent driver
        driverProfile.VehicleType.Should().Be("Motorcycle");
    }

    [Fact]
    public async Task Scenario3_DefensiveGuardrails_DuplicateChecks_And_RejectionFlow()
    {
        // Arrange
        await using var db = CreateDb();
        var (users, sessions, audit, clock, messages, notifier, currentUser) = CreateServices();

        // 1. Seed existing driver with national ID & plate
        var existingReg = new DriverRegistration
        {
            SubmittedByUserId = Guid.NewGuid(),
            FullNameAr = "سائق سابق",
            FullNameEn = "Existing Driver",
            Phone = "+96555111222",
            NationalId = "290010155555",
            VehiclePlate = "KW-99999",
            Status = VerificationStatus.Submitted
        };
        db.DriverRegistrations.Add(existingReg);
        await db.SaveChangesAsync();

        var registerHandler = new StaffDriverSelfRegistrationCommandHandler(
            users, db, audit, clock, messages, notifier, DriverRegistrationTestStorage.Create());

        // A) Attempt registration with duplicate National ID -> Rejected
        var duplicateNidCommand = new StaffDriverSelfRegistrationCommand(
            RestaurantId: null,
            FullNameAr: "سائق جديد",
            FullNameEn: "New Driver",
            Phone: "+96555333444",
            Email: null,
            Password: "Password123!",
            NationalId: "290010155555", // Same NID
            NationalIdExpiry: DateTime.UtcNow.AddYears(1),
            DateOfBirth: null,
            Nationality: "كويتي",
            VehicleType: "Car",
            VehicleModel: "Nissan Sunny",
            VehiclePlate: "KW-11111",
            VehicleYear: 2021,
            VehicleColor: "Silver",
            IsVehicleOwned: true,
            LicenseNumber: "DL-11111",
            LicenseExpiry: DateTime.UtcNow.AddYears(1),
            VehicleLicenseExpiry: DateTime.UtcNow.AddYears(1),
            ContractExpiry: null,
            NationalIdFrontStorageKey: "docs/nid_front.jpg",
            NationalIdBackStorageKey: "docs/nid_back.jpg",
            DrivingLicenseFrontStorageKey: "docs/lic_front.jpg",
            DrivingLicenseBackStorageKey: "docs/lic_back.jpg",
            VehicleRegistrationStorageKey: "docs/veh_reg.jpg");

        var duplicateNidResult = await registerHandler.Handle(duplicateNidCommand, CancellationToken.None);
        duplicateNidResult.IsSuccess.Should().BeFalse();
        duplicateNidResult.Error.Code.Should().Be(MessageCodes.Driver.NationalIdAlreadyRegistered);

        // B) Attempt registration with duplicate Vehicle Plate -> Rejected
        var duplicatePlateCommand = duplicateNidCommand with
        {
            NationalId = "290010199999", // Different NID
            VehiclePlate = "KW-99999"      // Same Plate
        };

        var duplicatePlateResult = await registerHandler.Handle(duplicatePlateCommand, CancellationToken.None);
        duplicatePlateResult.IsSuccess.Should().BeFalse();
        duplicatePlateResult.Error.Code.Should().Be(MessageCodes.Driver.VehiclePlateAlreadyRegistered);

        // C) Admin Rejection Scenario -> Moves User to Suspended and Stage 3
        var rejectedUserId = Guid.NewGuid();
        var rejectedReg = new DriverRegistration
        {
            SubmittedByUserId = rejectedUserId,
            FullNameAr = "سائق مرفوض",
            FullNameEn = "Rejected Driver",
            Phone = "+96555777888",
            NationalId = "290010177777",
            VehiclePlate = "KW-77777",
            Status = VerificationStatus.UnderReview,
            RestaurantApprovalStatus = VerificationStatus.Approved
        };
        db.DriverRegistrations.Add(rejectedReg);
        await db.SaveChangesAsync();

        currentUser.UserId.Returns(Guid.NewGuid());
        var adminHandler = new AdminDriverDecisionCommandHandler(
            db, users, currentUser, audit, clock, notifier, null, DriverRegistrationTestStorage.Create());

        var adminRejectionResult = await adminHandler.Handle(
            new AdminDriverDecisionCommand(rejectedReg.Id, "reject", "عدم استيفاء الشروط التنظيمية للمركبة"),
            CancellationToken.None);

        adminRejectionResult.IsSuccess.Should().BeTrue();
        adminRejectionResult.Value.Status.Should().Be(VerificationStatus.Rejected);

        // Verify account suspended
        await users.Received(1).UpdateAccountStatusAsync(rejectedUserId, AccountStatus.Suspended, Arg.Any<CancellationToken>());

        // Verify Status Query returns Stage 3 (Rejected) with reason
        var statusHandler = new GetDriverApplicationStatusQueryHandler(db, messages);
        var statusResult = await statusHandler.Handle(new GetDriverApplicationStatusQuery("+96555777888", null), CancellationToken.None);
        statusResult.Value.Stage.Should().Be(3);
        statusResult.Value.Badge.Should().Be("تم رفض الطلب");
        statusResult.Value.RejectionReason.Should().Be("عدم استيفاء الشروط التنظيمية للمركبة");
        statusResult.Value.CanResubmit.Should().BeTrue();
    }

    [Fact]
    public async Task Scenario4_PostApproval_PasswordResetFlow_And_RoleMismatch_Protection()
    {
        // =========================================================================
        // Arrange: Existing Active Approved Driver
        // =========================================================================
        await using var db = CreateDb();
        var (users, sessions, audit, clock, messages, notifier, currentUser) = CreateServices();
        var otp = Substitute.For<IOtpService>();
        var challenges = Substitute.For<IAuthLoginChallengeService>();
        var unitOfWork = Substitute.For<IUnitOfWork>();

        var driverUserId = Guid.NewGuid();
        var driverPhone = "+96555000111";
        var initialPassword = "OldPassword123!";
        var newPassword = "NewSecretPassword2026!";

        var driverAccount = new UserAccountDto(
            driverUserId,
            "active.driver@mealmate.com",
            driverPhone,
            "Nasser Al-Ajmi",
            UserType.Driver,
            AccountStatus.Active,
            RestaurantId: null,
            EmailConfirmed: true,
            PhoneNumberConfirmed: true,
            CreatedAtUtc: clock.UtcNow.AddDays(-10),
            LastLoginAtUtc: clock.UtcNow.AddDays(-1),
            HasSetPassword: true);

        users.FindByPhoneAsync(driverPhone, Arg.Any<CancellationToken>()).Returns(driverAccount);
        users.CheckPasswordAsync(driverUserId, initialPassword, Arg.Any<CancellationToken>()).Returns(true);
        users.CheckPasswordAsync(driverUserId, newPassword, Arg.Any<CancellationToken>()).Returns(false);

        // =========================================================================
        // 1. Staff Phone Lookup on Active Driver Account
        // =========================================================================
        var phoneLookupHandler = new StaffPhoneLookupQueryHandler(
            users, db, otp, audit, messages);

        var lookupResult = await phoneLookupHandler.Handle(
            new StaffPhoneLookupQuery(driverPhone, StaffRoles.Driver),
            CancellationToken.None);

        lookupResult.IsSuccess.Should().BeTrue();
        lookupResult.Value.Exists.Should().BeTrue();
        lookupResult.Value.IsFirstTimeSetup.Should().BeFalse();
        lookupResult.Value.Status.Should().Be("Active");

        // Role Mismatch check: querying with DeliveryManager on a Driver account fails
        var mismatchLookup = await phoneLookupHandler.Handle(
            new StaffPhoneLookupQuery(driverPhone, StaffRoles.DeliveryManager),
            CancellationToken.None);

        mismatchLookup.IsSuccess.Should().BeFalse();
        mismatchLookup.Error.Code.Should().Be(MessageCodes.Auth.RoleMismatchDetail);

        // =========================================================================
        // 2. Forgot Password Request
        // =========================================================================
        var forgotPasswordHandler = new StaffForgotPasswordCommandHandler(
            users, otp, audit);

        otp.SendAsync(Arg.Any<OtpSendRequest>(), Arg.Any<CancellationToken>())
            .Returns(Result.Success());

        // Role mismatch on forgot password
        var mismatchForgot = await forgotPasswordHandler.Handle(
            new StaffForgotPasswordCommand(driverPhone, StaffRoles.DeliveryManager),
            CancellationToken.None);

        mismatchForgot.IsSuccess.Should().BeFalse();
        mismatchForgot.Error.Code.Should().Be(MessageCodes.Auth.RoleMismatch);

        // Valid forgot password
        var validForgot = await forgotPasswordHandler.Handle(
            new StaffForgotPasswordCommand(driverPhone, StaffRoles.Driver),
            CancellationToken.None);

        validForgot.IsSuccess.Should().BeTrue();
        await otp.Received(1).SendAsync(
            Arg.Is<OtpSendRequest>(r => r.Destination == driverPhone && r.Purpose == OtpPurposes.PasswordReset),
            Arg.Any<CancellationToken>());

        // =========================================================================
        // 3. Reset Password With OTP
        // =========================================================================
        var resetPasswordHandler = new StaffResetPasswordCommandHandler(
            users, otp, sessions, challenges, audit, unitOfWork);

        // Invalid OTP rejected
        otp.VerifyAsync(Arg.Is<OtpVerifyRequest>(r => r.Code == "000000"), Arg.Any<CancellationToken>())
            .Returns(Error.Validation(MessageCodes.Auth.OtpInvalid, "رمز التحقق غير صحيح."));

        var invalidOtpReset = await resetPasswordHandler.Handle(
            new StaffResetPasswordCommand(driverPhone, StaffRoles.Driver, "000000", newPassword, newPassword),
            CancellationToken.None);

        invalidOtpReset.IsSuccess.Should().BeFalse();
        invalidOtpReset.Error.Code.Should().Be(MessageCodes.Auth.OtpInvalid);

        // Valid OTP succeeds
        var validOtp = "789123";
        otp.VerifyAsync(Arg.Is<OtpVerifyRequest>(r => r.Code == validOtp), Arg.Any<CancellationToken>())
            .Returns(Result.Success());

        users.GeneratePasswordResetTokenAsync(driverUserId, Arg.Any<CancellationToken>())
            .Returns("token_pwd_reset");
        users.ResetPasswordAsync(driverUserId, "token_pwd_reset", newPassword, Arg.Any<CancellationToken>())
            .Returns(Result.Success());

        var validReset = await resetPasswordHandler.Handle(
            new StaffResetPasswordCommand(driverPhone, StaffRoles.Driver, validOtp, newPassword, newPassword),
            CancellationToken.None);

        validReset.IsSuccess.Should().BeTrue();
        await sessions.Received(1).RevokeAllUserTokensAsync(driverUserId, Arg.Any<CancellationToken>());
        await challenges.Received(1).InvalidateAllForUserAsync(driverUserId, Arg.Any<CancellationToken>());

        // =========================================================================
        // 4. Login With New Password
        // =========================================================================
        users.CheckPasswordAsync(driverUserId, initialPassword, Arg.Any<CancellationToken>()).Returns(false);
        users.CheckPasswordAsync(driverUserId, newPassword, Arg.Any<CancellationToken>()).Returns(true);

        var now = new DateTime(2026, 9, 27, 10, 0, 0, DateTimeKind.Utc);
        var authSessionDto = new AuthSessionDto(
            driverUserId,
            driverAccount.Email,
            driverAccount.PhoneNumber,
            driverAccount.FullName,
            driverAccount.UserType,
            driverAccount.AccountStatus,
            driverAccount.RestaurantId,
            new List<string> { AppRoles.Driver },
            new List<string>(),
            new AuthTokensDto("new_session_token_999", now.AddHours(2), "new_ref_token", now.AddDays(7)));

        sessions.IssueSessionAsync(driverUserId, Arg.Any<string?>(), Arg.Any<string?>(), Arg.Any<CancellationToken>())
            .Returns(Result.Success(authSessionDto));

        var loginHandler = new StaffLoginCommandHandler(
            users, sessions, audit, unitOfWork, messages);

        // Old password rejected
        var oldLogin = await loginHandler.Handle(
            new StaffLoginCommand(driverPhone, StaffRoles.Driver, initialPassword),
            CancellationToken.None);

        oldLogin.IsSuccess.Should().BeFalse();
        oldLogin.Error.Code.Should().Be(MessageCodes.Auth.InvalidCredentials);

        // New password succeeds
        var newLogin = await loginHandler.Handle(
            new StaffLoginCommand(driverPhone, StaffRoles.Driver, newPassword),
            CancellationToken.None);

        newLogin.IsSuccess.Should().BeTrue();
        newLogin.Value.AccessToken.Should().Be("new_session_token_999");
    }
}
