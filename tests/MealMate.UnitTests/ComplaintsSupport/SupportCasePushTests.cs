using System.Text.Json;
using FluentAssertions;
using MealMate.Application.Common.Abstractions;
using MealMate.Application.Common.Outbox;
using MealMate.Domain.Common;
using MealMate.Domain.ComplaintsSupport;
using MealMate.Domain.DriverDelivery;
using MealMate.Domain.Enums;
using MealMate.Domain.IdentityAccess;
using MealMate.Domain.DriverDelivery;
using MealMate.Infrastructure.Push;
using MealMate.Persistence;
using MealMate.Persistence.Identity;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using NSubstitute;

namespace MealMate.UnitTests.ComplaintsSupport;

public sealed class SupportCasePushTests
{
    private const string EventType = "unified-support-case-push.v1";

    private static AppDbContext CreateDb()
    {
        var options = new DbContextOptionsBuilder<AppDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString())
            .Options;
        return new AppDbContext(options, Substitute.For<IDomainEventDispatcher>());
    }

    private static (SupportCase Case, SupportCaseEvent Event, ApplicationUser User) SeedCustomerCase(AppDbContext db)
    {
        var now = DateTime.UtcNow;
        var user = new ApplicationUser { Id = Guid.NewGuid(), UserName = "customer", UserType = UserType.Customer,
            AccountStatus = AccountStatus.Active };
        var supportCase = SupportCase.Create(SupportCaseChannel.Customer, SupportCaseKind.GeneralRequest,
            SupportCaseOwnerTeam.PlatformSupport, "general", "Private subject", "Private case body", user.Id, now);
        var supportEvent = new SupportCaseEvent { Id = Guid.NewGuid(), SupportCaseId = supportCase.Id,
            ActorUserId = Guid.NewGuid(), EventType = "MessageAdded", CreatedAtUtc = now };
        db.Users.Add(user);
        db.SupportCases.Add(supportCase);
        db.SupportCaseEvents.Add(supportEvent);
        db.CustomerProfiles.Add(new CustomerProfile { UserId = user.Id, PreferredLanguage = "en" });
        return (supportCase, supportEvent, user);
    }

    private static OutboxLifecycleNotification CreateOutboxNotification(Guid messageId, Guid notificationId, Guid caseEventId, Guid userId, string audience)
    {
        var payload = JsonSerializer.Serialize(new
        {
            notificationId,
            supportEventId = caseEventId,
            recipientUserId = userId,
            audience,
        });
        return new OutboxLifecycleNotification(messageId, EventType, "SupportCase", caseEventId.ToString("D"), null,
            $"unified-support-case-push:{notificationId:N}", payload, DateTime.UtcNow);
    }

    [Fact]
    public async Task Handler_SendsGenericEnglishCustomerPushOncePerDistinctActiveTokenWithStableIds()
    {
        await using var db = CreateDb();
        var (supportCase, supportEvent, user) = SeedCustomerCase(db);
        var notificationId = Guid.NewGuid();
        db.CustomerNotifications.Add(new CustomerNotification
        {
            Id = notificationId, UserId = user.Id, EventKey = "support.case.updated", SubjectId = supportEvent.Id,
            TitleAr = "تحديث على حالة الدعم", TitleEn = "Support case updated",
            BodyAr = "هناك تحديث جديد على حالة الدعم التي تتابعها.",
            BodyEn = "There is a new update to your support case.",
            DeepLink = $"/support/cases/{supportCase.Id:D}", CreatedAtUtc = DateTime.UtcNow,
        });
        db.CustomerDeviceTokens.AddRange(
            new CustomerDeviceToken { UserId = user.Id, Token = "device-token", Platform = MobilePlatform.Android,
                IsActive = true, LastSeenAtUtc = DateTime.UtcNow },
            new CustomerDeviceToken { UserId = user.Id, Token = "device-token", Platform = MobilePlatform.Ios,
                IsActive = true, LastSeenAtUtc = DateTime.UtcNow });
        await db.SaveChangesAsync();
        var fcm = Substitute.For<IFcmMessagingClient>();
        fcm.IsConfigured.Returns(true);
        fcm.SendAsync(Arg.Any<string>(), Arg.Any<string>(), Arg.Any<string>(), Arg.Any<IReadOnlyDictionary<string, string>>(),
            Arg.Any<CancellationToken>()).Returns(FcmSendResult.Success());
        var handler = new SupportCasePushHandler(db, fcm, NullLogger<SupportCasePushHandler>.Instance);

        var outboxMessageId = Guid.NewGuid();
        await handler.Handle(CreateOutboxNotification(outboxMessageId, notificationId, supportEvent.Id, user.Id, "customer"), CancellationToken.None);

        await fcm.Received(1).SendAsync("device-token", "Support case updated",
            "There is a new update to your support case.", Arg.Is<IReadOnlyDictionary<string, string>>(data =>
                data["eventId"] == outboxMessageId.ToString("D")
                && data["notificationId"] == notificationId.ToString("D")
                && data["caseId"] == supportCase.Id.ToString("D")
                && data["deepLink"] == $"/support/cases/{supportCase.Id:D}"
                && data["bodyEn"].Contains("support case", StringComparison.OrdinalIgnoreCase)), Arg.Any<CancellationToken>());
        fcm.ReceivedCalls().Select(call => call.GetArguments()).Should().NotContain(arguments =>
            arguments.OfType<string>().Any(value => value.Contains("Private", StringComparison.Ordinal)));
    }

    [Fact]
    public async Task Handler_DeactivatesInvalidTokenAndLeavesTransientFailureRetryable()
    {
        await using var db = CreateDb();
        var (supportCase, supportEvent, user) = SeedCustomerCase(db);
        var notificationId = Guid.NewGuid();
        db.CustomerNotifications.Add(new CustomerNotification
        {
            Id = notificationId, UserId = user.Id, EventKey = "support.case.updated", SubjectId = supportEvent.Id,
            TitleAr = "عنوان", TitleEn = "Title", BodyAr = "نص عام", BodyEn = "Generic body",
            DeepLink = $"/support/cases/{supportCase.Id:D}", CreatedAtUtc = DateTime.UtcNow,
        });
        var invalidToken = new CustomerDeviceToken { UserId = user.Id, Token = "invalid-token",
            Platform = MobilePlatform.Android, IsActive = true, LastSeenAtUtc = DateTime.UtcNow };
        db.CustomerDeviceTokens.Add(invalidToken);
        await db.SaveChangesAsync();
        var fcm = Substitute.For<IFcmMessagingClient>();
        fcm.IsConfigured.Returns(true);
        fcm.SendAsync(Arg.Any<string>(), Arg.Any<string>(), Arg.Any<string>(), Arg.Any<IReadOnlyDictionary<string, string>>(),
            Arg.Any<CancellationToken>()).Returns(FcmSendResult.InvalidToken());
        var handler = new SupportCasePushHandler(db, fcm, NullLogger<SupportCasePushHandler>.Instance);

        await handler.Handle(CreateOutboxNotification(Guid.NewGuid(), notificationId, supportEvent.Id, user.Id, "customer"), CancellationToken.None);

        invalidToken.IsActive.Should().BeFalse();

        var failedFcm = Substitute.For<IFcmMessagingClient>();
        failedFcm.IsConfigured.Returns(true);
        failedFcm.SendAsync(Arg.Any<string>(), Arg.Any<string>(), Arg.Any<string>(), Arg.Any<IReadOnlyDictionary<string, string>>(),
            Arg.Any<CancellationToken>()).Returns(FcmSendResult.Failed("provider unavailable"));
        invalidToken.IsActive = true;
        await db.SaveChangesAsync();
        var retryHandler = new SupportCasePushHandler(db, failedFcm, NullLogger<SupportCasePushHandler>.Instance);
        var act = () => retryHandler.Handle(CreateOutboxNotification(Guid.NewGuid(), notificationId, supportEvent.Id, user.Id, "customer"), CancellationToken.None);

        await act.Should().ThrowAsync<InvalidOperationException>();
        invalidToken.IsActive.Should().BeTrue();
    }

    [Fact]
    public async Task Handler_DropsPushWhenTheCustomerAccountIsNoLongerActive()
    {
        await using var db = CreateDb();
        var (supportCase, supportEvent, user) = SeedCustomerCase(db);
        user.AccountStatus = AccountStatus.Suspended;
        var notificationId = Guid.NewGuid();
        db.CustomerNotifications.Add(new CustomerNotification
        {
            Id = notificationId, UserId = user.Id, EventKey = "support.case.updated", SubjectId = supportEvent.Id,
            TitleAr = "عنوان", TitleEn = "Title", BodyAr = "نص عام", BodyEn = "Generic body",
            DeepLink = $"/support/cases/{supportCase.Id:D}", CreatedAtUtc = DateTime.UtcNow,
        });
        db.CustomerDeviceTokens.Add(new CustomerDeviceToken { UserId = user.Id, Token = "active-token",
            Platform = MobilePlatform.Android, IsActive = true, LastSeenAtUtc = DateTime.UtcNow });
        await db.SaveChangesAsync();
        var fcm = Substitute.For<IFcmMessagingClient>();
        fcm.IsConfigured.Returns(true);
        var handler = new SupportCasePushHandler(db, fcm, NullLogger<SupportCasePushHandler>.Instance);

        await handler.Handle(CreateOutboxNotification(Guid.NewGuid(), notificationId, supportEvent.Id, user.Id, "customer"), CancellationToken.None);

        await fcm.DidNotReceive().SendAsync(Arg.Any<string>(), Arg.Any<string>(), Arg.Any<string>(),
            Arg.Any<IReadOnlyDictionary<string, string>>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task Handler_DoesNotCallProviderWhenNoActiveRecipientTokensExist()
    {
        await using var db = CreateDb();
        var (supportCase, supportEvent, user) = SeedCustomerCase(db);
        var notificationId = Guid.NewGuid();
        db.CustomerNotifications.Add(new CustomerNotification
        {
            Id = notificationId, UserId = user.Id, EventKey = "support.case.updated", SubjectId = supportEvent.Id,
            TitleAr = "عنوان", TitleEn = "Title", BodyAr = "نص عام", BodyEn = "Generic body",
            DeepLink = $"/support/cases/{supportCase.Id:D}", CreatedAtUtc = DateTime.UtcNow,
        });
        db.CustomerDeviceTokens.Add(new CustomerDeviceToken { UserId = user.Id, Token = " ",
            Platform = MobilePlatform.Android, IsActive = true, LastSeenAtUtc = DateTime.UtcNow });
        await db.SaveChangesAsync();
        var fcm = Substitute.For<IFcmMessagingClient>();
        var handler = new SupportCasePushHandler(db, fcm, NullLogger<SupportCasePushHandler>.Instance);

        await handler.Handle(CreateOutboxNotification(Guid.NewGuid(), notificationId, supportEvent.Id, user.Id, "customer"), CancellationToken.None);

        await fcm.DidNotReceive().SendAsync(Arg.Any<string>(), Arg.Any<string>(), Arg.Any<string>(),
            Arg.Any<IReadOnlyDictionary<string, string>>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task Handler_RechecksDriverProfileAndSendsOnlyToTokensForTheCurrentProfile()
    {
        await using var db = CreateDb();
        var now = DateTime.UtcNow;
        var user = new ApplicationUser { Id = Guid.NewGuid(), UserName = "driver", UserType = UserType.Driver,
            AccountStatus = AccountStatus.Active };
        var supportCase = SupportCase.Create(SupportCaseChannel.Driver, SupportCaseKind.GeneralRequest,
            SupportCaseOwnerTeam.PlatformSupport, "general", "Private subject", "Private case body", user.Id, now);
        var supportEvent = new SupportCaseEvent { Id = Guid.NewGuid(), SupportCaseId = supportCase.Id,
            ActorUserId = Guid.NewGuid(), EventType = "MessageAdded", CreatedAtUtc = now };
        var profile = new DriverProfile { UserId = user.Id, FullNameAr = "سائق", FullNameEn = "Driver" };
        var notificationId = Guid.NewGuid();
        db.Users.Add(user);
        db.SupportCases.Add(supportCase);
        db.SupportCaseEvents.Add(supportEvent);
        db.DriverProfiles.Add(profile);
        db.DriverAppSettings.Add(new DriverAppSettings { DriverProfileId = profile.Id, PreferredLanguage = "en" });
        db.DriverNotifications.Add(new DriverNotification
        {
            Id = notificationId, UserId = user.Id, DriverProfileId = profile.Id, Type = "support-case-updated",
            TitleAr = "تحديث على حالة الدعم", TitleEn = "Support case updated", BodyAr = "هناك تحديث جديد على حالة الدعم التي تتابعها.",
            BodyEn = "There is a new update to your support case.", Section = "support", SubjectId = supportEvent.Id,
            DeepLink = $"/support/cases/{supportCase.Id:D}", CreatedAtUtc = now,
        });
        db.DriverDeviceTokens.AddRange(
            new DriverDeviceToken { UserId = user.Id, DriverProfileId = profile.Id, Token = "current-driver-token",
                Platform = MobilePlatform.Android, IsActive = true, LastSeenAtUtc = now },
            new DriverDeviceToken { UserId = user.Id, DriverProfileId = Guid.NewGuid(), Token = "stale-driver-token",
                Platform = MobilePlatform.Android, IsActive = true, LastSeenAtUtc = now },
            new DriverDeviceToken { UserId = user.Id, DriverProfileId = profile.Id, Token = "disabled-driver-token",
                Platform = MobilePlatform.Android, IsActive = false, LastSeenAtUtc = now },
            new DriverDeviceToken { UserId = Guid.NewGuid(), DriverProfileId = profile.Id, Token = "nonowned-driver-token",
                Platform = MobilePlatform.Android, IsActive = true, LastSeenAtUtc = now });
        await db.SaveChangesAsync();

        var fcm = Substitute.For<IFcmMessagingClient>();
        fcm.IsConfigured.Returns(true);
        fcm.SendAsync(Arg.Any<string>(), Arg.Any<string>(), Arg.Any<string>(), Arg.Any<IReadOnlyDictionary<string, string>>(),
            Arg.Any<CancellationToken>()).Returns(FcmSendResult.Success());
        var handler = new SupportCasePushHandler(db, fcm, NullLogger<SupportCasePushHandler>.Instance);

        await handler.Handle(CreateOutboxNotification(Guid.NewGuid(), notificationId, supportEvent.Id, user.Id, "driver"), CancellationToken.None);

        await fcm.Received(1).SendAsync("current-driver-token", "Support case updated",
            "There is a new update to your support case.", Arg.Is<IReadOnlyDictionary<string, string>>(data =>
                data["caseId"] == supportCase.Id.ToString("D") && data["notificationId"] == notificationId.ToString("D")),
            Arg.Any<CancellationToken>());
        foreach (var token in new[] { "stale-driver-token", "disabled-driver-token", "nonowned-driver-token" })
            await fcm.DidNotReceive().SendAsync(token, Arg.Any<string>(), Arg.Any<string>(),
                Arg.Any<IReadOnlyDictionary<string, string>>(), Arg.Any<CancellationToken>());

        profile.IsDeleted = true;
        await db.SaveChangesAsync();
        var staleProfileHandler = new SupportCasePushHandler(db, fcm, NullLogger<SupportCasePushHandler>.Instance);
        await staleProfileHandler.Handle(CreateOutboxNotification(Guid.NewGuid(), notificationId, supportEvent.Id, user.Id, "driver"), CancellationToken.None);
        await fcm.Received(1).SendAsync("current-driver-token", "Support case updated", Arg.Any<string>(),
            Arg.Any<IReadOnlyDictionary<string, string>>(), Arg.Any<CancellationToken>());
    }
}
