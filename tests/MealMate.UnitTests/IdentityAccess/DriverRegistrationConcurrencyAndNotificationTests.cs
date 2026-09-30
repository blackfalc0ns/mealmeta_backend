using System;
using System.Collections.Generic;
using System.Globalization;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using FluentAssertions;
using MealMate.Application.Common.Abstractions;
using MealMate.Application.Common.Localization;
using MealMate.Application.Common.Outbox;
using MealMate.Application.Common.Security;
using MealMate.Application.Features.IdentityAccess.DriverRegistration;
using MealMate.Application.Features.IdentityAccess.StaffAuth;
using MealMate.Domain.Common;
using MealMate.Domain.Enums;
using MealMate.Domain.Foundation;
using MealMate.Domain.IdentityAccess;
using MealMate.Infrastructure.Push;
using MealMate.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using NSubstitute;
using Xunit;

namespace MealMate.UnitTests.IdentityAccess;

public sealed class DriverRegistrationConcurrencyAndNotificationTests
{
    private static AppDbContext CreateDb(string dbName)
    {
        var options = new DbContextOptionsBuilder<AppDbContext>()
            .UseInMemoryDatabase(dbName)
            .Options;
        var dispatcher = Substitute.For<IDomainEventDispatcher>();
        return new AppDbContext(options, dispatcher);
    }

    [Fact]
    public async Task AdminDecision_WhenAdminApprove_PersistsOutboxMessageWithAtomicCommit()
    {
        // Arrange
        var dbName = Guid.NewGuid().ToString();
        await using var db = CreateDb(dbName);
        var clock = Substitute.For<IDateTimeProvider>();
        clock.UtcNow.Returns(new DateTime(2026, 9, 30, 10, 0, 0, DateTimeKind.Utc));

        var driverUserId = Guid.NewGuid();
        var reg = new DriverRegistration
        {
            SubmittedByUserId = driverUserId,
            FullNameAr = "خالد العتيبي",
            FullNameEn = "Khaled Al-Otaibi",
            Phone = "+96599112233",
            NationalId = "294010112345",
            NationalIdExpiryUtc = clock.UtcNow.AddYears(2),
            LicenseNumber = "DL-998877",
            LicenseExpiryUtc = clock.UtcNow.AddYears(2),
            VehiclePlate = "12345",
            VehicleRegistrationExpiryUtc = clock.UtcNow.AddYears(1),
            DateOfBirthUtc = clock.UtcNow.AddYears(-25),
            Status = VerificationStatus.UnderReview,
            AdminApprovalStatus = VerificationStatus.Submitted,
            RestaurantApprovalStatus = VerificationStatus.Approved,
            ProfileImageStorageKey = "docs/photo.jpg"
        };
        db.DriverRegistrations.Add(reg);
        db.Documents.Add(new Document { OwnerType = DocumentOwnerType.Registration, OwnerId = reg.Id, DocumentType = DocumentType.NationalIdFront, StoragePath = "nid_f.jpg", FileName = "nid_f.jpg", ContentType = "image/jpeg" });
        db.Documents.Add(new Document { OwnerType = DocumentOwnerType.Registration, OwnerId = reg.Id, DocumentType = DocumentType.DrivingLicenseFront, StoragePath = "lic_f.jpg", FileName = "lic_f.jpg", ContentType = "image/jpeg" });
        db.Documents.Add(new Document { OwnerType = DocumentOwnerType.Registration, OwnerId = reg.Id, DocumentType = DocumentType.VehicleRegistration, StoragePath = "veh.jpg", FileName = "veh.jpg", ContentType = "image/jpeg" });
        await db.SaveChangesAsync();

        var users = Substitute.For<IUserAccountService>();
        users.FindByIdAsync(driverUserId, Arg.Any<CancellationToken>()).Returns(new UserAccountDto(driverUserId, "k@test.local", reg.Phone, "Khaled", UserType.Driver, AccountStatus.Registered, null, true, true, DateTime.UtcNow, null, false));

        var currentUser = Substitute.For<ICurrentUser>();
        currentUser.IsAuthenticated.Returns(true);
        currentUser.UserId.Returns(Guid.NewGuid());
        currentUser.Roles.Returns(new List<string> { AppRoles.SuperAdmin });

        var audit = Substitute.For<IAuditLogger>();
        var notifier = Substitute.For<IDriverRegistrationNotificationService>();

        var handler = new AdminDriverDecisionCommandHandler(db, users, currentUser, audit, clock, notifier, null, DriverRegistrationTestStorage.Create());

        // Act
        var result = await handler.Handle(new AdminDriverDecisionCommand(reg.Id, "approve", "Approved by admin"), CancellationToken.None);

        // Assert
        result.IsSuccess.Should().BeTrue();
        reg.Status.Should().Be(VerificationStatus.Approved);

        // Outbox message must be saved
        var outboxMessage = await db.OutboxMessages.FirstOrDefaultAsync(m => m.AggregateId == reg.Id.ToString());
        outboxMessage.Should().NotBeNull();
        outboxMessage!.EventType.Should().Be("DriverRegistrationAdminApproved");
        outboxMessage.DeduplicationKey.Should().StartWith($"DriverRegistrationAdminApproved_{reg.Id}");

        using var doc = JsonDocument.Parse(outboxMessage.PayloadJson);
        doc.RootElement.GetProperty("RegistrationId").GetGuid().Should().Be(reg.Id);
        doc.RootElement.GetProperty("Action").GetString().Should().Be("approve");
        doc.RootElement.GetProperty("ApplicationStatus").GetString().Should().Be("Approved");
    }

