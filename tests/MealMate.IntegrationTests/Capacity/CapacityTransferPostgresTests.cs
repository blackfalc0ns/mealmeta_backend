using FluentAssertions;
using MealMate.Application.Common.Abstractions;
using MealMate.Application.Features.CustomerCalendar;
using MealMate.Application.Features.ProgramsSubscriptions;
using MealMate.Domain.CalendarMeals;
using MealMate.Domain.Common;
using MealMate.Domain.Enums;
using MealMate.Domain.ValueObjects;
using MealMate.Domain.ProgramsSubscriptions;
using MealMate.Domain.RestaurantOperations;
using MealMate.Persistence;
using MealMate.Persistence.Common;
using Microsoft.EntityFrameworkCore;
using NSubstitute;
using Testcontainers.PostgreSql;
using Xunit;
using Xunit.Sdk;

namespace MealMate.IntegrationTests.Capacity;

public sealed class CapacityTransferPostgresTests : IAsyncLifetime
{
    private PostgreSqlContainer _container = null!;

    public async Task InitializeAsync()
    {
        try
        {
            _container = new PostgreSqlBuilder()
                .WithImage("postgres:16-alpine")
                .Build();
            await _container.StartAsync();
            await using var db = CreateDb();
            await db.Database.MigrateAsync();
        }
        catch (Exception ex)
        {
            throw new XunitException("Docker is required for CapacityTransferPostgresTests.", ex);
        }
    }

    public async Task DisposeAsync() => await _container.DisposeAsync();

    private AppDbContext CreateDb()
    {
        var options = new DbContextOptionsBuilder<AppDbContext>()
            .UseNpgsql(_container.GetConnectionString())
            .Options;
        return new AppDbContext(options, Substitute.For<IDomainEventDispatcher>());
    }

