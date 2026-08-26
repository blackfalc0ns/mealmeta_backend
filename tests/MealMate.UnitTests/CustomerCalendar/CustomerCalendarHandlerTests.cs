using System.Text.Json;
using FluentAssertions;
using MealMate.Application.Common.Abstractions;
using MealMate.Application.Common.Localization;
using MealMate.Application.Features.CustomerCalendar;
using MealMate.Application.Features.CustomerCheckout;
using MealMate.Application.Features.ProgramsSubscriptions;
using MealMate.Domain.CalendarMeals;
using MealMate.Domain.Enums;
using MealMate.Domain.ProgramsSubscriptions;
using MealMate.Domain.RestaurantOperations;
using MealMate.Domain.ValueObjects;
using MealMate.Persistence;
using Microsoft.EntityFrameworkCore;
using NSubstitute;

namespace MealMate.UnitTests.CustomerCalendar;

public sealed class CustomerCalendarHandlerTests
{
    private static readonly DateOnly Day1 = new(2026, 9, 1);
    private static readonly DateOnly Day2 = new(2026, 9, 2);
    private static readonly DateOnly Day3 = new(2026, 9, 3);
    private static readonly DateOnly Day4 = new(2026, 9, 4);
    private static readonly DateTime Now = new(2026, 8, 20, 9, 0, 0, DateTimeKind.Utc);
    private static readonly DateTime Deadline = new(2026, 8, 29, 21, 0, 0, DateTimeKind.Utc);

    [Fact]
    public async Task GetCalendar_returns_filled_and_required_counts()
    {
        var fx = await SeedAsync();
        var breakfast = fx.BreakfastA;
        await SelectRestaurant(fx, Day1, fx.RestaurantA);
        await SelectSlot(fx, Day1, breakfast, "breakfast");

        var result = await fx.Handlers.Handle(new GetCustomerCalendarQuery(fx.SubscriptionId), CancellationToken.None);

        result.IsSuccess.Should().BeTrue();
        result.Value.RequiredCount.Should().Be(3);
        var day = result.Value.Days.Single(x => x.ServiceDate == Day1);
        day.FilledCount.Should().Be(1);
        day.RequiredCount.Should().Be(3);
        day.UiStatus.Should().Be(CustomerCalendarUiStatus.Drafting);
        day.CanEdit.Should().BeTrue();
        day.SelectedMeals.Should().ContainSingle(x => x.MealId == breakfast);
    }

    [Fact]
    public async Task GetCalendar_selected_meals_are_slim_without_nutrition_or_ingredients()
    {
        var fx = await SeedAsync();
        await SelectRestaurant(fx, Day1, fx.RestaurantA);
        await SelectSlot(fx, Day1, fx.BreakfastA, "breakfast");

        var calendar = await fx.Handlers.Handle(new GetCustomerCalendarQuery(fx.SubscriptionId), CancellationToken.None);
        calendar.IsSuccess.Should().BeTrue();
        var summaryMeal = calendar.Value.Days.Single(x => x.ServiceDate == Day1).SelectedMeals.Single();
        summaryMeal.Should().BeOfType<CustomerCalendarMealSummaryDto>();
        summaryMeal.MealId.Should().Be(fx.BreakfastA);
        summaryMeal.NameAr.Should().NotBeNullOrWhiteSpace();
        summaryMeal.Slot.Should().Be("breakfast");
        summaryMeal.IsSelected.Should().BeTrue();

        var day = await fx.Handlers.Handle(new GetCustomerCalendarDayQuery(fx.SubscriptionId, Day1), CancellationToken.None);
        day.IsSuccess.Should().BeTrue();
        var fullMeal = day.Value.SelectedMeals.Single();
        fullMeal.Should().BeOfType<CustomerCalendarMealDto>();
        fullMeal.Calories.Should().Be(400);
        fullMeal.DescriptionAr.Should().NotBeNullOrWhiteSpace();
        fullMeal.PresentAllergenCodes.Should().NotBeNull();
        fullMeal.Ingredients.Should().NotBeNull();
    }

