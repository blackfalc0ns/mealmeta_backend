using FluentAssertions;
using MealMate.Application.Common.Abstractions;
using MealMate.Application.Features.CustomerCheckout;
using MealMate.Domain.CalendarMeals;
using MealMate.Domain.Common;
using MealMate.Domain.Enums;
using MealMate.Domain.IdentityAccess;
using MealMate.Domain.ProgramsSubscriptions;
using MealMate.Domain.RestaurantOperations;
using MealMate.Persistence;
using Microsoft.EntityFrameworkCore;
using NSubstitute;
using Xunit;

namespace MealMate.UnitTests.CustomerCheckout;

public class OrderReplacementOrchestratorTests
{
    private static AppDbContext CreateDb()
    {
        var options = new DbContextOptionsBuilder<AppDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString())
            .Options;
        return new AppDbContext(options, Substitute.For<IDomainEventDispatcher>());
    }

    [Fact]
    public async Task AttemptAutoReplacementAsync_ReplacesWithSnapshotAlternative_WhenAvailable()
    {
        // Arrange
        await using var db = CreateDb();
        var now = DateTime.UtcNow;

        var restA = new RestaurantProfile
        {
            TradeNameAr = "مطعم ألف",
            TradeNameEn = "Restaurant A",
            LegalNameAr = "شركة ألف",
            LegalNameEn = "Company A",
            IsOperationallyActive = true
        };
        var restB = new RestaurantProfile
        {
            TradeNameAr = "مطعم باء",
            TradeNameEn = "Restaurant B",
            LegalNameAr = "شركة باء",
            LegalNameEn = "Company B",
            IsOperationallyActive = true
        };
        db.RestaurantProfiles.AddRange(restA, restB);

        var sub = new Subscription
        {
            CustomerUserId = Guid.NewGuid(),
            Status = SubscriptionStatus.Active,
            DurationDays = 30
        };
        db.Subscriptions.Add(sub);

        var snapA = new SubscriptionRestaurantSnapshot
        {
            SubscriptionId = sub.Id,
            RestaurantId = restA.Id,
            NameAr = restA.TradeNameAr,
            NameEn = restA.TradeNameEn,
            IsInitiallySelected = true
        };
        var snapB = new SubscriptionRestaurantSnapshot
        {
            SubscriptionId = sub.Id,
            RestaurantId = restB.Id,
            NameAr = restB.TradeNameAr,
            NameEn = restB.TradeNameEn,
            IsInitiallySelected = false
        };
        db.SubscriptionRestaurantSnapshots.AddRange(snapA, snapB);

        var day = new SubscriptionDay
        {
            SubscriptionId = sub.Id,
            RestaurantId = restA.Id,
            ServiceDate = DateOnly.FromDateTime(now.AddDays(3)),
            Status = SubscriptionDayStatus.ReplacementRequired
        };
        db.SubscriptionDays.Add(day);

        var dailyOrder = new RestaurantDailyOrder
        {
            RestaurantId = restA.Id,
            SubscriptionDayId = day.Id,
            ServiceDate = day.ServiceDate,
            Status = OrderStatus.RestaurantRejected,
            BoxCount = 1
        };
        db.RestaurantDailyOrders.Add(dailyOrder);
        await db.SaveChangesAsync();

        var clock = Substitute.For<IDateTimeProvider>();
        clock.UtcNow.Returns(now);
        var restaurantNotifier = Substitute.For<IRestaurantOrderNotifier>();
        var customerPush = Substitute.For<ICustomerPushNotifier>();

        var orchestrator = new OrderReplacementOrchestrator(
            db,
            clock,
            restaurantNotifier,
            customerPush);

        // Act
        var result = await orchestrator.AttemptAutoReplacementAsync(day.Id, CancellationToken.None);

        // Assert
        result.Status.Should().Be(ReplacementStatus.ReplacedWithAlternative);
        result.NewRestaurantId.Should().Be(restB.Id);

        day.RestaurantId.Should().Be(restB.Id);
        day.Status.Should().Be(SubscriptionDayStatus.SentToRestaurant);

        dailyOrder.RestaurantId.Should().Be(restB.Id);
        dailyOrder.Status.Should().Be(OrderStatus.AwaitingRestaurantConfirmation);

        await restaurantNotifier.Received(1).NotifyOrderSentToRestaurantAsync(
            restB.Id,
            day.Id,
            day.ServiceDate,
            Arg.Any<CancellationToken>());

        await customerPush.Received(1).NotifyOrderReplacedAsync(
            sub.CustomerUserId,
            day.Id,
            day.ServiceDate,
            Arg.Is<string>(n => n.Contains("باء") || n.Contains("B")),
            Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task AttemptAutoReplacementAsync_ReturnsNoEligibleAlternative_WhenNoActiveAlternativeExists()
    {
        // Arrange
        await using var db = CreateDb();
        var now = DateTime.UtcNow;

        var restA = new RestaurantProfile
        {
            TradeNameAr = "مطعم وحيد",
            TradeNameEn = "Sole Restaurant",
            LegalNameAr = "شركة وحيدة",
            LegalNameEn = "Sole Company",
            IsOperationallyActive = true
        };
        db.RestaurantProfiles.Add(restA);

        var sub = new Subscription
        {
            CustomerUserId = Guid.NewGuid(),
            Status = SubscriptionStatus.Active,
            DurationDays = 30
        };
        db.Subscriptions.Add(sub);

        var day = new SubscriptionDay
        {
            SubscriptionId = sub.Id,
            RestaurantId = restA.Id,
            ServiceDate = DateOnly.FromDateTime(now.AddDays(3)),
            Status = SubscriptionDayStatus.ReplacementRequired
        };
        db.SubscriptionDays.Add(day);
        await db.SaveChangesAsync();

        var clock = Substitute.For<IDateTimeProvider>();
        clock.UtcNow.Returns(now);
        var restaurantNotifier = Substitute.For<IRestaurantOrderNotifier>();
        var customerPush = Substitute.For<ICustomerPushNotifier>();

        var orchestrator = new OrderReplacementOrchestrator(
            db,
            clock,
            restaurantNotifier,
            customerPush);

        // Act
        var result = await orchestrator.AttemptAutoReplacementAsync(day.Id, CancellationToken.None);

        // Assert
        result.Status.Should().Be(ReplacementStatus.NoEligibleAlternative);
        day.Status.Should().Be(SubscriptionDayStatus.ReplacementRequired);
    }
}
