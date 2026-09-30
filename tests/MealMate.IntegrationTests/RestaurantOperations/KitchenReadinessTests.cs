using FluentAssertions;
using MealMate.Application.Common.Abstractions;
using MealMate.Application.Features.PrepDocuments;
using MealMate.Application.Features.RestaurantOrders;
using MealMate.Domain.CalendarMeals;
using MealMate.Domain.Enums;
using MealMate.Domain.IdentityAccess;
using MealMate.Domain.RestaurantOperations;
using MealMate.Persistence;
using Microsoft.EntityFrameworkCore;
using NSubstitute;
using Testcontainers.PostgreSql;
using Xunit;
using Result = MealMate.Application.Common.Models.Result;

namespace MealMate.IntegrationTests.RestaurantOperations;

public class KitchenReadinessTests : IAsyncLifetime
{
    private readonly PostgreSqlContainer _dbContainer = new PostgreSqlBuilder()
        .WithImage("postgres:16-alpine")
        .WithDatabase("mealmate_test_kitchen")
        .WithUsername("postgres")
        .WithPassword("postgres")
        .Build();

    private AppDbContext CreateDbContext()
    {
        var options = new DbContextOptionsBuilder<AppDbContext>()
            .UseNpgsql(_dbContainer.GetConnectionString())
            .Options;
        return new AppDbContext(options, Substitute.For<IDomainEventDispatcher>());
    }

    public async Task InitializeAsync()
    {
        await _dbContainer.StartAsync();
        await using var db = CreateDbContext();
        await db.Database.MigrateAsync();
    }

    public async Task DisposeAsync()
    {
        await _dbContainer.DisposeAsync();
    }

