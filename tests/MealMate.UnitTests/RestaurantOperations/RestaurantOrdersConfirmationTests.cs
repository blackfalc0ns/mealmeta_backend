using MealMate.Application.Common.Abstractions;
using MealMate.Application.Common.Localization;
using MealMate.Application.Common.Models;
using MealMate.Application.Common.Security;
using MealMate.Application.Features.RestaurantOrders;
using MealMate.Domain.CalendarMeals;
using MealMate.Domain.Common;
using MealMate.Domain.Entities;
using MealMate.Domain.DriverDelivery;
using MealMate.Domain.Enums;
using MealMate.Domain.IdentityAccess;
using MealMate.Domain.ProgramsSubscriptions;
using MealMate.Domain.RestaurantOperations;
using Microsoft.EntityFrameworkCore;
using NSubstitute;
using Xunit;

namespace MealMate.UnitTests.RestaurantOperations;

public class RestaurantOrdersConfirmationTests
{
    private readonly IAppDbContext _db;
    private readonly ICurrentUser _currentUser;
    private readonly IDateTimeProvider _clock;
    private readonly RestaurantOrderService _service;
    private readonly Guid _testRestaurantId = Guid.NewGuid();
    private readonly DateTime _baseNow = new DateTime(2026, 8, 11, 12, 0, 0, DateTimeKind.Utc);

    public RestaurantOrdersConfirmationTests()
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
    public async Task GetPendingConfirmation_CalculatesUrgencyAndSummariesCorrectly()
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

        // Day 1: Locked 20h ago -> 4h left -> Critical
        var day1 = new SubscriptionDay
        {
            SubscriptionId = subscription.Id,
            RestaurantId = _testRestaurantId,
            ServiceDate = new DateOnly(2026, 8, 14),
            Status = SubscriptionDayStatus.Locked72h,
            LockAtUtc = _baseNow.AddHours(-20)
        };

        // Day 2: Locked 16h ago -> 8h left -> Warning
        var day2 = new SubscriptionDay
        {
            SubscriptionId = subscription.Id,
            RestaurantId = _testRestaurantId,
            ServiceDate = new DateOnly(2026, 8, 15),
            Status = SubscriptionDayStatus.Locked72h,
            LockAtUtc = _baseNow.AddHours(-16)
        };

        // Day 3: Locked 8h ago -> 16h left -> Normal
        var day3 = new SubscriptionDay
        {
            SubscriptionId = subscription.Id,
            RestaurantId = _testRestaurantId,
            ServiceDate = new DateOnly(2026, 8, 16),
            Status = SubscriptionDayStatus.Locked72h,
            LockAtUtc = _baseNow.AddHours(-8)
        };

        // Day 4: Locked 26h ago -> -2h left -> Overdue
        var day4 = new SubscriptionDay
        {
            SubscriptionId = subscription.Id,
            RestaurantId = _testRestaurantId,
            ServiceDate = new DateOnly(2026, 8, 14),
            Status = SubscriptionDayStatus.Locked72h,
            LockAtUtc = _baseNow.AddHours(-26)
        };

        _db.SubscriptionDays.AddRange(day1, day2, day3, day4);
        await _db.SaveChangesAsync();

        // Act
        var result = await _service.GetPendingConfirmationAsync(new GetRestaurantPendingConfirmationQuery(), CancellationToken.None);

        // Assert
        Assert.True(result.IsSuccess);
        var data = result.Value;
        Assert.Equal(4, data.Orders.Count);

        var order1 = data.Orders.First(x => x.Id == day1.Id);
        Assert.Equal("critical", order1.Urgency);
        Assert.Equal(4, order1.HoursLeft);

        var order2 = data.Orders.First(x => x.Id == day2.Id);
        Assert.Equal("warning", order2.Urgency);
        Assert.Equal(8, order2.HoursLeft);

        var order3 = data.Orders.First(x => x.Id == day3.Id);
        Assert.Equal("normal", order3.Urgency);
        Assert.Equal(16, order3.HoursLeft);

        var order4 = data.Orders.First(x => x.Id == day4.Id);
        Assert.Equal("overdue", order4.Urgency);
        Assert.Equal(-2, order4.HoursLeft);

