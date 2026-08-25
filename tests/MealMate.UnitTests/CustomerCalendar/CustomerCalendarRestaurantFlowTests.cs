using FluentAssertions;
using System.Text.Json;
using MealMate.Application.Common.Abstractions;
using MealMate.Application.Common.Localization;
using MealMate.Application.Features.CustomerCalendar;
using MealMate.Application.Features.CustomerCheckout;
using MealMate.Application.Features.ProgramsSubscriptions;
using MealMate.Application.Features.RestaurantOrders;
using MealMate.Domain.CalendarMeals;
using MealMate.Domain.Enums;
using MealMate.Domain.ProgramsSubscriptions;
using MealMate.Domain.RestaurantOperations;
using MealMate.Domain.ValueObjects;
using MealMate.Persistence;
using Microsoft.EntityFrameworkCore;
using NSubstitute;

namespace MealMate.UnitTests.CustomerCalendar;

/// <summary>
/// End-to-end unit test: customer calendar selection → −72h lock/dispatch → restaurant confirmation.
/// </summary>
public sealed class CustomerCalendarRestaurantFlowTests
{
    private static readonly DateOnly Day1 = new(2026, 9, 1);
    private static readonly DateTime BeforeDeadline = new(2026, 8, 20, 9, 0, 0, DateTimeKind.Utc);
    private static readonly DateTime AfterDeadline = new(2026, 8, 29, 22, 0, 0, DateTimeKind.Utc);
    private static readonly DateTime Deadline = new(2026, 8, 29, 21, 0, 0, DateTimeKind.Utc);

    [Fact]
    public async Task CompleteBox_lock_dispatches_to_restaurant_and_confirm_succeeds()
    {
        var restaurantId = Guid.NewGuid();
        var fx = await SeedAsync(restaurantId, BeforeDeadline);
        await FillAndComplete(fx, Day1, fx.RestaurantA, fx.BreakfastA, fx.LunchA, fx.DinnerA);

        var lockResult = await fx.Handlers.Handle(
            new AutoSelectCustomerDayBoxCommand(fx.SubscriptionId, Day1, LockAfter: true),
            CancellationToken.None);
        lockResult.IsSuccess.Should().BeTrue();
        lockResult.Value.Status.Should().Be(SubscriptionDayStatus.SentToRestaurant);
        await fx.Db.SaveChangesAsync();

        var op = await fx.Db.SubscriptionOperations
            .FirstOrDefaultAsync(x => x.SubscriptionId == fx.SubscriptionId && x.OperationType == "ORDER_SENT_TO_RESTAURANT");
        op.Should().NotBeNull();

        var restaurantUser = Substitute.For<ICurrentUser>();
        restaurantUser.RestaurantId.Returns(restaurantId);
        restaurantUser.UserId.Returns(Guid.NewGuid());
        restaurantUser.UserType.Returns(UserType.Restaurant);

        var orderService = new RestaurantOrderService(fx.Db, restaurantUser, fx.Clock);
        var pending = await orderService.GetPendingConfirmationAsync(new GetRestaurantPendingConfirmationQuery(), CancellationToken.None);
        pending.IsSuccess.Should().BeTrue();
        pending.Value.Orders.Should().ContainSingle(x => x.Id == lockResult.Value.Id);
        pending.Value.Orders.Single(x => x.Id == lockResult.Value.Id).Confirmed.Should().BeFalse();

        var confirm = await orderService.ConfirmOrderAsync(
            new ConfirmRestaurantOrderCommand(lockResult.Value.Id, "Ready"),
            CancellationToken.None);
        confirm.IsSuccess.Should().BeTrue();

        var day = await fx.Db.SubscriptionDays.FindAsync(lockResult.Value.Id);
        day!.Status.Should().Be(SubscriptionDayStatus.ConfirmedByRestaurant);
        day.ConfirmedAtUtc.Should().NotBeNull();

        var daily = await orderService.GetDailyOrdersAsync(
            new GetRestaurantDailyOrdersQuery(Day1),
            CancellationToken.None);
        daily.IsSuccess.Should().BeTrue();
        daily.Value.Orders.Should().ContainSingle(x => x.Id == lockResult.Value.Id);
    }

    [Fact]
    public async Task ConfirmOrder_from_customer_selected_fails()
    {
        var restaurantId = Guid.NewGuid();
        var fx = await SeedAsync(restaurantId, BeforeDeadline);
        await FillAndComplete(fx, Day1, fx.RestaurantA, fx.BreakfastA, fx.LunchA, fx.DinnerA);

        var restaurantUser = Substitute.For<ICurrentUser>();
        restaurantUser.RestaurantId.Returns(restaurantId);
        var orderService = new RestaurantOrderService(fx.Db, restaurantUser, fx.Clock);

        var day = await fx.Db.SubscriptionDays.FirstAsync(x => x.ServiceDate == Day1);
        var result = await orderService.ConfirmOrderAsync(new ConfirmRestaurantOrderCommand(day.Id), CancellationToken.None);

        result.IsFailure.Should().BeTrue();
        result.Error.Code.Should().Be(MessageCodes.Orders.InvalidStatus);
    }

