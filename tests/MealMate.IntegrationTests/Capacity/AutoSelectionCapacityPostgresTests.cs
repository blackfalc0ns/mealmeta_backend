using FluentAssertions;
using MealMate.Application.Common.Abstractions;
using MealMate.Application.Features.CustomerCalendar;
using MealMate.Application.Features.CustomerCheckout;
using MealMate.Application.Features.IdentityAccess.Customers;
using MealMate.Application.Features.ProgramsSubscriptions;
using MealMate.Application.Features.RestaurantOperations.Availability;
using MealMate.Domain.CalendarMeals;
using MealMate.Domain.Enums;
using MealMate.Domain.ProgramsSubscriptions;
using MealMate.Domain.RestaurantOperations;
using MealMate.Domain.ValueObjects;
using MealMate.Persistence;
using MealMate.Persistence.Common;
using MealMate.Persistence.CustomerCheckout;
using Microsoft.EntityFrameworkCore;
using NSubstitute;
using Testcontainers.PostgreSql;
using Xunit;
using Xunit.Sdk;

namespace MealMate.IntegrationTests.Capacity;

/// <summary>
/// PostgreSQL concurrency contract for automatic selection: a restaurant switch decided by the
/// recommendation engine moves the capacity hold through the same transfer service as manual
/// selection, inside the engine's write transaction. Two concurrent auto-selections compete for
/// the last box at the target restaurant — exactly one wins; repeated runs never duplicate
/// holds or kitchen orders.
/// </summary>
public sealed class AutoSelectionCapacityPostgresTests : IAsyncLifetime
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
            throw new XunitException("Docker is required for AutoSelectionCapacityPostgresTests.", ex);
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
    public async Task Concurrent_auto_selections_for_the_last_box_allow_only_one_winner()
    {
        var date = DateOnly.FromDateTime(DateTime.UtcNow.AddDays(5));
        var targetRestaurant = Guid.NewGuid();

        var first = await SeedAutoSelectionScenarioAsync(date, targetRestaurant, initialCapacity: 1);
        var second = await SeedAutoSelectionScenarioAsync(date, targetRestaurant, initialCapacity: 1);

        await using var db1 = CreateDb();
        await using var db2 = CreateDb();
        var engine1 = CreateEngine(first, db1);
        var engine2 = CreateEngine(second, db2);

        // Both customers are allergic to their own checkout restaurant's menu, so both
        // recommendation engines independently pick the shared target restaurant, whose single
        // box can only serve one of them.
        var race = await Task.WhenAll(
            engine1.AutoSelectFullBoxAsync(first.SubscriptionId, date, lockAfter: true, requireOwner: false, CancellationToken.None),
            engine2.AutoSelectFullBoxAsync(second.SubscriptionId, date, lockAfter: true, requireOwner: false, CancellationToken.None));

        race.Count(r => r.IsSuccess).Should().Be(1, "only one identity fits the target's last box");
        race.Count(r => r.IsFailure && r.Error.Code == "RESTAURANT_CAPACITY_FULL").Should().Be(1);

        await using var verify = CreateDb();
        var winnerSubscriptionId = race[0].IsSuccess ? first.SubscriptionId : second.SubscriptionId;
        var loserSubscriptionId = race[0].IsSuccess ? second.SubscriptionId : first.SubscriptionId;
        var loserRestaurant = race[0].IsSuccess ? second.HoldRestaurantId : first.HoldRestaurantId;

        var winnerDay = await verify.SubscriptionDays.AsNoTracking()
            .SingleAsync(x => x.SubscriptionId == winnerSubscriptionId && x.ServiceDate == date);
        winnerDay.RestaurantId.Should().Be(targetRestaurant);
        winnerDay.Status.Should().Be(SubscriptionDayStatus.SentToRestaurant);

        var winnerHold = await verify.CapacityHolds.AsNoTracking()
            .SingleAsync(x => x.SubscriptionId == winnerSubscriptionId && x.ServiceDate == date);
        winnerHold.RestaurantId.Should().Be(targetRestaurant);

        (await verify.RestaurantDailyOrders.CountAsync(x => x.SubscriptionDayId == winnerDay.Id)).Should().Be(1);

        var loserDay = await verify.SubscriptionDays.AsNoTracking()
            .SingleAsync(x => x.SubscriptionId == loserSubscriptionId && x.ServiceDate == date);
        loserDay.RestaurantId.Should().BeNull("a lost capacity race must not switch the day");
        loserDay.Status.Should().Be(SubscriptionDayStatus.Open);

        var loserHold = await verify.CapacityHolds.AsNoTracking()
            .SingleAsync(x => x.SubscriptionId == loserSubscriptionId && x.ServiceDate == date);
        loserHold.RestaurantId.Should().Be(loserRestaurant, "the losing transfer keeps the previous hold");
        loserHold.Status.Should().Be(CapacityHoldStatus.PaymentConfirmed);

        (await verify.RestaurantDailyOrders.CountAsync()).Should().Be(1);
    }

    [Fact]
    public async Task Repeated_auto_selection_run_does_not_duplicate_hold_or_order()
    {
        var date = DateOnly.FromDateTime(DateTime.UtcNow.AddDays(5));
        var targetRestaurant = Guid.NewGuid();
        var scenario = await SeedAutoSelectionScenarioAsync(date, targetRestaurant, initialCapacity: 5);

        await using var db = CreateDb();
        var engine = CreateEngine(scenario, db);
        var first = await engine.AutoSelectFullBoxAsync(scenario.SubscriptionId, date, lockAfter: true, requireOwner: false, CancellationToken.None);
        first.IsSuccess.Should().BeTrue();

        // The job repeats every 15 seconds; a sent day refuses re-dispatch and nothing doubles.
        await engine.AutoSelectFullBoxAsync(scenario.SubscriptionId, date, lockAfter: true, requireOwner: false, CancellationToken.None);

        await using var verify = CreateDb();
        (await verify.CapacityHolds.CountAsync(x => x.SubscriptionId == scenario.SubscriptionId && x.ServiceDate == date)).Should().Be(1);
        var dayId = await verify.SubscriptionDays
            .Where(x => x.SubscriptionId == scenario.SubscriptionId && x.ServiceDate == date)
            .Select(x => x.Id).SingleAsync();
        (await verify.RestaurantDailyOrders.CountAsync(x => x.SubscriptionDayId == dayId)).Should().Be(1);

        var hold = await verify.CapacityHolds.AsNoTracking()
            .SingleAsync(x => x.SubscriptionId == scenario.SubscriptionId && x.ServiceDate == date);
        hold.RestaurantId.Should().Be(targetRestaurant);
        hold.Quantity.Should().Be(1);
    }

    private sealed record Scenario(
        Guid SubscriptionId,
        Guid HoldRestaurantId,
        Guid CustomerId);

    private CustomerCalendarEngine CreateEngine(Scenario scenario, AppDbContext db)
    {
        var user = Substitute.For<ICurrentUser>();
        user.UserId.Returns(scenario.CustomerId);
        user.UserType.Returns(UserType.Customer);
        var clock = Substitute.For<IDateTimeProvider>();
        clock.UtcNow.Returns(DateTime.UtcNow);
        clock.TodayUtc.Returns(DateOnly.FromDateTime(DateTime.UtcNow));

        var lockManager = new PostgresApplicationLockManager(db);
        var txExecutor = new PostgresApplicationTransactionExecutor(db);
        var transfer = new CapacityTransferService(db, lockManager, txExecutor, clock, new PostgresCapacityConcurrencyLock(db), new RestaurantCapacityReader(db, clock));
        var dispatch = new SubscriptionDayDispatchService(db, clock, new CustomerDeliveryAddressService(db, clock));
        return new CustomerCalendarEngine(
            db, user, clock, new RestaurantDiversificationCalculator(), dispatch,
            capacityTransferService: transfer, transactions: txExecutor, locks: lockManager);
    }

    private async Task<Scenario> SeedAutoSelectionScenarioAsync(DateOnly date, Guid targetRestaurant, int initialCapacity)
    {
        var customerId = Guid.NewGuid();
        var holdRestaurant = Guid.NewGuid();
        var mealId = Guid.NewGuid();
        var milkMealId = Guid.NewGuid();

        await using var db = CreateDb();
        var program = new Program { NameAr = "برنامج", NameEn = "Program", Code = $"AUTO-{Guid.NewGuid():N}".Substring(0, 12), IsActive = true };
        var bundle = new Bundle { ProgramId = program.Id, NameAr = "باقة", NameEn = "Bundle", Code = $"B-{Guid.NewGuid():N}".Substring(0, 12), LunchCount = 1, IsActive = true };
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
            FulfillmentCompletedAtUtc = DateTime.UtcNow,
        };
        db.Programs.Add(program);
        db.Bundles.Add(bundle);
        db.Subscriptions.Add(subscription);

        // The customer is allergic to milk; the checkout restaurant's lunch carries it, so the
        // recommendation engine must pick the shared target restaurant instead.
        db.CustomerAllergyPreferences.Add(new CustomerAllergyPreference
        {
            CustomerUserId = customerId,
            AllergenCode = "MILK",
            AllergenNameAr = "حليب",
            AllergenNameEn = "Milk",
            IsDislike = false,
        });

        var pool = new SubscriptionRestaurantPoolSnapshot
        {
            SubscriptionId = subscription.Id,
            EligibilityCheckedAtUtc = DateTime.UtcNow,
            RestaurantCount = 2,
            IsBackfilled = false,
        };
        var holdSnap = new SubscriptionRestaurantSnapshot
        {
            PoolSnapshotId = pool.Id,
            SubscriptionId = subscription.Id,
            RestaurantId = holdRestaurant,
            ProgramId = program.Id,
            BundleId = bundle.Id,
            RegionId = Guid.NewGuid(),
            Tier = RestaurantTier.Basic,
            NameAr = "مطعم الحجز",
            NameEn = "Hold Restaurant",
            IsInitiallySelected = true,
            OfferingSnapshotJson = "{}",
            CapturedAtUtc = DateTime.UtcNow,
        };
        var targetSnap = new SubscriptionRestaurantSnapshot
        {
            PoolSnapshotId = pool.Id,
            SubscriptionId = subscription.Id,
            RestaurantId = targetRestaurant,
            ProgramId = program.Id,
            BundleId = bundle.Id,
            RegionId = Guid.NewGuid(),
            Tier = RestaurantTier.Basic,
            NameAr = "مطعم الهدف",
            NameEn = "Target Restaurant",
            IsInitiallySelected = false,
            OfferingSnapshotJson = "{}",
            CapturedAtUtc = DateTime.UtcNow,
        };
        db.SubscriptionRestaurantPoolSnapshots.Add(pool);
        db.SubscriptionRestaurantSnapshots.AddRange(holdSnap, targetSnap);

        var versionId = Guid.NewGuid();
        var milkMeals = $$"""[{"mealId":"{{milkMealId}}","nameAr":"وجبة بالحليب","nameEn":"Milk Meal","slot":"lunch","sortOrder":1,"imageUrl":null,"calories":500,"proteinG":30,"carbsG":40,"fatG":15,"descriptionAr":null,"descriptionEn":null,"presentAllergenCodes":["MILK"],"mayContainAllergenCodes":[],"dietaryPreferenceCodes":[],"ingredients":[]}]""";
        var safeMeals = $$"""[{"mealId":"{{mealId}}","nameAr":"وجبة آمنة","nameEn":"Safe Meal","slot":"lunch","sortOrder":1,"imageUrl":null,"calories":500,"proteinG":30,"carbsG":40,"fatG":15,"descriptionAr":null,"descriptionEn":null,"presentAllergenCodes":[],"mayContainAllergenCodes":[],"dietaryPreferenceCodes":[],"ingredients":[]}]""";
        db.SubscriptionRestaurantDaySnapshots.AddRange(
            new SubscriptionRestaurantDaySnapshot
            {
                RestaurantSnapshotId = holdSnap.Id,
                SubscriptionId = subscription.Id,
                RestaurantId = holdRestaurant,
                ServiceDate = date,
                MenuPlanVersionId = versionId,
                SlotRequirementsJson = """[{"slot":"lunch","count":1}]""",
                MealsJson = milkMeals,
                ContentHash = "hash-hold",
                CapturedAtUtc = DateTime.UtcNow,
            },
            new SubscriptionRestaurantDaySnapshot
            {
                RestaurantSnapshotId = targetSnap.Id,
                SubscriptionId = subscription.Id,
                RestaurantId = targetRestaurant,
                ServiceDate = date,
                MenuPlanVersionId = versionId,
                SlotRequirementsJson = """[{"slot":"lunch","count":1}]""",
                MealsJson = safeMeals,
                ContentHash = "hash-target",
                CapturedAtUtc = DateTime.UtcNow,
            });

        db.SubscriptionDays.Add(new SubscriptionDay
        {
            SubscriptionId = subscription.Id,
            ServiceDate = date,
            RestaurantId = null,
            Status = SubscriptionDayStatus.Open,
            SelectionDeadlineUtc = DateTime.UtcNow.AddMinutes(-1),
        });

        db.CapacityHolds.Add(new CapacityHold
        {
            RestaurantId = holdRestaurant,
            SubscriptionId = subscription.Id,
            MenuPlanVersionId = versionId,
            ServiceDate = date,
            Quantity = 1,
            Status = CapacityHoldStatus.PaymentConfirmed,
            ConfirmedAtUtc = DateTime.UtcNow,
            ExpiresAtUtc = DateTime.UtcNow.AddDays(7),
        });

        db.DailyCapacities.Add(new DailyCapacity
        {
            RestaurantId = targetRestaurant,
            ServiceDate = date,
            CapacityType = CapacityType.Base,
            MaxBoxes = initialCapacity,
            ReservedBoxes = 0,
        });

        await db.SaveChangesAsync();
        return new Scenario(subscription.Id, holdRestaurant, customerId);
    }
}