    [Fact]
    public async Task GetDay_unknown_date_fails()
    {
        var fx = await SeedAsync();
        var result = await fx.Handlers.Handle(
            new GetCustomerCalendarDayQuery(fx.SubscriptionId, new DateOnly(2099, 1, 1)),
            CancellationToken.None);

        result.IsFailure.Should().BeTrue();
        result.Error.Code.Should().Be(MessageCodes.Calendar.DayUnavailable);
    }

    [Fact]
    public async Task GetDayRestaurants_marks_exhausted_when_used_at_limit()
    {
        var fx = await SeedAsync();
        await MarkCompleted(fx, Day1, fx.RestaurantA);
        await MarkCompleted(fx, Day2, fx.RestaurantA);
        await MarkCompleted(fx, Day3, fx.RestaurantA);

        var result = await fx.Handlers.Handle(new GetCustomerDayRestaurantsQuery(fx.SubscriptionId, Day4), CancellationToken.None);

        result.IsSuccess.Should().BeTrue();
        result.Value.Limit.Should().Be(3);
        var restaurantA = result.Value.Restaurants.Single(x => x.RestaurantId == fx.RestaurantA);
        restaurantA.UsedCount.Should().Be(3);
        restaurantA.IsQuotaExhausted.Should().BeTrue();
        restaurantA.IsEnabled.Should().BeFalse();
        restaurantA.DisabledReason.Should().Be(MessageCodes.Calendar.QuotaExhausted);
        result.Value.Restaurants.Single(x => x.RestaurantId == fx.RestaurantB).IsEnabled.Should().BeTrue();
    }

    [Fact]
    public async Task SelectRestaurant_from_pool_clears_meals()
    {
        var fx = await SeedAsync();
        await SelectRestaurant(fx, Day1, fx.RestaurantA);
        await SelectSlot(fx, Day1, fx.BreakfastA, "breakfast");
        var result = await fx.Handlers.Handle(
            new SelectCustomerDayRestaurantCommand(fx.SubscriptionId, Day1, fx.RestaurantB),
            CancellationToken.None);
        result.IsSuccess.Should().BeTrue();
        await fx.Db.SaveChangesAsync();

        result.Value.RestaurantId.Should().Be(fx.RestaurantB);
        result.Value.FilledCount.Should().Be(0);
        result.Value.Status.Should().Be(SubscriptionDayStatus.Open);
        (await fx.Db.DailyMealSelections.CountAsync(x => x.SubscriptionDayId == result.Value.Id && !x.IsDeleted)).Should().Be(0);
    }

    [Fact]
    public async Task SelectRestaurant_outside_pool_fails()
    {
        var fx = await SeedAsync();

        var result = await fx.Handlers.Handle(
            new SelectCustomerDayRestaurantCommand(fx.SubscriptionId, Day1, Guid.NewGuid()),
            CancellationToken.None);

        result.IsFailure.Should().BeTrue();
        result.Error.Code.Should().Be(MessageCodes.Calendar.RestaurantNotInPool);
    }

    [Fact]
    public async Task SelectRestaurant_when_locked_fails()
    {
        var fx = await SeedAsync(now: Deadline.AddSeconds(1));

        var result = await fx.Handlers.Handle(
            new SelectCustomerDayRestaurantCommand(fx.SubscriptionId, Day1, fx.RestaurantA),
            CancellationToken.None);

        result.IsFailure.Should().BeTrue();
        result.Error.Code.Should().Be(MessageCodes.Calendar.SelectionLocked);
    }

    [Fact]
    public async Task SelectSlotMeal_keeps_day_in_draft()
    {
        var fx = await SeedAsync();
        await SelectRestaurant(fx, Day1, fx.RestaurantA);

        var result = await fx.Handlers.Handle(
            new SelectCustomerDaySlotMealCommand(fx.SubscriptionId, Day1, fx.BreakfastA, "breakfast"),
            CancellationToken.None);

        result.IsSuccess.Should().BeTrue();
        result.Value.FilledCount.Should().Be(1);
        result.Value.Status.Should().Be(SubscriptionDayStatus.Open);
        result.Value.UiStatus.Should().Be(CustomerCalendarUiStatus.Drafting);
    }

