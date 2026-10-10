using FluentAssertions;
using MealMate.Application.Common.Abstractions;
using MealMate.Application.Common.Localization;
using MealMate.Application.Common.Models;
using MealMate.Application.Features.CustomerCalendar;
using MealMate.Application.Features.DriverDelivery.CustomerTracking;
using MealMate.Application.Features.ProgramsSubscriptions;
using MealMate.Application.Features.RestaurantOrders;
using MealMate.Domain.CalendarMeals;
using MealMate.Domain.Common;
using MealMate.Domain.DriverDelivery;
using MealMate.Domain.Enums;
using MealMate.Domain.IdentityAccess;
using MealMate.Domain.ProgramsSubscriptions;
using MealMate.Domain.RestaurantOperations;
using MealMate.Persistence;
using Microsoft.EntityFrameworkCore;
using NSubstitute;
using Xunit;

namespace MealMate.UnitTests.CustomerCalendar;

/// <summary>
/// The calendar summary, calendar detail and delivery widget must report the same
/// delivery status from the same saved events; CanEdit stays false while a locked day
/// moves through fulfillment; and repeated readiness/print calls never rewind a day
/// that already left the kitchen.
/// </summary>
public sealed class CustomerCalendarDeliveryStatusTests
{
    private static readonly DateTime Now = new(2026, 10, 10, 9, 0, 0, DateTimeKind.Utc);
    private static readonly DateOnly ServiceDate = new(2026, 10, 10);

    private static AppDbContext CreateDb()
        => new(
            new DbContextOptionsBuilder<AppDbContext>().UseInMemoryDatabase(Guid.NewGuid().ToString()).Options,
            Substitute.For<IDomainEventDispatcher>());

    private sealed record Fixture(
        AppDbContext Db,
        Guid SubscriptionId,
        Guid DayId,
        Guid RestaurantId,
        ICurrentUser CustomerUser);

    private static async Task<Fixture> SeedAsync(SubscriptionDayStatus dayStatus)
    {
        var db = CreateDb();
        var customerUserId = Guid.NewGuid();
        var restaurantId = Guid.NewGuid();
        var subscription = new Subscription
        {
            CustomerUserId = customerUserId,
            ProgramId = Guid.NewGuid(),
            BundleId = Guid.NewGuid(),
            DurationDays = 6,
            StartDate = ServiceDate,
            EndDate = ServiceDate.AddDays(5),
            Status = SubscriptionStatus.Active,
            FulfillmentCompletedAtUtc = Now
        };
        var day = new SubscriptionDay
        {
            SubscriptionId = subscription.Id,
            ServiceDate = ServiceDate,
            Status = dayStatus,
            RestaurantId = restaurantId
        };
        db.AddRange(subscription, day);
        await db.SaveChangesAsync();
        return new Fixture(db, subscription.Id, day.Id, restaurantId, UserOf(customerUserId));
    }

    private static ICurrentUser UserOf(Guid userId)
    {
        var user = Substitute.For<ICurrentUser>();
        user.IsAuthenticated.Returns(true);
        user.UserId.Returns(userId);
        user.UserType.Returns(UserType.Customer);
        return user;
    }

    private static ILocalizedMessageProvider Messages(bool isArabic = false)
    {
        var messages = Substitute.For<ILocalizedMessageProvider>();
        messages.CurrentCulture.Returns(isArabic
            ? System.Globalization.CultureInfo.GetCultureInfo("ar")
            : System.Globalization.CultureInfo.InvariantCulture);
        messages.GetMessage(Arg.Any<string>(), Arg.Any<string>())
            .Returns(callInfo => callInfo.ArgAt<string>(1));
        return messages;
    }

