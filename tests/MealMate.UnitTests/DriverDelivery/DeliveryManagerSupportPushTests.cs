using System.Text.Json;
using FluentAssertions;
using MealMate.Application.Common.Abstractions;
using MealMate.Application.Common.Outbox;
using MealMate.Application.Features.DriverDelivery.SupervisorNotifications;
using MealMate.Domain.Enums;
using MealMate.Domain.IdentityAccess;
using MealMate.Infrastructure.Push;
using MealMate.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using NSubstitute;

namespace MealMate.UnitTests.DriverDelivery;

public sealed class DeliveryManagerSupportPushTests : IDisposable
{
    private readonly AppDbContext db = new(new DbContextOptionsBuilder<AppDbContext>()
        .UseInMemoryDatabase(Guid.NewGuid().ToString()).Options, Substitute.For<IDomainEventDispatcher>());
    private readonly IFcmMessagingClient fcm = Substitute.For<IFcmMessagingClient>();

    private async Task<DeliveryManagerNotification> SeedAsync()
    {
        var notification = new DeliveryManagerNotification { UserId = Guid.NewGuid(), RestaurantId = Guid.NewGuid(),
            Type = "driver-support-issue", Section = "support", SubjectId = Guid.NewGuid(),
            TitleAr = "بلاغ السائق", TitleEn = "Driver issue", BodyAr = "مشكلة الطريق", BodyEn = "Road issue",
            DeepLink = "/dispatcher/support/issues/issue", SentAtUtc = DateTime.UtcNow };
        db.DeliveryManagerRegistrations.Add(new DeliveryManagerRegistration { ResultingUserId = notification.UserId,
            RestaurantId = notification.RestaurantId.Value, Status = VerificationStatus.Approved,
            FullName = "Manager", Phone = "123", Email = "manager@test.invalid", NationalId = "123" });
        DeliveryManagerSupportPushOutbox.Add(db, notification);
        await db.SaveChangesAsync();
        fcm.IsConfigured.Returns(true);
        fcm.SendAsync(Arg.Any<string>(), Arg.Any<string>(), Arg.Any<string>(), Arg.Any<IReadOnlyDictionary<string, string>>(), Arg.Any<CancellationToken>())
            .Returns(FcmSendResult.Success());
        return notification;
    }

    private DeliveryManagerSupportPushHandler Handler() => new(db, fcm, NullLogger<DeliveryManagerSupportPushHandler>.Instance);
    private async Task<OutboxLifecycleNotification> EventAsync()
    {
        var item = await db.OutboxMessages.SingleAsync();
        return new(item.Id, item.EventType, item.AggregateType, item.AggregateId, item.CorrelationId,
            item.DeduplicationKey, item.PayloadJson, item.CreatedAtUtc);
    }

    [Fact]
    public async Task EnqueuesExactPersistedNotificationAlongsideInbox()
    {
        var item = await SeedAsync();
        var message = await EventAsync();
        message.EventType.Should().Be(DeliveryManagerSupportPushOutbox.EventType);
        JsonDocument.Parse(message.PayloadJson).RootElement.GetProperty("NotificationId").GetGuid().Should().Be(item.Id);
        (await db.DeliveryManagerNotifications.SingleAsync()).Id.Should().Be(item.Id);
    }

    [Fact]
    public async Task SendsPersistedContentOnlyToActiveDevicesOfApprovedManagerInRestaurant()
    {
        var item = await SeedAsync();
        db.DeliveryManagerDeviceTokens.AddRange(
            new() { UserId = item.UserId, RestaurantId = item.RestaurantId, Token = "correct" },
            new() { UserId = item.UserId, RestaurantId = null, Token = "account-device" },
            new() { UserId = item.UserId, RestaurantId = Guid.NewGuid(), Token = "other-restaurant" },
            new() { UserId = Guid.NewGuid(), RestaurantId = item.RestaurantId, Token = "other-user" },
            new() { UserId = item.UserId, RestaurantId = item.RestaurantId, Token = "inactive", IsActive = false });
        await db.SaveChangesAsync();
        await Handler().Handle(await EventAsync(), default);
        await fcm.Received(2).SendAsync(Arg.Any<string>(), item.TitleAr, item.BodyAr,
            Arg.Is<IReadOnlyDictionary<string, string>>(d => d["notificationId"] == item.Id.ToString("D") && d["deepLink"] == item.DeepLink), Arg.Any<CancellationToken>());
        await fcm.DidNotReceive().SendAsync("other-restaurant", Arg.Any<string>(), Arg.Any<string>(), Arg.Any<IReadOnlyDictionary<string, string>>(), Arg.Any<CancellationToken>());
    }

    [Theory]
    [InlineData(VerificationStatus.Rejected, false)]
    [InlineData(VerificationStatus.Approved, true)]
    public async Task RevokedOrDeletedRegistrationDoesNotReceivePush(VerificationStatus status, bool deleted)
    {
        var item = await SeedAsync();
        var registration = await db.DeliveryManagerRegistrations.SingleAsync();
        registration.Status = status;
        registration.IsDeleted = deleted;
        db.DeliveryManagerDeviceTokens.Add(new() { UserId = item.UserId, Token = "device" });
        await db.SaveChangesAsync();
        await Handler().Handle(await EventAsync(), default);
        await fcm.DidNotReceiveWithAnyArgs().SendAsync(default!, default!, default!, default!, default);
    }

    [Fact]
    public async Task UnconfiguredProviderAndTransientFailureRemainRetryable()
    {
        var item = await SeedAsync();
        db.DeliveryManagerDeviceTokens.Add(new() { UserId = item.UserId, Token = "device" });
        await db.SaveChangesAsync();
        var message = await EventAsync();
        fcm.IsConfigured.Returns(false);
        await FluentActions.Awaiting(() => Handler().Handle(message, default)).Should().ThrowAsync<InvalidOperationException>();
        fcm.IsConfigured.Returns(true);
        fcm.SendAsync(Arg.Any<string>(), Arg.Any<string>(), Arg.Any<string>(), Arg.Any<IReadOnlyDictionary<string, string>>(), Arg.Any<CancellationToken>())
            .Returns(FcmSendResult.Failed("temporarily unavailable"));
        await FluentActions.Awaiting(() => Handler().Handle(message, default)).Should().ThrowAsync<InvalidOperationException>();
        (await db.DeliveryManagerDeviceTokens.SingleAsync()).IsActive.Should().BeTrue();
    }

    [Fact]
    public async Task InvalidTokenIsDeactivatedAndNotRetried()
    {
        var item = await SeedAsync();
        db.DeliveryManagerDeviceTokens.Add(new() { UserId = item.UserId, Token = "expired-device" });
        await db.SaveChangesAsync();
        fcm.SendAsync(Arg.Any<string>(), Arg.Any<string>(), Arg.Any<string>(), Arg.Any<IReadOnlyDictionary<string, string>>(), Arg.Any<CancellationToken>())
            .Returns(FcmSendResult.InvalidToken());
        var message = await EventAsync();
        await Handler().Handle(message, default);
        (await db.DeliveryManagerDeviceTokens.SingleAsync()).IsActive.Should().BeFalse();
        await Handler().Handle(message, default);
        await fcm.Received(1).SendAsync(Arg.Any<string>(), Arg.Any<string>(), Arg.Any<string>(), Arg.Any<IReadOnlyDictionary<string, string>>(), Arg.Any<CancellationToken>());
    }

    public void Dispose() => db.Dispose();
}