    [Fact]
    public async Task SelectSlotMeal_revives_soft_deleted_selection_for_same_meal()
    {
        var fx = await SeedAsync();
        await SelectRestaurant(fx, Day1, fx.RestaurantA);
        await SelectSlot(fx, Day1, fx.BreakfastA, "breakfast");

        var existing = await fx.Db.DailyMealSelections.SingleAsync(x => x.MealId == fx.BreakfastA);
        existing.IsDeleted = true;
        existing.DeletedAtUtc = Now;
        await fx.Db.SaveChangesAsync();
        fx.Db.ChangeTracker.Clear();

        var result = await fx.Handlers.Handle(
            new SelectCustomerDaySlotMealCommand(fx.SubscriptionId, Day1, fx.BreakfastA, "breakfast"),
            CancellationToken.None);

        result.IsSuccess.Should().BeTrue(result.IsFailure ? result.Error.Code : "");
        await fx.Db.SaveChangesAsync();

        var rows = await fx.Db.DailyMealSelections.IgnoreQueryFilters()
            .Where(x => x.SubscriptionDayId == result.Value.Id && x.MealId == fx.BreakfastA)
            .ToListAsync();
        rows.Should().ContainSingle();
        rows[0].IsDeleted.Should().BeFalse();
        result.Value.FilledCount.Should().Be(1);
        result.Value.SelectedMeals.Should().ContainSingle(x => x.MealId == fx.BreakfastA);
    }

    [Fact]
    public async Task SelectSlotMeal_rejects_allergy_conflict()
    {
        var fx = await SeedAsync(seedMilkAllergy: true);
        await SelectRestaurant(fx, Day1, fx.RestaurantA);

        var result = await fx.Handlers.Handle(
            new SelectCustomerDaySlotMealCommand(fx.SubscriptionId, Day1, fx.BreakfastAllergen, "breakfast"),
            CancellationToken.None);

        result.IsFailure.Should().BeTrue();
        result.Error.Code.Should().Be(MessageCodes.Calendar.AllergyConflict);
    }

    [Fact]
    public async Task CompleteBox_fails_when_incomplete()
    {
        var fx = await SeedAsync();
        await SelectRestaurant(fx, Day1, fx.RestaurantA);
        await SelectSlot(fx, Day1, fx.BreakfastA, "breakfast");

        var result = await fx.Handlers.Handle(
            new CompleteCustomerDayBoxCommand(fx.SubscriptionId, Day1),
            CancellationToken.None);

        result.IsFailure.Should().BeTrue();
        result.Error.Code.Should().Be(MessageCodes.Calendar.BoxIncomplete);
        (await fx.Db.SubscriptionDays.SingleAsync(x => x.SubscriptionId == fx.SubscriptionId && x.ServiceDate == Day1))
            .Status.Should().Be(SubscriptionDayStatus.Open);
    }

    [Fact]
    public async Task CompleteBox_succeeds_when_all_slots_filled()
    {
        var fx = await SeedAsync();
        await FillBox(fx, Day1, fx.RestaurantA, fx.BreakfastA, fx.LunchA, fx.DinnerA);

        var result = await fx.Handlers.Handle(
            new CompleteCustomerDayBoxCommand(fx.SubscriptionId, Day1),
            CancellationToken.None);

        result.IsSuccess.Should().BeTrue();
        result.Value.Status.Should().Be(SubscriptionDayStatus.CustomerSelected);
        result.Value.UiStatus.Should().Be(CustomerCalendarUiStatus.AwaitingRestaurant);
        result.Value.FilledCount.Should().Be(3);
        result.Value.IsAutoSelected.Should().BeFalse();
        await fx.Db.SaveChangesAsync();
    }

    [Fact]
    public async Task CompleteBox_is_idempotent_when_already_complete()
    {
        var fx = await SeedAsync();
        await FillBox(fx, Day1, fx.RestaurantA, fx.BreakfastA, fx.LunchA, fx.DinnerA);
        var first = await fx.Handlers.Handle(new CompleteCustomerDayBoxCommand(fx.SubscriptionId, Day1, "box-1"), CancellationToken.None);
        first.IsSuccess.Should().BeTrue(first.IsFailure ? first.Error.Code : "");
        await fx.Db.SaveChangesAsync();

        var again = await fx.Handlers.Handle(
            new CompleteCustomerDayBoxCommand(fx.SubscriptionId, Day1, "box-1"),
            CancellationToken.None);

        again.IsSuccess.Should().BeTrue();
        again.Value.Status.Should().Be(SubscriptionDayStatus.CustomerSelected);
        (await fx.Db.DailyMealSelections.CountAsync(x => x.SubscriptionDayId == again.Value.Id)).Should().Be(3);
    }