    private static CustomerCalendarEngine CreateEngine(Fixture fx, ILocalizedMessageProvider? messages = null)
    {
        var clock = Substitute.For<IDateTimeProvider>();
        clock.UtcNow.Returns(Now);
        clock.TodayUtc.Returns(ServiceDate);
        var transfer = Substitute.For<ICapacityTransferService>();
        transfer.TransferCapacityAsync(
                Arg.Any<Guid>(), Arg.Any<Guid>(), Arg.Any<DateOnly>(), Arg.Any<Guid?>(),
                Arg.Any<Guid>(), Arg.Any<int>(), Arg.Any<CancellationToken>())
            .Returns(MealMate.Application.Common.Models.Result.Success());
        return new CustomerCalendarEngine(
            fx.Db, fx.CustomerUser, clock, new RestaurantDiversificationCalculator(),
            capacityTransferService: transfer,
            deliveryRead: new CustomerDeliveryReadService(fx.Db),
            messages: messages ?? Messages());
    }

    private static async Task<RestaurantDailyOrder> SeedReadyOrderAsync(Fixture fx, OrderStatus status = OrderStatus.ReadyForPickup)
    {
        var order = new RestaurantDailyOrder
        {
            RestaurantId = fx.RestaurantId,
            SubscriptionDayId = fx.DayId,
            ServiceDate = ServiceDate,
            Status = status,
            ReadyForPickupAtUtc = Now.AddMinutes(-20),
            BoxCount = 1
        };
        fx.Db.RestaurantDailyOrders.Add(order);
        await fx.Db.SaveChangesAsync();
        return order;
    }

    [Fact]
    public async Task Calendar_summary_and_detail_share_ready_status_before_any_stop()
    {
        var fx = await SeedAsync(SubscriptionDayStatus.Preparing);
        var order = await SeedReadyOrderAsync(fx);
        var engine = CreateEngine(fx);

        var screen = await engine.GetCalendarAsync(fx.SubscriptionId, CancellationToken.None);
        var detail = await engine.GetDayAsync(fx.SubscriptionId, ServiceDate, CancellationToken.None);

        screen.IsSuccess.Should().BeTrue();
        detail.IsSuccess.Should().BeTrue();
        var summary = screen.Value!.Days.Should().ContainSingle().Subject;
        summary.DeliveryStatus.Should().Be("ReadyForPickup");
        summary.DeliveryStatusTextEn.Should().Be("Ready for pickup");
        summary.DeliveryStatusTextAr.Should().Be("تم التجهيز");
        summary.IsDeliveryTrackable.Should().BeFalse();
        summary.DeliveryStatusChangedAtUtc.Should().Be(order.ReadyForPickupAtUtc);
        summary.CanEdit.Should().BeFalse(); // Preparing stays locked for selection

        detail.Value!.DeliveryStatus.Should().Be("ReadyForPickup");
        detail.Value.DeliveryStatusText.Should().Be("Ready for pickup");
        detail.Value.CanEdit.Should().BeFalse();
    }

