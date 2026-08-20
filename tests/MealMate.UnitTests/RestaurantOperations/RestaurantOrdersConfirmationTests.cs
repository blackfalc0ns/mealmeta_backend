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