    [Fact]
    public async Task AutoSelect_fills_full_box_skipping_allergy_meals()
    {
        var fx = await SeedAsync(seedMilkAllergy: true);

        var result = await fx.Handlers.Handle(
            new AutoSelectCustomerDayBoxCommand(fx.SubscriptionId, Day1),
            CancellationToken.None);

        result.IsSuccess.Should().BeTrue();
        result.Value.FilledCount.Should().Be(3);
        result.Value.RequiredCount.Should().Be(3);
        result.Value.IsAutoSelected.Should().BeTrue();
        result.Value.Status.Should().Be(SubscriptionDayStatus.AutoSelected);
        result.Value.SelectedMeals.Should().NotContain(x => x.MealId == fx.BreakfastAllergen);
        result.Value.SelectedMeals.Select(x => x.Slot.ToLowerInvariant())
            .Should().BeEquivalentTo(["breakfast", "lunch", "dinner"]);
    }

    [Fact]
    public async Task AutoSelect_skips_restaurant_at_repetition_limit_when_alternative_exists()
    {
        var fx = await SeedAsync();
        await MarkCompleted(fx, Day2, fx.RestaurantA);
        await MarkCompleted(fx, Day3, fx.RestaurantA);
        await MarkCompleted(fx, Day4, fx.RestaurantA);

        var result = await fx.Handlers.Handle(
            new AutoSelectCustomerDayBoxCommand(fx.SubscriptionId, Day1),
            CancellationToken.None);

        result.IsSuccess.Should().BeTrue();
        result.Value.RestaurantId.Should().Be(fx.RestaurantB);
        result.Value.FilledCount.Should().Be(3);
    }

    [Fact]
    public async Task AutoSelect_never_leaves_a_partial_box()
    {
        var fx = await SeedAsync();
        await SelectRestaurant(fx, Day1, fx.RestaurantA);
        await SelectSlot(fx, Day1, fx.BreakfastA, "breakfast");

        var result = await fx.Handlers.Handle(
            new AutoSelectCustomerDayBoxCommand(fx.SubscriptionId, Day1),
            CancellationToken.None);

        result.IsSuccess.Should().BeTrue();
        result.Value.FilledCount.Should().Be(result.Value.RequiredCount);
        result.Value.FilledCount.Should().Be(3);
    }

    [Fact]
    public async Task Family_subscription_without_active_group_is_rejected()
    {
        var fx = await SeedAsync(family: true);

        var result = await fx.Handlers.Handle(new GetCustomerCalendarQuery(fx.SubscriptionId), CancellationToken.None);

        result.IsFailure.Should().BeTrue();
        result.Error.Code.Should().Be(MessageCodes.Family.GroupNotActive);
    }

    [Fact]
    public async Task Lock_after_deadline_auto_selects_then_locks()
    {
        var fx = await SeedAsync(now: Deadline.AddMinutes(1));

        var result = await fx.Handlers.Handle(
            new AutoSelectCustomerDayBoxCommand(fx.SubscriptionId, Day1, LockAfter: true),
            CancellationToken.None);

        result.IsSuccess.Should().BeTrue();
        result.Value.FilledCount.Should().Be(3);
        result.Value.Status.Should().Be(SubscriptionDayStatus.SentToRestaurant);
        result.Value.CanEdit.Should().BeFalse();
        result.Value.IsAutoSelected.Should().BeTrue();
    }

    private static async Task FillBox(Fixture fx, DateOnly date, Guid restaurantId, Guid breakfast, Guid lunch, Guid dinner)
    {
        await SelectRestaurant(fx, date, restaurantId);
        await SelectSlot(fx, date, breakfast, "breakfast");
        await SelectSlot(fx, date, lunch, "lunch");
        await SelectSlot(fx, date, dinner, "dinner");
    }

