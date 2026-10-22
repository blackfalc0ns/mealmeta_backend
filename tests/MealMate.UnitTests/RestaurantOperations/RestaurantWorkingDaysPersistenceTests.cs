using MealMate.Application.Common.Abstractions;
using MealMate.Application.Features.RestaurantOperations;
using MealMate.Domain.CalendarMeals;
using MealMate.Domain.Enums;
using MealMate.Domain.IdentityAccess;
using MealMate.Domain.RestaurantOperations;
using Microsoft.EntityFrameworkCore;
using NSubstitute;
using Xunit;

namespace MealMate.UnitTests.RestaurantOperations;

public sealed class RestaurantWorkingDaysPersistenceTests
{
    private readonly DbContextOptions<Persistence.AppDbContext> options = new DbContextOptionsBuilder<Persistence.AppDbContext>().UseInMemoryDatabase(Guid.NewGuid().ToString()).Options;
    private readonly ICurrentUser owner = Substitute.For<ICurrentUser>();
    private readonly IDateTimeProvider clock = Substitute.For<IDateTimeProvider>();
    private readonly Guid restaurantId = Guid.NewGuid();
    private Persistence.AppDbContext OpenDb() => new(options, Substitute.For<IDomainEventDispatcher>());

    public RestaurantWorkingDaysPersistenceTests()
    {
        owner.IsAuthenticated.Returns(true); owner.RestaurantId.Returns(restaurantId); owner.UserType.Returns(UserType.Restaurant);
        clock.UtcNow.Returns(new DateTime(2026, 10, 22, 9, 0, 0, DateTimeKind.Utc));
        using var db = OpenDb();
        db.RestaurantProfiles.Add(new RestaurantProfile { Id = restaurantId, UserId = Guid.NewGuid(), LegalNameAr = "مطعم", LegalNameEn = "Restaurant", TradeNameAr = "مطعم", TradeNameEn = "Restaurant", VerificationStatus = VerificationStatus.Approved });
        db.SaveChanges();
    }

    [Fact]
    public async Task ApprovedOwner_WeeklyChangeSurvivesNewContextWithoutReview()
    {
        using (var db = OpenDb())
        {
            var result = await new RestaurantCapacityService(db, owner, clock).UpdateWorkingDaysScheduleAsync(new(new() { new(5, false) }), default);
            Assert.True(result.IsSuccess, result.IsFailure ? result.Error.Code : null);
            Assert.Empty(db.RestaurantSettingsChangeRequests);
        }
        using var reloaded = OpenDb();
        var read = await new RestaurantCapacityService(reloaded, owner, clock).GetWorkingDaysScheduleAsync(new(), default);
        Assert.False(read.Value.WeeklyDays.Single(x => x.DayOfWeek == 5).IsWorkingDay);
        Assert.Single(reloaded.RestaurantOperatingSchedules);
    }

    [Fact]
    public async Task Exception_CreateUpdateAndDeletePersistWithoutDuplicates()
    {
        var date = new DateOnly(2026, 10, 23);
        Guid id;
        using (var db = OpenDb())
        {
            var service = new RestaurantCapacityService(db, owner, clock);
            var created = await service.AddWorkingDayExceptionAsync(new(date, false, "Closure"), default);
            Assert.True(created.IsSuccess); id = created.Value.Id;
            var updated = await service.AddWorkingDayExceptionAsync(new(date, true, "Open"), default);
            Assert.True(updated.IsSuccess); Assert.Equal(id, updated.Value.Id);
        }
        using (var db = OpenDb())
        {
            var service = new RestaurantCapacityService(db, owner, clock);
            var read = await service.GetWorkingDaysScheduleAsync(new(), default);
            var saved = Assert.Single(read.Value.Exceptions);
            Assert.Equal(id, saved.Id); Assert.True(saved.IsWorkingDay); Assert.Equal("Open", saved.Reason);
            Assert.True((await service.DeleteWorkingDayExceptionAsync(new(date), default)).IsSuccess);
        }
        using var reloaded = OpenDb();
        Assert.Empty(reloaded.RestaurantOperatingDayExceptions);
    }

    [Fact]
    public async Task OwnerCannotChangeAnotherRestaurantsScheduleOrExceptions()
    {
        using var db = OpenDb();
        var service = new RestaurantCapacityService(db, owner, clock);
        var other = Guid.NewGuid();
        Assert.True((await service.UpdateWorkingDaysScheduleAsync(new(new() { new(0, false) }, other), default)).IsFailure);
        Assert.True((await service.AddWorkingDayExceptionAsync(new(new(2026, 10, 23), false, null, other), default)).IsFailure);
        Assert.Empty(db.RestaurantOperatingSchedules); Assert.Empty(db.RestaurantOperatingDayExceptions);
    }

    [Fact]
    public async Task HolidayWithConfirmedMealsStillFailsWithoutPersisting()
    {
        using var db = OpenDb();
        var date = new DateOnly(2026, 10, 23);
        db.SubscriptionDays.Add(new SubscriptionDay { RestaurantId = restaurantId, ServiceDate = date, Status = SubscriptionDayStatus.ConfirmedByRestaurant });
        await db.SaveChangesAsync();
        var result = await new RestaurantCapacityService(db, owner, clock).AddWorkingDayExceptionAsync(new(date, false), default);
        Assert.True(result.IsFailure); Assert.Equal("working_days.confirmed_meals_exist", result.Error.Code);
        Assert.Empty(db.RestaurantOperatingDayExceptions);
    }
}