    [Fact]
    public async Task Lock_after_deadline_auto_selects_full_box_and_dispatches()
    {
        var restaurantId = Guid.NewGuid();
        var fx = await SeedAsync(restaurantId, AfterDeadline);

        var result = await fx.Handlers.Handle(
            new AutoSelectCustomerDayBoxCommand(fx.SubscriptionId, Day1, LockAfter: true),
            CancellationToken.None);

        result.IsSuccess.Should().BeTrue();
        result.Value.FilledCount.Should().Be(3);
        result.Value.Status.Should().Be(SubscriptionDayStatus.SentToRestaurant);
        result.Value.IsAutoSelected.Should().BeTrue();
    }

    private static async Task FillAndComplete(
        FlowFixture fx, DateOnly date, Guid restaurantId, Guid breakfast, Guid lunch, Guid dinner)
    {
        await SelectRestaurant(fx, date, restaurantId);
        await SelectSlot(fx, date, breakfast, "breakfast");
        await SelectSlot(fx, date, lunch, "lunch");
        await SelectSlot(fx, date, dinner, "dinner");
        var complete = await fx.Handlers.Handle(new CompleteCustomerDayBoxCommand(fx.SubscriptionId, date), CancellationToken.None);
        complete.IsSuccess.Should().BeTrue();
        await fx.Db.SaveChangesAsync();
    }

    private static async Task SelectRestaurant(FlowFixture fx, DateOnly date, Guid restaurantId)
    {
        var result = await fx.Handlers.Handle(
            new SelectCustomerDayRestaurantCommand(fx.SubscriptionId, date, restaurantId),
            CancellationToken.None);
        result.IsSuccess.Should().BeTrue();
        await fx.Db.SaveChangesAsync();
    }

    private static async Task SelectSlot(FlowFixture fx, DateOnly date, Guid mealId, string slot)
    {
        var result = await fx.Handlers.Handle(
            new SelectCustomerDaySlotMealCommand(fx.SubscriptionId, date, mealId, slot),
            CancellationToken.None);
        result.IsSuccess.Should().BeTrue();
        await fx.Db.SaveChangesAsync();
    }

    private static async Task<FlowFixture> SeedAsync(Guid restaurantId, DateTime now)
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
            EndDate = Day1,
            Status = SubscriptionStatus.Active,
            PaidAmount = new Money(25m, "KWD"),
            FulfillmentCompletedAtUtc = now.AddDays(-1)
        };

        var restaurantB = Guid.NewGuid();
        var breakfastA = Guid.NewGuid();
        var lunchA = Guid.NewGuid();
        var dinnerA = Guid.NewGuid();
        var breakfastB = Guid.NewGuid();
        var lunchB = Guid.NewGuid();
        var dinnerB = Guid.NewGuid();
        var versionA = Guid.NewGuid();
        var versionB = Guid.NewGuid();

        var pool = new SubscriptionRestaurantPoolSnapshot
        {
            SubscriptionId = subscription.Id,
            RestaurantCount = 2,
            EligibilityCheckedAtUtc = now
        };
        var snapA = FrozenRestaurant(pool.Id, subscription, restaurantId, "مطعم أ", "Restaurant A", true);
        var snapB = FrozenRestaurant(pool.Id, subscription, restaurantB, "مطعم ب", "Restaurant B", false);

        db.AddRange(program, bundle, subscription, pool, snapA, snapB);
        db.Add(new SubscriptionDay
        {
            SubscriptionId = subscription.Id,
            ServiceDate = Day1,
            Status = SubscriptionDayStatus.Open,
            SelectionDeadlineUtc = Deadline
        });
        db.Add(RestaurantDay(snapA.Id, subscription.Id, restaurantId, Day1, versionA,
            MealsJson(breakfastA, lunchA, dinnerA, "A")));
        db.Add(RestaurantDay(snapB.Id, subscription.Id, restaurantB, Day1, versionB,
            MealsJson(breakfastB, lunchB, dinnerB, "B")));

        await db.SaveChangesAsync();

        var user = Substitute.For<ICurrentUser>();
        user.UserId.Returns(customerId);
        user.UserType.Returns(UserType.Customer);
        var clock = Substitute.For<IDateTimeProvider>();
        clock.UtcNow.Returns(now);
        clock.TodayUtc.Returns(DateOnly.FromDateTime(now));

        var dispatch = new SubscriptionDayDispatchService(db, clock);
        var engine = new CustomerCalendarEngine(db, user, clock, new RestaurantDiversificationCalculator(), dispatch);
        var handlers = new CustomerCalendarHandlers(engine);

        return new FlowFixture(
            db, handlers, clock, subscription.Id, restaurantId, restaurantB,
            breakfastA, lunchA, dinnerA, breakfastB, lunchB, dinnerB);
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
            CapturedAtUtc = DateTime.UtcNow
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
            CapturedAtUtc = DateTime.UtcNow
        };

    private static string MealsJson(Guid breakfast, Guid lunch, Guid dinner, string suffix)
    {
        var meals = new List<object>
        {
            Meal(breakfast, $"فطور {suffix}", $"Breakfast {suffix}", "breakfast", 1, []),
            Meal(lunch, $"غداء {suffix}", $"Lunch {suffix}", "lunch", 2, []),
            Meal(dinner, $"عشاء {suffix}", $"Dinner {suffix}", "dinner", 3, [])
        };
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

    private sealed record FlowFixture(
        AppDbContext Db,
        CustomerCalendarHandlers Handlers,
        IDateTimeProvider Clock,
        Guid SubscriptionId,
        Guid RestaurantA,
        Guid RestaurantB,
        Guid BreakfastA,
        Guid LunchA,
        Guid DinnerA,
        Guid BreakfastB,
        Guid LunchB,
        Guid DinnerB);
}