    [Fact]
    public async Task Pickup_progresses_delivery_status_to_out_for_delivery_across_all_readers()
    {
        var fx = await SeedAsync(SubscriptionDayStatus.Preparing);
        var order = await SeedReadyOrderAsync(fx);
        var engine = CreateEngine(fx);

        var driver = new DriverProfile { UserId = Guid.NewGuid(), FullNameAr = "سائق", FullNameEn = "Driver" };
        fx.Db.DriverProfiles.Add(driver);
        var trip = new Trip { RestaurantId = fx.RestaurantId, Code = "TRP-C1", Status = TripStatus.InProgress, PlannedDate = ServiceDate };
        fx.Db.Trips.Add(trip);
        fx.Db.DriverAssignments.Add(new DriverAssignment { TripId = trip.Id, DriverProfileId = driver.Id, Status = "Active", AssignedAtUtc = Now });
        var stop = new TripStop
        {
            TripId = trip.Id,
            SubscriptionDayId = fx.DayId,
            DailyOrderId = order.Id,
            OrderCode = "#BX-77",
            CustomerMaskedId = "CUST",
            ZoneNameAr = "z",
            ZoneNameEn = "z",
            MealSummaryAr = "وجبة",
            MealSummaryEn = "Meal",
            SlotLabel = "lunch",
            Status = TripStopStatus.PickedUp,
            ArrivedAtUtc = null
        };
        fx.Db.TripStops.Add(stop);
        fx.Db.OrderHandovers.Add(new OrderHandover
        {
            DailyOrderId = order.Id,
            TripId = trip.Id,
            TripStopId = stop.Id,
            DriverProfileId = driver.Id,
            HandedOverAtUtc = Now.AddMinutes(-10),
            Status = "Completed"
        });
        var day = await fx.Db.SubscriptionDays.SingleAsync(d => d.Id == fx.DayId);
        day.Status = SubscriptionDayStatus.OutForDelivery;
        order.Status = OrderStatus.OnTheWay;
        await fx.Db.SaveChangesAsync();

        var screen = await engine.GetCalendarAsync(fx.SubscriptionId, CancellationToken.None);
        var detail = await engine.GetDayAsync(fx.SubscriptionId, ServiceDate, CancellationToken.None);
        var widget = await new GetCustomerDeliveryWidgetQueryHandler(
                fx.Db, fx.CustomerUser,
                new MealMate.Application.Features.FamilyManager.FamilyCalendarAccessGate(fx.Db, fx.CustomerUser),
                new CustomerDeliveryReadService(fx.Db),
                CreateNullLocationBuilder(),
                Substitute.For<IDateTimeProvider>(),
                Messages())
            .Handle(new GetCustomerDeliveryWidgetQuery(ServiceDate, fx.SubscriptionId), CancellationToken.None);

        screen.Value!.Days.Should().ContainSingle().Which.DeliveryStatus.Should().Be("OutForDelivery");
        detail.Value!.DeliveryStatus.Should().Be("OutForDelivery");
        widget.Value!.Items.Should().ContainSingle().Which.DeliveryStatus.Should().Be("OutForDelivery");

        // Arrival alone never becomes delivery.
        stop.ArrivedAtUtc = Now.AddMinutes(-3);
        await fx.Db.SaveChangesAsync();
        var afterArrival = await engine.GetDayAsync(fx.SubscriptionId, ServiceDate, CancellationToken.None);
        afterArrival.Value!.DeliveryStatus.Should().Be("OutForDelivery");
        afterArrival.Value.DeliveryStatusChangedAtUtc.Should().NotBeNull();

        // Proof-confirmed completion becomes Delivered — day, detail and widget agree.
        stop.Status = TripStopStatus.Delivered;
        stop.DeliveredAtUtc = Now.AddMinutes(-1);
        day.Status = SubscriptionDayStatus.Delivered;
        order.Status = OrderStatus.Delivered;
        await fx.Db.SaveChangesAsync();

        var afterDelivery = await engine.GetDayAsync(fx.SubscriptionId, ServiceDate, CancellationToken.None);
        afterDelivery.Value!.DeliveryStatus.Should().Be("Delivered");
        afterDelivery.Value.CanEdit.Should().BeFalse();
        afterDelivery.Value.DeliveryStatusTextAr.Should().Be("تم التسليم");
        var deliveredSummary = (await engine.GetCalendarAsync(fx.SubscriptionId, CancellationToken.None)).Value!.Days.Should().ContainSingle().Subject;
        deliveredSummary.DeliveryStatus.Should().Be("Delivered");
        deliveredSummary.DeliveryStatusChangedAtUtc.Should().Be(Now.AddMinutes(-1));
    }