    [Fact]
    public async Task SelectRestaurant_WhenNewRestaurantFull_AbortsWithoutWipingSelections()
    {
        var customerId = Guid.NewGuid();
        var oldRestId = Guid.NewGuid();
        var newRestId = Guid.NewGuid();
        var date = DateOnly.FromDateTime(DateTime.UtcNow.AddDays(5));

        Guid subscriptionId;
        Guid dayId;
        Guid mealId = Guid.NewGuid();

        await using (var db = CreateDb())
        {
            var program = new Program
            {
                NameAr = "برنامج متوازن",
                NameEn = "Balanced Program",
                Code = "BALANCED",
                IsActive = true
            };
            db.Programs.Add(program);

            var bundle = new Bundle
            {
                ProgramId = program.Id,
                NameAr = "باقة الغداء",
                NameEn = "Lunch Bundle",
                Code = "LUNCH-1",
                LunchCount = 1,
                IsActive = true
            };
            db.Bundles.Add(bundle);

            var subscription = new Subscription
            {
                CustomerUserId = customerId,
                ProgramId = program.Id,
                BundleId = bundle.Id,
                StartDate = date,
                EndDate = date.AddDays(30),
                Status = SubscriptionStatus.Active,
                DurationDays = 30,
                PaidAmount = new Money(50, "KWD"),
                FulfillmentCompletedAtUtc = DateTime.UtcNow
            };
            subscriptionId = subscription.Id;
            db.Subscriptions.Add(subscription);

            var pool = new SubscriptionRestaurantPoolSnapshot
            {
                SubscriptionId = subscriptionId,
                EligibilityCheckedAtUtc = DateTime.UtcNow,
                RestaurantCount = 2,
                IsBackfilled = false
            };
            db.SubscriptionRestaurantPoolSnapshots.Add(pool);

            var snapOld = new SubscriptionRestaurantSnapshot
            {
                PoolSnapshotId = pool.Id,
                SubscriptionId = subscriptionId,
                RestaurantId = oldRestId,
                ProgramId = program.Id,
                BundleId = bundle.Id,
                RegionId = Guid.NewGuid(),
                Tier = RestaurantTier.Basic,
                NameAr = "مطعم قديم",
                NameEn = "Old Restaurant",
                IsInitiallySelected = true,
                OfferingSnapshotJson = "{}",
                CapturedAtUtc = DateTime.UtcNow
            };
            var snapNew = new SubscriptionRestaurantSnapshot
            {
                PoolSnapshotId = pool.Id,
                SubscriptionId = subscriptionId,
                RestaurantId = newRestId,
                ProgramId = program.Id,
                BundleId = bundle.Id,
                RegionId = Guid.NewGuid(),
                Tier = RestaurantTier.Basic,
                NameAr = "مطعم جديد",
                NameEn = "New Restaurant",
                IsInitiallySelected = false,
                OfferingSnapshotJson = "{}",
                CapturedAtUtc = DateTime.UtcNow
            };
            db.SubscriptionRestaurantSnapshots.AddRange(snapOld, snapNew);

            var versionId = Guid.NewGuid();
            var mealsJson = $$"""[{"mealId":"{{mealId}}","nameAr":"وجبة","nameEn":"Meal","slot":"lunch","sortOrder":1,"imageUrl":null,"calories":500,"proteinG":30,"carbsG":40,"fatG":15,"descriptionAr":null,"descriptionEn":null,"presentAllergenCodes":[],"mayContainAllergenCodes":[],"dietaryPreferenceCodes":[],"ingredients":[]}]""";
            var daySnapOld = new SubscriptionRestaurantDaySnapshot
            {
                RestaurantSnapshotId = snapOld.Id,
                SubscriptionId = subscriptionId,
                RestaurantId = oldRestId,
                ServiceDate = date,
                MenuPlanVersionId = versionId,
                SlotRequirementsJson = """[{"slot":"lunch","count":1}]""",
                MealsJson = mealsJson,
                ContentHash = "hash-old",
                CapturedAtUtc = DateTime.UtcNow
            };
            var daySnapNew = new SubscriptionRestaurantDaySnapshot
            {
                RestaurantSnapshotId = snapNew.Id,
                SubscriptionId = subscriptionId,
                RestaurantId = newRestId,
                ServiceDate = date,
                MenuPlanVersionId = versionId,
                SlotRequirementsJson = """[{"slot":"lunch","count":1}]""",
                MealsJson = mealsJson,
                ContentHash = "hash-new",
                CapturedAtUtc = DateTime.UtcNow
            };
            db.SubscriptionRestaurantDaySnapshots.AddRange(daySnapOld, daySnapNew);

            var day = new SubscriptionDay
            {
                SubscriptionId = subscriptionId,
                ServiceDate = date,
                RestaurantId = oldRestId,
                Status = SubscriptionDayStatus.Open,
                IsAutoSelected = false
            };
            dayId = day.Id;
            db.SubscriptionDays.Add(day);

            var selection = new DailyMealSelection
            {
                SubscriptionDayId = dayId,
                MealId = mealId,
                SortOrder = 1,
                SelectedAtUtc = DateTime.UtcNow
            };
            db.DailyMealSelections.Add(selection);

            // Hold for old restaurant
            var oldHold = new CapacityHold
            {
                RestaurantId = oldRestId,
                SubscriptionId = subscriptionId,
                MenuPlanVersionId = versionId,
                ServiceDate = date,
                Quantity = 1,
                Status = CapacityHoldStatus.Confirmed,
                ConfirmedAtUtc = DateTime.UtcNow
            };
            db.CapacityHolds.Add(oldHold);

            // New restaurant capacity: MaxBoxes = 1
            db.DailyCapacities.Add(new DailyCapacity
            {
                RestaurantId = newRestId,
                ServiceDate = date,
                CapacityType = CapacityType.DateOverride,
                MaxBoxes = 1,
                ReservedBoxes = 1
            });

            // Another customer holds the only 1 box for new restaurant
            db.CapacityHolds.Add(new CapacityHold
            {
                RestaurantId = newRestId,
                SubscriptionId = Guid.NewGuid(),
                MenuPlanVersionId = versionId,
                ServiceDate = date,
                Quantity = 1,
                Status = CapacityHoldStatus.Confirmed,
                ConfirmedAtUtc = DateTime.UtcNow
            });

            await db.SaveChangesAsync();
        }

        await using var engineDb = CreateDb();
        var user = Substitute.For<ICurrentUser>();
        user.UserId.Returns(customerId);
        user.UserType.Returns(UserType.Customer);

        var clock = Substitute.For<IDateTimeProvider>();
        clock.UtcNow.Returns(DateTime.UtcNow);
        clock.TodayUtc.Returns(DateOnly.FromDateTime(DateTime.UtcNow));

        var lockManager = new PostgresApplicationLockManager(engineDb);
        var txExecutor = new PostgresApplicationTransactionExecutor(engineDb);
        var transferService = new CapacityTransferService(engineDb, lockManager, txExecutor, clock);

        var engine = new CustomerCalendarEngine(
            engineDb,
            user,
            clock,
            new RestaurantDiversificationCalculator(),
            capacityTransferService: transferService);

        var result = await engine.SelectRestaurantAsync(subscriptionId, date, newRestId, CancellationToken.None);

        result.IsFailure.Should().BeTrue();
        result.Error.Code.Should().Be("RESTAURANT_CAPACITY_FULL");

        // Verify day.RestaurantId remains oldRestId and selections were not wiped
        await using var verifyDb = CreateDb();
        var reloadedDay = await verifyDb.SubscriptionDays.FindAsync(dayId);
        reloadedDay.Should().NotBeNull();
        reloadedDay!.RestaurantId.Should().Be(oldRestId);

        var remainingSelections = await verifyDb.DailyMealSelections.Where(s => s.SubscriptionDayId == dayId).ToListAsync();
        remainingSelections.Should().HaveCount(1, "Selections must not be wiped when restaurant change fails capacity check.");
    }