    private static async Task SelectRestaurant(Fixture fx, DateOnly date, Guid restaurantId)
    {
        var result = await fx.Handlers.Handle(
            new SelectCustomerDayRestaurantCommand(fx.SubscriptionId, date, restaurantId),
            CancellationToken.None);
        result.IsSuccess.Should().BeTrue(result.IsFailure ? result.Error.Code : "");
        await fx.Db.SaveChangesAsync();
    }

    private static async Task SelectSlot(Fixture fx, DateOnly date, Guid mealId, string slot)
    {
        var result = await fx.Handlers.Handle(
            new SelectCustomerDaySlotMealCommand(fx.SubscriptionId, date, mealId, slot),
            CancellationToken.None);
        result.IsSuccess.Should().BeTrue(result.IsFailure ? result.Error.Code : "");
        await fx.Db.SaveChangesAsync();
    }

    private static async Task MarkCompleted(Fixture fx, DateOnly date, Guid restaurantId)
    {
        var meals = restaurantId == fx.RestaurantA
            ? (fx.BreakfastA, fx.LunchA, fx.DinnerA)
            : (fx.BreakfastB, fx.LunchB, fx.DinnerB);
        await FillBox(fx, date, restaurantId, meals.Item1, meals.Item2, meals.Item3);
        var result = await fx.Handlers.Handle(new CompleteCustomerDayBoxCommand(fx.SubscriptionId, date), CancellationToken.None);
        result.IsSuccess.Should().BeTrue(result.IsFailure ? result.Error.Code : "");
        await fx.Db.SaveChangesAsync();
    }

    private static async Task<Fixture> SeedAsync(
        bool seedMilkAllergy = false,
        bool family = false,
        DateTime? now = null)
    {
        var db = new AppDbContext(
            new DbContextOptionsBuilder<AppDbContext>().UseInMemoryDatabase(Guid.NewGuid().ToString()).Options,
            Substitute.For<IDomainEventDispatcher>());

        var customerId = Guid.NewGuid();
        var program = new Program
        {
            Code = "WL",
            NameAr = "وزن",
            NameEn = "Weight",
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
            DurationDays = 6,
            IncludeFriday = false,
            StartDate = Day1,
            EndDate = Day4,
            Status = SubscriptionStatus.Active,
            PaidAmount = new Money(25m, "KWD"),
            FulfillmentCompletedAtUtc = Now,
            FamilyGroupId = family ? Guid.NewGuid() : null
        };

        var restaurantA = Guid.NewGuid();
        var restaurantB = Guid.NewGuid();
        var breakfastA = Guid.NewGuid();
        var lunchA = Guid.NewGuid();
        var dinnerA = Guid.NewGuid();
        var breakfastAllergen = Guid.NewGuid();
        var breakfastB = Guid.NewGuid();
        var lunchB = Guid.NewGuid();
        var dinnerB = Guid.NewGuid();
        var versionA = Guid.NewGuid();
        var versionB = Guid.NewGuid();

        var pool = new SubscriptionRestaurantPoolSnapshot
        {
            SubscriptionId = subscription.Id,
            RestaurantCount = 2,
            EligibilityCheckedAtUtc = Now
        };
        var snapA = FrozenRestaurant(pool.Id, subscription, restaurantA, "مطعم أ", "Restaurant A", true);
        var snapB = FrozenRestaurant(pool.Id, subscription, restaurantB, "مطعم ب", "Restaurant B", false);

        db.AddRange(program, bundle, subscription, pool, snapA, snapB);
        if (seedMilkAllergy)
        {
            db.Add(new CustomerAllergyPreference
            {
                CustomerUserId = customerId,
                AllergenCode = "MILK",
                AllergenNameAr = "حليب",
                AllergenNameEn = "Milk",
                IsDislike = false
            });
        }

        foreach (var date in new[] { Day1, Day2, Day3, Day4 })
        {
            db.Add(new SubscriptionDay
            {
                SubscriptionId = subscription.Id,
                ServiceDate = date,
                Status = SubscriptionDayStatus.Open,
                SelectionDeadlineUtc = Deadline
            });
            db.Add(RestaurantDay(snapA.Id, subscription.Id, restaurantA, date, versionA,
                MealsJson(breakfastA, lunchA, dinnerA, breakfastAllergen, "A")));
            db.Add(RestaurantDay(snapB.Id, subscription.Id, restaurantB, date, versionB,
                MealsJson(breakfastB, lunchB, dinnerB, allergenBreakfast: null, suffix: "B")));
        }

        await db.SaveChangesAsync();

        var user = Substitute.For<ICurrentUser>();
        user.UserId.Returns(customerId);
        user.UserType.Returns(UserType.Customer);
        var clock = Substitute.For<IDateTimeProvider>();
        clock.UtcNow.Returns(now ?? Now);
        clock.TodayUtc.Returns(DateOnly.FromDateTime(now ?? Now));

        var engine = new CustomerCalendarEngine(db, user, clock, new RestaurantDiversificationCalculator());
        var handlers = new CustomerCalendarHandlers(engine);
        return new Fixture(db, handlers, subscription.Id, restaurantA, restaurantB, breakfastA, lunchA, dinnerA,
            breakfastAllergen, breakfastB, lunchB, dinnerB);
    }

