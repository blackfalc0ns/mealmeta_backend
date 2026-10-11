using FluentAssertions;
using MealMate.Application.Common.Abstractions;
using MealMate.Application.Features.CustomerCalendar;
using MealMate.Application.Features.AdminDashboard.OperationalPolicies;
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
using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.Hosting;
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
    [Theory]
    [InlineData(false, false, false)]
    [InlineData(true, false, false)]
    [InlineData(false, true, false)]
    [InlineData(true, true, false)]
    [InlineData(true, false, true)]
    [InlineData(true, true, true)]
    public async Task Unsafe_saved_box_persists_replacement_failure_without_sending_or_changing_capacity(bool automatic, bool partialRepair, bool trial)
    {
        var date = DateOnly.FromDateTime(DateTime.UtcNow.AddDays(5));
        var scenario = await SeedAutoSelectionScenarioAsync(date, Guid.NewGuid(), initialCapacity: 1);
        Guid selectionId;
        Guid mealId;
        Guid? secondSelectionId = null;
        Guid? secondMealId = null;
        await using (var db = CreateDb())
        {
            var day = await db.SubscriptionDays.SingleAsync(x => x.SubscriptionId == scenario.SubscriptionId);
            day.RestaurantId = scenario.HoldRestaurantId;
            day.Status = SubscriptionDayStatus.CustomerSelected;
            day.SelectionDeadlineUtc = DateTime.UtcNow.AddMinutes(-1);
            day.LockAtUtc = trial ? null : DateTime.UtcNow.AddMinutes(-1);
            var menu = await db.SubscriptionRestaurantDaySnapshots.SingleAsync(x =>
                x.SubscriptionId == scenario.SubscriptionId && x.RestaurantId == scenario.HoldRestaurantId);
            using var document = System.Text.Json.JsonDocument.Parse(menu.MealsJson);
            mealId = document.RootElement[0].GetProperty("mealId").GetGuid();
            var selection = new DailyMealSelection
            {
                SubscriptionDayId = day.Id, MealId = mealId, SortOrder = 0, SelectedAtUtc = DateTime.UtcNow
            };
            selectionId = selection.Id;
            db.DailyMealSelections.Add(selection);
            if (partialRepair)
            {
                // The first unsafe meal can be repaired, but a second lunch has no remaining
                // safe alternative. None of that incomplete repair may survive the failure.
                var meals = System.Text.Json.Nodes.JsonNode.Parse(menu.MealsJson)!.AsArray();
                var secondUnsafe = meals[0]!.DeepClone();
                secondMealId = Guid.NewGuid();
                secondUnsafe["mealId"] = secondMealId.Value.ToString();
                secondUnsafe["sortOrder"] = 2;
                var safeAlternative = meals[0]!.DeepClone();
                safeAlternative["mealId"] = Guid.NewGuid().ToString();
                safeAlternative["presentAllergenCodes"] = new System.Text.Json.Nodes.JsonArray();
                meals.Add(secondUnsafe);
                meals.Add(safeAlternative);
                menu.MealsJson = meals.ToJsonString();
                foreach (var dayMenu in await db.SubscriptionRestaurantDaySnapshots
                    .Where(x => x.SubscriptionId == scenario.SubscriptionId).ToListAsync())
                    dayMenu.SlotRequirementsJson = """[{"slot":"lunch","count":2}]""";
                var subscription = await db.Subscriptions.SingleAsync(x => x.Id == scenario.SubscriptionId);
                (await db.Bundles.SingleAsync(x => x.Id == subscription.BundleId)).LunchCount = 2;
                var secondSelection = new DailyMealSelection
                {
                    SubscriptionDayId = day.Id, MealId = secondMealId.Value, SortOrder = 1, SelectedAtUtc = DateTime.UtcNow
                };
                secondSelectionId = secondSelection.Id;
                db.DailyMealSelections.Add(secondSelection);
            }
            await db.SaveChangesAsync();
        }

        await using (var db = CreateDb())
        {
            if (trial)
            {
                var clock = Substitute.For<IDateTimeProvider>();
                clock.UtcNow.Returns(DateTime.UtcNow);
                var environment = Substitute.For<IHostEnvironment>();
                environment.EnvironmentName.Returns(Environments.Development);
                using var cache = new MemoryCache(new MemoryCacheOptions());
                var policies = new OperationalPolicyService(db, cache, Substitute.For<IAuditLogger>(), clock);
                var service = new OperationalTrialService(db, clock, environment, policies,
                    new PostgresApplicationTransactionExecutor(db), new PostgresApplicationLockManager(db));
                var result = await service.RunAsync(scenario.SubscriptionId, date, "dispatch", "Verify allergy safety",
                    Guid.NewGuid(), CreateEngine(scenario, db, clock, policies, environment), default);
                result.IsFailure.Should().BeTrue();
                result.Error.Code.Should().Be("calendar.no_safe_box");
            }
            else
            {
                var engine = CreateEngine(scenario, db);
                var result = automatic
                    ? await engine.AutoSelectFullBoxAsync(scenario.SubscriptionId, date, true, false, default)
                    : await engine.ConfirmAndSendToRestaurantAsync(scenario.SubscriptionId, date, default);
                result.IsFailure.Should().BeTrue();
                result.Error.Code.Should().Be("calendar.no_safe_box");
            }
        }

        await using var verify = CreateDb();
        var persisted = await verify.SubscriptionDays.SingleAsync(x => x.SubscriptionId == scenario.SubscriptionId);
        persisted.Status.Should().Be(SubscriptionDayStatus.ReplacementRequired);
        persisted.FailureReasonCode.Should().Be("calendar.no_safe_box");
        persisted.LockAtUtc.Should().BeNull();
        persisted.RestaurantId.Should().Be(scenario.HoldRestaurantId);
        var savedSelections = await verify.DailyMealSelections.Where(x => x.SubscriptionDayId == persisted.Id).ToListAsync();
        savedSelections.Should().HaveCount(partialRepair ? 2 : 1);
        var savedSelection = savedSelections.Single(x => x.Id == selectionId);
        savedSelection.Id.Should().Be(selectionId);
        savedSelection.MealId.Should().Be(mealId);
        if (partialRepair)
            savedSelections.Single(x => x.Id == secondSelectionId).MealId.Should().Be(secondMealId!.Value);
        (await verify.CapacityHolds.SingleAsync(x => x.SubscriptionId == scenario.SubscriptionId))
            .RestaurantId.Should().Be(scenario.HoldRestaurantId);
        (await verify.RestaurantDailyOrders.CountAsync()).Should().Be(0);
    }

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

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task Replacement_window_round_trips_through_PostgreSQL_including_legacy_nulls(bool hasWindow)
    {
        var date = DateOnly.FromDateTime(DateTime.UtcNow.AddDays(5));
        var scenario = await SeedAutoSelectionScenarioAsync(date, Guid.NewGuid(), initialCapacity: 1);
        DateTime? opensAt = hasWindow ? new DateTime(2026, 10, 11, 12, 0, 0, DateTimeKind.Utc) : null;
        DateTime? closesAt = hasWindow ? new DateTime(2026, 10, 11, 12, 30, 0, DateTimeKind.Utc) : null;
        await using (var db = CreateDb())
        {
            var day = await db.SubscriptionDays.SingleAsync(x => x.SubscriptionId == scenario.SubscriptionId);
            day.ReplacementWindowOpensAtUtc = opensAt;
            day.ReplacementWindowClosesAtUtc = closesAt;
            await db.SaveChangesAsync();
        }

        await using var reloaded = CreateDb();
        var persisted = await reloaded.SubscriptionDays.AsNoTracking()
            .SingleAsync(x => x.SubscriptionId == scenario.SubscriptionId);
        persisted.ReplacementWindowOpensAtUtc.Should().Be(opensAt);
        persisted.ReplacementWindowClosesAtUtc.Should().Be(closesAt);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("RESTAURANT_CAPACITY_FULL")]
    [InlineData("calendar.restaurant_coverage_unavailable")]
    [InlineData("calendar.box_not_ready")]
    public async Task Replacement_reason_survives_PostgreSQL_reload_and_is_consistent_in_calendar_detail_and_summary(string? code)
    {
        var date = DateOnly.FromDateTime(DateTime.UtcNow.AddDays(5));
        var scenario = await SeedAutoSelectionScenarioAsync(date, Guid.NewGuid(), initialCapacity: 1);
        await using (var db = CreateDb())
        {
            var day = await db.SubscriptionDays.SingleAsync(x => x.SubscriptionId == scenario.SubscriptionId);
            day.Status = SubscriptionDayStatus.ReplacementRequired;
            day.FailureReasonCode = code;
            await db.SaveChangesAsync();
        }
        await using var reloaded = CreateDb();
        var engine = CreateEngine(scenario, reloaded);
        var detail = await engine.GetDayAsync(scenario.SubscriptionId, date, default);
        var calendar = await engine.GetCalendarAsync(scenario.SubscriptionId, default);
        var expectedCode = code ?? "calendar.no_safe_box";
        detail.IsSuccess.Should().BeTrue();
        detail.Value.FailureReasonCode.Should().Be(expectedCode);
        calendar.Value.Days.Single(x => x.ServiceDate == date).FailureReasonCode.Should().Be(expectedCode);
        (await reloaded.SubscriptionDays.AsNoTracking().SingleAsync(x => x.SubscriptionId == scenario.SubscriptionId)).FailureReasonCode.Should().Be(code);
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
    public async Task Full_recommended_restaurant_falls_back_atomically_to_another_safe_box()
    {
        var date = DateOnly.FromDateTime(DateTime.UtcNow.AddDays(5));
        var fullRestaurant = Guid.NewGuid();
        var alternativeRestaurant = Guid.NewGuid();
        var alternativeVersion = Guid.NewGuid();
        var scenario = await SeedAutoSelectionScenarioAsync(date, fullRestaurant, initialCapacity: 0);
        await using (var seed = CreateDb())
        {
            var original = await seed.SubscriptionRestaurantSnapshots.SingleAsync(x => x.SubscriptionId == scenario.SubscriptionId && x.RestaurantId == fullRestaurant);
            var originalDay = await seed.SubscriptionRestaurantDaySnapshots.SingleAsync(x => x.SubscriptionId == scenario.SubscriptionId && x.RestaurantId == fullRestaurant);
            var alternative = new SubscriptionRestaurantSnapshot
            {
                PoolSnapshotId = original.PoolSnapshotId, SubscriptionId = scenario.SubscriptionId,
                RestaurantId = alternativeRestaurant, ProgramId = original.ProgramId, BundleId = original.BundleId,
                RegionId = original.RegionId, Tier = original.Tier, NameAr = "مطعم بديل", NameEn = "Alternative Restaurant",
                OfferingSnapshotJson = original.OfferingSnapshotJson, CapturedAtUtc = DateTime.UtcNow
            };
            seed.SubscriptionRestaurantSnapshots.Add(alternative);
            seed.SubscriptionRestaurantDaySnapshots.Add(new SubscriptionRestaurantDaySnapshot
            {
                RestaurantSnapshotId = alternative.Id, SubscriptionId = scenario.SubscriptionId,
                RestaurantId = alternativeRestaurant, ServiceDate = date, MenuPlanVersionId = alternativeVersion,
                SlotRequirementsJson = originalDay.SlotRequirementsJson, MealsJson = originalDay.MealsJson,
                ContentHash = originalDay.ContentHash, CapturedAtUtc = DateTime.UtcNow
            });
            (await seed.SubscriptionDays.SingleAsync(x => x.SubscriptionId == scenario.SubscriptionId)).RestaurantId = fullRestaurant;
            seed.DailyCapacities.Add(new DailyCapacity
            {
                RestaurantId = alternativeRestaurant, ServiceDate = date, CapacityType = CapacityType.Base, MaxBoxes = 1
            });
            await seed.SaveChangesAsync();
        }
        await using var db = CreateDb();
        var result = await CreateEngine(scenario, db).AutoSelectFullBoxAsync(scenario.SubscriptionId, date, true, false, default);
        result.IsSuccess.Should().BeTrue();
        await using var verify = CreateDb();
        var day = await verify.SubscriptionDays.SingleAsync(x => x.SubscriptionId == scenario.SubscriptionId);
        day.RestaurantId.Should().Be(alternativeRestaurant);
        day.Status.Should().Be(SubscriptionDayStatus.SentToRestaurant);
        var hold = await verify.CapacityHolds.SingleAsync(x => x.SubscriptionId == scenario.SubscriptionId);
        hold.RestaurantId.Should().Be(alternativeRestaurant);
        hold.MenuPlanVersionId.Should().Be(alternativeVersion);
        (await verify.SubscriptionDayMealSnapshots.SingleAsync(x => x.SubscriptionDayId == day.Id)).RestaurantId.Should().Be(alternativeRestaurant);
        (await verify.RestaurantDailyOrders.SingleAsync(x => x.SubscriptionDayId == day.Id)).RestaurantId.Should().Be(alternativeRestaurant);
    }

    [Fact]
    public async Task Concurrent_fallbacks_preserve_one_winner_for_last_alternative_box()
    {
        var date = DateOnly.FromDateTime(DateTime.UtcNow.AddDays(5));
        var fullRestaurant = Guid.NewGuid();
        var alternativeRestaurant = Guid.NewGuid();
        var first = await SeedAutoSelectionScenarioAsync(date, fullRestaurant, initialCapacity: 0);
        var second = await SeedAutoSelectionScenarioAsync(date, fullRestaurant, initialCapacity: 0);
        await using (var seed = CreateDb())
        {
            foreach (var scenario in new[] { first, second })
            {
                var original = await seed.SubscriptionRestaurantSnapshots.SingleAsync(x => x.SubscriptionId == scenario.SubscriptionId && x.RestaurantId == fullRestaurant);
                var menu = await seed.SubscriptionRestaurantDaySnapshots.SingleAsync(x => x.SubscriptionId == scenario.SubscriptionId && x.RestaurantId == fullRestaurant);
                var alternative = new SubscriptionRestaurantSnapshot
                {
                    PoolSnapshotId = original.PoolSnapshotId, SubscriptionId = scenario.SubscriptionId,
                    RestaurantId = alternativeRestaurant, ProgramId = original.ProgramId, BundleId = original.BundleId,
                    RegionId = original.RegionId, Tier = original.Tier, NameAr = "مطعم بديل", NameEn = "Alternative Restaurant",
                    OfferingSnapshotJson = original.OfferingSnapshotJson, CapturedAtUtc = DateTime.UtcNow
                };
                seed.SubscriptionRestaurantSnapshots.Add(alternative);
                seed.SubscriptionRestaurantDaySnapshots.Add(new SubscriptionRestaurantDaySnapshot
                {
                    RestaurantSnapshotId = alternative.Id, SubscriptionId = scenario.SubscriptionId,
                    RestaurantId = alternativeRestaurant, ServiceDate = date, MenuPlanVersionId = Guid.NewGuid(),
                    SlotRequirementsJson = menu.SlotRequirementsJson, MealsJson = menu.MealsJson,
                    ContentHash = menu.ContentHash, CapturedAtUtc = DateTime.UtcNow
                });
                (await seed.SubscriptionDays.SingleAsync(x => x.SubscriptionId == scenario.SubscriptionId)).RestaurantId = fullRestaurant;
            }
            seed.DailyCapacities.Add(new DailyCapacity
            {
                RestaurantId = alternativeRestaurant, ServiceDate = date, CapacityType = CapacityType.Base, MaxBoxes = 1
            });
            await seed.SaveChangesAsync();
        }
        await using var db1 = CreateDb();
        await using var db2 = CreateDb();
        var results = await Task.WhenAll(
            CreateEngine(first, db1).AutoSelectFullBoxAsync(first.SubscriptionId, date, true, false, default),
            CreateEngine(second, db2).AutoSelectFullBoxAsync(second.SubscriptionId, date, true, false, default))
            .WaitAsync(TimeSpan.FromSeconds(30));
        results.Count(x => x.IsSuccess).Should().Be(1);
        results.Count(x => x.IsFailure && x.Error.Code == "RESTAURANT_CAPACITY_FULL").Should().Be(1);
        var winner = results[0].IsSuccess ? first : second;
        var loser = results[0].IsSuccess ? second : first;
        await using var verify = CreateDb();
        (await verify.CapacityHolds.SingleAsync(x => x.SubscriptionId == winner.SubscriptionId)).RestaurantId.Should().Be(alternativeRestaurant);
        (await verify.CapacityHolds.SingleAsync(x => x.SubscriptionId == loser.SubscriptionId)).RestaurantId.Should().Be(loser.HoldRestaurantId);
        (await verify.SubscriptionDays.SingleAsync(x => x.SubscriptionId == loser.SubscriptionId)).Status.Should().Be(SubscriptionDayStatus.Open);
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

    private CustomerCalendarEngine CreateEngine(Scenario scenario, AppDbContext db,
        IDateTimeProvider? providedClock = null, IOperationalPolicyService? policies = null, IHostEnvironment? environment = null)
    {
        var user = Substitute.For<ICurrentUser>();
        user.UserId.Returns(scenario.CustomerId);
        user.UserType.Returns(UserType.Customer);
        var clock = providedClock ?? Substitute.For<IDateTimeProvider>();
        if (providedClock is null)
        {
            clock.UtcNow.Returns(DateTime.UtcNow);
            clock.TodayUtc.Returns(DateOnly.FromDateTime(DateTime.UtcNow));
        }

        var lockManager = new PostgresApplicationLockManager(db);
        var txExecutor = new PostgresApplicationTransactionExecutor(db);
        var transfer = new CapacityTransferService(db, lockManager, txExecutor, clock, new PostgresCapacityConcurrencyLock(db), new RestaurantCapacityReader(db, clock));
        var dispatch = new SubscriptionDayDispatchService(db, clock, new CustomerDeliveryAddressService(db, clock));
        return new CustomerCalendarEngine(
            db, user, clock, new RestaurantDiversificationCalculator(), dispatch,
            capacityTransferService: transfer, transactions: txExecutor, locks: lockManager, policies: policies, environment: environment);
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