    [Fact]
    public async Task UpdatePrepStatus_ToReady_CannotPrecedePreparing()
    {
        // Arrange
        await using var db = CreateDbContext();
        var restaurantId = Guid.NewGuid();
        var restaurant = new RestaurantProfile
        {
            TradeNameAr = "مطعم أصيل",
            TradeNameEn = "Aseel Restaurant",
            LegalNameAr = "شركة أصيل",
            LegalNameEn = "Aseel Company"
        };
        db.RestaurantProfiles.Add(restaurant);

        var day = new SubscriptionDay
        {
            SubscriptionId = Guid.NewGuid(),
            RestaurantId = restaurant.Id,
            ServiceDate = DateOnly.FromDateTime(DateTime.UtcNow.AddDays(1)),
            Status = SubscriptionDayStatus.ConfirmedByRestaurant
        };
        db.SubscriptionDays.Add(day);

        // Daily order is in RestaurantConfirmed, NOT Preparing
        var order = new RestaurantDailyOrder
        {
            RestaurantId = restaurant.Id,
            SubscriptionDayId = day.Id,
            ServiceDate = day.ServiceDate,
            Status = OrderStatus.RestaurantConfirmed,
            BoxCount = 1,
            CreatedAtUtc = DateTime.UtcNow
        };
        db.RestaurantDailyOrders.Add(order);
        await db.SaveChangesAsync();

        var currentUser = Substitute.For<ICurrentUser>();
        currentUser.IsAuthenticated.Returns(true);
        currentUser.RestaurantId.Returns(restaurant.Id);
        currentUser.UserId.Returns(Guid.NewGuid());
        currentUser.UserType.Returns(UserType.Restaurant);

        var clock = Substitute.For<IDateTimeProvider>();
        clock.UtcNow.Returns(DateTime.UtcNow);

        var prepDocGen = Substitute.For<IPrepDocumentsGenerator>();
        var service = new RestaurantOrderService(db, currentUser, clock, prepDocumentsGenerator: prepDocGen);

        // Act: Attempt to jump directly from Confirmed to ReadyForPickup
        var result = await service.UpdateDailyOrderPrepStatusAsync(
            new UpdateDailyOrderPrepStatusCommand(day.Id, "ready-for-pickup"),
            CancellationToken.None);

        // Assert: Must fail state transition guard
        result.IsFailure.Should().BeTrue();
        result.Error.Code.Should().Be("INVALID_ORDER_TRANSITION");
        day.Status.Should().Be(SubscriptionDayStatus.ConfirmedByRestaurant);
        order.Status.Should().Be(OrderStatus.RestaurantConfirmed);
        await prepDocGen.DidNotReceive().GenerateForDayAsync(
            Arg.Any<Guid>(), Arg.Any<bool>(), Arg.Any<bool>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task UpdatePrepStatus_ToReady_PersistsImmutableReadyAtUtc()
    {
        // Arrange
        await using var db = CreateDbContext();
        var restaurantId = Guid.NewGuid();
        var restaurant = new RestaurantProfile
        {
            TradeNameAr = "مطعم أصيل",
            TradeNameEn = "Aseel Restaurant",
            LegalNameAr = "شركة أصيل",
            LegalNameEn = "Aseel Company"
        };
        db.RestaurantProfiles.Add(restaurant);

        var day = new SubscriptionDay
        {
            SubscriptionId = Guid.NewGuid(),
            RestaurantId = restaurant.Id,
            ServiceDate = DateOnly.FromDateTime(DateTime.UtcNow.AddDays(1)),
            Status = SubscriptionDayStatus.Preparing
        };
        db.SubscriptionDays.Add(day);

        var order = new RestaurantDailyOrder
        {
            RestaurantId = restaurant.Id,
            SubscriptionDayId = day.Id,
            ServiceDate = day.ServiceDate,
            Status = OrderStatus.Preparing,
            BoxCount = 1,
            CreatedAtUtc = DateTime.UtcNow
        };
        db.RestaurantDailyOrders.Add(order);
        await db.SaveChangesAsync();

        var currentUser = Substitute.For<ICurrentUser>();
        currentUser.IsAuthenticated.Returns(true);
        currentUser.RestaurantId.Returns(restaurant.Id);
        currentUser.UserId.Returns(Guid.NewGuid());
        currentUser.UserType.Returns(UserType.Restaurant);

        var initialReadyTime = new DateTime(2026, 9, 28, 14, 0, 0, DateTimeKind.Utc);
        var clock = Substitute.For<IDateTimeProvider>();
        clock.UtcNow.Returns(initialReadyTime);

        var prepDocGen = Substitute.For<IPrepDocumentsGenerator>();
        prepDocGen.GenerateForDayAsync(Arg.Any<Guid>(), Arg.Any<bool>(), Arg.Any<bool>(), Arg.Any<CancellationToken>())
            .Returns(Result.Success());
        var service = new RestaurantOrderService(db, currentUser, clock, prepDocumentsGenerator: prepDocGen);

        // Act 1: Transition Preparing -> ReadyForPickup
        var result1 = await service.UpdateDailyOrderPrepStatusAsync(
            new UpdateDailyOrderPrepStatusCommand(day.Id, "ready-for-pickup"),
            CancellationToken.None);

        result1.IsSuccess.Should().BeTrue();

        var reloadedOrder1 = await db.RestaurantDailyOrders.FindAsync(order.Id);
        reloadedOrder1.Should().NotBeNull();
        reloadedOrder1!.Status.Should().Be(OrderStatus.ReadyForPickup);
        reloadedOrder1.ReadyForPickupAtUtc.Should().Be(initialReadyTime);

        // Act 2: Repeated request 10 minutes later
        var laterTime = initialReadyTime.AddMinutes(10);
        clock.UtcNow.Returns(laterTime);

        var result2 = await service.UpdateDailyOrderPrepStatusAsync(
            new UpdateDailyOrderPrepStatusCommand(day.Id, "ready-for-pickup"),
            CancellationToken.None);

        result2.IsSuccess.Should().BeTrue();

        var reloadedOrder2 = await db.RestaurantDailyOrders.FindAsync(order.Id);
        reloadedOrder2.Should().NotBeNull();
        // Crucial invariant: ReadyForPickupAtUtc must remain the original immutable timestamp!
        reloadedOrder2!.ReadyForPickupAtUtc.Should().Be(initialReadyTime);
    }
}
