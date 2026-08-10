using MealMate.Application.Common.Abstractions;
using MealMate.Application.Common.Models;
using MealMate.Application.Common.Security;
using MealMate.Application.Features.RestaurantOperations;
using MealMate.Domain.CalendarMeals;
using MealMate.Domain.Common;
using MealMate.Domain.Enums;
using MealMate.Domain.RestaurantOperations;
using Microsoft.EntityFrameworkCore;
using NSubstitute;
using Xunit;

namespace MealMate.UnitTests.RestaurantOperations;

public class RestaurantOperationsTests
{
    private readonly IAppDbContext _db;
    private readonly ICurrentUser _currentUser;
    private readonly IDateTimeProvider _clock;
    private readonly RestaurantCapacityService _service;
    private readonly Guid _testRestaurantId = Guid.NewGuid();

    public RestaurantOperationsTests()
    {
        var options = new DbContextOptionsBuilder<Persistence.AppDbContext>()
            .UseInMemoryDatabase(databaseName: Guid.NewGuid().ToString())
            .Options;

        _db = new Persistence.AppDbContext(options, Substitute.For<IDomainEventDispatcher>());

        _currentUser = Substitute.For<ICurrentUser>();
        _currentUser.RestaurantId.Returns(_testRestaurantId);

        _clock = Substitute.For<IDateTimeProvider>();
        _clock.UtcNow.Returns(new DateTime(2026, 8, 10, 10, 0, 0, DateTimeKind.Utc)); // Monday, Aug 10, 2026

        _service = new RestaurantCapacityService(_db, _currentUser, _clock);
    }

    [Fact]
    public async Task GetCapacityOverview_ReflectsStandardWeeklySchedule_FridayIsOffByDefault()
    {
        // Act
        var result = await _service.GetCapacityOverviewAsync(
            new GetRestaurantCapacityOverviewQuery(new DateOnly(2026, 8, 10), new DateOnly(2026, 8, 16)),
            CancellationToken.None);

        // Assert
        Assert.True(result.IsSuccess);
        var overview = result.Value;
        Assert.Equal(40, overview.DailyLimit);

        // Monday Aug 10 should be open
        var monday = overview.Days.First(x => x.DateIso == "2026-08-10");
        Assert.True(monday.IsWorkingDay);
        Assert.False(monday.IsHoliday);
        Assert.Equal(40, monday.Limit);

        // Friday Aug 14 should be off by default
        var friday = overview.Days.First(x => x.DateIso == "2026-08-14");
        Assert.False(friday.IsWorkingDay);
        Assert.True(friday.IsHoliday);
        Assert.Equal(0, friday.Limit);
        Assert.Equal("holiday", friday.BusyKind);
    }

    [Fact]
    public async Task UpdateWeeklySchedule_EnablingFriday_ReflectsOnCapacityOverview()
    {
        // Arrange
        var scheduleInput = new List<WeeklyDayScheduleInputDto>
        {
            new(0, true), // Sun
            new(1, true), // Mon
            new(2, true), // Tue
            new(3, true), // Wed
            new(4, true), // Thu
            new(5, true), // Fri -> ENABLED
            new(6, true), // Sat
        };

        // Act
        var updateResult = await _service.UpdateWorkingDaysScheduleAsync(
            new UpdateWorkingDaysScheduleCommand(scheduleInput), CancellationToken.None);
        Assert.True(updateResult.IsSuccess);

        var overviewResult = await _service.GetCapacityOverviewAsync(
            new GetRestaurantCapacityOverviewQuery(new DateOnly(2026, 8, 14), new DateOnly(2026, 8, 14)),
            CancellationToken.None);

        // Assert
        var friday = overviewResult.Value.Days.First();
        Assert.True(friday.IsWorkingDay);
        Assert.False(friday.IsHoliday);
        Assert.Equal(40, friday.Limit);
        Assert.NotEqual("holiday", friday.BusyKind);
    }

    [Fact]
    public async Task AddException_HolidayOnWorkingDay_ClosesDay()
    {
        // Act - Add holiday for Monday Aug 10
        var excResult = await _service.AddWorkingDayExceptionAsync(
            new AddWorkingDayExceptionCommand(new DateOnly(2026, 8, 10), false, "National Day"),
            CancellationToken.None);

        // Assert exception created
        Assert.True(excResult.IsSuccess);
        Assert.False(excResult.Value.IsWorkingDay);

        var overviewResult = await _service.GetCapacityOverviewAsync(
            new GetRestaurantCapacityOverviewQuery(new DateOnly(2026, 8, 10), new DateOnly(2026, 8, 10)),
            CancellationToken.None);

        var monday = overviewResult.Value.Days.First();
        Assert.False(monday.IsWorkingDay);
        Assert.True(monday.IsHoliday);
        Assert.Equal(0, monday.Limit);
        Assert.Equal("National Day", monday.HolidayReason);
    }