    [Fact]
    public async Task Repeated_prep_status_after_pickup_is_rejected_and_does_not_rewind_the_day()
    {
        // AssignedToDriver can still legally transition to ReadyForPickup (reassignment
        // rule), so without the day-level guard a stale kitchen "ready" call rewound an
        // already-picked-up day back to Preparing.
        var fx = await SeedAsync(SubscriptionDayStatus.OutForDelivery);
        await SeedReadyOrderAsync(fx, OrderStatus.AssignedToDriver);
        var day = await fx.Db.SubscriptionDays.SingleAsync(d => d.Id == fx.DayId);
        day.Status = SubscriptionDayStatus.OutForDelivery;
        await fx.Db.SaveChangesAsync();

        var restaurantUser = Substitute.For<ICurrentUser>();
        restaurantUser.RestaurantId.Returns(fx.RestaurantId);
        restaurantUser.UserType.Returns(UserType.Restaurant);
        var clock = Substitute.For<IDateTimeProvider>();
        clock.UtcNow.Returns(Now);
        var service = new RestaurantOrderService(
            fx.Db,
            restaurantUser,
            clock,
            Substitute.For<ICustomerPushNotifier>(),
            Substitute.For<IRestaurantOrderNotifier>());

        var result = await service.UpdateDailyOrderPrepStatusAsync(
            new UpdateDailyOrderPrepStatusCommand(fx.DayId, "ready"), CancellationToken.None);

        result.IsFailure.Should().BeTrue();
        var unchanged = await fx.Db.SubscriptionDays.SingleAsync(d => d.Id == fx.DayId);
        unchanged.Status.Should().Be(SubscriptionDayStatus.OutForDelivery);

        // The daily order must not have been rewound either.
        var order = await fx.Db.RestaurantDailyOrders.SingleAsync(o => o.SubscriptionDayId == fx.DayId);
        order.Status.Should().Be(OrderStatus.AssignedToDriver);
    }

    [Fact]
    public async Task Repeated_prep_status_for_delivered_day_is_rejected_and_keeps_delivered()
    {
        var fx = await SeedAsync(SubscriptionDayStatus.Delivered);
        await SeedReadyOrderAsync(fx, OrderStatus.Delivered);
        var restaurantUser = Substitute.For<ICurrentUser>();
        restaurantUser.RestaurantId.Returns(fx.RestaurantId);
        restaurantUser.UserType.Returns(UserType.Restaurant);
        var clock = Substitute.For<IDateTimeProvider>();
        clock.UtcNow.Returns(Now);
        var service = new RestaurantOrderService(
            fx.Db,
            restaurantUser,
            clock,
            Substitute.For<ICustomerPushNotifier>(),
            Substitute.For<IRestaurantOrderNotifier>());

        var result = await service.UpdateDailyOrderPrepStatusAsync(
            new UpdateDailyOrderPrepStatusCommand(fx.DayId, "ready"), CancellationToken.None);

        result.IsFailure.Should().BeTrue();
        (await fx.Db.SubscriptionDays.SingleAsync(d => d.Id == fx.DayId)).Status
            .Should().Be(SubscriptionDayStatus.Delivered);
    }

    [Fact]
    public async Task Engine_without_read_service_still_maps_calendar_without_delivery_fields()
    {
        var fx = await SeedAsync(SubscriptionDayStatus.Preparing);
        var engine = CreateEngine(fx, messages: null);
        // deliveryRead defaulted internally from db; no orders exist → NotStarted.
        var screen = await engine.GetCalendarAsync(fx.SubscriptionId, CancellationToken.None);
        screen.IsSuccess.Should().BeTrue();
        var summary = screen.Value!.Days.Should().ContainSingle().Subject;
        summary.DeliveryStatus.Should().Be("NotStarted");
        summary.IsDeliveryTrackable.Should().BeFalse();
    }

    /// <summary>Location substitute that reports nothing (all evidence absent).</summary>
    private static ICustomerDeliveryLocationBuilder CreateNullLocationBuilder()
    {
        var builder = Substitute.For<ICustomerDeliveryLocationBuilder>();
        builder.BuildAsync(Arg.Any<CustomerDeliveryLocationRequest>(), Arg.Any<CancellationToken>())
            .Returns(new CustomerDeliveryLocationResult(null, null, null, null, false, null, null, null));
        return builder;
    }
}
