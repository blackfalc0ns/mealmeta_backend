using MealMate.Application.Common.Abstractions;
using MealMate.Application.Common.Models;
using MealMate.Application.Common.Security;
using MealMate.Application.Features.RestaurantOrders;
using MealMate.Domain.CalendarMeals;
using MealMate.Domain.Common;
using MealMate.Domain.Entities;
using MealMate.Domain.Enums;
using MealMate.Domain.IdentityAccess;
using MealMate.Domain.ProgramsSubscriptions;
using MealMate.Domain.RestaurantOperations;
using Microsoft.EntityFrameworkCore;
using NSubstitute;
using Xunit;

namespace MealMate.UnitTests.RestaurantOperations;

public class DailyOrdersAndUpcoming24hTests
{
    private readonly IAppDbContext _db;
    private readonly ICurrentUser _currentUser;
    private readonly IDateTimeProvider _clock;
    private readonly RestaurantOrderService _service;
    private readonly Guid _testRestaurantId = Guid.NewGuid();
    private readonly DateTime _baseNow = new DateTime(2026, 8, 11, 10, 0, 0, DateTimeKind.Utc);
    private readonly DateOnly _today = new DateOnly(2026, 8, 11);

    public DailyOrdersAndUpcoming24hTests()
    {
        var options = new DbContextOptionsBuilder<Persistence.AppDbContext>()
            .UseInMemoryDatabase(databaseName: Guid.NewGuid().ToString())
            .Options;

        _db = new Persistence.AppDbContext(options, Substitute.For<IDomainEventDispatcher>());

        _currentUser = Substitute.For<ICurrentUser>();
        _currentUser.RestaurantId.Returns(_testRestaurantId);

        _clock = Substitute.For<IDateTimeProvider>();
        _clock.UtcNow.Returns(_baseNow);

        _service = new RestaurantOrderService(_db, _currentUser, _clock);
    }

    [Fact]
    public async Task GetDailyOrders_ReturnsOrdersForSelectedDateAndCalculatesSummaries()
    {
        // Arrange
        var program = new Program { Code = "PRG-FIT", NameAr = "لياقة", NameEn = "Fitness" };
        var bundle = new Bundle { Code = "BND-FULL", NameAr = "باقة كاملة", NameEn = "Full Package", ProgramId = program.Id };
        _db.Programs.Add(program);
        _db.Bundles.Add(bundle);

        var subscription = new Subscription
        {
            ProgramId = program.Id,
            BundleId = bundle.Id,
            CustomerUserId = Guid.NewGuid(),
            SelectedTier = RestaurantTier.Platinum
        };
        _db.Subscriptions.Add(subscription);

        // Day 1: Preparing
        var day1 = new SubscriptionDay
        {
            SubscriptionId = subscription.Id,
            RestaurantId = _testRestaurantId,
            ServiceDate = _today,
            Status = SubscriptionDayStatus.Preparing,
            LockAtUtc = _baseNow.AddHours(-48)
        };

        // Day 2: OutForDelivery
        var day2 = new SubscriptionDay
        {
            SubscriptionId = subscription.Id,
            RestaurantId = _testRestaurantId,
            ServiceDate = _today,
            Status = SubscriptionDayStatus.OutForDelivery,
            LockAtUtc = _baseNow.AddHours(-48)
        };

        // Day 3: Delivered
        var day3 = new SubscriptionDay
        {
            SubscriptionId = subscription.Id,
            RestaurantId = _testRestaurantId,
            ServiceDate = _today,
            Status = SubscriptionDayStatus.Delivered,
            LockAtUtc = _baseNow.AddHours(-48)
        };

        // Day 4: Different day (tomorrow)
        var day4 = new SubscriptionDay
        {
            SubscriptionId = subscription.Id,
            RestaurantId = _testRestaurantId,
            ServiceDate = _today.AddDays(1),
            Status = SubscriptionDayStatus.Preparing,
            LockAtUtc = _baseNow.AddHours(-24)
        };

        _db.SubscriptionDays.AddRange(day1, day2, day3, day4);
        await _db.SaveChangesAsync();

        // Act
        var result = await _service.GetDailyOrdersAsync(new GetRestaurantDailyOrdersQuery(_today), CancellationToken.None);

        // Assert
        Assert.True(result.IsSuccess);
        var data = result.Value;
        Assert.Equal(3, data.Orders.Count);

        var totalSummary = data.Summaries.First(s => s.Id == "total");
        Assert.Equal(3, totalSummary.Value);

        var preparingSummary = data.Summaries.First(s => s.Id == "preparing");
        Assert.Equal(1, preparingSummary.Value);

        var driverSummary = data.Summaries.First(s => s.Id == "handover");
        Assert.Equal(1, driverSummary.Value);
    }

