using System.Text.Json;
using FluentAssertions;
using MealMate.Application.Common.Abstractions;
using MealMate.Application.Common.Models;
using MealMate.Application.Features.CustomerCalendar;
using MealMate.Application.Features.ProgramsSubscriptions;
using MealMate.Application.Features.RestaurantOrders;
using MealMate.Domain.CalendarMeals;
using MealMate.Domain.Common;
using MealMate.Domain.Enums;
using MealMate.Domain.IdentityAccess;
using MealMate.Domain.ProgramsSubscriptions;
using MealMate.Domain.RestaurantOperations;
using MealMate.Domain.ValueObjects;
using MealMate.Persistence;
using Microsoft.EntityFrameworkCore;
using NSubstitute;
using Xunit;

namespace MealMate.UnitTests.RestaurantOrders;

public class DispatchDailyOrdersTests
{
    private static readonly DateTime Now = new(2026, 9, 28, 10, 0, 0, DateTimeKind.Utc);
    private static readonly DateOnly TargetDate = new(2026, 9, 28);

    private static AppDbContext CreateDb()
    {
        var options = new DbContextOptionsBuilder<AppDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString())
            .Options;
        return new AppDbContext(options, Substitute.For<IDomainEventDispatcher>());
    }

    [Fact]
    public async Task DispatchOrders_WhenAutoSelectionFails_DoesNotAssignRandomMeal_AndMarksReplacementRequired()
    {
        // Arrange
        await using var db = CreateDb();

        var customerId = Guid.NewGuid();

        var restaurant = new RestaurantProfile
        {
            TradeNameAr = "مطعم أصيل",
            TradeNameEn = "Aseel Restaurant",
            LegalNameAr = "شركة أصيل",
            LegalNameEn = "Aseel Company"
        };
        db.RestaurantProfiles.Add(restaurant);

        // Also add a meal in db for this restaurant, which the buggy code would erroneously pick as fallback!
        var fallbackMeal = new Meal
        {
            RestaurantId = restaurant.Id,
            Code = "FB-001",
            NameAr = "وجبة عشوائية ممنوعة",
            NameEn = "Forbidden Random Meal",
            IsDeleted = false
        };
        db.Meals.Add(fallbackMeal);

        var program = new Program
        {
            Code = "PROG-1",
            NameAr = "برنامج متوازن",
            NameEn = "Balanced Program",
            Status = ProgramStatus.Active,
            IsActive = true
        };
        var bundle = new Bundle
        {
            ProgramId = program.Id,
            Code = "3M",
            NameAr = "3 وجبات",
            NameEn = "3 meals",
            MealsPerDay = 3,
            BreakfastCount = 1,
            LunchCount = 1,
            DinnerCount = 1,
            SnackCount = 0,
            Status = ProgramStatus.Active,
            IsActive = true
        };

        var subscription = new Subscription
        {
            CustomerUserId = customerId,
            ProgramId = program.Id,
            BundleId = bundle.Id,
            SelectedTier = RestaurantTier.Platinum,
            DurationDays = 7,
            IncludeFriday = false,
            StartDate = TargetDate,
            EndDate = TargetDate.AddDays(7),
            Status = SubscriptionStatus.Active,
            PaidAmount = new Money(500m, "SAR"),
            FulfillmentCompletedAtUtc = Now
        };

        var pool = new SubscriptionRestaurantPoolSnapshot
        {
            SubscriptionId = subscription.Id,
            RestaurantCount = 1,
            EligibilityCheckedAtUtc = Now
        };

        var snap = new SubscriptionRestaurantSnapshot
        {
            PoolSnapshotId = pool.Id,
            SubscriptionId = subscription.Id,
            RestaurantId = restaurant.Id,
            ProgramId = program.Id,
            BundleId = bundle.Id,
            RegionId = Guid.NewGuid(),
            Tier = RestaurantTier.Platinum,
            NameAr = "مطعم أصيل",
            NameEn = "Aseel Restaurant",
            IsInitiallySelected = true,
            OfferingSnapshotJson = "{}",
            CapturedAtUtc = Now
        };

        // Day with allergy conflict: restaurant day has PEANUTS allergen, customer is allergic to peanuts!
        db.CustomerAllergyPreferences.Add(new CustomerAllergyPreference
        {
            CustomerUserId = customerId,
            AllergenCode = "PEANUTS",
            AllergenNameAr = "فول سوداني",
            AllergenNameEn = "Peanuts",
            IsDislike = false
        });

        var day = new SubscriptionDay
        {
            SubscriptionId = subscription.Id,
            ServiceDate = TargetDate,
            RestaurantId = restaurant.Id,
            Status = SubscriptionDayStatus.Open
        };

        var peanutMeal = new
        {
            mealId = Guid.NewGuid(),
            nameAr = "وجبة فول سوداني",
            nameEn = "Peanut Meal",
            descriptionAr = "وجبة فول سوداني",
            descriptionEn = "Peanut Meal",
            slot = "lunch",
            sortOrder = 1,
            imageUrl = (string?)null,
            calories = 400,
            proteinG = 30,
            carbsG = 20,
            fatG = 10,
            presentAllergenCodes = new[] { "PEANUTS" },
            mayContainAllergenCodes = Array.Empty<string>(),
            dietaryPreferenceCodes = Array.Empty<string>(),
            ingredients = Array.Empty<object>()
        };

        // Restaurant Day offering only meals containing PEANUTS allergen
        var restDaySnap = new SubscriptionRestaurantDaySnapshot
        {
            RestaurantSnapshotId = snap.Id,
            SubscriptionId = subscription.Id,
            RestaurantId = restaurant.Id,
            ServiceDate = TargetDate,
            MenuPlanVersionId = Guid.NewGuid(),
            SlotRequirementsJson = "[{\"slot\":\"breakfast\",\"count\":1},{\"slot\":\"lunch\",\"count\":1},{\"slot\":\"dinner\",\"count\":1}]",
            MealsJson = JsonSerializer.Serialize(new[] { peanutMeal }),
            ContentHash = "hash",
            CapturedAtUtc = Now
        };

        db.AddRange(program, bundle, subscription, pool, snap, restDaySnap, day);
        await db.SaveChangesAsync();

        var currentUser = Substitute.For<ICurrentUser>();
        currentUser.IsAuthenticated.Returns(true);
        currentUser.RestaurantId.Returns(restaurant.Id);
        currentUser.UserId.Returns(Guid.NewGuid());
        currentUser.UserType.Returns(UserType.Restaurant);

        var clock = Substitute.For<IDateTimeProvider>();
        clock.UtcNow.Returns(Now);
        clock.TodayUtc.Returns(TargetDate);

        var calendarEngine = new CustomerCalendarEngine(db, currentUser, clock, new RestaurantDiversificationCalculator());
        var dispatchService = Substitute.For<ISubscriptionDayDispatchService>();
        var service = new RestaurantOrderService(db, currentUser, clock);

        var command = new DispatchRestaurantDailyOrdersCommand(
            RestaurantId: restaurant.Id,
            TargetDate: TargetDate,
            AutoSelectIfEmpty: true);

        // Act
        var result = await service.DispatchDailyOrdersAsync(command, dispatchService, calendarEngine, CancellationToken.None);

        // Assert
        result.IsSuccess.Should().BeTrue();
        result.Value.Should().Be(0, "the day has an allergy conflict and cannot be dispatched");

        // 1. Zero fallback meal selections introduced
        var selections = await db.DailyMealSelections.Where(s => s.SubscriptionDayId == day.Id).ToListAsync();
        selections.Should().BeEmpty("no fallback or random meal may ever be assigned");

        // 2. Day transitions to ReplacementRequired
        var reloadedDay = await db.SubscriptionDays.FindAsync(day.Id);
        reloadedDay.Should().NotBeNull();
        reloadedDay!.Status.Should().Be(SubscriptionDayStatus.ReplacementRequired);
        reloadedDay.LockAtUtc.Should().BeNull();

        // 3. No RestaurantDailyOrder is created for this failed day
        var orders = await db.RestaurantDailyOrders.Where(o => o.SubscriptionDayId == day.Id).ToListAsync();
        orders.Should().BeEmpty();
    }
}
