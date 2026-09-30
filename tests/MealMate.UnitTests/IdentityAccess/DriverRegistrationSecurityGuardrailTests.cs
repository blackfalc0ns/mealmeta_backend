using FluentAssertions;
using MealMate.Application.Common.Abstractions;
using MealMate.Application.Common.Localization;
using MealMate.Application.Common.Models;
using MealMate.Application.Common.Security;
using MealMate.Application.Features.IdentityAccess.DriverRegistration;
using MealMate.Application.Features.IdentityAccess.StaffAuth;
using MealMate.Domain.Common;
using MealMate.Domain.Enums;
using MealMate.Domain.Foundation;
using MealMate.Domain.IdentityAccess;
using MealMate.Persistence;
using Microsoft.EntityFrameworkCore;
using NSubstitute;
using Xunit;

namespace MealMate.UnitTests.IdentityAccess;

public class DriverRegistrationSecurityGuardrailTests
{
    private static AppDbContext CreateDb()
    {
        var options = new DbContextOptionsBuilder<AppDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString())
            .Options;
        return new AppDbContext(options, Substitute.For<IDomainEventDispatcher>());
    }

    [Fact]
    public async Task Resubmit_WhenUnauthenticated_ReturnsUnauthorized()
    {
        await using var db = CreateDb();
        var reg = new DriverRegistration
        {
            SubmittedByUserId = Guid.NewGuid(),
            Phone = "+96550000001",
            FullNameAr = "سائق تجربة",
            FullNameEn = "Test Driver",
            NationalId = "123456789012",
            VehiclePlate = "12345",
            Status = VerificationStatus.NeedsChanges
        };
        db.DriverRegistrations.Add(reg);
        await db.SaveChangesAsync();

        var currentUser = Substitute.For<ICurrentUser>();
        currentUser.IsAuthenticated.Returns(false);
        var messages = NullLocalizedMessageProvider.Instance;
        var audit = Substitute.For<IAuditLogger>();
        var clock = Substitute.For<IDateTimeProvider>();
        var notifier = Substitute.For<IDriverRegistrationNotificationService>();

        var handler = new ResubmitDriverRegistrationCommandHandler(db, audit, clock, messages, notifier, currentUser);

        var result = await handler.Handle(new ResubmitDriverRegistrationCommand(
            RegistrationId: reg.Id,
            VehicleType: "Car",
            VehicleModel: "Camry",
            VehiclePlate: "12345",
            LicenseNumber: "DL123",
            LicenseExpiry: DateTime.UtcNow.AddYears(1),
            VehicleLicenseExpiry: DateTime.UtcNow.AddYears(1),
            NationalIdFrontStorageKey: "docs/nid_f.jpg",
            NationalIdBackStorageKey: "docs/nid_b.jpg",
            DrivingLicenseFrontStorageKey: "docs/lic_f.jpg",
            DrivingLicenseBackStorageKey: "docs/lic_b.jpg",
            VehicleRegistrationStorageKey: "docs/veh.jpg"), CancellationToken.None);

        result.IsSuccess.Should().BeFalse();
        result.Error.Code.Should().Be("errors.common.unauthorized");
    }

    [Fact]
    public async Task Resubmit_WhenNotOwnerOrAdmin_ReturnsForbidden()
    {
        await using var db = CreateDb();
        var ownerId = Guid.NewGuid();
        var callerId = Guid.NewGuid();
        var reg = new DriverRegistration
        {
            SubmittedByUserId = ownerId,
            Phone = "+96550000001",
            FullNameAr = "سائق تجربة",
            FullNameEn = "Test Driver",
            NationalId = "123456789012",
            VehiclePlate = "12345",
            Status = VerificationStatus.NeedsChanges
        };
        db.DriverRegistrations.Add(reg);
        await db.SaveChangesAsync();

        var currentUser = Substitute.For<ICurrentUser>();
        currentUser.IsAuthenticated.Returns(true);
        currentUser.UserId.Returns(callerId);
        currentUser.Roles.Returns(Array.Empty<string>());
        var messages = NullLocalizedMessageProvider.Instance;
        var audit = Substitute.For<IAuditLogger>();
        var clock = Substitute.For<IDateTimeProvider>();
        var notifier = Substitute.For<IDriverRegistrationNotificationService>();

        var handler = new ResubmitDriverRegistrationCommandHandler(db, audit, clock, messages, notifier, currentUser);

        var result = await handler.Handle(new ResubmitDriverRegistrationCommand(
            RegistrationId: reg.Id,
            VehicleType: "Car",
            VehicleModel: "Camry",
            VehiclePlate: "12345",
            LicenseNumber: "DL123",
            LicenseExpiry: DateTime.UtcNow.AddYears(1),
            VehicleLicenseExpiry: DateTime.UtcNow.AddYears(1),
            NationalIdFrontStorageKey: "docs/nid_f.jpg",
            NationalIdBackStorageKey: "docs/nid_b.jpg",
            DrivingLicenseFrontStorageKey: "docs/lic_f.jpg",
            DrivingLicenseBackStorageKey: "docs/lic_b.jpg",
            VehicleRegistrationStorageKey: "docs/veh.jpg"), CancellationToken.None);

        result.IsSuccess.Should().BeFalse();
        result.Error.Code.Should().Be("auth.forbidden");
    }

    [Fact]
    public async Task Resubmit_WhenAlreadyApproved_ReturnsConflict()
    {
        await using var db = CreateDb();
        var ownerId = Guid.NewGuid();
        var reg = new DriverRegistration
        {
            SubmittedByUserId = ownerId,
            Phone = "+96550000001",
            FullNameAr = "سائق تجربة",
            FullNameEn = "Test Driver",
            NationalId = "123456789012",
            VehiclePlate = "12345",
            Status = VerificationStatus.Approved
        };
        db.DriverRegistrations.Add(reg);
        await db.SaveChangesAsync();

        var currentUser = Substitute.For<ICurrentUser>();
        currentUser.IsAuthenticated.Returns(true);
        currentUser.UserId.Returns(ownerId);
        currentUser.Roles.Returns(Array.Empty<string>());
        var messages = NullLocalizedMessageProvider.Instance;
        var audit = Substitute.For<IAuditLogger>();
        var clock = Substitute.For<IDateTimeProvider>();
        var notifier = Substitute.For<IDriverRegistrationNotificationService>();

        var handler = new ResubmitDriverRegistrationCommandHandler(db, audit, clock, messages, notifier, currentUser);

        var result = await handler.Handle(new ResubmitDriverRegistrationCommand(
            RegistrationId: reg.Id,
            VehicleType: "Car",
            VehicleModel: "Camry",
            VehiclePlate: "12345",
            LicenseNumber: "DL123",
            LicenseExpiry: DateTime.UtcNow.AddYears(1),
            VehicleLicenseExpiry: DateTime.UtcNow.AddYears(1),
            NationalIdFrontStorageKey: "docs/nid_f.jpg",
            NationalIdBackStorageKey: "docs/nid_b.jpg",
            DrivingLicenseFrontStorageKey: "docs/lic_f.jpg",
            DrivingLicenseBackStorageKey: "docs/lic_b.jpg",
            VehicleRegistrationStorageKey: "docs/veh.jpg"), CancellationToken.None);

        result.IsSuccess.Should().BeFalse();
        result.Error.Code.Should().Be("ALREADY_APPROVED");
    }

    [Fact]
    public async Task RestaurantDriverDecision_WhenRestaurantScopeMismatch_ReturnsForbidden()
    {
        await using var db = CreateDb();
        var regRestaurantId = Guid.NewGuid();
        var callerRestaurantId = Guid.NewGuid();
        var reg = new DriverRegistration
        {
            RestaurantId = regRestaurantId,
            Phone = "+96550000001",
            FullNameAr = "سائق تجربة",
            FullNameEn = "Test Driver",
            NationalId = "123456789012",
            VehiclePlate = "12345",
            Status = VerificationStatus.Submitted,
            RestaurantApprovalStatus = VerificationStatus.Submitted
        };
        db.DriverRegistrations.Add(reg);
        await db.SaveChangesAsync();

        var currentUser = Substitute.For<ICurrentUser>();
        currentUser.IsAuthenticated.Returns(true);
        currentUser.RestaurantId.Returns(callerRestaurantId);
        currentUser.Roles.Returns(new[] { AppRoles.RestaurantOwner });
        var audit = Substitute.For<IAuditLogger>();
        var clock = Substitute.For<IDateTimeProvider>();
        var notifier = Substitute.For<IDriverRegistrationNotificationService>();

        var handler = new RestaurantDriverDecisionCommandHandler(db, currentUser, audit, clock, notifier);

        var result = await handler.Handle(new RestaurantDriverDecisionCommand(
            reg.Id, "approve", "Approved by restaurant"), CancellationToken.None);

        result.IsSuccess.Should().BeFalse();
        result.Error.Code.Should().Be("auth.forbidden");
    }

    [Fact]
    public async Task RegisterRestaurantDriver_CreatesUnderReviewWithApprovedRestaurantAndSubmittedAdmin()
    {
        await using var db = CreateDb();
        var restaurantId = Guid.NewGuid();
        var restaurant = new RestaurantProfile
        {
            UserId = Guid.NewGuid(),
            TradeNameAr = "مطعم الاختبار",
            TradeNameEn = "Test Restaurant",
            LegalNameAr = "مطعم الاختبار ذ.م.م",
            LegalNameEn = "Test Restaurant LLC",
            VerificationStatus = VerificationStatus.Approved,
            IsOperationallyActive = true
        };
        db.RestaurantProfiles.Add(restaurant);
        await db.SaveChangesAsync();

        var currentUser = Substitute.For<ICurrentUser>();
        currentUser.IsAuthenticated.Returns(true);
        currentUser.RestaurantId.Returns(restaurantId);
        currentUser.Roles.Returns(new[] { AppRoles.RestaurantOwner });

        var newDriverUserId = Guid.NewGuid();
        var users = Substitute.For<IUserAccountService>();
        users.FindByPhoneAsync(Arg.Any<string>(), Arg.Any<CancellationToken>()).Returns((UserAccountDto?)null);
        users.CreateAsync(Arg.Any<CreateUserRequest>(), Arg.Any<CancellationToken>())
            .Returns(Task.FromResult(MealMate.Application.Common.Models.Result.Success(new UserAccountDto(
                newDriverUserId, "driver@example.com", "+96590001111", "سائق جديد",
                UserType.Driver, AccountStatus.Registered, restaurantId, false, false, DateTime.UtcNow, null))));

        var audit = Substitute.For<IAuditLogger>();
        var clock = Substitute.For<IDateTimeProvider>();
        var notifier = Substitute.For<IDriverRegistrationNotificationService>();

        var handler = new RegisterRestaurantDriverCommandHandler(users, db, currentUser, audit, clock, notifier, DriverRegistrationTestStorage.Create());

        var result = await handler.Handle(new RegisterRestaurantDriverCommand(
            RestaurantId: restaurantId,
            FullNameAr: "سائق جديد",
            FullNameEn: "New Driver",
            Phone: "+96590001111",
            Email: "driver@example.com",
            NationalId: "299010199999",
            NationalIdExpiry: DateTime.UtcNow.AddYears(1),
            VehiclePlate: "KW-99988",
            VehicleType: "Car",
            VehicleModel: "Toyota",
            LicenseNumber: "DL99988",
            LicenseExpiry: DateTime.UtcNow.AddYears(1),
            VehicleLicenseExpiry: DateTime.UtcNow.AddYears(1),
            ContractExpiry: null,
            NationalIdFrontStorageKey: "docs/nid_f.jpg",
            NationalIdBackStorageKey: "docs/nid_b.jpg",
            DrivingLicenseFrontStorageKey: "docs/lic_f.jpg",
            DrivingLicenseBackStorageKey: "docs/lic_b.jpg",
            VehicleRegistrationStorageKey: "docs/veh.jpg",
            ProfileImageStorageKey: "docs/photo.jpg"), CancellationToken.None);

        result.IsSuccess.Should().BeTrue();
        result.Value.Status.Should().Be(VerificationStatus.UnderReview);
        result.Value.RestaurantApprovalStatus.Should().Be(VerificationStatus.Approved);
        result.Value.AdminApprovalStatus.Should().Be(VerificationStatus.Submitted);

        // Crucial requirement: No active driver profile created yet
        var profile = await db.DriverProfiles.FirstOrDefaultAsync(d => d.UserId == newDriverUserId);
        profile.Should().BeNull("Restaurant-created drivers must not have an active operational profile until final Admin approval.");
    }

    [Fact]
    public async Task SendExpiryAlert_WhenCallerLacksPermission_ReturnsForbidden()
    {
        await using var db = CreateDb();
        var driver = new DriverProfile
        {
            UserId = Guid.NewGuid(),
            FullNameAr = "سائق تجريبي",
            FullNameEn = "Test Driver",
            VerificationStatus = VerificationStatus.Approved
        };
        db.DriverProfiles.Add(driver);
        await db.SaveChangesAsync();

        var currentUser = Substitute.For<ICurrentUser>();
        currentUser.IsAuthenticated.Returns(true);
        currentUser.Roles.Returns(new List<string> { "Customer" });
        currentUser.HasPermission(PermissionCodes.DriverRegView).Returns(false);

        var clock = Substitute.For<IDateTimeProvider>();
        clock.UtcNow.Returns(DateTime.UtcNow);

        var handler = new SendDriverDocumentExpiryAlertCommandHandler(db, currentUser, clock);

        var result = await handler.Handle(new SendDriverDocumentExpiryAlertCommand(driver.Id), CancellationToken.None);

        result.IsSuccess.Should().BeFalse();
        result.Error.Code.Should().Be("Driver.AlertForbidden");
    }

    [Fact]
    public async Task SendExpiryAlert_WhenAdmin_PersistsNotificationRecordInDatabase()
    {
        await using var db = CreateDb();
        var driverUserId = Guid.NewGuid();
        var driver = new DriverProfile
        {
            UserId = driverUserId,
            FullNameAr = "سائق تجريبي",
            FullNameEn = "Test Driver",
            VerificationStatus = VerificationStatus.Approved
        };
        db.DriverProfiles.Add(driver);
        await db.SaveChangesAsync();

        var currentUser = Substitute.For<ICurrentUser>();
        currentUser.IsAuthenticated.Returns(true);
        currentUser.Roles.Returns(new List<string> { AppRoles.SuperAdmin });

        var now = new DateTime(2026, 9, 30, 10, 0, 0, DateTimeKind.Utc);
        var clock = Substitute.For<IDateTimeProvider>();
        clock.UtcNow.Returns(now);

        var handler = new SendDriverDocumentExpiryAlertCommandHandler(db, currentUser, clock);

        var result = await handler.Handle(new SendDriverDocumentExpiryAlertCommand(driver.Id), CancellationToken.None);

        result.IsSuccess.Should().BeTrue();

        var savedNotification = await db.DriverNotifications
            .FirstOrDefaultAsync(n => n.DriverProfileId == driver.Id && n.UserId == driverUserId);

        savedNotification.Should().NotBeNull();
        savedNotification!.Type.Should().Be("document-expiry-warning");
        savedNotification.DeepLink.Should().Be("/driver/profile/documents");
        savedNotification.SentAtUtc.Should().Be(now);
        savedNotification.IsRead.Should().BeFalse();
    }

    [Theory]
    [InlineData(VerificationStatus.UnderReview)]
    [InlineData(VerificationStatus.Rejected)]
    public async Task GetRestaurantDriverDetail_WhenQueriedMultipleTimes_DoesNotMutateStatusOrProfile(VerificationStatus initialStatus)
    {
        await using var db = CreateDb();
        var restaurantId = Guid.NewGuid();
        var driverUserId = Guid.NewGuid();

        var registration = new DriverRegistration
        {
            SubmittedByUserId = driverUserId,
            RestaurantId = restaurantId,
            RegistrationSource = "RestaurantDashboard",
            FullNameAr = "سائق مطعم",
            FullNameEn = "Restaurant Driver",
            Phone = "+96598765432",
            NationalId = "299010199887",
            Status = initialStatus,
            RestaurantApprovalStatus = VerificationStatus.Approved,
            AdminApprovalStatus = initialStatus == VerificationStatus.Rejected ? VerificationStatus.Rejected : VerificationStatus.Submitted,
            ResultingDriverProfileId = null
        };
        db.DriverRegistrations.Add(registration);
        await db.SaveChangesAsync();

        var currentUser = Substitute.For<ICurrentUser>();
        currentUser.IsAuthenticated.Returns(true);
        currentUser.RestaurantId.Returns(restaurantId);

        var users = Substitute.For<IUserAccountService>();
        users.FindByIdAsync(driverUserId, Arg.Any<CancellationToken>())
            .Returns(new UserAccountDto(
                driverUserId,
                null,
                registration.Phone,
                registration.FullNameAr,
                UserType.Driver,
                AccountStatus.Registered,
                restaurantId,
                false,
                true,
                DateTime.UtcNow,
                null,
                true));

        var handler = new GetRestaurantDriverDetailQueryHandler(db, users, currentUser);

        // Act - Call query multiple times
        var result1 = await handler.Handle(new GetRestaurantDriverDetailQuery(registration.Id), CancellationToken.None);
        var result2 = await handler.Handle(new GetRestaurantDriverDetailQuery(registration.Id), CancellationToken.None);

        // Assert - Query returns the actual current status, not fake Approved
        result1.IsSuccess.Should().BeTrue();
        result1.Value.VerificationStatus.Should().Be(initialStatus);
        result1.Value.DriverProfileId.Should().BeNull();
        result1.Value.AccountStatus.Should().Be(AccountStatus.Registered);

        result2.IsSuccess.Should().BeTrue();
        result2.Value.VerificationStatus.Should().Be(initialStatus);

        // Assert - Database entity was NOT mutated
        var inDb = await db.DriverRegistrations.AsNoTracking().FirstAsync(r => r.Id == registration.Id);
        inDb.Status.Should().Be(initialStatus);
        inDb.AdminApprovalStatus.Should().Be(initialStatus == VerificationStatus.Rejected ? VerificationStatus.Rejected : VerificationStatus.Submitted);
        inDb.ResultingDriverProfileId.Should().BeNull();

        // Assert - No DriverProfile was created
        var profileInDb = await db.DriverProfiles.FirstOrDefaultAsync(p => p.UserId == driverUserId);
        profileInDb.Should().BeNull("Reading driver details must NEVER create or auto-approve a DriverProfile");
    }

    [Fact]
    public async Task AdminApprove_WhenOlderDocumentWasRejectedButNewDocumentUploaded_Succeeds()
    {
        await using var db = CreateDb();
        var driverUserId = Guid.NewGuid();
        var reg = new DriverRegistration
        {
            SubmittedByUserId = driverUserId,
            FullNameAr = "سائق معتمد",
            FullNameEn = "Approved Driver",
            Phone = "+96590008888",
            NationalId = "299010177777",
            NationalIdExpiryUtc = DateTime.UtcNow.AddYears(1),
            LicenseNumber = "DL-7777",
            LicenseExpiryUtc = DateTime.UtcNow.AddYears(1),
            VehiclePlate = "12345",
            VehicleRegistrationExpiryUtc = DateTime.UtcNow.AddYears(1),
            Status = VerificationStatus.UnderReview,
            RestaurantApprovalStatus = VerificationStatus.Approved,
            AdminApprovalStatus = VerificationStatus.Submitted,
            ProfileImageStorageKey = "docs/photo.jpg"
        };
        db.DriverRegistrations.Add(reg);

        db.Documents.Add(new Document { OwnerType = DocumentOwnerType.Registration, OwnerId = reg.Id, DocumentType = DocumentType.NationalIdFront, StoragePath = "docs/nid.jpg", FileName = "nid.jpg", ContentType = "image/jpeg" });
        db.Documents.Add(new Document { OwnerType = DocumentOwnerType.Registration, OwnerId = reg.Id, DocumentType = DocumentType.VehicleRegistration, StoragePath = "docs/veh.jpg", FileName = "veh.jpg", ContentType = "image/jpeg" });

        var oldDoc = new Document
        {
            OwnerType = DocumentOwnerType.Registration,
            OwnerId = reg.Id,
            DocumentType = DocumentType.DrivingLicenseFront,
            StoragePath = "docs/old_dl_front.jpg",
            FileName = "old_dl_front.jpg",
            ContentType = "image/jpeg",
            CreatedAtUtc = DateTime.UtcNow.AddDays(-2),
            ExpiresAtUtc = DateTime.UtcNow.AddYears(1)
        };
        oldDoc.Verifications.Add(new DocumentVerification
        {
            Status = VerificationStatus.Rejected,
            Reason = "Blurry photo",
            CreatedAtUtc = DateTime.UtcNow.AddDays(-2)
        });
        db.Documents.Add(oldDoc);

        var newDoc = new Document
        {
            OwnerType = DocumentOwnerType.Registration,
            OwnerId = reg.Id,
            DocumentType = DocumentType.DrivingLicenseFront,
            StoragePath = "docs/new_dl_front.jpg",
            FileName = "new_dl_front.jpg",
            ContentType = "image/jpeg",
            CreatedAtUtc = DateTime.UtcNow.AddDays(-1),
            ExpiresAtUtc = DateTime.UtcNow.AddYears(1)
        };
        newDoc.Verifications.Add(new DocumentVerification
        {
            Status = VerificationStatus.Submitted,
            CreatedAtUtc = DateTime.UtcNow.AddDays(-1)
        });
        db.Documents.Add(newDoc);

        await db.SaveChangesAsync();

        var users = Substitute.For<IUserAccountService>();
        users.FindByIdAsync(driverUserId, Arg.Any<CancellationToken>())
            .Returns(new UserAccountDto(driverUserId, "driver@test.local", reg.Phone, reg.FullNameEn, UserType.Driver, AccountStatus.Registered, null, true, true, DateTime.UtcNow, null, false));
        var currentUser = Substitute.For<ICurrentUser>();
        currentUser.IsAuthenticated.Returns(true);
        currentUser.Roles.Returns(new List<string> { AppRoles.SuperAdmin });

        var audit = Substitute.For<IAuditLogger>();
        var clock = Substitute.For<IDateTimeProvider>();
        clock.UtcNow.Returns(DateTime.UtcNow);
        var notifier = Substitute.For<IDriverRegistrationNotificationService>();

        var handler = new AdminDriverDecisionCommandHandler(db, users, currentUser, audit, clock, notifier, null, DriverRegistrationTestStorage.Create());

        // Act
        var result = await handler.Handle(new AdminDriverDecisionCommand(reg.Id, "approve", "Approved"), CancellationToken.None);

        // Assert - Approval must succeed because the latest document version is not rejected
        result.IsSuccess.Should().BeTrue();
        reg.Status.Should().Be(VerificationStatus.Approved);
    }

    [Fact]
    public async Task AdminApprove_WhenRegistrationHasPhoneMatchingAnotherDriver_DoesNotMutateOtherDriverProfile()
    {
        await using var db = CreateDb();

        // Driver B: existing approved driver with their own profile and phone
        var userBId = Guid.NewGuid();
        var profileB = new DriverProfile
        {
            UserId = userBId,
            FullNameAr = "السائق أحمد الأصلي",
            FullNameEn = "Original Driver B",
            NationalId = "299000000002",
            VerificationStatus = VerificationStatus.Approved,
            IsAvailable = true
        };
        db.DriverProfiles.Add(profileB);

        // Driver A: separate user with registration, but registration phone was set to Driver B's phone
        var userAId = Guid.NewGuid();
        var sharedPhone = "+96550000002";

        var regA = new DriverRegistration
        {
            SubmittedByUserId = userAId,
            Phone = sharedPhone,
            FullNameAr = "السائق منتحل أ",
            FullNameEn = "Attacker Driver A",
            NationalId = "299000000001",
            NationalIdExpiryUtc = DateTime.UtcNow.AddYears(2),
            LicenseNumber = "DL99901",
            LicenseExpiryUtc = DateTime.UtcNow.AddYears(2),
            VehiclePlate = "KW99901",
            VehicleType = "Car",
            VehicleRegistrationExpiryUtc = DateTime.UtcNow.AddYears(1),
            Status = VerificationStatus.UnderReview,
            ProfileImageStorageKey = "docs/photo_a.jpg"
        };
        db.DriverRegistrations.Add(regA);
        db.Documents.Add(new Document { OwnerType = DocumentOwnerType.Registration, OwnerId = regA.Id, DocumentType = DocumentType.NationalIdFront, StoragePath = "nid_a.jpg", FileName = "nid_a.jpg", ContentType = "image/jpeg" });
        db.Documents.Add(new Document { OwnerType = DocumentOwnerType.Registration, OwnerId = regA.Id, DocumentType = DocumentType.DrivingLicenseFront, StoragePath = "lic_a.jpg", FileName = "lic_a.jpg", ContentType = "image/jpeg" });
        db.Documents.Add(new Document { OwnerType = DocumentOwnerType.Registration, OwnerId = regA.Id, DocumentType = DocumentType.VehicleRegistration, StoragePath = "veh_a.jpg", FileName = "veh_a.jpg", ContentType = "image/jpeg" });

        await db.SaveChangesAsync();

        var users = Substitute.For<IUserAccountService>();
        // user A account exists
        users.FindByIdAsync(userAId, Arg.Any<CancellationToken>()).Returns(new UserAccountDto(userAId, "a@test.local", sharedPhone, "Driver A", UserType.Driver, AccountStatus.Registered, null, true, true, DateTime.UtcNow, null, false));
        // FindByPhone returns Driver B
        users.FindByPhoneAsync(sharedPhone, Arg.Any<CancellationToken>()).Returns(new UserAccountDto(userBId, "b@test.local", sharedPhone, "Driver B", UserType.Driver, AccountStatus.Active, null, true, true, DateTime.UtcNow, null, true));

        var currentUser = Substitute.For<ICurrentUser>();
        currentUser.IsAuthenticated.Returns(true);
        currentUser.Roles.Returns(new List<string> { AppRoles.SuperAdmin });

        var audit = Substitute.For<IAuditLogger>();
        var clock = Substitute.For<IDateTimeProvider>();
        clock.UtcNow.Returns(DateTime.UtcNow);
        var notifier = Substitute.For<IDriverRegistrationNotificationService>();

        var handler = new AdminDriverDecisionCommandHandler(db, users, currentUser, audit, clock, notifier, null, DriverRegistrationTestStorage.Create());

        // Act
        var result = await handler.Handle(new AdminDriverDecisionCommand(regA.Id, "approve", "Approved"), CancellationToken.None);

        // Assert: Driver B's profile MUST NOT be touched or mutated!
        var reloadedProfileB = await db.DriverProfiles.FirstAsync(p => p.Id == profileB.Id);
        reloadedProfileB.FullNameAr.Should().Be("السائق أحمد الأصلي");
        reloadedProfileB.FullNameEn.Should().Be("Original Driver B");
        reloadedProfileB.NationalId.Should().Be("299000000002");
        reloadedProfileB.UserId.Should().Be(userBId);

        // Instead, a new/separate profile for Driver A (userAId) was created or linked
        var profileA = await db.DriverProfiles.FirstOrDefaultAsync(p => p.UserId == userAId);
        profileA.Should().NotBeNull();
        profileA!.FullNameAr.Should().Be("السائق منتحل أ");
        regA.ResultingDriverProfileId.Should().Be(profileA.Id);
    }

    [Fact]
    public async Task GetRestaurantDriverDetail_WhenCallerIsCustomerOrDriver_ReturnsForbidden()
    {
        await using var db = CreateDb();
        var reg = new DriverRegistration
        {
            SubmittedByUserId = Guid.NewGuid(),
            Phone = "+96551111111",
            FullNameAr = "سائق خاص",
            FullNameEn = "Private Driver",
            RestaurantId = Guid.NewGuid(),
            Status = VerificationStatus.UnderReview
        };
        db.DriverRegistrations.Add(reg);
        await db.SaveChangesAsync();

        var users = Substitute.For<IUserAccountService>();
        var currentUser = Substitute.For<ICurrentUser>();
        currentUser.IsAuthenticated.Returns(true);
        currentUser.Roles.Returns(new List<string> { AppRoles.Customer }); // Regular customer, no restaurant id
        currentUser.RestaurantId.Returns((Guid?)null);

        var handler = new GetRestaurantDriverDetailQueryHandler(db, users, currentUser);

        // Act
        var result = await handler.Handle(new GetRestaurantDriverDetailQuery(reg.Id), CancellationToken.None);

        // Assert: Must be Forbidden
        result.IsSuccess.Should().BeFalse();
        result.Error.Code.Should().Be("errors.auth.forbidden");
    }

    [Fact]
    public async Task GetRestaurantDriverDetail_WhenCallerHasDifferentRestaurantId_ReturnsNotFound()
    {
        await using var db = CreateDb();
        var targetRestId = Guid.NewGuid();
        var callerRestId = Guid.NewGuid();

        var reg = new DriverRegistration
        {
            SubmittedByUserId = Guid.NewGuid(),
            Phone = "+96551111112",
            FullNameAr = "سائق مطعم آخر",
            FullNameEn = "Other Restaurant Driver",
            RestaurantId = targetRestId,
            Status = VerificationStatus.UnderReview
        };
        db.DriverRegistrations.Add(reg);
        await db.SaveChangesAsync();

        var users = Substitute.For<IUserAccountService>();
        var currentUser = Substitute.For<ICurrentUser>();
        currentUser.IsAuthenticated.Returns(true);
        currentUser.Roles.Returns(new List<string> { AppRoles.RestaurantOwner });
        currentUser.RestaurantId.Returns(callerRestId);

        var handler = new GetRestaurantDriverDetailQueryHandler(db, users, currentUser);

        // Act
        var result = await handler.Handle(new GetRestaurantDriverDetailQuery(reg.Id), CancellationToken.None);

        // Assert: Must be NotFound
        result.IsSuccess.Should().BeFalse();
        result.Error.Code.Should().Be("Driver.NotFound");
    }

    [Fact]
    public async Task GetRestaurantDriverDetail_WhenTargetIsIndependentDriver_AndCallerIsRestaurant_ReturnsNotFound()
    {
        await using var db = CreateDb();
        var callerRestId = Guid.NewGuid();

        var reg = new DriverRegistration
        {
            SubmittedByUserId = Guid.NewGuid(),
            Phone = "+96551111113",
            FullNameAr = "سائق حر مستقل",
            FullNameEn = "Freelance Driver",
            RestaurantId = null, // Independent
            Status = VerificationStatus.UnderReview
        };
        db.DriverRegistrations.Add(reg);
        await db.SaveChangesAsync();

        var users = Substitute.For<IUserAccountService>();
        var currentUser = Substitute.For<ICurrentUser>();
        currentUser.IsAuthenticated.Returns(true);
        currentUser.Roles.Returns(new List<string> { AppRoles.RestaurantOwner });
        currentUser.RestaurantId.Returns(callerRestId);

        var handler = new GetRestaurantDriverDetailQueryHandler(db, users, currentUser);

        // Act
        var result = await handler.Handle(new GetRestaurantDriverDetailQuery(reg.Id), CancellationToken.None);

        // Assert: Must be NotFound
        result.IsSuccess.Should().BeFalse();
        result.Error.Code.Should().Be("Driver.NotFound");
    }

    [Fact]
    public async Task GetRestaurantDriverDetail_WhenCallerIsAdmin_ReturnsSuccess()
    {
        await using var db = CreateDb();
        var restId = Guid.NewGuid();

        var reg = new DriverRegistration
        {
            SubmittedByUserId = Guid.NewGuid(),
            Phone = "+96551111114",
            FullNameAr = "سائق مطعم",
            FullNameEn = "Restaurant Driver",
            RestaurantId = restId,
            Status = VerificationStatus.UnderReview
        };
        db.DriverRegistrations.Add(reg);
        await db.SaveChangesAsync();

        var users = Substitute.For<IUserAccountService>();
        var currentUser = Substitute.For<ICurrentUser>();
        currentUser.IsAuthenticated.Returns(true);
        currentUser.Roles.Returns(new List<string> { AppRoles.OperationsAdmin });
        currentUser.IsInRole(AppRoles.OperationsAdmin).Returns(true);
        currentUser.RestaurantId.Returns((Guid?)null);

        var handler = new GetRestaurantDriverDetailQueryHandler(db, users, currentUser);

        // Act
        var result = await handler.Handle(new GetRestaurantDriverDetailQuery(reg.Id), CancellationToken.None);

        // Assert: Admin can view any driver registration
        result.IsSuccess.Should().BeTrue();
        result.Value.FullNameAr.Should().Be("سائق مطعم");
    }

    [Fact]
    public async Task Resubmit_WhenNewPhoneBelongsToAnotherUser_ReturnsConflict()
    {
        await using var db = CreateDb();
        var myUserId = Guid.NewGuid();
        var otherUserId = Guid.NewGuid();
        var otherPhone = "+96559998888";

        var reg = new DriverRegistration
        {
            SubmittedByUserId = myUserId,
            Phone = "+96551112222",
            FullNameAr = "سائق مقدم الطلب",
            FullNameEn = "Applicant Driver",
            Status = VerificationStatus.NeedsChanges,
            AdminApprovalStatus = VerificationStatus.NeedsChanges
        };
        db.DriverRegistrations.Add(reg);
        await db.SaveChangesAsync();

        var users = Substitute.For<IUserAccountService>();
        users.FindByPhoneAsync(otherPhone, Arg.Any<CancellationToken>())
            .Returns(new UserAccountDto(otherUserId, "other@test.local", otherPhone, "Other Driver", UserType.Driver, AccountStatus.Active, null, true, true, DateTime.UtcNow, null, true));

        var audit = Substitute.For<IAuditLogger>();
        var clock = Substitute.For<IDateTimeProvider>();
        clock.UtcNow.Returns(DateTime.UtcNow);
        var messages = Substitute.For<ILocalizedMessageProvider>();
        var notifier = Substitute.For<IDriverRegistrationNotificationService>();
        var currentUser = Substitute.For<ICurrentUser>();
        currentUser.IsAuthenticated.Returns(true);
        currentUser.UserId.Returns(myUserId);
        currentUser.Roles.Returns(new List<string> { AppRoles.Driver });

        var handler = new ResubmitDriverRegistrationCommandHandler(db, audit, clock, messages, notifier, currentUser, users, DriverRegistrationTestStorage.Create());

        // Act
        var result = await handler.Handle(new ResubmitDriverRegistrationCommand(
            reg.Id,
            Phone: otherPhone,
            VehiclePlate: "NEW-123"), CancellationToken.None);

        // Assert: Must reject phone change with Conflict
        result.IsSuccess.Should().BeFalse();
        result.Error.Code.Should().Be("Driver.PhoneChangeRequiresVerification");
    }
}
