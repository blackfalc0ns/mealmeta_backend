using FluentAssertions;
using System.Text.Json;
using MealMate.Application.Common.Abstractions;
using MealMate.Application.Common.Localization;
using MealMate.Application.Features.CustomerCalendar;
using MealMate.Application.Features.CustomerCheckout;
using MealMate.Application.Features.IdentityAccess.Customers;
using MealMate.Application.Features.ProgramsSubscriptions;
using MealMate.Application.Features.RestaurantOperations.Availability;
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
    [Fact]
    public async Task RapidTrial_DispatchUsesRealEngine_RepeatedRunCreatesOneOrder_AndCannotReopenDay()
    {
        var fx = await SeedAsync(Guid.NewGuid(), BeforeDeadline);
        await FillAndComplete(fx, Day1, fx.RestaurantA, fx.BreakfastA, fx.LunchA, fx.DinnerA);
        var environment = Substitute.For<Microsoft.Extensions.Hosting.IHostEnvironment>();
        environment.EnvironmentName.Returns("Development");
        var policies = new MealMate.Application.Features.AdminDashboard.OperationalPolicies.OperationalPolicyService(
            fx.Db, new Microsoft.Extensions.Caching.Memory.MemoryCache(new Microsoft.Extensions.Caching.Memory.MemoryCacheOptions()), Substitute.For<IAuditLogger>());
        var trial = new MealMate.Application.Features.AdminDashboard.OperationalPolicies.OperationalTrialService(fx.Db, fx.Clock, environment, policies);
        var actor = Guid.NewGuid();
        (await trial.ConfigureAsync(fx.SubscriptionId, Day1, new(5, "Lifecycle trial"), Guid.Empty, default)).IsFailure.Should().BeTrue();
        var first = await trial.RunAsync(fx.SubscriptionId, Day1, "dispatch", "Actual dispatch test", actor, fx.Engine, default);
        first.IsSuccess.Should().BeTrue(); first.Value.Status.Should().Be(nameof(SubscriptionDayStatus.SentToRestaurant));
        var repeat = await trial.RunAsync(fx.SubscriptionId, Day1, "dispatch", "Repeat same action", actor, fx.Engine, default);
        repeat.IsSuccess.Should().BeTrue(); repeat.Value.OrderCount.Should().Be(first.Value.OrderCount);
        first.Value.OrderCount.Should().Be(1);
        var reopen = await trial.ConfigureAsync(fx.SubscriptionId, Day1, new(5, "Try reopening committed day"), actor, default);
        reopen.IsFailure.Should().BeTrue();
        (await fx.Db.SubscriptionDays.SingleAsync(x => x.SubscriptionId == fx.SubscriptionId && x.ServiceDate == Day1)).Status.Should().Be(SubscriptionDayStatus.SentToRestaurant);
    }
    private static readonly DateOnly Day1 = new(2026, 9, 1);
    private static readonly DateTime BeforeDeadline = new(2026, 8, 20, 9, 0, 0, DateTimeKind.Utc);
    private static readonly DateTime AfterDeadline = new(2026, 8, 29, 22, 0, 0, DateTimeKind.Utc);
    private static readonly DateTime Deadline = new(2026, 8, 29, 21, 0, 0, DateTimeKind.Utc);

    [Theory]
    [InlineData(0)]
    [InlineData(1)]
    public async Task Open_day_at_or_after_deadline_has_locked_ui_in_summary_and_detail(int secondsAfter)
    {
        var fx = await SeedAsync(Guid.NewGuid(), BeforeDeadline);
        fx.Clock.UtcNow.Returns(Deadline.AddSeconds(secondsAfter));
        var detail = await fx.Handlers.Handle(new GetCustomerCalendarDayQuery(fx.SubscriptionId, Day1), default);
        detail.Value.CanEdit.Should().BeFalse();
        detail.Value.EditBlockReason.Should().Be(CalendarEditBlockReasons.SelectionDeadlinePassed);
        detail.Value.UiStatus.Should().Be(CustomerCalendarUiStatus.Locked);
        var calendar = await fx.Handlers.Handle(new GetCustomerCalendarQuery(fx.SubscriptionId), default);
        var summary = calendar.Value.Days.Single(x => x.ServiceDate == Day1);
        summary.CanEdit.Should().BeFalse();
        summary.UiStatus.Should().Be(CustomerCalendarUiStatus.Locked);
    }

    [Fact]
    public async Task Open_day_with_existing_confirmation_keeps_lock_and_reports_locked_ui()
    {
        var fx = await SeedAsync(Guid.NewGuid(), BeforeDeadline);
        var day = await fx.Db.SubscriptionDays.SingleAsync(x => x.ServiceDate == Day1);
        day.ConfirmedAtUtc = BeforeDeadline.AddDays(-1);
        await fx.Db.SaveChangesAsync();
        var detail = await fx.Handlers.Handle(new GetCustomerCalendarDayQuery(fx.SubscriptionId, Day1), default);
        detail.Value.CanEdit.Should().BeFalse();
        detail.Value.EditBlockReason.Should().Be(CalendarEditBlockReasons.DayLocked);
        detail.Value.UiStatus.Should().Be(CustomerCalendarUiStatus.Locked);
        (await fx.Handlers.Handle(new SelectCustomerDayRestaurantCommand(fx.SubscriptionId, Day1, fx.RestaurantA), default))
            .IsFailure.Should().BeTrue();
        var stored = await fx.Db.SubscriptionDays.AsNoTracking().SingleAsync(x => x.ServiceDate == Day1);
        stored.ConfirmedAtUtc.Should().Be(BeforeDeadline.AddDays(-1));
    }

    [Fact]
    public async Task Open_day_with_operational_lock_cannot_be_edited_before_deadline()
    {
        var fx = await SeedAsync(Guid.NewGuid(), BeforeDeadline);
        var day = await fx.Db.SubscriptionDays.SingleAsync(x => x.ServiceDate == Day1);
        day.LockAtUtc = BeforeDeadline.AddSeconds(-1);
        await fx.Db.SaveChangesAsync();
        var detail = await fx.Handlers.Handle(new GetCustomerCalendarDayQuery(fx.SubscriptionId, Day1), default);
        detail.Value.CanEdit.Should().BeFalse();
        detail.Value.EditBlockReason.Should().Be(CalendarEditBlockReasons.DayLocked);
        detail.Value.UiStatus.Should().Be(CustomerCalendarUiStatus.Locked);
        var edit = await fx.Handlers.Handle(new SelectCustomerDayRestaurantCommand(fx.SubscriptionId, Day1, fx.RestaurantA), default);
        edit.IsFailure.Should().BeTrue();
        edit.Error.Code.Should().Be(MessageCodes.Calendar.SelectionLocked);
    }

    [Fact]
    public async Task CompleteBox_lock_dispatches_to_restaurant_and_confirm_succeeds()
    {
        var restaurantId = Guid.NewGuid();
        var fx = await SeedAsync(restaurantId, BeforeDeadline);
        await FillAndComplete(fx, Day1, fx.RestaurantA, fx.BreakfastA, fx.LunchA, fx.DinnerA);
        var earlyLock = await fx.Handlers.Handle(new AutoSelectCustomerDayBoxCommand(fx.SubscriptionId, Day1, LockAfter: true), default);
        earlyLock.IsFailure.Should().BeTrue();
        earlyLock.Error.Code.Should().Be("subscription.cutoff_not_due");
        (await fx.Db.RestaurantDailyOrders.CountAsync()).Should().Be(0);
        fx.Clock.UtcNow.Returns(AfterDeadline);

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
    public async Task ConfirmBox_before_deadline_keeps_selections_editable_and_defers_dispatch()
    {
        var fx = await SeedAsync(Guid.NewGuid(), BeforeDeadline);
        await FillAndComplete(fx, Day1, fx.RestaurantA, fx.BreakfastA, fx.LunchA, fx.DinnerA);

        var first = await fx.Handlers.Handle(
            new ConfirmCustomerDayBoxCommand(fx.SubscriptionId, Day1),
            CancellationToken.None);

        first.IsSuccess.Should().BeTrue();
        first.Value.Status.Should().Be(SubscriptionDayStatus.CustomerSelected);
        first.Value.CanEdit.Should().BeTrue();
        first.Value.LockAtUtc.Should().BeNull();
        (await fx.Db.RestaurantDailyOrders.CountAsync()).Should().Be(0);

        var repeat = await fx.Handlers.Handle(
            new ConfirmCustomerDayBoxCommand(fx.SubscriptionId, Day1),
            CancellationToken.None);

        repeat.IsSuccess.Should().BeTrue();
        repeat.Value.Status.Should().Be(SubscriptionDayStatus.CustomerSelected);
        repeat.Value.CanEdit.Should().BeTrue();
        (await fx.Db.RestaurantDailyOrders.CountAsync()).Should().Be(0);

        var calendar = await fx.Handlers.Handle(new GetCustomerCalendarQuery(fx.SubscriptionId), default);
        calendar.Value.Days.Single(x => x.ServiceDate == Day1).CanEdit.Should().BeTrue();

        var edit = await fx.Handlers.Handle(
            new SelectCustomerDayRestaurantCommand(fx.SubscriptionId, Day1, fx.RestaurantB), default);
        edit.IsSuccess.Should().BeTrue();
        edit.Value.CanEdit.Should().BeTrue();
    }

    [Theory]
    [InlineData(0)]
    [InlineData(1)]
    public async Task ConfirmBox_at_or_after_deadline_dispatches_once_and_locks(int secondsAfterDeadline)
    {
        var fx = await SeedAsync(Guid.NewGuid(), BeforeDeadline);
        await FillAndComplete(fx, Day1, fx.RestaurantA, fx.BreakfastA, fx.LunchA, fx.DinnerA);
        fx.Clock.UtcNow.Returns(Deadline.AddSeconds(secondsAfterDeadline));

        for (var attempt = 0; attempt < 2; attempt++)
        {
            var result = await fx.Handlers.Handle(new ConfirmCustomerDayBoxCommand(fx.SubscriptionId, Day1), default);
            result.IsSuccess.Should().BeTrue();
            result.Value.Status.Should().Be(SubscriptionDayStatus.SentToRestaurant);
            result.Value.CanEdit.Should().BeFalse();
            result.Value.UiStatus.Should().Be(CustomerCalendarUiStatus.Locked);
            result.Value.LockAtUtc.Should().Be(Deadline.AddSeconds(secondsAfterDeadline));
        }

        (await fx.Db.RestaurantDailyOrders.CountAsync()).Should().Be(1);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(1)]
    public async Task Unconfirmed_dispatched_day_cannot_be_edited_at_or_after_cutoff(int secondsAfterDeadline)
    {
        var fx = await SeedAsync(Guid.NewGuid(), BeforeDeadline);
        await FillAndComplete(fx, Day1, fx.RestaurantA, fx.BreakfastA, fx.LunchA, fx.DinnerA);
        var day = await fx.Db.SubscriptionDays.SingleAsync(x => x.ServiceDate == Day1);
        await new SubscriptionDayDispatchService(fx.Db, fx.Clock, new CustomerDeliveryAddressService(fx.Db, fx.Clock)).LockAndSendToRestaurantAsync(day, fx.SubscriptionId, default);
        await fx.Db.SaveChangesAsync();
        fx.Clock.UtcNow.Returns(Deadline.AddSeconds(secondsAfterDeadline));

        var calendar = await fx.Handlers.Handle(new GetCustomerCalendarQuery(fx.SubscriptionId), default);
        var summary = calendar.Value.Days.Single(x => x.ServiceDate == Day1);
        summary.CanEdit.Should().BeFalse();
        summary.UiStatus.Should().Be(CustomerCalendarUiStatus.Locked);
        (await fx.Handlers.Handle(new SelectCustomerDayRestaurantCommand(fx.SubscriptionId, Day1, fx.RestaurantB), default)).IsFailure.Should().BeTrue();
        (await fx.Handlers.Handle(new SelectCustomerDaySlotMealCommand(fx.SubscriptionId, Day1, fx.BreakfastA, "breakfast"), default)).IsFailure.Should().BeTrue();
    }

    [Fact]
    public async Task Dispatched_box_can_change_restaurant_ten_days_ahead_until_restaurant_confirms()
    {
        var fx = await SeedAsync(Guid.NewGuid(), BeforeDeadline);
        await FillAndComplete(fx, Day1, fx.RestaurantA, fx.BreakfastA, fx.LunchA, fx.DinnerA);
        fx.Clock.UtcNow.Returns(Day1.ToDateTime(TimeOnly.MinValue, DateTimeKind.Utc).AddDays(-10));
        var day = await fx.Db.SubscriptionDays.SingleAsync(x => x.ServiceDate == Day1);
        await new SubscriptionDayDispatchService(fx.Db, fx.Clock, new CustomerDeliveryAddressService(fx.Db, fx.Clock)).LockAndSendToRestaurantAsync(day, fx.SubscriptionId, default);
        await fx.Db.SaveChangesAsync();
        var sent = await fx.Handlers.Handle(new ConfirmCustomerDayBoxCommand(fx.SubscriptionId, Day1), default);
        sent.IsSuccess.Should().BeTrue();
        var calendar = await fx.Handlers.Handle(new GetCustomerCalendarQuery(fx.SubscriptionId), default);
        calendar.Value.Days.Single(x => x.ServiceDate == Day1).CanEdit.Should().BeTrue();
        calendar.Value.Days.Single(x => x.ServiceDate == Day1).UiStatus.Should().Be(CustomerCalendarUiStatus.AwaitingRestaurant);

        await FillAndComplete(fx, Day1, fx.RestaurantB, fx.BreakfastB, fx.LunchB, fx.DinnerB);
        var resent = await fx.Handlers.Handle(new ConfirmCustomerDayBoxCommand(fx.SubscriptionId, Day1), default);
        resent.IsSuccess.Should().BeTrue();
        resent.Value.Status.Should().Be(SubscriptionDayStatus.SentToRestaurant);
        var order = await fx.Db.RestaurantDailyOrders.SingleAsync();
        order.RestaurantId.Should().Be(fx.RestaurantB);
        order.Status.Should().Be(OrderStatus.AwaitingRestaurantConfirmation);

        var user = Substitute.For<ICurrentUser>();
        user.RestaurantId.Returns(fx.RestaurantA);
        var restaurant = new RestaurantOrderService(fx.Db, user, fx.Clock);
        (await restaurant.ConfirmOrderAsync(new ConfirmRestaurantOrderCommand(sent.Value.Id), default)).IsFailure.Should().BeTrue();
        user.RestaurantId.Returns(fx.RestaurantB);
        (await restaurant.ConfirmOrderAsync(new ConfirmRestaurantOrderCommand(sent.Value.Id), default)).IsSuccess.Should().BeTrue();
        calendar = await fx.Handlers.Handle(new GetCustomerCalendarQuery(fx.SubscriptionId), default);
        calendar.Value.Days.Single(x => x.ServiceDate == Day1).CanEdit.Should().BeFalse();
        calendar.Value.Days.Single(x => x.ServiceDate == Day1).UiStatus.Should().Be(CustomerCalendarUiStatus.Locked);
        (await fx.Handlers.Handle(new SelectCustomerDayRestaurantCommand(fx.SubscriptionId, Day1, fx.RestaurantA), default)).IsFailure.Should().BeTrue();
        (await fx.Handlers.Handle(new SelectCustomerDaySlotMealCommand(fx.SubscriptionId, Day1, fx.BreakfastB, "breakfast"), default)).IsFailure.Should().BeTrue();
    }

    [Fact]
    public async Task Dispatched_box_meal_edit_preserves_pending_confirmation()
    {
        var fx = await SeedAsync(Guid.NewGuid(), BeforeDeadline);
        await FillAndComplete(fx, Day1, fx.RestaurantA, fx.BreakfastA, fx.LunchA, fx.DinnerA);
        fx.Clock.UtcNow.Returns(Day1.ToDateTime(TimeOnly.MinValue, DateTimeKind.Utc).AddDays(-10));
        var day = await fx.Db.SubscriptionDays.SingleAsync(x => x.ServiceDate == Day1);
        await new SubscriptionDayDispatchService(fx.Db, fx.Clock, new CustomerDeliveryAddressService(fx.Db, fx.Clock)).LockAndSendToRestaurantAsync(day, fx.SubscriptionId, default);
        await fx.Db.SaveChangesAsync();
        var sent = await fx.Handlers.Handle(new ConfirmCustomerDayBoxCommand(fx.SubscriptionId, Day1), default);
        var snapshot = await fx.Db.SubscriptionRestaurantDaySnapshots.SingleAsync(x => x.RestaurantId == fx.RestaurantA && x.ServiceDate == Day1);
        var replacement = Guid.NewGuid();
        var meals = JsonSerializer.Deserialize<List<JsonElement>>(snapshot.MealsJson)!;
        snapshot.MealsJson = JsonSerializer.Serialize(meals.Cast<object>().Append(Meal(replacement, "فطور", "Breakfast replacement", "breakfast", 4, [])));
        await fx.Db.SaveChangesAsync();

        var edit = await fx.Handlers.Handle(new SelectCustomerDaySlotMealCommand(fx.SubscriptionId, Day1, replacement, "breakfast"), default);
        edit.IsSuccess.Should().BeTrue();
        edit.Value.CanEdit.Should().BeTrue();
        edit.Value.Status.Should().Be(SubscriptionDayStatus.SentToRestaurant);
        edit.Value.SelectedMeals.Should().Contain(x => x.MealId == replacement);
        var complete = await fx.Handlers.Handle(new CompleteCustomerDayBoxCommand(fx.SubscriptionId, Day1), default);
        complete.IsSuccess.Should().BeTrue();
        complete.Value.Status.Should().Be(SubscriptionDayStatus.SentToRestaurant);
        (await fx.Db.RestaurantDailyOrders.CountAsync()).Should().Be(1);
    }

    [Fact]
    public async Task Changing_dispatched_restaurant_keeps_empty_replacement_box_unconfirmable()
    {
        var fx = await SeedAsync(Guid.NewGuid(), BeforeDeadline);
        await FillAndComplete(fx, Day1, fx.RestaurantA, fx.BreakfastA, fx.LunchA, fx.DinnerA);
        var day = await fx.Db.SubscriptionDays.SingleAsync(x => x.ServiceDate == Day1);
        await new SubscriptionDayDispatchService(fx.Db, fx.Clock, new CustomerDeliveryAddressService(fx.Db, fx.Clock))
            .LockAndSendToRestaurantAsync(day, fx.SubscriptionId, default);
        await fx.Db.SaveChangesAsync();
        var changed = await fx.Handlers.Handle(new SelectCustomerDayRestaurantCommand(fx.SubscriptionId, Day1, fx.RestaurantB), default);
        changed.IsSuccess.Should().BeTrue();
        changed.Value.CanEdit.Should().BeTrue();
        changed.Value.SelectedMeals.Should().BeEmpty();
        var restaurantUser = Substitute.For<ICurrentUser>();
        restaurantUser.RestaurantId.Returns(fx.RestaurantB);
        var restaurant = new RestaurantOrderService(fx.Db, restaurantUser, fx.Clock);
        var confirm = await restaurant.ConfirmOrderAsync(new ConfirmRestaurantOrderCommand(day.Id), default);
        confirm.IsFailure.Should().BeTrue();
    }

    [Fact]
    public async Task Pending_redispatched_day_with_old_confirmation_timestamp_remains_editable_before_cutoff()
    {
        var fx = await SeedAsync(Guid.NewGuid(), BeforeDeadline);
        await FillAndComplete(fx, Day1, fx.RestaurantA, fx.BreakfastA, fx.LunchA, fx.DinnerA);
        var day = await fx.Db.SubscriptionDays.SingleAsync(x => x.ServiceDate == Day1);
        await new SubscriptionDayDispatchService(fx.Db, fx.Clock, new CustomerDeliveryAddressService(fx.Db, fx.Clock)).LockAndSendToRestaurantAsync(day, fx.SubscriptionId, default);
        day.ConfirmedAtUtc = BeforeDeadline.AddDays(-4);
        await fx.Db.SaveChangesAsync();

        var detail = await fx.Handlers.Handle(new GetCustomerCalendarDayQuery(fx.SubscriptionId, Day1), default);
        detail.Value.CanEdit.Should().BeTrue();
        detail.Value.UiStatus.Should().Be(CustomerCalendarUiStatus.AwaitingRestaurant);
        var calendar = await fx.Handlers.Handle(new GetCustomerCalendarQuery(fx.SubscriptionId), default);
        calendar.Value.Days.Single(x => x.ServiceDate == Day1).CanEdit.Should().BeTrue();
        var edit = await fx.Handlers.Handle(new SelectCustomerDayRestaurantCommand(fx.SubscriptionId, Day1, fx.RestaurantB), default);
        edit.IsSuccess.Should().BeTrue();
        edit.Value.CanEdit.Should().BeTrue();
    }

    [Fact]
    public async Task Dispatch_clears_confirmation_timestamp_from_previous_attempt()
    {
        var fx = await SeedAsync(Guid.NewGuid(), BeforeDeadline);
        await FillAndComplete(fx, Day1, fx.RestaurantA, fx.BreakfastA, fx.LunchA, fx.DinnerA);
        var day = await fx.Db.SubscriptionDays.SingleAsync(x => x.ServiceDate == Day1);
        day.ConfirmedAtUtc = BeforeDeadline.AddDays(-4);
        await new SubscriptionDayDispatchService(fx.Db, fx.Clock, new CustomerDeliveryAddressService(fx.Db, fx.Clock)).LockAndSendToRestaurantAsync(day, fx.SubscriptionId, default);
        day.Status.Should().Be(SubscriptionDayStatus.SentToRestaurant);
        day.ConfirmedAtUtc.Should().BeNull();
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
        // Post-fulfillment state: the checkout hold sits at the initially-selected restaurant
        // while the day itself has no restaurant yet.
        fx.Db.CapacityHolds.Add(new CapacityHold
        {
            RestaurantId = restaurantId,
            SubscriptionId = fx.SubscriptionId,
            ServiceDate = Day1,
            Quantity = 1,
            Status = CapacityHoldStatus.PaymentConfirmed,
            ConfirmedAtUtc = DateTime.UtcNow,
            ExpiresAtUtc = DateTime.UtcNow.AddDays(7),
        });
        await fx.Db.SaveChangesAsync();

        var result = await fx.Handlers.Handle(
            new AutoSelectCustomerDayBoxCommand(fx.SubscriptionId, Day1, LockAfter: true),
            CancellationToken.None);

        result.IsSuccess.Should().BeTrue();
        result.Value.FilledCount.Should().Be(3);
        result.Value.Status.Should().Be(SubscriptionDayStatus.SentToRestaurant);
        result.Value.IsAutoSelected.Should().BeTrue();
    }

    [Fact]
    public async Task GetDayRestaurants_filters_by_day_delivery_region_coverage()
    {
        var restA = Guid.NewGuid();
        var fx = await SeedAsync(restA, BeforeDeadline);

        var region1 = Guid.NewGuid();
        var region2 = Guid.NewGuid();

        // Restaurant A only serves Region 1
        fx.Db.RestaurantServiceRegions.Add(new RestaurantServiceRegion
        {
            RestaurantId = fx.RestaurantA,
            RegionId = region1,
            IsActive = true
        });

        // Restaurant B only serves Region 2
        fx.Db.RestaurantServiceRegions.Add(new RestaurantServiceRegion
        {
            RestaurantId = fx.RestaurantB,
            RegionId = region2,
            IsActive = true
        });

        var day = await fx.Db.SubscriptionDays.SingleAsync(x => x.SubscriptionId == fx.SubscriptionId && x.ServiceDate == Day1);
        day.DeliveryRegionId = region1;
        await fx.Db.SaveChangesAsync();

        var queryRegion1 = await fx.Handlers.Handle(new GetCustomerDayRestaurantsQuery(fx.SubscriptionId, Day1), CancellationToken.None);
        queryRegion1.IsSuccess.Should().BeTrue();
        var restaurantsRegion1 = queryRegion1.Value.Restaurants.Select(r => r.RestaurantId).ToList();
        restaurantsRegion1.Should().Contain(fx.RestaurantA);
        restaurantsRegion1.Should().NotContain(fx.RestaurantB);

        // Now change delivery address region to Region 2
        day.DeliveryRegionId = region2;
        await fx.Db.SaveChangesAsync();

        var queryRegion2 = await fx.Handlers.Handle(new GetCustomerDayRestaurantsQuery(fx.SubscriptionId, Day1), CancellationToken.None);
        queryRegion2.IsSuccess.Should().BeTrue();
        var restaurantsRegion2 = queryRegion2.Value.Restaurants.Select(r => r.RestaurantId).ToList();
        restaurantsRegion2.Should().Contain(fx.RestaurantB);
        restaurantsRegion2.Should().NotContain(fx.RestaurantA);
    }

    // ---------- Day restaurants pagination ----------

    [Fact]
    public async Task GetDayRestaurants_two_pages_over_three_restaurants_slice_and_union()
    {
        var fx = await SeedPoolAsync(3);

        var page1 = await fx.Handlers.Handle(new GetCustomerDayRestaurantsQuery(fx.SubscriptionId, Day1, null, 1, 2), CancellationToken.None);
        page1.IsSuccess.Should().BeTrue();
        page1.Value.Restaurants.Should().HaveCount(2);
        page1.Value.TotalCount.Should().Be(3);
        page1.Value.Page.Should().Be(1);
        page1.Value.PageSize.Should().Be(2);
        page1.Value.TotalPages.Should().Be(2);
        page1.Value.HasNextPage.Should().BeTrue();
        page1.Value.HasPreviousPage.Should().BeFalse();
        page1.Value.Restaurants.Select(x => x.RestaurantId).Should().Equal(fx.RestaurantIds[0], fx.RestaurantIds[1]);

        var page2 = await fx.Handlers.Handle(new GetCustomerDayRestaurantsQuery(fx.SubscriptionId, Day1, null, 2, 2), CancellationToken.None);
        page2.IsSuccess.Should().BeTrue();
        page2.Value.Restaurants.Should().HaveCount(1);
        page2.Value.Restaurants[0].RestaurantId.Should().Be(fx.RestaurantIds[2]);
        page2.Value.TotalCount.Should().Be(3);
        page2.Value.HasNextPage.Should().BeFalse();
        page2.Value.HasPreviousPage.Should().BeTrue();

        page1.Value.Restaurants.Select(x => x.RestaurantId)
            .Concat(page2.Value.Restaurants.Select(x => x.RestaurantId))
            .Should().BeEquivalentTo(fx.RestaurantIds);
    }

    [Fact]
    public async Task GetDayRestaurants_search_reduces_total_count()
    {
        var fx = await SeedPoolAsync(3);

        var result = await fx.Handlers.Handle(new GetCustomerDayRestaurantsQuery(fx.SubscriptionId, Day1, "Pool R2", 1, 2), CancellationToken.None);

        result.IsSuccess.Should().BeTrue();
        result.Value.Restaurants.Should().ContainSingle(x => x.RestaurantId == fx.RestaurantIds[1]);
        result.Value.TotalCount.Should().Be(1);
        result.Value.TotalPages.Should().Be(1);
    }

    [Fact]
    public async Task GetDayRestaurants_page_beyond_range_returns_empty_with_metadata()
    {
        var fx = await SeedPoolAsync(3);

        var result = await fx.Handlers.Handle(new GetCustomerDayRestaurantsQuery(fx.SubscriptionId, Day1, null, 9, 2), CancellationToken.None);

        result.IsSuccess.Should().BeTrue();
        result.Value.Restaurants.Should().BeEmpty();
        result.Value.TotalCount.Should().Be(3);
        result.Value.TotalPages.Should().Be(2);
        result.Value.HasNextPage.Should().BeFalse();
        result.Value.HasPreviousPage.Should().BeTrue();
    }

    [Fact]
    public async Task GetDayRestaurants_without_paging_keeps_full_list_and_reports_single_page()
    {
        var fx = await SeedPoolAsync(3);

        var result = await fx.Handlers.Handle(new GetCustomerDayRestaurantsQuery(fx.SubscriptionId, Day1), CancellationToken.None);

        result.IsSuccess.Should().BeTrue();
        result.Value.Restaurants.Should().HaveCount(3);
        result.Value.TotalCount.Should().Be(3);
        result.Value.Page.Should().Be(1);
        result.Value.PageSize.Should().Be(3);
        result.Value.TotalPages.Should().Be(1);
        result.Value.HasNextPage.Should().BeFalse();
        result.Value.HasPreviousPage.Should().BeFalse();
    }

    [Theory]
    [InlineData(0)]
    [InlineData(-4)]
    public async Task GetDayRestaurants_page_below_one_clamps_to_first(int requestedPage)
    {
        var fx = await SeedPoolAsync(3);

        var result = await fx.Handlers.Handle(new GetCustomerDayRestaurantsQuery(fx.SubscriptionId, Day1, null, requestedPage, 2), CancellationToken.None);

        result.IsSuccess.Should().BeTrue();
        result.Value.Page.Should().Be(1);
        result.Value.Restaurants.Should().HaveCount(2);
    }

    [Theory]
    [InlineData(0, 20)]
    [InlineData(-1, 20)]
    [InlineData(101, 100)]
    public async Task GetDayRestaurants_page_size_is_normalized(int requestedPageSize, int expectedPageSize)
    {
        var fx = await SeedPoolAsync(3);

        var result = await fx.Handlers.Handle(new GetCustomerDayRestaurantsQuery(fx.SubscriptionId, Day1, null, 1, requestedPageSize), CancellationToken.None);

        result.IsSuccess.Should().BeTrue();
        result.Value.PageSize.Should().Be(expectedPageSize);
        result.Value.Restaurants.Should().HaveCount(3);
    }

    [Fact]
    public async Task GetDayRestaurants_equal_names_order_deterministically_by_restaurant_id()
    {
        var fx = await SeedPoolAsync(2, sharedNameEn: "Same Name", sharedNameAr: "نفس الاسم", markInitial: false);

        var result = await fx.Handlers.Handle(new GetCustomerDayRestaurantsQuery(fx.SubscriptionId, Day1), CancellationToken.None);

        var ids = result.Value.Restaurants.Select(x => x.RestaurantId).ToList();
        ids.Should().HaveCount(2);
        ids.Should().Equal(ids.OrderBy(x => x));
    }

    [Fact]
    public async Task GetDayRestaurants_quota_exhausted_stays_disabled_beyond_page_and_search()
    {
        // Duration 6 over 4 restaurants => limit = clamp(ceil(6/4), 3, 8) = 3; R0 burned all 3.
        var fx = await SeedPoolAsync(4, exhaustedIndex: 0);

        var paged = await fx.Handlers.Handle(new GetCustomerDayRestaurantsQuery(fx.SubscriptionId, Day1, null, 1, 2), CancellationToken.None);
        paged.IsSuccess.Should().BeTrue();
        paged.Value.Restaurants.Should().HaveCount(2);
        var exhaustedOnPage = paged.Value.Restaurants.Single(x => x.RestaurantId == fx.RestaurantIds[0]);
        exhaustedOnPage.IsQuotaExhausted.Should().BeTrue();
        exhaustedOnPage.IsEnabled.Should().BeFalse();
        exhaustedOnPage.DisabledReason.Should().Be(MessageCodes.Calendar.QuotaExhausted);
        paged.Value.Restaurants.Single(x => x.RestaurantId == fx.RestaurantIds[1]).IsEnabled.Should().BeTrue();

        var searched = await fx.Handlers.Handle(new GetCustomerDayRestaurantsQuery(fx.SubscriptionId, Day1, "Pool R1"), CancellationToken.None);
        searched.IsSuccess.Should().BeTrue();
        searched.Value.Restaurants.Should().ContainSingle(x => x.RestaurantId == fx.RestaurantIds[0]);
        searched.Value.Restaurants[0].IsEnabled.Should().BeFalse();
        searched.Value.Restaurants[0].DisabledReason.Should().Be(MessageCodes.Calendar.QuotaExhausted);
    }

    [Fact]
    public async Task Dispatch_replaces_saved_meal_that_became_allergy_conflicting_and_does_not_duplicate_order()
    {
        var fx = await SeedAsync(Guid.NewGuid(), BeforeDeadline);
        var safeLunch = await RewriteRestaurantALunchAsync(fx, includeSafeAlternative: true);
        await FillAndComplete(fx, Day1, fx.RestaurantA, fx.BreakfastA, fx.LunchA, fx.DinnerA);

        // The customer saves a complete box, THEN adds a milk allergy; the saved lunch carries MILK.
        await AddMilkAllergyAsync(fx);
        fx.Clock.UtcNow.Returns(AfterDeadline);

        var lockResult = await fx.Handlers.Handle(
            new AutoSelectCustomerDayBoxCommand(fx.SubscriptionId, Day1, LockAfter: true),
            CancellationToken.None);
        lockResult.IsSuccess.Should().BeTrue();
        lockResult.Value.Status.Should().Be(SubscriptionDayStatus.SentToRestaurant);

        var sentMealIds = await fx.Db.DailyMealSelections.AsNoTracking()
            .Where(x => x.SubscriptionDayId == lockResult.Value.Id)
            .Select(x => x.MealId).ToListAsync();
        sentMealIds.Should().NotContain(fx.LunchA, "a saved meal that now conflicts with the allergy must not be sent");
        sentMealIds.Should().Contain(safeLunch, "the conflicting slot is completed with the safe alternative from the same restaurant");
        sentMealIds.Should().Contain(fx.BreakfastA).And.Contain(fx.DinnerA);
        (await fx.Db.RestaurantDailyOrders.CountAsync()).Should().Be(1);

        // Re-running the dispatch must not create a second order; the already-sent day
        // refuses re-dispatch and stays sent with exactly one kitchen order.
        await fx.Handlers.Handle(
            new AutoSelectCustomerDayBoxCommand(fx.SubscriptionId, Day1, LockAfter: true),
            CancellationToken.None);
        (await fx.Db.RestaurantDailyOrders.CountAsync()).Should().Be(1);
        var dayAfterRepeat = await fx.Db.SubscriptionDays.AsNoTracking().SingleAsync(x => x.Id == lockResult.Value.Id);
        dayAfterRepeat.Status.Should().Be(SubscriptionDayStatus.SentToRestaurant);
    }

    [Fact]
    public async Task Dispatch_with_unrepairable_saved_allergy_conflict_requires_replacement_without_order()
    {
        var fx = await SeedAsync(Guid.NewGuid(), BeforeDeadline);
        await RewriteRestaurantALunchAsync(fx, includeSafeAlternative: false);
        await FillAndComplete(fx, Day1, fx.RestaurantA, fx.BreakfastA, fx.LunchA, fx.DinnerA);
        await AddMilkAllergyAsync(fx);
        fx.Clock.UtcNow.Returns(AfterDeadline);

        var result = await fx.Handlers.Handle(
            new AutoSelectCustomerDayBoxCommand(fx.SubscriptionId, Day1, LockAfter: true),
            CancellationToken.None);
        result.IsFailure.Should().BeTrue();
        result.Error.Code.Should().Be(MessageCodes.Calendar.NoSafeBox);

        var day = await fx.Db.SubscriptionDays.AsNoTracking()
            .SingleAsync(x => x.SubscriptionId == fx.SubscriptionId && x.ServiceDate == Day1);
        day.Status.Should().Be(SubscriptionDayStatus.ReplacementRequired);
        day.LockAtUtc.Should().BeNull();
        // No kitchen order may exist for an unsafe box.
        (await fx.Db.RestaurantDailyOrders.CountAsync()).Should().Be(0);
    }

    [Fact]
    public async Task Customer_confirm_does_not_save_or_send_box_that_became_allergy_conflicting()
    {
        var fx = await SeedAsync(Guid.NewGuid(), BeforeDeadline);
        await RewriteRestaurantALunchAsync(fx, includeSafeAlternative: false);
        await FillAndComplete(fx, Day1, fx.RestaurantA, fx.BreakfastA, fx.LunchA, fx.DinnerA);
        await AddMilkAllergyAsync(fx);

        var confirm = await fx.Handlers.Handle(
            new ConfirmCustomerDayBoxCommand(fx.SubscriptionId, Day1),
            CancellationToken.None);
        confirm.IsFailure.Should().BeTrue();
        confirm.Error.Code.Should().Be(MessageCodes.Calendar.NoSafeBox);

        var day = await fx.Db.SubscriptionDays.AsNoTracking()
            .SingleAsync(x => x.SubscriptionId == fx.SubscriptionId && x.ServiceDate == Day1);
        day.Status.Should().Be(SubscriptionDayStatus.ReplacementRequired);
        (await fx.Db.RestaurantDailyOrders.CountAsync()).Should().Be(0);

        var repeat = await fx.Handlers.Handle(
            new ConfirmCustomerDayBoxCommand(fx.SubscriptionId, Day1),
            CancellationToken.None);
        repeat.IsFailure.Should().BeTrue();
        (await fx.Db.RestaurantDailyOrders.CountAsync()).Should().Be(0);
    }

    [Fact]
    public async Task Auto_selection_tries_safe_alternative_after_capacity_rejection()
    {
        var fx = await SeedAsync(Guid.NewGuid(), BeforeDeadline);
        await RewriteRestaurantALunchAsync(fx, includeSafeAlternative: false);
        await AddMilkAllergyAsync(fx);
        await SeedHoldAtRestaurantAAsync(fx);
        await SeedCapacityAsync(fx, fx.RestaurantB, maxBoxes: 0);
        var subscription = await fx.Db.Subscriptions.SingleAsync();
        var pool = await fx.Db.SubscriptionRestaurantPoolSnapshots.SingleAsync();
        var third = FrozenRestaurant(pool.Id, subscription, Guid.NewGuid(), "مطعم ج", "Restaurant C", false);
        fx.Db.Add(third);
        fx.Db.Add(RestaurantDay(third.Id, subscription.Id, third.RestaurantId, Day1, Guid.NewGuid(),
            MealsJson(Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid(), "C")));
        // B is the designated recommendation; its live capacity rejects the transfer.
        (await fx.Db.SubscriptionDays.SingleAsync()).RestaurantId = fx.RestaurantB;
        await fx.Db.SaveChangesAsync();
        await SeedCapacityAsync(fx, third.RestaurantId, maxBoxes: 5);
        var result = await CreateEngineWithCapacityTransfer(fx, AfterDeadline)
            .AutoSelectFullBoxAsync(fx.SubscriptionId, Day1, true, false, default);
        result.IsSuccess.Should().BeTrue();
        result.Value.RestaurantId.Should().Be(third.RestaurantId);
        (await fx.Db.CapacityHolds.SingleAsync()).RestaurantId.Should().Be(third.RestaurantId);
        (await fx.Db.RestaurantDailyOrders.SingleAsync()).RestaurantId.Should().Be(third.RestaurantId);
    }

    [Fact]
    public async Task Exhausted_capacity_lock_job_escalates_once_without_moving_hold_or_dispatching()
    {
        var fx = await SeedAsync(Guid.NewGuid(), BeforeDeadline);
        await RewriteRestaurantALunchAsync(fx, includeSafeAlternative: false);
        await AddMilkAllergyAsync(fx);
        await SeedHoldAtRestaurantAAsync(fx);
        await SeedCapacityAsync(fx, fx.RestaurantB, maxBoxes: 0);
        fx.Clock.UtcNow.Returns(AfterDeadline);
        var provider = Substitute.For<IServiceProvider>();
        provider.GetService(typeof(IAppDbContext)).Returns(fx.Db);
        provider.GetService(typeof(IDateTimeProvider)).Returns(fx.Clock);
        var engine = CreateEngineWithCapacityTransfer(fx, AfterDeadline);
        provider.GetService(typeof(CustomerCalendarEngine)).Returns(engine);
        var job = new MealMate.Application.Features.CustomerCheckout.BackgroundJobs.SubscriptionDayLockJob(
            Substitute.For<Microsoft.Extensions.DependencyInjection.IServiceScopeFactory>(),
            Substitute.For<Microsoft.Extensions.Logging.ILogger<MealMate.Application.Features.CustomerCheckout.BackgroundJobs.SubscriptionDayLockJob>>());
        var result = await job.ProcessDueDaysAsync(provider, default);
        result.EscalatedCount.Should().Be(1);
        (await fx.Db.SubscriptionDays.SingleAsync()).Status.Should().Be(SubscriptionDayStatus.ReplacementRequired);
        (await fx.Db.CapacityHolds.SingleAsync()).RestaurantId.Should().Be(fx.RestaurantA);
        (await fx.Db.RestaurantDailyOrders.CountAsync()).Should().Be(0);
        var repeat = await job.ProcessDueDaysAsync(provider, default);
        repeat.EscalatedCount.Should().Be(0);
        (await fx.Db.OutboxMessages.CountAsync(x => x.EventType == "CustomerReplacementRequired")).Should().Be(1);
    }

    [Theory]
    [InlineData(600, 66.7, 22.2)]
    [InlineData(0, 0, null)]
    [InlineData(-100, 0, null)]
    public async Task Nutrition_percentages_use_persisted_program_target(int mainCalories, decimal totalPct, double? mealPct)
    {
        var fx = await SeedAsync(Guid.NewGuid(), BeforeDeadline);
        var subscription = await fx.Db.Subscriptions.SingleAsync();
        fx.Db.ProgramNutritionStandards.AddRange(
            new ProgramNutritionStandard { ProgramId = subscription.ProgramId, MealCategory = "MAIN", KcalMin = mainCalories, KcalMax = mainCalories },
            new ProgramNutritionStandard { ProgramId = subscription.ProgramId, MealCategory = "SNACK" });
        await fx.Db.SaveChangesAsync();
        await FillAndComplete(fx, Day1, fx.RestaurantA, fx.BreakfastA, fx.LunchA, fx.DinnerA);
        var day = await fx.Handlers.Handle(new GetCustomerCalendarDayQuery(fx.SubscriptionId, Day1), default);
        day.IsSuccess.Should().BeTrue();
        day.Value.NutritionTotals.CaloriesPercentage.Should().Be(totalPct);
        day.Value.SelectedMeals.Should().OnlyContain(m => m.CaloriesPercentage == (decimal?)mealPct);
    }

    [Fact]
    public async Task Nutrition_missing_target_returns_neutral_total_and_unknown_meal_percentage()
    {
        var fx = await SeedAsync(Guid.NewGuid(), BeforeDeadline);
        await FillAndComplete(fx, Day1, fx.RestaurantA, fx.BreakfastA, fx.LunchA, fx.DinnerA);
        var day = await fx.Handlers.Handle(new GetCustomerCalendarDayQuery(fx.SubscriptionId, Day1), default);
        day.Value.NutritionTotals.CaloriesPercentage.Should().Be(0);
        day.Value.SelectedMeals.Should().OnlyContain(m => m.CaloriesPercentage == null);
    }

    [Fact]
    public async Task Auto_selection_restaurant_switch_moves_the_capacity_hold()
    {
        var fx = await SeedAsync(Guid.NewGuid(), BeforeDeadline);
        await RewriteRestaurantALunchAsync(fx, includeSafeAlternative: false);
        await AddMilkAllergyAsync(fx);
        // Fulfillment-shaped state: the checkout hold still sits at restaurant A while the day
        // itself has no restaurant; B has free capacity.
        await SeedHoldAtRestaurantAAsync(fx);
        await SeedCapacityAsync(fx, fx.RestaurantB, maxBoxes: 5);

        var engine = CreateEngineWithCapacityTransfer(fx, AfterDeadline);
        var result = await new CustomerCalendarHandlers(engine).Handle(
            new AutoSelectCustomerDayBoxCommand(fx.SubscriptionId, Day1, LockAfter: true),
            CancellationToken.None);
        result.IsSuccess.Should().BeTrue();

        var day = await fx.Db.SubscriptionDays.AsNoTracking()
            .SingleAsync(x => x.SubscriptionId == fx.SubscriptionId && x.ServiceDate == Day1);
        day.RestaurantId.Should().Be(fx.RestaurantB);
        day.Status.Should().Be(SubscriptionDayStatus.SentToRestaurant);

        // Day, hold, frozen snapshot and the kitchen order must all point at B.
        var hold = await fx.Db.CapacityHolds.AsNoTracking()
            .SingleAsync(x => x.SubscriptionId == fx.SubscriptionId && x.ServiceDate == Day1);
        hold.RestaurantId.Should().Be(fx.RestaurantB);
        hold.MenuPlanVersionId.Should().Be((await fx.Db.SubscriptionRestaurantDaySnapshots.SingleAsync(x => x.RestaurantId == fx.RestaurantB)).MenuPlanVersionId);

        var snapshot = await fx.Db.SubscriptionDayMealSnapshots.AsNoTracking()
            .SingleAsync(x => x.SubscriptionDayId == day.Id);
        snapshot.RestaurantId.Should().Be(fx.RestaurantB);

        var order = await fx.Db.RestaurantDailyOrders.AsNoTracking()
            .SingleAsync(x => x.SubscriptionDayId == day.Id);
        order.RestaurantId.Should().Be(fx.RestaurantB);
    }

    [Fact]
    public async Task Auto_selection_restaurant_switch_is_refused_when_target_capacity_is_full()
    {
        var fx = await SeedAsync(Guid.NewGuid(), BeforeDeadline);
        await RewriteRestaurantALunchAsync(fx, includeSafeAlternative: false);
        await AddMilkAllergyAsync(fx);
        await SeedHoldAtRestaurantAAsync(fx);
        await SeedCapacityAsync(fx, fx.RestaurantB, maxBoxes: 1);
        // Another identity consumes the only box at B.
        fx.Db.CapacityHolds.Add(new CapacityHold
        {
            RestaurantId = fx.RestaurantB,
            SubscriptionId = Guid.NewGuid(),
            ServiceDate = Day1,
            Quantity = 1,
            Status = CapacityHoldStatus.Confirmed,
            ConfirmedAtUtc = DateTime.UtcNow,
            ExpiresAtUtc = DateTime.UtcNow.AddDays(7),
        });
        await fx.Db.SaveChangesAsync();

        var engine = CreateEngineWithCapacityTransfer(fx, AfterDeadline);
        var result = await new CustomerCalendarHandlers(engine).Handle(
            new AutoSelectCustomerDayBoxCommand(fx.SubscriptionId, Day1, LockAfter: true),
            CancellationToken.None);
        result.IsFailure.Should().BeTrue();
        result.Error.Code.Should().Be("RESTAURANT_CAPACITY_FULL");

        // Previous state is preserved: no day switch, hold still at A, no kitchen order.
        var day = await fx.Db.SubscriptionDays.AsNoTracking()
            .SingleAsync(x => x.SubscriptionId == fx.SubscriptionId && x.ServiceDate == Day1);
        day.RestaurantId.Should().BeNull();
        day.Status.Should().Be(SubscriptionDayStatus.Open);

        var hold = await fx.Db.CapacityHolds.AsNoTracking()
            .SingleAsync(x => x.SubscriptionId == fx.SubscriptionId && x.ServiceDate == Day1);
        hold.RestaurantId.Should().Be(fx.RestaurantA);

        (await fx.Db.RestaurantDailyOrders.CountAsync()).Should().Be(0);
        (await fx.Db.SubscriptionDayMealSnapshots.CountAsync(x => x.SubscriptionDayId == day.Id)).Should().Be(0);
    }

    [Fact]
    public async Task Auto_selection_restaurant_switch_requires_the_capacity_transfer_service()
    {
        var fx = await SeedAsync(Guid.NewGuid(), BeforeDeadline);
        await RewriteRestaurantALunchAsync(fx, includeSafeAlternative: false);
        await AddMilkAllergyAsync(fx);
        await SeedHoldAtRestaurantAAsync(fx);
        await SeedCapacityAsync(fx, fx.RestaurantB, maxBoxes: 5);
        fx.Clock.UtcNow.Returns(AfterDeadline);

        // fx.Engine has no capacity transfer service wired; a write path that can change the
        // restaurant must fail closed instead of switching silently.
        var result = await fx.Handlers.Handle(
            new AutoSelectCustomerDayBoxCommand(fx.SubscriptionId, Day1, LockAfter: true),
            CancellationToken.None);
        result.IsFailure.Should().BeTrue();
        result.Error.Code.Should().Be("calendar.capacity_transfer_unavailable");

        var day = await fx.Db.SubscriptionDays.AsNoTracking()
            .SingleAsync(x => x.SubscriptionId == fx.SubscriptionId && x.ServiceDate == Day1);
        day.RestaurantId.Should().BeNull();
        (await fx.Db.RestaurantDailyOrders.CountAsync()).Should().Be(0);
    }

    private static async Task SeedHoldAtRestaurantAAsync(FlowFixture fx)
    {
        fx.Db.CapacityHolds.Add(new CapacityHold
        {
            RestaurantId = fx.RestaurantA,
            SubscriptionId = fx.SubscriptionId,
            ServiceDate = Day1,
            Quantity = 1,
            Status = CapacityHoldStatus.PaymentConfirmed,
            ConfirmedAtUtc = DateTime.UtcNow,
            ExpiresAtUtc = DateTime.UtcNow.AddDays(7),
        });
        await fx.Db.SaveChangesAsync();
    }

    private static async Task SeedCapacityAsync(FlowFixture fx, Guid restaurantId, int maxBoxes)
    {
        fx.Db.DailyCapacities.Add(new DailyCapacity
        {
            RestaurantId = restaurantId,
            ServiceDate = Day1,
            CapacityType = CapacityType.Base,
            MaxBoxes = maxBoxes,
            ReservedBoxes = 0,
        });
        await fx.Db.SaveChangesAsync();
    }

    private static CustomerCalendarEngine CreateEngineWithCapacityTransfer(FlowFixture fx, DateTime nowUtc)
    {
        var customerId = fx.Db.Subscriptions
            .Where(x => x.Id == fx.SubscriptionId)
            .Select(x => x.CustomerUserId)
            .Single();
        var user = Substitute.For<ICurrentUser>();
        user.UserId.Returns(customerId);
        user.UserType.Returns(UserType.Customer);
        var clock = Substitute.For<IDateTimeProvider>();
        clock.UtcNow.Returns(nowUtc);
        clock.TodayUtc.Returns(DateOnly.FromDateTime(nowUtc));
        var transfer = new CapacityTransferService(
            fx.Db,
            Substitute.For<IApplicationLockManager>(),
            new InlineTransactionExecutor(),
            clock,
            Substitute.For<ICapacityConcurrencyLock>(),
            new RestaurantCapacityReader(fx.Db, clock));
        var dispatch = new SubscriptionDayDispatchService(fx.Db, clock, new CustomerDeliveryAddressService(fx.Db, clock));
        return new CustomerCalendarEngine(fx.Db, user, clock, new RestaurantDiversificationCalculator(), dispatch, capacityTransferService: transfer);
    }

    private sealed class InlineTransactionExecutor : ICheckoutTransactionExecutor
    {
        public Task<T> ExecuteAsync<T>(Func<CancellationToken, Task<T>> operation, CancellationToken cancellationToken = default) => operation(cancellationToken);
    }

    private static async Task<Guid> RewriteRestaurantALunchAsync(FlowFixture fx, bool includeSafeAlternative)
    {
        // Restaurant A's lunch becomes a milk meal; optionally add a safe lunch alternative.
        var safeLunch = Guid.NewGuid();
        var meals = new List<object>
        {
            Meal(fx.BreakfastA, "فطور أ", "Breakfast A", "breakfast", 1, []),
            Meal(fx.LunchA, "غداء بالحليب", "Milk Lunch A", "lunch", 2, ["MILK"]),
        };
        if (includeSafeAlternative)
            meals.Insert(2, Meal(safeLunch, "غداء آمن", "Safe Lunch A", "lunch", 4, []));
        meals.Add(Meal(fx.DinnerA, "عشاء أ", "Dinner A", "dinner", 3, []));

        var daySnapshot = await fx.Db.SubscriptionRestaurantDaySnapshots
            .SingleAsync(x => x.RestaurantId == fx.RestaurantA && x.ServiceDate == Day1);
        daySnapshot.MealsJson = JsonSerializer.Serialize(meals);
        await fx.Db.SaveChangesAsync();
        return safeLunch;
    }

    private static async Task AddMilkAllergyAsync(FlowFixture fx)
    {
        var customerId = await fx.Db.Subscriptions
            .Where(x => x.Id == fx.SubscriptionId)
            .Select(x => x.CustomerUserId)
            .SingleAsync();
        fx.Db.CustomerAllergyPreferences.Add(new CustomerAllergyPreference
        {
            CustomerUserId = customerId,
            AllergenCode = "MILK",
            AllergenNameAr = "حليب",
            AllergenNameEn = "Milk",
            IsDislike = false,
        });
        await fx.Db.SaveChangesAsync();
    }

    private static async Task<PoolFixture> SeedPoolAsync(
        int restaurantCount, string? sharedNameEn = null, string? sharedNameAr = null, bool markInitial = true, int? exhaustedIndex = null)
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
            FulfillmentCompletedAtUtc = BeforeDeadline.AddDays(-1)
        };

        var pool = new SubscriptionRestaurantPoolSnapshot
        {
            SubscriptionId = subscription.Id,
            RestaurantCount = restaurantCount,
            EligibilityCheckedAtUtc = BeforeDeadline
        };
        var restaurantIds = new List<Guid>();
        for (var i = 0; i < restaurantCount; i++)
        {
            var restaurantId = Guid.NewGuid();
            restaurantIds.Add(restaurantId);
            var snapshot = FrozenRestaurant(pool.Id, subscription, restaurantId,
                sharedNameAr ?? $"مطعم مجموعة {i + 1}",
                sharedNameEn ?? $"Pool R{i + 1}",
                markInitial && i == 0);
            db.Add(snapshot);
            db.Add(RestaurantDay(snapshot.Id, subscription.Id, restaurantId, Day1, Guid.NewGuid(),
                MealsJson(Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid(), $"R{i + 1}")));
        }

        db.AddRange(program, bundle, subscription, pool);
        db.Add(new SubscriptionDay
        {
            SubscriptionId = subscription.Id,
            ServiceDate = Day1,
            Status = SubscriptionDayStatus.Open,
            SelectionDeadlineUtc = Deadline
        });

        if (exhaustedIndex is { } burned)
        {
            for (var used = 0; used < 3; used++)
            {
                db.Add(new SubscriptionDay
                {
                    SubscriptionId = subscription.Id,
                    ServiceDate = Day1.AddDays(-(used + 1)),
                    Status = SubscriptionDayStatus.CustomerSelected,
                    RestaurantId = restaurantIds[burned]
                });
            }
        }

        await db.SaveChangesAsync();

        var user = Substitute.For<ICurrentUser>();
        user.UserId.Returns(customerId);
        user.UserType.Returns(UserType.Customer);
        var clock = Substitute.For<IDateTimeProvider>();
        clock.UtcNow.Returns(BeforeDeadline);
        clock.TodayUtc.Returns(DateOnly.FromDateTime(BeforeDeadline));

        var dispatch = new SubscriptionDayDispatchService(db, clock, new CustomerDeliveryAddressService(db, clock));
        var engine = new CustomerCalendarEngine(db, user, clock, new RestaurantDiversificationCalculator(), dispatch);
        var handlers = new CustomerCalendarHandlers(engine);

        return new PoolFixture(db, handlers, subscription.Id, restaurantIds);
    }

    private sealed record PoolFixture(
        AppDbContext Db,
        CustomerCalendarHandlers Handlers,
        Guid SubscriptionId,
        IReadOnlyList<Guid> RestaurantIds);

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

        var dispatch = new SubscriptionDayDispatchService(db, clock, new CustomerDeliveryAddressService(db, clock));
        var engine = new CustomerCalendarEngine(db, user, clock, new RestaurantDiversificationCalculator(), dispatch);
        var handlers = new CustomerCalendarHandlers(engine);

        return new FlowFixture(
            db, handlers, clock, subscription.Id, restaurantId, restaurantB,
            breakfastA, lunchA, dinnerA, breakfastB, lunchB, dinnerB, engine);
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
        Guid DinnerB,
        CustomerCalendarEngine Engine);
}