        var pendingSummary = data.Summaries.First(x => x.Id == "pending");
        Assert.Equal(4, pendingSummary.Value);
    }

    [Fact]
    public async Task ConfirmOrder_from_sent_to_restaurant_succeeds()
    {
        var day = new SubscriptionDay
        {
            SubscriptionId = Guid.NewGuid(),
            RestaurantId = _testRestaurantId,
            ServiceDate = new DateOnly(2026, 8, 14),
            Status = SubscriptionDayStatus.SentToRestaurant,
            LockAtUtc = _baseNow.AddHours(-10)
        };
        _db.SubscriptionDays.Add(day);
        await _db.SaveChangesAsync();

        var result = await _service.ConfirmOrderAsync(new ConfirmRestaurantOrderCommand(day.Id, "Ready to cook"), CancellationToken.None);

        Assert.True(result.IsSuccess);
        var updated = await _db.SubscriptionDays.FindAsync(day.Id);
        Assert.NotNull(updated);
        Assert.Equal(SubscriptionDayStatus.ConfirmedByRestaurant, updated.Status);
        Assert.Equal(_baseNow, updated.ConfirmedAtUtc);
    }

    [Fact]
    public async Task ConfirmOrder_from_open_status_fails()
    {
        var day = new SubscriptionDay
        {
            SubscriptionId = Guid.NewGuid(),
            RestaurantId = _testRestaurantId,
            ServiceDate = new DateOnly(2026, 8, 14),
            Status = SubscriptionDayStatus.CustomerSelected,
            LockAtUtc = null
        };
        _db.SubscriptionDays.Add(day);
        await _db.SaveChangesAsync();

        var result = await _service.ConfirmOrderAsync(new ConfirmRestaurantOrderCommand(day.Id), CancellationToken.None);

        Assert.False(result.IsSuccess);
        Assert.Equal(MessageCodes.Orders.InvalidStatus, result.Error.Code);
    }

    [Fact]
    public async Task ConfirmOrder_UpdatesStatusAndRecordsTimestamp()
    {
        // Arrange
        var day = new SubscriptionDay
        {
            SubscriptionId = Guid.NewGuid(),
            RestaurantId = _testRestaurantId,
            ServiceDate = new DateOnly(2026, 8, 14),
            Status = SubscriptionDayStatus.Locked72h,
            LockAtUtc = _baseNow.AddHours(-10)
        };
        _db.SubscriptionDays.Add(day);
        await _db.SaveChangesAsync();

        // Act
        var result = await _service.ConfirmOrderAsync(new ConfirmRestaurantOrderCommand(day.Id, "Ready to cook"), CancellationToken.None);

        // Assert
        Assert.True(result.IsSuccess);
        var updated = await _db.SubscriptionDays.FindAsync(day.Id);
        Assert.NotNull(updated);
        Assert.Equal(SubscriptionDayStatus.ConfirmedByRestaurant, updated.Status);
        Assert.Equal(_baseNow, updated.ConfirmedAtUtc);
    }

    [Fact]
    public async Task RejectOrder_UpdatesStatusToReplacementRequired()
    {
        // Arrange
        var day = new SubscriptionDay
        {
            SubscriptionId = Guid.NewGuid(),
            RestaurantId = _testRestaurantId,
            ServiceDate = new DateOnly(2026, 8, 14),
            Status = SubscriptionDayStatus.Locked72h,
            LockAtUtc = _baseNow.AddHours(-10)
        };
        _db.SubscriptionDays.Add(day);
        await _db.SaveChangesAsync();

        // Act
        var result = await _service.RejectOrderAsync(new RejectRestaurantOrderCommand(day.Id, "Ingredient shortage"), CancellationToken.None);

        // Assert
        Assert.True(result.IsSuccess);
        var updated = await _db.SubscriptionDays.FindAsync(day.Id);
        Assert.NotNull(updated);
        Assert.Equal(SubscriptionDayStatus.ReplacementRequired, updated.Status);
    }

    [Fact]
    public async Task GetOrderDetail_ReturnsCompleteDetailsWithMealsAndNutrition()
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

        var day = new SubscriptionDay
        {
            SubscriptionId = subscription.Id,
            RestaurantId = _testRestaurantId,
            ServiceDate = new DateOnly(2026, 8, 14),
            Status = SubscriptionDayStatus.Locked72h,
            LockAtUtc = _baseNow.AddHours(-10)
        };
        _db.SubscriptionDays.Add(day);

        var meal = new Meal
        {
            MenuId = Guid.NewGuid(),
            RestaurantId = _testRestaurantId,
            Code = "ML-101",
            NameAr = "دجاج مشوي مع كينوا",
            NameEn = "Grilled Chicken with Quinoa",
            DescriptionAr = "صدور دجاج متبلة مع كينوا",
            DescriptionEn = "Marinated chicken breast with quinoa",
            Slot = "main",
            Calories = 450,
            ProteinG = 42,
            CarbsG = 35,
            FatG = 12,
            FiberG = 6,
            TotalSugarsG = 3,
            SodiumMg = 480,
            ServingWeightG = 350
        };
        _db.Meals.Add(meal);

        var selection = new DailyMealSelection
        {
            SubscriptionDayId = day.Id,
            MealId = meal.Id,
            SortOrder = 0,
            SelectedAtUtc = _baseNow.AddDays(-2)
        };
        _db.DailyMealSelections.Add(selection);

        await _db.SaveChangesAsync();

        // Act
        var result = await _service.GetOrderDetailAsync(new GetRestaurantOrderDetailQuery(day.Id.ToString()), CancellationToken.None);

        // Assert
        Assert.True(result.IsSuccess);
        var detail = result.Value;
        Assert.Equal(day.Id, detail.Id);
        Assert.Single(detail.Meals);
        var mealItem = detail.Meals[0];
        Assert.Equal("دجاج مشوي مع كينوا", mealItem.Name.Ar);
        Assert.Equal(450, mealItem.Nutrition.Calories);
        Assert.Equal(42, mealItem.Nutrition.ProteinGrams);
        Assert.NotEmpty(detail.Checklist);
        Assert.NotEmpty(detail.Timeline);
    }

    [Fact]
    public async Task GetOrderDetail_ReflectsLiveOperationalState()
    {
        // Arrange
        var subscription = new Subscription
        {
            ProgramId = Guid.NewGuid(),
            BundleId = Guid.NewGuid(),
            CustomerUserId = Guid.NewGuid(),
            SelectedTier = RestaurantTier.Platinum
        };
        _db.Subscriptions.Add(subscription);

        var day = new SubscriptionDay
        {
            SubscriptionId = subscription.Id,
            RestaurantId = _testRestaurantId,
            ServiceDate = new DateOnly(2026, 8, 14),
            Status = SubscriptionDayStatus.OutForDelivery,
            LockAtUtc = _baseNow.AddHours(-50),
            ConfirmedAtUtc = _baseNow.AddHours(-48)
        };
        _db.SubscriptionDays.Add(day);

        var driver = new DriverProfile
        {
            UserId = Guid.NewGuid(),
            FullNameAr = "يحيي سواق",
            FullNameEn = "yahya driver"
        };
        _db.DriverProfiles.Add(driver);

        var dailyOrder = new RestaurantDailyOrder
        {
            RestaurantId = _testRestaurantId,
            SubscriptionDayId = day.Id,
            ServiceDate = day.ServiceDate,
            Status = OrderStatus.OnTheWay,
            BoxCount = 2,
            ConfirmedAtUtc = _baseNow.AddHours(-48),
            ReadyForPickupAtUtc = _baseNow.AddHours(-4)
        };
        _db.RestaurantDailyOrders.Add(dailyOrder);

        var trip = new Trip
        {
            RestaurantId = _testRestaurantId,
            Code = "TRP-TEST-0001",
            Status = TripStatus.InProgress,
            PlannedDate = day.ServiceDate
        };
        _db.Trips.Add(trip);

        _db.TripStops.Add(new TripStop
        {
            TripId = trip.Id,
            DailyOrderId = dailyOrder.Id,
            SubscriptionDayId = day.Id,
            OrderCode = "ORD-TEST",
            CustomerMaskedId = "CUS-•••001",
            ZoneNameAr = "منطقة",
            ZoneNameEn = "Zone",
            MealSummaryAr = "وجبات",
            MealSummaryEn = "Meals",
            SlotLabel = "main",
            SequenceNumber = 1,
            Status = TripStopStatus.PickedUp
        });

        _db.OrderHandovers.Add(new OrderHandover
        {
            DailyOrderId = dailyOrder.Id,
            TripId = trip.Id,
            DriverProfileId = driver.Id,
            Status = "Completed",
            HandedOverAtUtc = _baseNow.AddHours(-2)
        });

        await _db.SaveChangesAsync();

        // Act
        var result = await _service.GetOrderDetailAsync(new GetRestaurantOrderDetailQuery(day.Id.ToString()), CancellationToken.None);

        // Assert
        Assert.True(result.IsSuccess);
        var detail = result.Value;
        Assert.Equal("waiting-driver", detail.Status);
        Assert.All(detail.Checklist, item => Assert.True(item.Done));
        Assert.Equal("يحيي سواق", detail.DriverCode);
        Assert.Equal(2, detail.BoxCount);
        Assert.Contains("TRP-TEST-0001", detail.WindowLabel.Ar);

        var timelineIds = detail.Timeline.Select(t => t.Id).ToList();
        Assert.Contains("tl-ready", timelineIds);
        Assert.Contains("tl-handover", timelineIds);
        Assert.True(timelineIds.IndexOf("tl-handover") < timelineIds.IndexOf("tl-ready"));
    }

    [Fact]
    public async Task GetOrderDetail_MapsReadyForPickupDailyOrderToReadyStatus()
    {
        // Arrange
        var day = new SubscriptionDay
        {
            SubscriptionId = Guid.NewGuid(),
            RestaurantId = _testRestaurantId,
            ServiceDate = new DateOnly(2026, 8, 14),
            Status = SubscriptionDayStatus.Preparing,
            LockAtUtc = _baseNow.AddHours(-10),
            ConfirmedAtUtc = _baseNow.AddHours(-9)
        };
        _db.SubscriptionDays.Add(day);

        _db.RestaurantDailyOrders.Add(new RestaurantDailyOrder
        {
            RestaurantId = _testRestaurantId,
            SubscriptionDayId = day.Id,
            ServiceDate = day.ServiceDate,
            Status = OrderStatus.ReadyForPickup,
            ConfirmedAtUtc = _baseNow.AddHours(-9),
            ReadyForPickupAtUtc = _baseNow.AddHours(-1)
        });

        await _db.SaveChangesAsync();

        // Act
        var result = await _service.GetOrderDetailAsync(new GetRestaurantOrderDetailQuery(day.Id.ToString()), CancellationToken.None);

        // Assert
        Assert.True(result.IsSuccess);
        var detail = result.Value;
        Assert.Equal("ready", detail.Status);
        Assert.Equal(4, detail.Checklist.Count(item => item.Done));
        Assert.True(detail.Checklist[2].Done);
        Assert.True(detail.Checklist[3].Done);
        Assert.False(detail.Checklist[4].Done);
        Assert.Contains(detail.Timeline, t => t.Id == "tl-ready");
        Assert.DoesNotContain(detail.Timeline, t => t.Id == "tl-handover");
    }

    [Fact]
    public async Task RestaurantIsolation_UnauthorizedWhenNoRestaurantContext()
    {
        // Arrange
        var unauthorizedUser = Substitute.For<ICurrentUser>();
        unauthorizedUser.RestaurantId.Returns((Guid?)null);
        var unauthService = new RestaurantOrderService(_db, unauthorizedUser, _clock);

        // Act
        var result = await unauthService.GetPendingConfirmationAsync(new GetRestaurantPendingConfirmationQuery(), CancellationToken.None);

        // Assert
        Assert.True(result.IsFailure);
        Assert.Equal(ErrorType.Unauthorized, result.Error.Type);
    }
}