    [Fact]
    public async Task GetUpcoming24h_ReturnsOrdersWithin24hWindow()
    {
        // Arrange
        var program = new Program { Code = "PRG-SLIM", NameAr = "رشاقة", NameEn = "Slim" };
        var bundle = new Bundle { Code = "BND-LUNCH", NameAr = "باقة غداء", NameEn = "Lunch Package", ProgramId = program.Id };
        _db.Programs.Add(program);
        _db.Bundles.Add(bundle);

        var subscription = new Subscription
        {
            ProgramId = program.Id,
            BundleId = bundle.Id,
            CustomerUserId = Guid.NewGuid(),
            SelectedTier = RestaurantTier.Platinum
        };
        _db.Subscriptions.Add(subscription);

        var tomorrow = _today.AddDays(1);
        var dayTomorrow = new SubscriptionDay
        {
            SubscriptionId = subscription.Id,
            RestaurantId = _testRestaurantId,
            ServiceDate = tomorrow,
            Status = SubscriptionDayStatus.ConfirmedByRestaurant,
            LockAtUtc = _baseNow.AddHours(-10)
        };

        _db.SubscriptionDays.Add(dayTomorrow);
        await _db.SaveChangesAsync();

        // Act
        var result = await _service.GetUpcoming24hAsync(new GetRestaurantUpcoming24hQuery(tomorrow), CancellationToken.None);

        // Assert
        Assert.True(result.IsSuccess);
        var data = result.Value;
        Assert.Single(data.Orders);

        var order = data.Orders[0];
        Assert.Equal(dayTomorrow.Id, order.Id);
        Assert.True(order.HoursToDelivery > 0 && order.HoursToDelivery <= 24);
        Assert.NotEmpty(data.Summaries);
    }

    [Fact]
    public async Task ArchiveDailyOrder_CompletesOrder()
    {
        // Arrange
        var day = new SubscriptionDay
        {
            SubscriptionId = Guid.NewGuid(),
            RestaurantId = _testRestaurantId,
            ServiceDate = _today,
            Status = SubscriptionDayStatus.Delivered,
            LockAtUtc = _baseNow.AddHours(-48)
        };
        _db.SubscriptionDays.Add(day);
        await _db.SaveChangesAsync();

        // Act
        var result = await _service.ArchiveDailyOrderAsync(new ArchiveDailyOrderCommand(day.Id), CancellationToken.None);

        // Assert
        Assert.True(result.IsSuccess);
        var updated = await _db.SubscriptionDays.FindAsync(day.Id);
        Assert.NotNull(updated);
        Assert.Equal(SubscriptionDayStatus.Delivered, updated.Status);
    }

    [Fact]
    public async Task RestaurantIsolation_UnauthorizedWhenNoRestaurantContext()
    {
        // Arrange
        var unauthorizedUser = Substitute.For<ICurrentUser>();
        unauthorizedUser.RestaurantId.Returns((Guid?)null);
        var unauthService = new RestaurantOrderService(_db, unauthorizedUser, _clock);

        // Act
        var result = await unauthService.GetDailyOrdersAsync(new GetRestaurantDailyOrdersQuery(), CancellationToken.None);

        // Assert
        Assert.True(result.IsFailure);
        Assert.Equal(ErrorType.Unauthorized, result.Error.Type);
    }
}