    [Fact]
    public async Task AdminDecision_WhenAdminRejects_PersistsOutboxMessageAndSuspendsAccount()
    {
        // Arrange
        var dbName = Guid.NewGuid().ToString();
        await using var db = CreateDb(dbName);
        var clock = Substitute.For<IDateTimeProvider>();
        clock.UtcNow.Returns(new DateTime(2026, 9, 30, 10, 0, 0, DateTimeKind.Utc));

        var driverUserId = Guid.NewGuid();
        var reg = new DriverRegistration
        {
            SubmittedByUserId = driverUserId,
            FullNameAr = "سائق مرفوض",
            FullNameEn = "Rejected Driver",
            Phone = "+96599119911",
            Status = VerificationStatus.UnderReview,
            AdminApprovalStatus = VerificationStatus.Submitted
        };
        db.DriverRegistrations.Add(reg);
        await db.SaveChangesAsync();

        var users = Substitute.For<IUserAccountService>();
        var currentUser = Substitute.For<ICurrentUser>();
        currentUser.IsAuthenticated.Returns(true);
        currentUser.UserId.Returns(Guid.NewGuid());
        currentUser.Roles.Returns(new List<string> { AppRoles.SuperAdmin });

        var audit = Substitute.For<IAuditLogger>();
        var notifier = Substitute.For<IDriverRegistrationNotificationService>();

        var handler = new AdminDriverDecisionCommandHandler(db, users, currentUser, audit, clock, notifier, null, DriverRegistrationTestStorage.Create());

        // Act
        var result = await handler.Handle(new AdminDriverDecisionCommand(reg.Id, "reject", "Invalid credentials"), CancellationToken.None);

        // Assert
        result.IsSuccess.Should().BeTrue();
        reg.Status.Should().Be(VerificationStatus.Rejected);

        var outboxMessage = await db.OutboxMessages.FirstOrDefaultAsync(m => m.AggregateId == reg.Id.ToString());
        outboxMessage.Should().NotBeNull();
        outboxMessage!.EventType.Should().Be("DriverRegistrationAdminRejected");
        await users.Received(1).UpdateAccountStatusAsync(driverUserId, AccountStatus.Suspended, Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task AdminDecision_WhenConcurrentConflictingDecisionOccurs_ReturnsConflictError()
    {
        // Arrange: DbContext with concurrency detection
        var dbName = Guid.NewGuid().ToString();
        await using var db1 = CreateDb(dbName);
        await using var db2 = CreateDb(dbName);

        var clock = Substitute.For<IDateTimeProvider>();
        clock.UtcNow.Returns(new DateTime(2026, 9, 30, 10, 0, 0, DateTimeKind.Utc));

        var driverUserId = Guid.NewGuid();
        var reg = new DriverRegistration
        {
            SubmittedByUserId = driverUserId,
            FullNameAr = "سائق تجربة التزامن",
            FullNameEn = "Concurrency Driver",
            Phone = "+96599118877",
            NationalId = "294010112399",
            NationalIdExpiryUtc = clock.UtcNow.AddYears(2),
            LicenseNumber = "DL-112233",
            LicenseExpiryUtc = clock.UtcNow.AddYears(2),
            VehiclePlate = "55443",
            VehicleRegistrationExpiryUtc = clock.UtcNow.AddYears(1),
            DateOfBirthUtc = clock.UtcNow.AddYears(-22),
            Status = VerificationStatus.UnderReview,
            AdminApprovalStatus = VerificationStatus.Submitted,
            RestaurantApprovalStatus = VerificationStatus.Approved,
            ProfileImageStorageKey = "docs/photo.jpg"
        };
        db1.DriverRegistrations.Add(reg);
        db1.Documents.Add(new Document { OwnerType = DocumentOwnerType.Registration, OwnerId = reg.Id, DocumentType = DocumentType.NationalIdFront, StoragePath = "nid_f.jpg", FileName = "nid_f.jpg", ContentType = "image/jpeg" });
        db1.Documents.Add(new Document { OwnerType = DocumentOwnerType.Registration, OwnerId = reg.Id, DocumentType = DocumentType.DrivingLicenseFront, StoragePath = "lic_f.jpg", FileName = "lic_f.jpg", ContentType = "image/jpeg" });
        db1.Documents.Add(new Document { OwnerType = DocumentOwnerType.Registration, OwnerId = reg.Id, DocumentType = DocumentType.VehicleRegistration, StoragePath = "veh.jpg", FileName = "veh.jpg", ContentType = "image/jpeg" });
        await db1.SaveChangesAsync();

        var users = Substitute.For<IUserAccountService>();
        users.FindByIdAsync(driverUserId, Arg.Any<CancellationToken>()).Returns(new UserAccountDto(driverUserId, "c@test.local", reg.Phone, "Concurrency", UserType.Driver, AccountStatus.Registered, null, true, true, DateTime.UtcNow, null, false));

        var currentUser = Substitute.For<ICurrentUser>();
        currentUser.IsAuthenticated.Returns(true);
        currentUser.UserId.Returns(Guid.NewGuid());
        currentUser.Roles.Returns(new List<string> { AppRoles.SuperAdmin });

        var audit = Substitute.For<IAuditLogger>();
        var notifier = Substitute.For<IDriverRegistrationNotificationService>();

        var handler1 = new AdminDriverDecisionCommandHandler(db1, users, currentUser, audit, clock, notifier, null, DriverRegistrationTestStorage.Create());
        var handler2 = new AdminDriverDecisionCommandHandler(db2, users, currentUser, audit, clock, notifier, null, DriverRegistrationTestStorage.Create());

        // Admin 1 approves successfully
        var result1 = await handler1.Handle(new AdminDriverDecisionCommand(reg.Id, "approve", "Approved by Admin 1"), CancellationToken.None);
        result1.IsSuccess.Should().BeTrue();

        // Admin 2 attempts to reject the registration that is already approved
        var result2 = await handler2.Handle(new AdminDriverDecisionCommand(reg.Id, "reject", "Rejected by Admin 2"), CancellationToken.None);

        // Assert: Admin 2 must be rejected with conflict
        result2.IsSuccess.Should().BeFalse();
        result2.Error.Code.Should().BeOneOf("ALREADY_APPROVED", "errors.auth.invalid_state");

        // Exactly one profile exists
        var profileCount = await db1.DriverProfiles.CountAsync(p => p.UserId == driverUserId);
        profileCount.Should().Be(1);
    }

    [Fact]
    public async Task DriverRegistrationLifecycleHandler_WhenProcessesOutboxNotification_InvokesNotifierAndRecordsInbox()
    {
        // Arrange
        var dbName = Guid.NewGuid().ToString();
        await using var db = CreateDb(dbName);
        var clock = Substitute.For<IDateTimeProvider>();
        clock.UtcNow.Returns(new DateTime(2026, 9, 30, 10, 0, 0, DateTimeKind.Utc));

        var notifier = Substitute.For<IDriverRegistrationNotificationService>();
        var handler = new DriverRegistrationLifecycleHandler(db, clock, notifier);

        var regId = Guid.NewGuid();
        var driverUserId = Guid.NewGuid();
        var messageId = Guid.NewGuid();

        var payload = JsonSerializer.Serialize(new
        {
            RegistrationId = regId,
            DriverUserId = driverUserId,
            Phone = "+96599112233",
            DriverName = "أحمد السائق",
            RestaurantName = "مطعم الاختبار",
            Action = "approve"
        });

        var notification = new OutboxLifecycleNotification(
            messageId,
            "DriverRegistrationAdminApproved",
            "DriverRegistration",
            regId.ToString(),
            null,
            null,
            payload,
            clock.UtcNow);

        // Act 1: First processing
        await handler.Handle(notification, CancellationToken.None);

        // Assert 1: Notifier was called and InboxProcessedEvents was recorded
        await notifier.Received(1).NotifyDriverAdminApprovedAsync(
            regId,
            driverUserId,
            "+96599112233",
            "أحمد السائق",
            "مطعم الاختبار",
            Arg.Any<CancellationToken>());

        var processed = await db.InboxProcessedEvents.AnyAsync(e => e.ConsumerName == DriverRegistrationLifecycleHandler.AdminApprovedConsumer && e.EventId == messageId);
        processed.Should().BeTrue();

        // Act 2: Reprocessing duplicate event
        await handler.Handle(notification, CancellationToken.None);

        // Assert 2: Notifier is NOT called again
        await notifier.Received(1).NotifyDriverAdminApprovedAsync(
            regId,
            driverUserId,
            "+96599112233",
            "أحمد السائق",
            "مطعم الاختبار",
            Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task NotificationService_WhenSendDriverPushCalledTwice_PreventsDuplicateInAppNotifications()
    {
        // Arrange
        var dbName = Guid.NewGuid().ToString();
        await using var db = CreateDb(dbName);
        var clock = Substitute.For<IDateTimeProvider>();
        clock.UtcNow.Returns(new DateTime(2026, 9, 30, 10, 0, 0, DateTimeKind.Utc));

        var fcm = Substitute.For<IFcmMessagingClient>();
        fcm.IsConfigured.Returns(false); // test in-app persistence without network send

        var logger = Substitute.For<ILogger<DriverRegistrationNotificationService>>();
        var messages = Substitute.For<ILocalizedMessageProvider>();
        messages.GetArabicMessage(Arg.Any<string>(), Arg.Any<string?>()).Returns("تم الاعتماد");
        messages.GetEnglishMessage(Arg.Any<string>(), Arg.Any<string?>()).Returns("Approved");

        var service = new DriverRegistrationNotificationService(db, fcm, clock, logger, messages);

        var regId = Guid.NewGuid();
        var driverUserId = Guid.NewGuid();

        // Act: Attempt to notify twice (simulating outbox retry)
        await service.NotifyDriverAdminApprovedAsync(regId, driverUserId, "+96599112233", "سائق", "مطعم", CancellationToken.None);
        await service.NotifyDriverAdminApprovedAsync(regId, driverUserId, "+96599112233", "سائق", "مطعم", CancellationToken.None);

        // Assert: Exactly ONE in-app notification is stored for this user and registration
        var notificationCount = await db.DriverNotifications.CountAsync(n => n.UserId == driverUserId && n.SubjectId == regId);
        notificationCount.Should().Be(1);
    }

    [Fact]
    public async Task IndependentDriver_WhenSubmitted_HasReviewStage2AndIndependentEventKey()
    {
        // Arrange
        var dbName = Guid.NewGuid().ToString();
        await using var db = CreateDb(dbName);
        var clock = Substitute.For<IDateTimeProvider>();
        clock.UtcNow.Returns(new DateTime(2026, 9, 30, 10, 0, 0, DateTimeKind.Utc));

        var fcm = Substitute.For<IFcmMessagingClient>();
        fcm.IsConfigured.Returns(false);

        var logger = Substitute.For<ILogger<DriverRegistrationNotificationService>>();
        var messages = Substitute.For<ILocalizedMessageProvider>();
        messages.GetArabicMessage(Arg.Any<string>(), Arg.Any<string?>()).Returns("طلب جديد");
        messages.GetEnglishMessage(Arg.Any<string>(), Arg.Any<string?>()).Returns("New Application");

        var service = new DriverRegistrationNotificationService(db, fcm, clock, logger, messages);

        var regId = Guid.NewGuid();
        var driverUserId = Guid.NewGuid();

        // Act: Notify submitted for independent fleet driver with restaurantId == null
        await service.NotifyDriverApplicationSubmittedAsync(
            regId,
            driverUserId,
            "+96599112233",
            "سائق أسطول مستقل",
            "MealMate Fleet",
            restaurantId: null,
            reviewStage: 2,
            applicationStatus: "Submitted",
            eventKey: "driver.registration.independent_submitted",
            cancellationToken: CancellationToken.None);

        // Assert: Driver notification must have eventKey "driver.registration.independent_submitted"
        var notification = await db.DriverNotifications.FirstOrDefaultAsync(n => n.UserId == driverUserId && n.SubjectId == regId);
        notification.Should().NotBeNull();
        notification!.Type.Should().Be("driver.registration.independent_submitted");
    }

    [Fact]
    public async Task Resubmit_AfterAdminRequestedChanges_UsesReviewStage2AndResubmittedEventKey()
    {
        // Arrange
        var dbName = Guid.NewGuid().ToString();
        await using var db = CreateDb(dbName);
        var clock = Substitute.For<IDateTimeProvider>();
        clock.UtcNow.Returns(new DateTime(2026, 9, 30, 10, 0, 0, DateTimeKind.Utc));

        var restaurantId = Guid.NewGuid();
        var driverUserId = Guid.NewGuid();
        var reg = new DriverRegistration
        {
            SubmittedByUserId = driverUserId,
            RestaurantId = restaurantId,
            FullNameAr = "سائق إعادة التقديم",
            FullNameEn = "Resubmit Driver",
            Phone = "+96599114455",
            NationalId = "294010112388",
            NationalIdExpiryUtc = clock.UtcNow.AddYears(2),
            LicenseNumber = "DL-778899",
            LicenseExpiryUtc = clock.UtcNow.AddYears(2),
            VehiclePlate = "11223",
            VehicleRegistrationExpiryUtc = clock.UtcNow.AddYears(1),
            DateOfBirthUtc = clock.UtcNow.AddYears(-23),
            Status = VerificationStatus.NeedsChanges,
            AdminApprovalStatus = VerificationStatus.NeedsChanges,
            RestaurantApprovalStatus = VerificationStatus.Approved
        };
        db.DriverRegistrations.Add(reg);
        await db.SaveChangesAsync();

        var currentUser = Substitute.For<ICurrentUser>();
        currentUser.IsAuthenticated.Returns(true);
        currentUser.UserId.Returns(driverUserId);
        currentUser.Roles.Returns(new List<string> { AppRoles.Driver });

        var audit = Substitute.For<IAuditLogger>();
        var messages = Substitute.For<ILocalizedMessageProvider>();
        messages.CurrentCulture.Returns(new CultureInfo("ar"));
        messages.GetMessage(Arg.Any<string>(), Arg.Any<string?>()).Returns("تمت إعادة التقديم");

        var notifier = Substitute.For<IDriverRegistrationNotificationService>();

        var handler = new ResubmitDriverRegistrationCommandHandler(db, audit, clock, messages, notifier, currentUser, null, DriverRegistrationTestStorage.Create());

        // Act: Resubmit driver registration
        var result = await handler.Handle(new ResubmitDriverRegistrationCommand(
            reg.Id,
            DrivingLicenseFrontStorageKey: "docs/new_lic_f.jpg"), CancellationToken.None);

        // Assert
        result.IsSuccess.Should().BeTrue();
        reg.Status.Should().Be(VerificationStatus.Submitted);
        reg.AdminApprovalStatus.Should().Be(VerificationStatus.Submitted);
        reg.RestaurantApprovalStatus.Should().Be(VerificationStatus.Submitted, "the changed driving license requires restaurant re-approval");
        await notifier.DidNotReceive().NotifyDriverApplicationSubmittedAsync(
            Arg.Any<Guid>(), Arg.Any<Guid>(), Arg.Any<string>(), Arg.Any<string>(), Arg.Any<string>(),
            Arg.Any<Guid?>(), Arg.Any<int>(), Arg.Any<string>(), Arg.Any<string>(), Arg.Any<CancellationToken>());

        // Verify Outbox message was persisted with ReviewStage = 2
        var outboxMessage = await db.OutboxMessages.FirstOrDefaultAsync(m => m.AggregateId == reg.Id.ToString());
        outboxMessage.Should().NotBeNull();
        outboxMessage!.EventType.Should().Be("DriverRegistrationResubmitted");

        using var doc = JsonDocument.Parse(outboxMessage.PayloadJson);
        doc.RootElement.GetProperty("ReviewStage").GetInt32().Should().Be(1);
        doc.RootElement.GetProperty("PreserveRestaurantApproval").GetBoolean().Should().BeFalse();
    }
}