    private static SubscriptionRestaurantSnapshot FrozenRestaurant(
        Guid poolId, Subscription subscription, Guid restaurantId, string nameAr, string nameEn, bool initial)
        => new()
        {
            PoolSnapshotId = poolId,
            SubscriptionId = subscription.Id,
            RestaurantId = restaurantId,
            ProgramId = subscription.ProgramId,
            BundleId = subscription.BundleId,
            RegionId = Guid.NewGuid(),
            Tier = RestaurantTier.Platinum,
            NameAr = nameAr,
            NameEn = nameEn,
            IsInitiallySelected = initial,
            OfferingSnapshotJson = "{}",
            CapturedAtUtc = Now
        };

    private static SubscriptionRestaurantDaySnapshot RestaurantDay(
        Guid restaurantSnapshotId, Guid subscriptionId, Guid restaurantId, DateOnly date, Guid versionId, string mealsJson)
        => new()
        {
            RestaurantSnapshotId = restaurantSnapshotId,
            SubscriptionId = subscriptionId,
            RestaurantId = restaurantId,
            ServiceDate = date,
            MenuPlanVersionId = versionId,
            SlotRequirementsJson = "[{\"slot\":\"breakfast\",\"count\":1},{\"slot\":\"lunch\",\"count\":1},{\"slot\":\"dinner\",\"count\":1}]",
            MealsJson = mealsJson,
            ContentHash = "hash",
            CapturedAtUtc = Now
        };

    private static string MealsJson(Guid breakfast, Guid lunch, Guid dinner, Guid? allergenBreakfast, string suffix)
    {
        var meals = new List<object>
        {
            Meal(breakfast, $"فطور {suffix}", $"Breakfast {suffix}", "breakfast", 1, []),
            Meal(lunch, $"غداء {suffix}", $"Lunch {suffix}", "lunch", 2, []),
            Meal(dinner, $"عشاء {suffix}", $"Dinner {suffix}", "dinner", 3, [])
        };
        if (allergenBreakfast is Guid allergen)
            meals.Insert(0, Meal(allergen, "فطور حليب", "Milk breakfast", "breakfast", 0, new[] { "MILK" }));
        return JsonSerializer.Serialize(meals);
    }

    private static object Meal(Guid id, string ar, string en, string slot, int sort, string[] allergens) => new
    {
        mealId = id,
        nameAr = ar,
        nameEn = en,
        descriptionAr = ar,
        descriptionEn = en,
        slot,
        sortOrder = sort,
        imageUrl = (string?)null,
        calories = 400,
        proteinG = 30,
        carbsG = 20,
        fatG = 10,
        presentAllergenCodes = allergens,
        mayContainAllergenCodes = Array.Empty<string>(),
        dietaryPreferenceCodes = Array.Empty<string>(),
        ingredients = Array.Empty<object>()
    };

    private sealed record Fixture(
        AppDbContext Db,
        CustomerCalendarHandlers Handlers,
        Guid SubscriptionId,
        Guid RestaurantA,
        Guid RestaurantB,
        Guid BreakfastA,
        Guid LunchA,
        Guid DinnerA,
        Guid BreakfastAllergen,
        Guid BreakfastB,
        Guid LunchB,
        Guid DinnerB);
}