    [Fact]
    public async Task SelectRestaurant_WhenCapacityAvailable_TransfersHoldAndUpdatesDay()
    {
        var customerId = Guid.NewGuid();
        var oldRestId = Guid.NewGuid();
        var newRestId = Guid.NewGuid();
        var date = DateOnly.FromDateTime(DateTime.UtcNow.AddDays(6));

        Guid subscriptionId;
        Guid dayId;
        Guid mealId = Guid.NewGuid();

        await using (var db = CreateDb())
        {
            var program = new Program { NameAr = "برنامج متوازن", NameEn = "Balanced", Code = "BAL-2", IsActive = true };
            db.Programs.Add(program);
            var bundle = new Bundle { ProgramId = program.Id, NameAr = "باقة", NameEn = "Bundle", Code = "BUN-2", LunchCount = 1, IsActive = true };
            db.Bundles.Add(bundle);

            var subscription = new Subscription
            {
                CustomerUserId = customerId,
                ProgramId = program.Id,
                BundleId = bundle.Id,
                StartDate = date,
                EndDate = date.AddDays(30),
                Status = SubscriptionStatus.Active,
                DurationDays = 30,
                PaidAmount = new Money(50, "KWD"),
                FulfillmentCompletedAtUtc = DateTime.UtcNow
            };
            subscriptionId = subscription.Id;
            db.Subscriptions.Add(subscription);

            var pool = new SubscriptionRestaurantPoolSnapshot
            {
                SubscriptionId = subscriptionId,
                EligibilityCheckedAtUtc = DateTime.UtcNow,
                RestaurantCount = 2,
                IsBackfilled = false
            };
            db.SubscriptionRestaurantPoolSnapshots.Add(pool);

            var snapOld = new SubscriptionRestaurantSnapshot
            {
                PoolSnapshotId = pool.Id,
                SubscriptionId = subscriptionId,
                RestaurantId = oldRestId,
                ProgramId = program.Id,
                BundleId = bundle.Id,
                RegionId = Guid.NewGuid(),
                Tier = RestaurantTier.Basic,
                NameAr = "مطعم 1",
                NameEn = "Rest 1",
                IsInitiallySelected = true,
                OfferingSnapshotJson = "{}",
                CapturedAtUtc = DateTime.UtcNow
            };
            var snapNew = new SubscriptionRestaurantSnapshot
            {
                PoolSnapshotId = pool.Id,
                SubscriptionId = subscriptionId,
                RestaurantId = newRestId,
                ProgramId = program.Id,
                BundleId = bundle.Id,
                RegionId = Guid.NewGuid(),
                Tier = RestaurantTier.Basic,
                NameAr = "مطعم 2",
                NameEn = "Rest 2",
                IsInitiallySelected = false,
                OfferingSnapshotJson = "{}",
                CapturedAtUtc = DateTime.UtcNow
            };
            db.SubscriptionRestaurantSnapshots.AddRange(snapOld, snapNew);

            var versionId = Guid.NewGuid();
            var mealsJson = $$"""[{"mealId":"{{mealId}}","nameAr":"وجبة","nameEn":"Meal","slot":"lunch","sortOrder":1,"imageUrl":null,"calories":500,"proteinG":30,"carbsG":40,"fatG":15,"descriptionAr":null,"descriptionEn":null,"presentAllergenCodes":[],"mayContainAllergenCodes":[],"dietaryPreferenceCodes":[],"ingredients":[]}]""";
            var daySnapOld = new SubscriptionRestaurantDaySnapshot
            {
                RestaurantSnapshotId = snapOld.Id,
                SubscriptionId = subscriptionId,
                RestaurantId = oldRestId,
                ServiceDate = date,
                MenuPlanVersionId = versionId,
                SlotRequirementsJson = """[{"slot":"lunch","count":1}]""",
                MealsJson = mealsJson,
                ContentHash = "hash-old-2",
                CapturedAtUtc = DateTime.UtcNow
            };
            var daySnapNew = new SubscriptionRestaurantDaySnapshot
            {
                RestaurantSnapshotId = snapNew.Id,
                SubscriptionId = subscriptionId,
                RestaurantId = newRestId,
                ServiceDate = date,
                MenuPlanVersionId = versionId,
                SlotRequirementsJson = """[{"slot":"lunch","count":1}]""",
                MealsJson = mealsJson,
                ContentHash = "hash-new-2",
                CapturedAtUtc = DateTime.UtcNow
            };
            db.SubscriptionRestaurantDaySnapshots.AddRange(daySnapOld, daySnapNew);

            var day = new SubscriptionDay
            {
                SubscriptionId = subscriptionId,
                ServiceDate = date,
                RestaurantId = oldRestId,
                Status = SubscriptionDayStatus.Open,
                IsAutoSelected = false
            };
            dayId = day.Id;
            db.SubscriptionDays.Add(day);

            var selection = new DailyMealSelection
            {
                SubscriptionDayId = dayId,
                MealId = mealId,
                SortOrder = 1,
                SelectedAtUtc = DateTime.UtcNow
            };
            db.DailyMealSelections.Add(selection);

            // Hold for old restaurant
            db.CapacityHolds.Add(new CapacityHold
            {
                RestaurantId = oldRestId,
                SubscriptionId = subscriptionId,
                MenuPlanVersionId = versionId,
                ServiceDate = date,
                Quantity = 1,
                Status = CapacityHoldStatus.Confirmed,
                ConfirmedAtUtc = DateTime.UtcNow
            });

            // New restaurant capacity has plenty of room: MaxBoxes = 50
            db.DailyCapacities.Add(new DailyCapacity
            {
                RestaurantId = newRestId,
                ServiceDate = date,
                CapacityType = CapacityType.Base,
                MaxBoxes = 50,
                ReservedBoxes = 0
            });

            await db.SaveChangesAsync();
        }

        await using var engineDb = CreateDb();
        var user = Substitute.For<ICurrentUser>();
        user.UserId.Returns(customerId);
        user.UserType.Returns(UserType.Customer);

        var clock = Substitute.For<IDateTimeProvider>();
        clock.UtcNow.Returns(DateTime.UtcNow);
        clock.TodayUtc.Returns(DateOnly.FromDateTime(DateTime.UtcNow));

        var lockManager = new PostgresApplicationLockManager(engineDb);
        var txExecutor = new PostgresApplicationTransactionExecutor(engineDb);
        var transferService = new CapacityTransferService(engineDb, lockManager, txExecutor, clock);

        var engine = new CustomerCalendarEngine(
            engineDb,
            user,
            clock,
            new RestaurantDiversificationCalculator(),
            capacityTransferService: transferService);

        var result = await engine.SelectRestaurantAsync(subscriptionId, date, newRestId, CancellationToken.None);

        result.IsSuccess.Should().BeTrue();
        await engineDb.SaveChangesAsync();

        // Verify day.RestaurantId is updated to newRestId
        await using var verifyDb = CreateDb();
        var reloadedDay = await verifyDb.SubscriptionDays.FindAsync(dayId);
        reloadedDay.Should().NotBeNull();
        reloadedDay!.RestaurantId.Should().Be(newRestId);

        // Verify old meal selections were wiped
        var remainingSelections = await verifyDb.DailyMealSelections.Where(s => s.SubscriptionDayId == dayId).ToListAsync();
        remainingSelections.Should().BeEmpty("Incompatible meal selections from old restaurant must be wiped.");

        // Verify hold was transferred to newRestId
        var reloadedHold = await verifyDb.CapacityHolds.SingleAsync(h => h.SubscriptionId == subscriptionId && h.ServiceDate == date);
        reloadedHold.RestaurantId.Should().Be(newRestId);
        reloadedHold.Status.Should().Be(CapacityHoldStatus.Confirmed);
    }
}
