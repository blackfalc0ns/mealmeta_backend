using FluentAssertions;
using MealMate.Application.Common.Localization;
using MealMate.Application.Features.CustomerCalendar;
using MealMate.Application.Features.IdentityAccess.Customers;
using MealMate.Domain.Enums;
using MealMate.Domain.IdentityAccess;
using MealMate.Domain.RestaurantOperations;
using Microsoft.EntityFrameworkCore;
using NSubstitute;

namespace MealMate.UnitTests.CustomerCalendar;

public sealed partial class CustomerCalendarRestaurantFlowTests
{
    [Fact]
    public async Task Missing_date_menu_disables_restaurant_and_returns_known_errors_without_using_another_date()
    {
        var fx = await SeedAsync(Guid.NewGuid(), BeforeDeadline);
        var menu = await fx.Db.SubscriptionRestaurantDaySnapshots.SingleAsync(x => x.SubscriptionId == fx.SubscriptionId && x.RestaurantId == fx.RestaurantA);
        menu.ServiceDate = Day1.AddDays(1);
        await fx.Db.SaveChangesAsync();

        var list = await fx.Engine.GetDayRestaurantsAsync(fx.SubscriptionId, Day1, null, default);
        var restaurant = list.Value.Restaurants.Single(x => x.RestaurantId == fx.RestaurantA);
        restaurant.IsEnabled.Should().BeFalse();
        restaurant.DisabledReason.Should().Be(MessageCodes.Calendar.BoxNotReady);
        var meals = await fx.Engine.GetRestaurantMealsAsync(fx.SubscriptionId, Day1, fx.RestaurantA, null, 1, 20, default);
        meals.IsFailure.Should().BeTrue();
        meals.Error.Code.Should().Be(MessageCodes.Calendar.BoxNotReady);
        var selected = await fx.Engine.SelectRestaurantAsync(fx.SubscriptionId, Day1, fx.RestaurantA, default);
        selected.IsFailure.Should().BeTrue();
        selected.Error.Code.Should().Be(MessageCodes.Calendar.BoxNotReady);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task Busy_current_restaurant_is_not_auto_selected_when_enabled_alternative_exists(bool savedBox)
    {
        var fx = await SeedAsync(Guid.NewGuid(), BeforeDeadline);
        if (savedBox) await FillAndComplete(fx, Day1, fx.RestaurantA, fx.BreakfastA, fx.LunchA, fx.DinnerA);
        else (await fx.Engine.SelectRestaurantAsync(fx.SubscriptionId, Day1, fx.RestaurantA, default)).IsSuccess.Should().BeTrue();
        fx.Db.Add(new BusyStatus { RestaurantId = fx.RestaurantA, IsBusy = true,
            StartsAtUtc = Day1.ToDateTime(TimeOnly.MinValue, DateTimeKind.Utc),
            EndsAtUtc = Day1.AddDays(1).ToDateTime(TimeOnly.MinValue, DateTimeKind.Utc) });
        await fx.Db.SaveChangesAsync();
        await SeedCapacityAsync(fx, fx.RestaurantB, 5);

        var list = await fx.Engine.GetDayRestaurantsAsync(fx.SubscriptionId, Day1, null, default);
        list.Value.Restaurants.Single(x => x.RestaurantId == fx.RestaurantA).IsEnabled.Should().BeFalse();
        list.Value.Restaurants.Single(x => x.RestaurantId == fx.RestaurantB).IsEnabled.Should().BeTrue();
        var auto = await CreateEngineWithCapacityTransfer(fx, BeforeDeadline)
            .AutoSelectFullBoxAsync(fx.SubscriptionId, Day1, false, true, default);
        auto.IsSuccess.Should().BeTrue();
        auto.Value.RestaurantId.Should().Be(fx.RestaurantB);
        auto.Value.SelectedMeals.Select(x => x.MealId).Should().BeEquivalentTo([fx.BreakfastB, fx.LunchB, fx.DinnerB]);
    }

    [Theory]
    [InlineData(false, false)]
    [InlineData(false, true)]
    [InlineData(true, true)]
    public async Task Saved_box_without_region_coverage_cannot_be_confirmed_or_dispatched(bool scheduledCutoff, bool afterDeadline)
    {
        var fx = await SeedAsync(Guid.NewGuid(), BeforeDeadline);
        await AddDeliveryCoverageAsync(fx, bothRestaurants: false);
        await FillAndComplete(fx, Day1, fx.RestaurantA, fx.BreakfastA, fx.LunchA, fx.DinnerA);
        var coverage = await fx.Db.RestaurantServiceRegions.SingleAsync(x => x.RestaurantId == fx.RestaurantA);
        coverage.IsActive = false;
        await fx.Db.SaveChangesAsync();
        fx.Clock.UtcNow.Returns(afterDeadline ? AfterDeadline : BeforeDeadline);

        var result = scheduledCutoff
            ? await fx.Engine.AutoSelectFullBoxAsync(fx.SubscriptionId, Day1, true, true, default)
            : await fx.Engine.ConfirmAndSendToRestaurantAsync(fx.SubscriptionId, Day1, default);
        result.IsFailure.Should().BeTrue();
        result.Error.Code.Should().Be("calendar.restaurant_coverage_unavailable");
        (await fx.Db.RestaurantDailyOrders.CountAsync()).Should().Be(0);
        (await fx.Db.SubscriptionDays.SingleAsync(x => x.SubscriptionId == fx.SubscriptionId)).Status.Should().Be(SubscriptionDayStatus.CustomerSelected);
    }

    [Fact]
    public async Task Saved_box_moves_to_covered_alternative_with_capacity_before_cutoff_dispatch()
    {
        var fx = await SeedAsync(Guid.NewGuid(), BeforeDeadline);
        await AddDeliveryCoverageAsync(fx, bothRestaurants: true);
        await FillAndComplete(fx, Day1, fx.RestaurantA, fx.BreakfastA, fx.LunchA, fx.DinnerA);
        var coverage = await fx.Db.RestaurantServiceRegions.SingleAsync(x => x.RestaurantId == fx.RestaurantA);
        coverage.IsActive = false;
        await fx.Db.SaveChangesAsync();
        await SeedCapacityAsync(fx, fx.RestaurantB, 5);

        var auto = await CreateEngineWithCapacityTransfer(fx, AfterDeadline)
            .AutoSelectFullBoxAsync(fx.SubscriptionId, Day1, true, true, default);
        auto.IsSuccess.Should().BeTrue();
        auto.Value.RestaurantId.Should().Be(fx.RestaurantB);
        auto.Value.Status.Should().Be(SubscriptionDayStatus.SentToRestaurant);
        (await fx.Db.RestaurantDailyOrders.SingleAsync()).RestaurantId.Should().Be(fx.RestaurantB);
        (await fx.Db.CapacityHolds.SingleAsync()).RestaurantId.Should().Be(fx.RestaurantB);
    }

    [Fact]
    public async Task Dispatch_rechecks_actual_delivery_region_after_snapshot_before_creating_order()
    {
        var fx = await SeedAsync(Guid.NewGuid(), BeforeDeadline);
        await AddDeliveryCoverageAsync(fx, bothRestaurants: false);
        await FillAndComplete(fx, Day1, fx.RestaurantA, fx.BreakfastA, fx.LunchA, fx.DinnerA);
        var profile = await fx.Db.CustomerProfiles.SingleAsync();
        profile.ActiveDeliveryRegionId = Guid.NewGuid();
        await fx.Db.SaveChangesAsync();
        var day = await fx.Db.SubscriptionDays.SingleAsync(x => x.SubscriptionId == fx.SubscriptionId);

        var result = await new SubscriptionDayDispatchService(fx.Db, fx.Clock, new CustomerDeliveryAddressService(fx.Db, fx.Clock))
            .LockAndSendToRestaurantAsync(day, fx.SubscriptionId, default);
        result.IsFailure.Should().BeTrue();
        result.Error.Code.Should().Be("calendar.restaurant_coverage_unavailable");
        (await fx.Db.RestaurantDailyOrders.CountAsync()).Should().Be(0);
        fx.Db.RestaurantDailyOrders.Local.Should().BeEmpty();
        day.Status.Should().Be(SubscriptionDayStatus.CustomerSelected);
        day.LockAtUtc.Should().BeNull();
    }

    private static async Task AddDeliveryCoverageAsync(FlowFixture fx, bool bothRestaurants)
    {
        var regionId = Guid.NewGuid();
        var subscription = await fx.Db.Subscriptions.SingleAsync(x => x.Id == fx.SubscriptionId);
        subscription.RegionId = regionId;
        fx.Db.Add(new CustomerProfile { UserId = subscription.CustomerUserId,
            ActiveDeliveryRegionId = regionId, ActiveDeliveryAddressLine = "Delivery fixture address" });
        fx.Db.Add(new RestaurantServiceRegion { RestaurantId = fx.RestaurantA, RegionId = regionId, IsActive = true });
        if (bothRestaurants) fx.Db.Add(new RestaurantServiceRegion { RestaurantId = fx.RestaurantB, RegionId = regionId, IsActive = true });
        await fx.Db.SaveChangesAsync();
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task Disabled_automatic_selection_still_escalates_invalid_saved_box_with_actual_reason(bool missingMenu)
    {
        var fx = await SeedAsync(Guid.NewGuid(), BeforeDeadline);
        await AddDeliveryCoverageAsync(fx, bothRestaurants: false);
        await FillAndComplete(fx, Day1, fx.RestaurantA, fx.BreakfastA, fx.LunchA, fx.DinnerA);
        if (missingMenu)
            fx.Db.SubscriptionRestaurantDaySnapshots.Remove(await fx.Db.SubscriptionRestaurantDaySnapshots.SingleAsync(x => x.RestaurantId == fx.RestaurantA));
        else (await fx.Db.RestaurantServiceRegions.SingleAsync()).IsActive = false;
        fx.Db.Add(new MealMate.Domain.AdminDashboard.SystemSetting
        {
            Key = MealMate.Application.Features.AdminDashboard.OperationalPolicies.OperationalPolicyService.SettingKey,
            IsActive = true,
            ValueJson = System.Text.Json.JsonSerializer.Serialize(new MealMate.Application.Features.AdminDashboard.OperationalPolicies.OperationalPolicySettings()
                with { AutoSelection = new(false) })
        });
        await fx.Db.SaveChangesAsync();
        fx.Clock.UtcNow.Returns(AfterDeadline);
        var provider = Substitute.For<IServiceProvider>();
        provider.GetService(typeof(MealMate.Application.Common.Abstractions.IAppDbContext)).Returns(fx.Db);
        provider.GetService(typeof(MealMate.Application.Common.Abstractions.IDateTimeProvider)).Returns(fx.Clock);
        provider.GetService(typeof(CustomerCalendarEngine)).Returns(fx.Engine);
        var job = new MealMate.Application.Features.CustomerCheckout.BackgroundJobs.SubscriptionDayLockJob(
            Substitute.For<Microsoft.Extensions.DependencyInjection.IServiceScopeFactory>(),
            Substitute.For<Microsoft.Extensions.Logging.ILogger<MealMate.Application.Features.CustomerCheckout.BackgroundJobs.SubscriptionDayLockJob>>());

        var result = await job.ProcessDueDaysAsync(provider, default);
        result.EscalatedCount.Should().Be(1);
        var detail = await fx.Engine.GetDayAsync(fx.SubscriptionId, Day1, default);
        detail.Value.Status.Should().Be(SubscriptionDayStatus.ReplacementRequired);
        detail.Value.FailureReasonCode.Should().Be(missingMenu ? MessageCodes.Calendar.BoxNotReady : "calendar.restaurant_coverage_unavailable");
        (await fx.Db.RestaurantDailyOrders.CountAsync()).Should().Be(0);
        (await fx.Db.OutboxMessages.CountAsync(x => x.EventType == "CustomerReplacementRequired")).Should().Be(1);
    }
}
