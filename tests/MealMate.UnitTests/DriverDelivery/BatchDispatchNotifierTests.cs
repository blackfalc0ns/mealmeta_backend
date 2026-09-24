using FluentAssertions;
using MealMate.Application.Common.Abstractions;
using MealMate.Domain.CalendarMeals;
using MealMate.Domain.Common;
using MealMate.Domain.Enums;
using MealMate.Domain.IdentityAccess;
using MealMate.Domain.RestaurantOperations;
using MealMate.Infrastructure.Push;
using MealMate.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using NSubstitute;
using Xunit;

namespace MealMate.UnitTests.DriverDelivery;

public class BatchDispatchNotifierTests
{
    private static AppDbContext CreateDb()
    {
        var options = new DbContextOptionsBuilder<AppDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString())
            .Options;
        return new AppDbContext(options, Substitute.For<IDomainEventDispatcher>());
    }

    [Fact]
    public async Task WhenUnassignedReadyOrdersLessThan5_DoesNotSendPushNotification()
    {
        // Arrange
        await using var db = CreateDb();
        var fcm = Substitute.For<IFcmMessagingClient>();
        fcm.IsConfigured.Returns(true);
        var clock = Substitute.For<IDateTimeProvider>();
        clock.UtcNow.Returns(new DateTime(2026, 9, 24, 12, 0, 0, DateTimeKind.Utc));
        var logger = Substitute.For<ILogger<BatchDispatchNotifier>>();

        var notifier = new BatchDispatchNotifier(db, fcm, clock, logger);

        var restaurantId = Guid.NewGuid();
        db.RestaurantDeviceTokens.Add(new RestaurantDeviceToken
        {
            RestaurantId = restaurantId,
            Token = "token_disp_1",
            IsActive = true
        });

        // Add 3 ready orders
        for (var i = 0; i < 3; i++)
        {
            var dayId = Guid.NewGuid();
            db.RestaurantDailyOrders.Add(new RestaurantDailyOrder
            {
                RestaurantId = restaurantId,
                SubscriptionDayId = dayId,
                Status = OrderStatus.ReadyForPickup,
                BoxCount = 1
            });
        }
        await db.SaveChangesAsync();

        // Act
        await notifier.NotifyIfBatchThresholdReachedAsync(restaurantId, Guid.NewGuid(), CancellationToken.None);

        // Assert
        await fcm.DidNotReceiveWithAnyArgs().SendAsync(
            Arg.Any<string>(), Arg.Any<string>(), Arg.Any<string>(), Arg.Any<IReadOnlyDictionary<string, string>>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task WhenUnassignedReadyOrdersReachThreshold_SendsFcmPushToRestaurantDeviceTokens()
    {
        // Arrange
        await using var db = CreateDb();
        var fcm = Substitute.For<IFcmMessagingClient>();
        fcm.IsConfigured.Returns(true);
        fcm.SendAsync(Arg.Any<string>(), Arg.Any<string>(), Arg.Any<string>(), Arg.Any<IReadOnlyDictionary<string, string>>(), Arg.Any<CancellationToken>())
            .Returns(FcmSendResult.Success());

        var clock = Substitute.For<IDateTimeProvider>();
        clock.UtcNow.Returns(new DateTime(2026, 9, 24, 12, 0, 0, DateTimeKind.Utc));
        var logger = Substitute.For<ILogger<BatchDispatchNotifier>>();

        var notifier = new BatchDispatchNotifier(db, fcm, clock, logger);

        var restaurantId = Guid.NewGuid();
        db.RestaurantDeviceTokens.Add(new RestaurantDeviceToken
        {
            RestaurantId = restaurantId,
            Token = "token_disp_target",
            IsActive = true
        });

        // Add 5 ready orders
        var lastDayId = Guid.Empty;
        for (var i = 0; i < 5; i++)
        {
            lastDayId = Guid.NewGuid();
            db.RestaurantDailyOrders.Add(new RestaurantDailyOrder
            {
                RestaurantId = restaurantId,
                SubscriptionDayId = lastDayId,
                Status = OrderStatus.ReadyForPickup,
                BoxCount = 1
            });
        }
        await db.SaveChangesAsync();

        // Act
        await notifier.NotifyIfBatchThresholdReachedAsync(restaurantId, lastDayId, CancellationToken.None);

        // Assert
        await fcm.Received(1).SendAsync(
            "token_disp_target",
            Arg.Is<string>(t => t.Contains("دفعة طلبات جاهزة")),
            Arg.Is<string>(b => b.Contains("5")),
            Arg.Is<IReadOnlyDictionary<string, string>>(d => d["eventKey"] == "dispatcher.batch.ready" && d["readyCount"] == "5"),
            Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task WhenSubsequentOrderArrivesWithinCooldownOrBeforeNextMultiple_SuppressesPushNotification()
    {
        // Arrange
        await using var db = CreateDb();
        var fcm = Substitute.For<IFcmMessagingClient>();
        fcm.IsConfigured.Returns(true);
        fcm.SendAsync(Arg.Any<string>(), Arg.Any<string>(), Arg.Any<string>(), Arg.Any<IReadOnlyDictionary<string, string>>(), Arg.Any<CancellationToken>())
            .Returns(FcmSendResult.Success());

        var initialTime = new DateTime(2026, 9, 24, 12, 0, 0, DateTimeKind.Utc);
        var clock = Substitute.For<IDateTimeProvider>();
        clock.UtcNow.Returns(initialTime);
        var logger = Substitute.For<ILogger<BatchDispatchNotifier>>();

        var notifier = new BatchDispatchNotifier(db, fcm, clock, logger);

        var restaurantId = Guid.NewGuid();
        db.RestaurantDeviceTokens.Add(new RestaurantDeviceToken
        {
            RestaurantId = restaurantId,
            Token = "token_disp_target",
            IsActive = true
        });

        // 5 ready orders
        for (var i = 0; i < 5; i++)
        {
            db.RestaurantDailyOrders.Add(new RestaurantDailyOrder
            {
                RestaurantId = restaurantId,
                SubscriptionDayId = Guid.NewGuid(),
                Status = OrderStatus.ReadyForPickup,
                BoxCount = 1
            });
        }
        await db.SaveChangesAsync();

        // Trigger 1st batch (count = 5)
        await notifier.NotifyIfBatchThresholdReachedAsync(restaurantId, Guid.NewGuid(), CancellationToken.None);
        fcm.ClearReceivedCalls();

        // 6th order arrives 30 seconds later (cooldown is 3 minutes, and 6 is not next threshold 10)
        clock.UtcNow.Returns(initialTime.AddSeconds(30));
        db.RestaurantDailyOrders.Add(new RestaurantDailyOrder
        {
            RestaurantId = restaurantId,
            SubscriptionDayId = Guid.NewGuid(),
            Status = OrderStatus.ReadyForPickup,
            BoxCount = 1
        });
        await db.SaveChangesAsync();

        // Act
        await notifier.NotifyIfBatchThresholdReachedAsync(restaurantId, Guid.NewGuid(), CancellationToken.None);

        // Assert: FCM is NOT called again
        await fcm.DidNotReceiveWithAnyArgs().SendAsync(
            Arg.Any<string>(), Arg.Any<string>(), Arg.Any<string>(), Arg.Any<IReadOnlyDictionary<string, string>>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task WhenCountReaches10AndCooldownElapsed_SendsSecondBatchNotification()
    {
        // Arrange
        await using var db = CreateDb();
        var fcm = Substitute.For<IFcmMessagingClient>();
        fcm.IsConfigured.Returns(true);
        fcm.SendAsync(Arg.Any<string>(), Arg.Any<string>(), Arg.Any<string>(), Arg.Any<IReadOnlyDictionary<string, string>>(), Arg.Any<CancellationToken>())
            .Returns(FcmSendResult.Success());

        var initialTime = new DateTime(2026, 9, 24, 12, 0, 0, DateTimeKind.Utc);
        var clock = Substitute.For<IDateTimeProvider>();
        clock.UtcNow.Returns(initialTime);
        var logger = Substitute.For<ILogger<BatchDispatchNotifier>>();

        var notifier = new BatchDispatchNotifier(db, fcm, clock, logger);

        var restaurantId = Guid.NewGuid();
        db.RestaurantDeviceTokens.Add(new RestaurantDeviceToken
        {
            RestaurantId = restaurantId,
            Token = "token_disp_target",
            IsActive = true
        });

        // 5 ready orders
        for (var i = 0; i < 5; i++)
        {
            db.RestaurantDailyOrders.Add(new RestaurantDailyOrder
            {
                RestaurantId = restaurantId,
                SubscriptionDayId = Guid.NewGuid(),
                Status = OrderStatus.ReadyForPickup,
                BoxCount = 1
            });
        }
        await db.SaveChangesAsync();

        // 1st batch notification
        await notifier.NotifyIfBatchThresholdReachedAsync(restaurantId, Guid.NewGuid(), CancellationToken.None);
        fcm.ClearReceivedCalls();

        // 5 more orders ready, 4 minutes later (cooldown elapsed)
        clock.UtcNow.Returns(initialTime.AddMinutes(4));
        for (var i = 0; i < 5; i++)
        {
            db.RestaurantDailyOrders.Add(new RestaurantDailyOrder
            {
                RestaurantId = restaurantId,
                SubscriptionDayId = Guid.NewGuid(),
                Status = OrderStatus.ReadyForPickup,
                BoxCount = 1
            });
        }
        await db.SaveChangesAsync();

        // Act
        await notifier.NotifyIfBatchThresholdReachedAsync(restaurantId, Guid.NewGuid(), CancellationToken.None);

        // Assert: 2nd batch notification sent with count 10
        await fcm.Received(1).SendAsync(
            "token_disp_target",
            Arg.Is<string>(t => t.Contains("دفعة طلبات جاهزة")),
            Arg.Is<string>(b => b.Contains("10")),
            Arg.Is<IReadOnlyDictionary<string, string>>(d => d["eventKey"] == "dispatcher.batch.ready" && d["readyCount"] == "10"),
            Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task CheckAndNotifyHangingBatchesAsync_WhenHangingOrdersAndDebounceElapsed_SendsSummaryNotification()
    {
        // Arrange
        await using var db = CreateDb();
        var fcm = Substitute.For<IFcmMessagingClient>();
        fcm.IsConfigured.Returns(true);
        fcm.SendAsync(Arg.Any<string>(), Arg.Any<string>(), Arg.Any<string>(), Arg.Any<IReadOnlyDictionary<string, string>>(), Arg.Any<CancellationToken>())
            .Returns(FcmSendResult.Success());

        var initialTime = new DateTime(2026, 9, 24, 12, 0, 0, DateTimeKind.Utc);
        var clock = Substitute.For<IDateTimeProvider>();
        clock.UtcNow.Returns(initialTime);
        var logger = Substitute.For<ILogger<BatchDispatchNotifier>>();

        var notifier = new BatchDispatchNotifier(db, fcm, clock, logger);

        var restaurantId = Guid.NewGuid();
        db.RestaurantDeviceTokens.Add(new RestaurantDeviceToken
        {
            RestaurantId = restaurantId,
            Token = "token_disp_target",
            IsActive = true
        });

        // 3 hanging orders (less than 5)
        for (var i = 0; i < 3; i++)
        {
            db.RestaurantDailyOrders.Add(new RestaurantDailyOrder
            {
                RestaurantId = restaurantId,
                SubscriptionDayId = Guid.NewGuid(),
                Status = OrderStatus.ReadyForPickup,
                BoxCount = 1
            });
        }
        await db.SaveChangesAsync();

        // 11 minutes pass without reaching 5
        clock.UtcNow.Returns(initialTime.AddMinutes(11));

        // Act
        await notifier.CheckAndNotifyHangingBatchesAsync(restaurantId, CancellationToken.None);

        // Assert: summary push notification sent for the 3 hanging orders
        await fcm.Received(1).SendAsync(
            "token_disp_target",
            Arg.Is<string>(t => t.Contains("جاهزة")),
            Arg.Is<string>(b => b.Contains("3")),
            Arg.Is<IReadOnlyDictionary<string, string>>(d => d["eventKey"] == "dispatcher.batch.ready" && d["readyCount"] == "3"),
            Arg.Any<CancellationToken>());
    }
}
