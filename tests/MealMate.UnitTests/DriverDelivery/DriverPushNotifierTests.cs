using FluentAssertions;
using MealMate.Application.Common.Abstractions;
using MealMate.Domain.Common;
using MealMate.Domain.Enums;
using MealMate.Domain.IdentityAccess;
using MealMate.Infrastructure.Push;
using MealMate.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using NSubstitute;
using Xunit;

namespace MealMate.UnitTests.DriverDelivery;

public class DriverPushNotifierTests
{
    private static AppDbContext CreateDb()
    {
        var options = new DbContextOptionsBuilder<AppDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString())
            .Options;
        return new AppDbContext(options, Substitute.For<IDomainEventDispatcher>());
    }

    [Fact]
    public async Task NotifyBoxAssignedAsync_QueriesDriverDeviceTokens_AndSendsFcmMessage()
    {
        // Arrange
        await using var db = CreateDb();
        var fcm = Substitute.For<IFcmMessagingClient>();
        fcm.IsConfigured.Returns(true);
        fcm.SendAsync(Arg.Any<string>(), Arg.Any<string>(), Arg.Any<string>(), Arg.Any<IReadOnlyDictionary<string, string>>(), Arg.Any<CancellationToken>())
            .Returns(FcmSendResult.Success());

        var logger = Substitute.For<ILogger<FirebaseDriverPushNotifier>>();
        var notifier = new FirebaseDriverPushNotifier(fcm, db, logger);

        var driverUserId = Guid.NewGuid();
        db.DriverDeviceTokens.Add(new DriverDeviceToken
        {
            UserId = driverUserId,
            Token = "fcm_test_token_123",
            Platform = MobilePlatform.Android,
            IsActive = true,
            LastSeenAtUtc = DateTime.UtcNow
        });
        await db.SaveChangesAsync();

        // Act
        await notifier.NotifyBoxAssignedAsync(driverUserId, "#BX-1234", "السالمية", CancellationToken.None);

        // Assert
        await fcm.Received(1).SendAsync(
            "fcm_test_token_123",
            Arg.Is<string>(t => t.Contains("تم إسناد طلب جديد")),
            Arg.Is<string>(b => b.Contains("#BX-1234") && b.Contains("السالمية")),
            Arg.Is<IReadOnlyDictionary<string, string>>(d => d["type"] == "box-assigned" && d["boxCode"] == "#BX-1234"),
            Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task NotifyTripAssignedAsync_SendsFcmNotificationWithTripCode()
    {
        // Arrange
        await using var db = CreateDb();
        var fcm = Substitute.For<IFcmMessagingClient>();
        fcm.IsConfigured.Returns(true);
        fcm.SendAsync(Arg.Any<string>(), Arg.Any<string>(), Arg.Any<string>(), Arg.Any<IReadOnlyDictionary<string, string>>(), Arg.Any<CancellationToken>())
            .Returns(FcmSendResult.Success());

        var logger = Substitute.For<ILogger<FirebaseDriverPushNotifier>>();
        var notifier = new FirebaseDriverPushNotifier(fcm, db, logger);

        var driverUserId = Guid.NewGuid();
        db.DriverDeviceTokens.Add(new DriverDeviceToken
        {
            UserId = driverUserId,
            Token = "fcm_trip_token_456",
            Platform = MobilePlatform.Android,
            IsActive = true,
            LastSeenAtUtc = DateTime.UtcNow
        });
        await db.SaveChangesAsync();

        // Act
        await notifier.NotifyTripAssignedAsync(driverUserId, "TRP-105", 8, CancellationToken.None);

        // Assert
        await fcm.Received(1).SendAsync(
            "fcm_trip_token_456",
            Arg.Is<string>(t => t.Contains("تم تعيين رحلة جديدة")),
            Arg.Is<string>(b => b.Contains("TRP-105") && b.Contains("8")),
            Arg.Is<IReadOnlyDictionary<string, string>>(d => d["type"] == "trip-assigned" && d["tripCode"] == "TRP-105"),
            Arg.Any<CancellationToken>());
    }
}