    [Fact]
    public async Task AddException_ExtraordinaryOpenOnOffDay_OpensFriday()
    {
        // Act - Open Friday Aug 14 extraordinarily
        var excResult = await _service.AddWorkingDayExceptionAsync(
            new AddWorkingDayExceptionCommand(new DateOnly(2026, 8, 14), true, "Special Event"),
            CancellationToken.None);

        Assert.True(excResult.IsSuccess);

        var overviewResult = await _service.GetCapacityOverviewAsync(
            new GetRestaurantCapacityOverviewQuery(new DateOnly(2026, 8, 14), new DateOnly(2026, 8, 14)),
            CancellationToken.None);

        var friday = overviewResult.Value.Days.First();
        Assert.True(friday.IsWorkingDay);
        Assert.False(friday.IsHoliday);
        Assert.Equal(40, friday.Limit);
    }

    [Fact]
    public async Task UpdateBaseCapacity_ReductionAttempt_ReturnsError()
    {
        // Arrange - base capacity is 40
        // Act - try to reduce to 30
        var result = await _service.UpdateBaseCapacityAsync(
            new UpdateBaseCapacityCommand(30), CancellationToken.None);

        // Assert
        Assert.True(result.IsFailure);
        Assert.Contains("cannot be reduced", result.Error.Description);
    }

    [Fact]
    public async Task UpdateBaseCapacity_Increase_Succeeds()
    {
        // Act - increase capacity to 60
        var result = await _service.UpdateBaseCapacityAsync(
            new UpdateBaseCapacityCommand(60), CancellationToken.None);

        // Assert
        Assert.True(result.IsSuccess);
        Assert.Equal(60, result.Value.DailyLimit);
    }

    [Fact]
    public async Task UpdateDateCapacity_IncreasesOnlySelectedDay()
    {
        var selectedDate = new DateOnly(2026, 8, 11);

        var updateResult = await _service.UpdateDateCapacityAsync(
            new UpdateDateCapacityCommand(selectedDate, 65), CancellationToken.None);

        Assert.True(updateResult.IsSuccess);
        Assert.Equal(65, updateResult.Value.Limit);

        var overviewResult = await _service.GetCapacityOverviewAsync(
            new GetRestaurantCapacityOverviewQuery(
                new DateOnly(2026, 8, 10),
                new DateOnly(2026, 8, 12)),
            CancellationToken.None);

        Assert.Equal(40, overviewResult.Value.Days.Single(x => x.DateIso == "2026-08-10").Limit);
        Assert.Equal(65, overviewResult.Value.Days.Single(x => x.DateIso == "2026-08-11").Limit);
        Assert.Equal(40, overviewResult.Value.Days.Single(x => x.DateIso == "2026-08-12").Limit);
    }

    [Fact]
    public async Task UpdateDateCapacity_CannotReduceBelowBaseLimit()
    {
        var result = await _service.UpdateDateCapacityAsync(
            new UpdateDateCapacityCommand(new DateOnly(2026, 8, 11), 30),
            CancellationToken.None);

        Assert.True(result.IsFailure);
        Assert.Equal("capacity.cannot_reduce", result.Error.Code);
    }

    [Fact]
    public async Task AddException_HolidayWhenConfirmedMealsExist_FailsWithMealCountMessage()
    {
        // Arrange - add confirmed subscription day for Aug 10
        _db.SubscriptionDays.Add(new SubscriptionDay
        {
            RestaurantId = _testRestaurantId,
            ServiceDate = new DateOnly(2026, 8, 10),
            Status = SubscriptionDayStatus.ConfirmedByRestaurant
        });
        await _db.SaveChangesAsync();

        // Act - try to mark Aug 10 as holiday
        var result = await _service.AddWorkingDayExceptionAsync(
            new AddWorkingDayExceptionCommand(new DateOnly(2026, 8, 10), false, "Maintenance"),
            CancellationToken.None);

        // Assert - rejected with message mentioning confirmed meals
        Assert.True(result.IsFailure);
        Assert.Contains("confirmed meals", result.Error.Description, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task RestaurantIsolation_NoContext_ReturnsUnauthorizedFailure()
    {
        // Arrange - null restaurant context
        var unauthorizedUser = Substitute.For<ICurrentUser>();
        unauthorizedUser.RestaurantId.Returns((Guid?)null);
        var unauthService = new RestaurantCapacityService(_db, unauthorizedUser, _clock);

        // Act
        var result = await unauthService.GetCapacityOverviewAsync(
            new GetRestaurantCapacityOverviewQuery(), CancellationToken.None);

        // Assert
        Assert.True(result.IsFailure);
        Assert.Equal(ErrorType.Unauthorized, result.Error.Type);
    }
}
