using FluentAssertions;
using MealMate.Application.Common.Abstractions;
using MealMate.Application.Features.CustomerCalendar;
using MealMate.Application.Features.IdentityAccess.Customers;
using MealMate.Domain.CalendarMeals;
using MealMate.Domain.Common;
using MealMate.Domain.Enums;
using MealMate.Domain.ProgramsSubscriptions;
using MealMate.Domain.RestaurantOperations;
using MealMate.Persistence;
using Microsoft.EntityFrameworkCore;
using NSubstitute;
using Xunit;

namespace MealMate.UnitTests.CustomerCalendar;

public class SubscriptionDayDispatchServiceTests
{
    private static AppDbContext CreateDb()
    {
        var options = new DbContextOptionsBuilder<AppDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString())
            .Options;
        return new AppDbContext(options, Substitute.For<IDomainEventDispatcher>());
    }

    [Fact]
    public async Task LockAndSendToRestaurantAsync_CreatesRestaurantDailyOrder_WhenNotExists()
    {
        // Arrange
        await using var db = CreateDb();
        var clock = Substitute.For<IDateTimeProvider>();
        var now = DateTime.UtcNow;
        clock.UtcNow.Returns(now);

        var deliveryAddresses = Substitute.For<ICustomerDeliveryAddressService>();
        var notifier = Substitute.For<IRestaurantOrderNotifier>();
        var customerPush = Substitute.For<ICustomerPushNotifier>();

        var service = new SubscriptionDayDispatchService(
            db,
            clock,
            deliveryAddresses,
            notifier,
            customerPush);

        var customerUserId = Guid.NewGuid();
        var restaurantId = Guid.NewGuid();
        var sub = new Subscription
        {
            CustomerUserId = customerUserId,
            Status = SubscriptionStatus.Active,
            DurationDays = 30
        };
        db.Subscriptions.Add(sub);

        var day = new SubscriptionDay
        {
            SubscriptionId = sub.Id,
            RestaurantId = restaurantId,
            ServiceDate = DateOnly.FromDateTime(now.AddDays(3)),
            Status = SubscriptionDayStatus.Open
        };
        db.SubscriptionDays.Add(day);
        await db.SaveChangesAsync();

        // Act
        await service.LockAndSendToRestaurantAsync(day, sub.Id, CancellationToken.None);
        await db.SaveChangesAsync();

        // Assert
        day.Status.Should().Be(SubscriptionDayStatus.SentToRestaurant);
        day.LockAtUtc.Should().Be(now);

        var dailyOrder = await db.RestaurantDailyOrders
            .FirstOrDefaultAsync(r => r.SubscriptionDayId == day.Id);

        dailyOrder.Should().NotBeNull();
        dailyOrder!.RestaurantId.Should().Be(restaurantId);
        dailyOrder.Status.Should().Be(OrderStatus.AwaitingRestaurantConfirmation);
        dailyOrder.BoxCount.Should().Be(1);
    }
}
