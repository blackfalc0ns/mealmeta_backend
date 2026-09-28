using System.Text.Json;
using FluentAssertions;
using MealMate.Application.Common.Abstractions;
using MealMate.Application.Common.Models;
using MealMate.Application.Features.CustomerCalendar;
using MealMate.Application.Features.CustomerCheckout.BackgroundJobs;
using MealMate.Application.Features.ProgramsSubscriptions;
using MealMate.Domain.CalendarMeals;
using MealMate.Domain.Common;
using MealMate.Domain.Enums;
using MealMate.Domain.IdentityAccess;
using MealMate.Domain.ProgramsSubscriptions;
using MealMate.Domain.RestaurantOperations;
using MealMate.Domain.ValueObjects;
using MealMate.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using NSubstitute;
using Xunit;

namespace MealMate.UnitTests.CustomerCheckout;

public class SubscriptionDayLockJobTests
{
    private static readonly DateTime Now = new(2026, 9, 28, 12, 0, 0, DateTimeKind.Utc);
    private static readonly DateOnly TargetDate = new(2026, 9, 29);

    private static AppDbContext CreateDb()
    {
        var options = new DbContextOptionsBuilder<AppDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString())
            .Options;
        return new AppDbContext(options, Substitute.For<IDomainEventDispatcher>());
    }

    [Fact]
    public async Task ProcessDueDays_WhenNoSafeMealsFound_MarksReplacementRequired_AndCreatesOneEscalationOnly()
    {
        // Arrange
        await using var db = CreateDb();

        var customerId = Guid.NewGuid();
        var restaurantId = Guid.NewGuid();

        var restaurant = new RestaurantProfile
        {
            TradeNameAr = "مطعم أصيل",
            TradeNameEn = "Aseel Restaurant",
            LegalNameAr = "شركة أصيل",
            LegalNameEn = "Aseel Company"
        };
        db.RestaurantProfiles.Add(restaurant);

        var program = new Program
        {
            Code = "BALANCED",
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

        // Customer has PEANUTS allergy
        db.CustomerAllergyPreferences.Add(new CustomerAllergyPreference
        {
            CustomerUserId = customerId,
            AllergenCode = "PEANUTS",
            AllergenNameAr = "فول سوداني",
            AllergenNameEn = "Peanuts",
            IsDislike = false
        });

        // Day past deadline
        var day = new SubscriptionDay
        {
            SubscriptionId = subscription.Id,
            ServiceDate = TargetDate,
            RestaurantId = restaurant.Id,
            Status = SubscriptionDayStatus.Open,
            SelectionDeadlineUtc = Now.AddHours(-1)
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
        currentUser.UserId.Returns(customerId);
        currentUser.UserType.Returns(UserType.Customer);

        var clock = Substitute.For<IDateTimeProvider>();
        clock.UtcNow.Returns(Now);
        clock.TodayUtc.Returns(DateOnly.FromDateTime(Now));

        var calendarEngine = new CustomerCalendarEngine(db, currentUser, clock, new RestaurantDiversificationCalculator());

        var serviceProvider = Substitute.For<IServiceProvider>();
        serviceProvider.GetService(typeof(IAppDbContext)).Returns(db);
        serviceProvider.GetService(typeof(IDateTimeProvider)).Returns(clock);
        serviceProvider.GetService(typeof(CustomerCalendarEngine)).Returns(calendarEngine);

        var scope = Substitute.For<IServiceScope>();
        scope.ServiceProvider.Returns(serviceProvider);

        var scopeFactory = Substitute.For<IServiceScopeFactory>();
        scopeFactory.CreateScope().Returns(scope);

        var logger = Substitute.For<ILogger<SubscriptionDayLockJob>>();
        var job = new SubscriptionDayLockJob(scopeFactory, logger);

        // Act 1: Run First Time
        var result1 = await job.ProcessDueDaysAsync(serviceProvider, CancellationToken.None);

        // Assert 1: Failure is surfaced, not counted as processed successfully
        result1.DueCount.Should().Be(1);
        result1.SucceededCount.Should().Be(0);
        result1.FailedCount.Should().Be(1);
        result1.EscalatedCount.Should().Be(1);

        var reloadedDay = await db.SubscriptionDays.FindAsync(day.Id);
        reloadedDay.Should().NotBeNull();
        reloadedDay!.Status.Should().Be(SubscriptionDayStatus.ReplacementRequired);
        reloadedDay.LockAtUtc.Should().BeNull();

        var outboxMessages = await db.OutboxMessages.ToListAsync();
        outboxMessages.Should().ContainSingle(m => m.EventType == "CustomerReplacementRequired");

        // Act 2: Run Second Time (idempotency check: should not create duplicate escalation)
        var result2 = await job.ProcessDueDaysAsync(serviceProvider, CancellationToken.None);
        result2.EscalatedCount.Should().Be(0, "escalation was already recorded for this day");

        var outboxMessagesAfter = await db.OutboxMessages.ToListAsync();
        outboxMessagesAfter.Should().ContainSingle(m => m.EventType == "CustomerReplacementRequired");
    }

    [Fact]
    public async Task ProcessDueDays_WhenSafeMealsExist_LocksSuccessfully_AndCountsSucceeded()
    {
        // Arrange
        await using var db = CreateDb();

        var customerId = Guid.NewGuid();
        var restaurantId = Guid.NewGuid();

        var restaurant = new RestaurantProfile
        {
            TradeNameAr = "مطعم أصيل",
            TradeNameEn = "Aseel Restaurant",
            LegalNameAr = "شركة أصيل",
            LegalNameEn = "Aseel Company"
        };
        db.RestaurantProfiles.Add(restaurant);

        var program = new Program
        {
            Code = "BALANCED",
            NameAr = "برنامج متوازن",
            NameEn = "Balanced Program",
            Status = ProgramStatus.Active,
            IsActive = true
        };
        var bundle = new Bundle
        {
            ProgramId = program.Id,
            Code = "1M",
            NameAr = "وجبة واحدة",
            NameEn = "1 meal",
            MealsPerDay = 1,
            BreakfastCount = 0,
            LunchCount = 1,
            DinnerCount = 0,
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

        // Day past deadline
        var day = new SubscriptionDay
        {
            SubscriptionId = subscription.Id,
            ServiceDate = TargetDate,
            RestaurantId = restaurant.Id,
            Status = SubscriptionDayStatus.Open,
            SelectionDeadlineUtc = Now.AddHours(-1)
        };

        var safeMeal = new
        {
            mealId = Guid.NewGuid(),
            nameAr = "وجبة صحية آمنة",
            nameEn = "Safe Healthy Meal",
            descriptionAr = "وجبة صحية آمنة",
            descriptionEn = "Safe Healthy Meal",
            slot = "lunch",
            sortOrder = 1,
            imageUrl = (string?)null,
            calories = 400,
            proteinG = 30,
            carbsG = 20,
            fatG = 10,
            presentAllergenCodes = Array.Empty<string>(),
            mayContainAllergenCodes = Array.Empty<string>(),
            dietaryPreferenceCodes = Array.Empty<string>(),
            ingredients = Array.Empty<object>()
        };

        var restDaySnap = new SubscriptionRestaurantDaySnapshot
        {
            RestaurantSnapshotId = snap.Id,
            SubscriptionId = subscription.Id,
            RestaurantId = restaurant.Id,
            ServiceDate = TargetDate,
            MenuPlanVersionId = Guid.NewGuid(),
            SlotRequirementsJson = "[{\"slot\":\"lunch\",\"count\":1}]",
            MealsJson = JsonSerializer.Serialize(new[] { safeMeal }),
            ContentHash = "hash",
            CapturedAtUtc = Now
        };

        db.AddRange(program, bundle, subscription, pool, snap, restDaySnap, day);
        await db.SaveChangesAsync();

        var currentUser = Substitute.For<ICurrentUser>();
        currentUser.IsAuthenticated.Returns(true);
        currentUser.UserId.Returns(customerId);
        currentUser.UserType.Returns(UserType.Customer);

        var clock = Substitute.For<IDateTimeProvider>();
        clock.UtcNow.Returns(Now);
        clock.TodayUtc.Returns(DateOnly.FromDateTime(Now));

        var calendarEngine = new CustomerCalendarEngine(db, currentUser, clock, new RestaurantDiversificationCalculator());

        var serviceProvider = Substitute.For<IServiceProvider>();
        serviceProvider.GetService(typeof(IAppDbContext)).Returns(db);
        serviceProvider.GetService(typeof(IDateTimeProvider)).Returns(clock);
        serviceProvider.GetService(typeof(CustomerCalendarEngine)).Returns(calendarEngine);

        var scope = Substitute.For<IServiceScope>();
        scope.ServiceProvider.Returns(serviceProvider);

        var scopeFactory = Substitute.For<IServiceScopeFactory>();
        scopeFactory.CreateScope().Returns(scope);

        var logger = Substitute.For<ILogger<SubscriptionDayLockJob>>();
        var job = new SubscriptionDayLockJob(scopeFactory, logger);

        // Act
        var result = await job.ProcessDueDaysAsync(serviceProvider, CancellationToken.None);

        // Assert
        result.DueCount.Should().Be(1);
        result.SucceededCount.Should().Be(1);
        result.FailedCount.Should().Be(0);
        result.EscalatedCount.Should().Be(0);

        var reloadedDay = await db.SubscriptionDays.FindAsync(day.Id);
        reloadedDay.Should().NotBeNull();
        reloadedDay!.Status.Should().Be(SubscriptionDayStatus.SentToRestaurant);
    }
}
