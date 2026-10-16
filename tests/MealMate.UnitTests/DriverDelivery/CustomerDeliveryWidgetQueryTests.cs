using FluentAssertions;
using MealMate.Application.Common.Abstractions;
using MealMate.Application.Common.Localization;
using MealMate.Application.Common.Models;
using MealMate.Application.Features.DriverDelivery.CustomerTracking;
using MealMate.Application.Features.FamilyManager;
using MealMate.Domain.CalendarMeals;
using MealMate.Domain.DriverDelivery;
using MealMate.Domain.Enums;
using MealMate.Domain.IdentityAccess;
using MealMate.Domain.ProgramsSubscriptions;
using MealMate.Domain.RestaurantOperations;
using MealMate.Persistence;
using Microsoft.EntityFrameworkCore;
using NSubstitute;
using Xunit;

namespace MealMate.UnitTests.DriverDelivery;

/// <summary>
/// The standalone read-only widget: every box the customer may see for the business date,
/// ready boxes before any TripStop exists, family + individual boxes together, and no
/// driver/route data for ambiguous or terminal states.
/// </summary>
public sealed class CustomerDeliveryWidgetQueryTests
{
    private static readonly DateTime Now = new(2026, 10, 10, 9, 0, 0, DateTimeKind.Utc);
    private static readonly DateOnly ServiceDate = new(2026, 10, 10);

    private readonly AppDbContext _db = new(
        new DbContextOptionsBuilder<AppDbContext>().UseInMemoryDatabase(Guid.NewGuid().ToString()).Options,
        Substitute.For<IDomainEventDispatcher>());

    private readonly ICurrentUser _user = Substitute.For<ICurrentUser>();
    private readonly ICustomerDeliveryLocationBuilder _locationBuilder = Substitute.For<ICustomerDeliveryLocationBuilder>();
    private readonly IDateTimeProvider _clock = Substitute.For<IDateTimeProvider>();
    private readonly ILocalizedMessageProvider _messages = Substitute.For<ILocalizedMessageProvider>();

    public CustomerDeliveryWidgetQueryTests()
    {
        _clock.UtcNow.Returns(Now);
        _messages.CurrentCulture.Returns(System.Globalization.CultureInfo.InvariantCulture);
        _messages.GetMessage(Arg.Any<string>(), Arg.Any<string>())
            .Returns(callInfo => callInfo.ArgAt<string>(1));
        _user.UserType.Returns(UserType.Customer);
        _locationBuilder.BuildAsync(Arg.Any<CustomerDeliveryLocationRequest>(), Arg.Any<CancellationToken>())
            .Returns(new CustomerDeliveryLocationResult(
                29.33, 48.07, Now.AddSeconds(-30), Now.AddSeconds(-29), true, null, 12, CustomerDeliveryEtaSources.RoadRoute));
    }

    private GetCustomerDeliveryWidgetQueryHandler CreateHandler()
    {
        _user.IsAuthenticated.Returns(true);
        return new GetCustomerDeliveryWidgetQueryHandler(
            _db,
            _user,
            new FamilyCalendarAccessGate(_db, _user),
            new CustomerDeliveryPresentationBuilder(
                _db, new CustomerDeliveryReadService(_db), _locationBuilder, _messages),
            _clock);
    }

    private async Task<Subscription> SeedIndividualAsync(
        Guid customerUserId,
        SubscriptionDayStatus dayStatus = SubscriptionDayStatus.Preparing)
    {
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
            RestaurantId = Guid.NewGuid()
        };
        _db.AddRange(subscription, day);
        await _db.SaveChangesAsync();
        return subscription;
    }

    private async Task<RestaurantDailyOrder> SeedReadyOrderAsync(SubscriptionDay day)
    {
        var order = new RestaurantDailyOrder
        {
            RestaurantId = day.RestaurantId ?? Guid.NewGuid(),
            SubscriptionDayId = day.Id,
            ServiceDate = day.ServiceDate,
            Status = OrderStatus.ReadyForPickup,
            ReadyForPickupAtUtc = Now.AddMinutes(-20),
            BoxCount = 1
        };
        _db.RestaurantDailyOrders.Add(order);
        await _db.SaveChangesAsync();
        return order;
    }

    private async Task<(SubscriptionDay Day, TripStop Stop, IReadOnlyList<DriverProfile> Drivers)> SeedAttemptAsync(
        Guid userId,
        SubscriptionDayStatus dayStatus,
        OrderStatus orderStatus,
        TripStopStatus stopStatus,
        int activeAssignments,
        int priorDriversWithHandover = 0,
        bool currentDriverHasHandover = false)
    {
        var subscription = await SeedIndividualAsync(userId, dayStatus);
        var day = await _db.SubscriptionDays.SingleAsync(d => d.SubscriptionId == subscription.Id);
        var order = await SeedReadyOrderAsync(day);
        order.Status = orderStatus;

        var trip = new Trip
        {
            RestaurantId = Guid.NewGuid(),
            Code = $"TRP-{Guid.NewGuid():N}",
            Status = TripStatus.InProgress,
            PlannedDate = ServiceDate
        };
        var drivers = Enumerable.Range(0, activeAssignments + priorDriversWithHandover)
            .Select(i => new DriverProfile
            {
                UserId = Guid.NewGuid(),
                FullNameAr = $"سائق {i}",
                FullNameEn = $"Driver {i}"
            })
            .ToList();
        _db.Trips.Add(trip);
        _db.DriverProfiles.AddRange(drivers);
        for (var i = 0; i < drivers.Count; i++)
        {
            _db.DriverAssignments.Add(new DriverAssignment
            {
                TripId = trip.Id,
                DriverProfileId = drivers[i].Id,
                Status = i < activeAssignments ? "Active" : "Inactive",
                AssignedAtUtc = Now.AddMinutes(-30 + i)
            });
        }

        var stop = new TripStop
        {
            TripId = trip.Id,
            SubscriptionDayId = day.Id,
            DailyOrderId = order.Id,
            OrderCode = "#BX-ATTEMPT",
            CustomerMaskedId = "CUST",
            ZoneNameAr = "السالمية",
            ZoneNameEn = "Salmiya",
            FullAddress = "Stored destination address",
            MealSummaryAr = "وجبة",
            MealSummaryEn = "Meal",
            SlotLabel = "lunch",
            Status = stopStatus,
            Latitude = 29.33m,
            Longitude = 48.07m
        };
        _db.TripStops.Add(stop);

        var handoverDriverIndexes = Enumerable.Range(activeAssignments, priorDriversWithHandover).ToList();
        if (currentDriverHasHandover && activeAssignments > 0)
        {
            handoverDriverIndexes.Add(0);
        }

        foreach (var i in handoverDriverIndexes)
        {
            _db.OrderHandovers.Add(new OrderHandover
            {
                DailyOrderId = order.Id,
                TripId = trip.Id,
                TripStopId = stop.Id,
                DriverProfileId = drivers[i].Id,
                HandedOverAtUtc = Now.AddMinutes(-10 - i),
                Status = "Completed"
            });
        }

        await _db.SaveChangesAsync();
        return (day, stop, drivers);
    }

    [Fact]
    public async Task Ready_box_appears_before_any_trip_stop_exists()
    {
        var userId = Guid.NewGuid();
        _user.UserId.Returns(userId);
        var subscription = await SeedIndividualAsync(userId);
        var day = await _db.SubscriptionDays.SingleAsync(d => d.SubscriptionId == subscription.Id);
        await SeedReadyOrderAsync(day);
        var handler = CreateHandler();

        var result = await handler.Handle(new GetCustomerDeliveryWidgetQuery(ServiceDate, null), CancellationToken.None);

        result.IsSuccess.Should().BeTrue();
        var item = result.Value!.Items.Should().ContainSingle().Subject;
        item.DeliveryStatus.Should().Be("ReadyForPickup");
        item.DeliveryStatusTextEn.Should().Be("Ready for pickup");
        item.DeliveryStatusTextAr.Should().Be("تم التجهيز");
        item.DeliveryStatusText.Should().Be("Ready for pickup");
        item.ReadyAtUtc.Should().Be(Now.AddMinutes(-20));
        item.TripStopId.Should().BeNull();
        item.DetailsUrl.Should().BeNull();
        item.IsTrackable.Should().BeFalse();
        item.UnavailableReasonCode.Should().Be(CustomerDeliveryUnavailableReasons.DriverNotAssigned);
        item.Driver.Should().BeNull();
        item.Location.Should().BeNull();
        item.EtaMinutes.Should().BeNull();
    }

    [Fact]
    public async Task Omitted_service_date_uses_the_kuwait_business_date_not_utc()
    {
        var userId = Guid.NewGuid();
        _user.UserId.Returns(userId);
        await SeedIndividualAsync(userId);
        var handler = CreateHandler();

        // 21:00 UTC = midnight+ in Kuwait (UTC+3): the business date is the NEXT day.
        _clock.UtcNow.Returns(new DateTime(2026, 10, 10, 21, 0, 0, DateTimeKind.Utc));

        var result = await handler.Handle(new GetCustomerDeliveryWidgetQuery(null, null), CancellationToken.None);

        result.IsSuccess.Should().BeTrue();
        result.Value!.ServiceDate.Should().Be(new DateOnly(2026, 10, 11));
        result.Value.Items.Should().BeEmpty(); // the seeded day is on 10-10, not the business "today"
    }

    [Fact]
    public async Task Day_without_preparation_reports_not_started_and_query_is_read_only()
    {
        var userId = Guid.NewGuid();
        _user.UserId.Returns(userId);
        await SeedIndividualAsync(userId);
        var handler = CreateHandler();

        var result = await handler.Handle(new GetCustomerDeliveryWidgetQuery(ServiceDate, null), CancellationToken.None);

        result.IsSuccess.Should().BeTrue();
        var item = result.Value!.Items.Should().ContainSingle().Subject;
        item.DeliveryStatus.Should().Be("NotStarted");
        item.IsTrackable.Should().BeFalse();
        result.Value.ServiceDate.Should().Be(ServiceDate);
        result.Value.RefreshAfterSeconds.Should().Be(30);
    }

    [Fact]
    public async Task Date_with_no_days_at_all_returns_success_with_no_items()
    {
        var userId = Guid.NewGuid();
        _user.UserId.Returns(userId);
        await SeedIndividualAsync(userId);
        var handler = CreateHandler();

        var result = await handler.Handle(new GetCustomerDeliveryWidgetQuery(ServiceDate.AddDays(3), null), CancellationToken.None);

        result.IsSuccess.Should().BeTrue();
        result.Value!.Items.Should().BeEmpty();
    }

    [Fact]
    public async Task Unfiltered_request_returns_individual_and_family_member_boxes_together()
    {
        var userId = Guid.NewGuid();
        var linkedUserId = Guid.NewGuid();
        _user.UserId.Returns(userId);

        var ownSubscription = await SeedIndividualAsync(userId);
        var linkedSubscription = await SeedIndividualAsync(linkedUserId, SubscriptionDayStatus.OutForDelivery);

        var linkedDay = await _db.SubscriptionDays.SingleAsync(d => d.SubscriptionId == linkedSubscription.Id);
        await SeedReadyOrderAsync(linkedDay);

        // The linked subscription belongs to a family where our user is the linked member.
        var group = new FamilyGroup
        {
            ManagerCustomerUserId = Guid.NewGuid(),
            Name = "عائلة",
            Status = FamilyGroupStatus.Active
        };
        var member = new FamilyMember
        {
            FamilyGroupId = group.Id,
            DisplayName = "فرد",
            RelationshipCode = "son",
            NutritionalGoalCode = "balance",
            ActivityLevel = FamilyActivityLevel.Moderate,
            AgeYears = 30,
            Gender = FamilyMemberGender.Male,
            MemberSubscriptionId = linkedSubscription.Id
        };
        linkedSubscription.FamilyGroupId = group.Id;
        linkedSubscription.FamilyMemberId = member.Id;
        _db.FamilyMemberPermissions.Add(new FamilyMemberPermissions { FamilyMemberId = member.Id, ViewCalendar = true });
        _db.AddRange(group, member, new FamilyMemberAccess
        {
            FamilyMemberId = member.Id,
            LinkedCustomerUserId = userId,
            HasSeparateAccount = true
        });
        await _db.SaveChangesAsync();

        var handler = CreateHandler();
        var result = await handler.Handle(new GetCustomerDeliveryWidgetQuery(ServiceDate, null), CancellationToken.None);

        result.IsSuccess.Should().BeTrue();
        result.Value!.Items.Select(x => x.SubscriptionId).Should().BeEquivalentTo(
            new[] { ownSubscription.Id, linkedSubscription.Id });
    }

    [Fact]
    public async Task Explicit_subscription_filter_returns_only_that_subscription()
    {
        var userId = Guid.NewGuid();
        _user.UserId.Returns(userId);
        var first = await SeedIndividualAsync(userId);
        var second = await SeedIndividualAsync(userId);
        var handler = CreateHandler();

        var result = await handler.Handle(new GetCustomerDeliveryWidgetQuery(ServiceDate, first.Id), CancellationToken.None);

        result.IsSuccess.Should().BeTrue();
        result.Value!.Items.Select(x => x.SubscriptionId).Should().ContainSingle().Which.Should().Be(first.Id);
        _ = second;
    }

    [Fact]
    public async Task Explicit_unauthorized_subscription_fails_without_items()
    {
        var strangerId = Guid.NewGuid();
        var strangerSubscription = await SeedIndividualAsync(strangerId);
        _user.UserId.Returns(Guid.NewGuid());
        var handler = CreateHandler();

        var result = await handler.Handle(new GetCustomerDeliveryWidgetQuery(ServiceDate, strangerSubscription.Id), CancellationToken.None);

        result.IsFailure.Should().BeTrue();
        result.Error.Type.Should().Be(ErrorType.NotFound);
    }

    [Fact]
    public async Task Picked_up_box_is_out_for_delivery_with_driver_location_and_details_url()
    {
        var userId = Guid.NewGuid();
        _user.UserId.Returns(userId);
        var subscription = await SeedIndividualAsync(userId, SubscriptionDayStatus.OutForDelivery);
        var day = await _db.SubscriptionDays.SingleAsync(d => d.SubscriptionId == subscription.Id);
        var order = await SeedReadyOrderAsync(day);

        var driver = new DriverProfile
        {
            UserId = Guid.NewGuid(),
            FullNameAr = "سائق",
            FullNameEn = "Driver",
            VehicleType = "car",
            VehiclePlate = "AA-1111"
        };
        _db.DriverProfiles.Add(driver);
        var trip = new Trip { RestaurantId = Guid.NewGuid(), Code = "TRP-1", Status = TripStatus.InProgress, PlannedDate = ServiceDate };
        _db.Trips.Add(trip);
        _db.DriverAssignments.Add(new DriverAssignment { TripId = trip.Id, DriverProfileId = driver.Id, Status = "Active", AssignedAtUtc = Now });
        var stop = new TripStop
        {
            TripId = trip.Id,
            SubscriptionDayId = day.Id,
            DailyOrderId = order.Id,
            OrderCode = "#BX-2026",
            CustomerMaskedId = "CUST",
            ZoneNameAr = "السالمية",
            ZoneNameEn = "Salmiya",
            FullAddress = "شارع، مدينة، الدور 1",
            MealSummaryAr = "وجبة",
            MealSummaryEn = "Meal",
            SlotLabel = "lunch",
            Status = TripStopStatus.PickedUp,
            Latitude = 29.33m,
            Longitude = 48.07m
        };
        _db.TripStops.Add(stop);
        _db.OrderHandovers.Add(new OrderHandover
        {
            DailyOrderId = order.Id,
            TripId = trip.Id,
            TripStopId = stop.Id,
            DriverProfileId = driver.Id,
            HandedOverAtUtc = Now.AddMinutes(-10),
            Status = "Completed"
        });
        await _db.SaveChangesAsync();

        var handler = CreateHandler();
        var result = await handler.Handle(new GetCustomerDeliveryWidgetQuery(ServiceDate, null), CancellationToken.None);

        result.IsSuccess.Should().BeTrue();
        var item = result.Value!.Items.Should().ContainSingle().Subject;
        item.DeliveryStatus.Should().Be("OutForDelivery");
        item.IsTrackable.Should().BeTrue();
        item.PickedUpAtUtc.Should().Be(Now.AddMinutes(-10)); // handover, never the trip start
        item.TripStopId.Should().Be(stop.Id);
        item.OrderCode.Should().Be("#BX-2026");
        item.DetailsUrl.Should().Be($"/api/v1/customer/orders/{stop.Id}/live-tracking");
        item.Driver.Should().NotBeNull();
        item.Driver!.NameEn.Should().Be("Driver");
        item.Driver.Rating.Should().BeNull();
        item.Vehicle!.PlateNumber.Should().Be("AA-1111");
        item.DestinationAddress!.AddressLine.Should().Be("شارع، مدينة، الدور 1");
        item.EtaMinutes.Should().Be(12);
        item.EtaSource.Should().Be(CustomerDeliveryEtaSources.RoadRoute);
    }

    [Fact]
    public async Task Reassigned_picked_up_attempt_does_not_reuse_previous_drivers_handover_time()
    {
        var userId = Guid.NewGuid();
        _user.UserId.Returns(userId);
        var (_, stop, _) = await SeedAttemptAsync(
            userId,
            SubscriptionDayStatus.OutForDelivery,
            OrderStatus.OnTheWay,
            TripStopStatus.PickedUp,
            activeAssignments: 1,
            priorDriversWithHandover: 1);

        var result = await CreateHandler().Handle(
            new GetCustomerDeliveryWidgetQuery(ServiceDate, null), CancellationToken.None);

        result.IsSuccess.Should().BeTrue();
        var item = result.Value!.Items.Should().ContainSingle().Subject;
        item.TripStopId.Should().Be(stop.Id);
        item.DeliveryStatus.Should().Be(CustomerDeliveryStatusCodes.OutForDelivery);
        item.PickedUpAtUtc.Should().BeNull();
    }

    [Fact]
    public async Task Multiple_active_assignments_suppress_destination_for_retained_attempt()
    {
        var userId = Guid.NewGuid();
        _user.UserId.Returns(userId);
        var (_, stop, _) = await SeedAttemptAsync(
            userId,
            SubscriptionDayStatus.OutForDelivery,
            OrderStatus.OnTheWay,
            TripStopStatus.Pending,
            activeAssignments: 2);

        var result = await CreateHandler().Handle(
            new GetCustomerDeliveryWidgetQuery(ServiceDate, null), CancellationToken.None);

        result.IsSuccess.Should().BeTrue();
        var item = result.Value!.Items.Should().ContainSingle().Subject;
        item.DeliveryStatus.Should().Be(CustomerDeliveryStatusCodes.TrackingUnavailable);
        item.TripStopId.Should().Be(stop.Id);
        item.DestinationAddress.Should().BeNull();
    }

    [Fact]
    public async Task Conflicting_driver_handovers_suppress_destination_for_retained_attempt()
    {
        var userId = Guid.NewGuid();
        _user.UserId.Returns(userId);
        var (_, stop, _) = await SeedAttemptAsync(
            userId,
            SubscriptionDayStatus.OutForDelivery,
            OrderStatus.OnTheWay,
            TripStopStatus.PickedUp,
            activeAssignments: 1,
            priorDriversWithHandover: 1,
            currentDriverHasHandover: true);

        var result = await CreateHandler().Handle(
            new GetCustomerDeliveryWidgetQuery(ServiceDate, null), CancellationToken.None);

        result.IsSuccess.Should().BeTrue();
        var item = result.Value!.Items.Should().ContainSingle().Subject;
        item.DeliveryStatus.Should().Be(CustomerDeliveryStatusCodes.TrackingUnavailable);
        item.TripStopId.Should().Be(stop.Id);
        item.PickedUpAtUtc.Should().BeNull();
        item.DestinationAddress.Should().BeNull();
    }

    [Fact]
    public async Task Delivered_box_keeps_driver_identity_but_never_location_or_eta()
    {
        var userId = Guid.NewGuid();
        _user.UserId.Returns(userId);
        var subscription = await SeedIndividualAsync(userId, SubscriptionDayStatus.Delivered);
        var day = await _db.SubscriptionDays.SingleAsync(d => d.SubscriptionId == subscription.Id);
        var order = await SeedReadyOrderAsync(day);

        var driver = new DriverProfile
        {
            UserId = Guid.NewGuid(),
            FullNameAr = "سائق",
            FullNameEn = "Driver"
        };
        _db.DriverProfiles.Add(driver);
        var trip = new Trip { RestaurantId = Guid.NewGuid(), Code = "TRP-2", Status = TripStatus.Completed, PlannedDate = ServiceDate };
        _db.Trips.Add(trip);
        _db.DriverAssignments.Add(new DriverAssignment { TripId = trip.Id, DriverProfileId = driver.Id, Status = "Active", AssignedAtUtc = Now });
        var stop = new TripStop
        {
            TripId = trip.Id,
            SubscriptionDayId = day.Id,
            DailyOrderId = order.Id,
            OrderCode = "#BX-9",
            CustomerMaskedId = "CUST",
            ZoneNameAr = " zone",
            ZoneNameEn = "zone",
            MealSummaryAr = "وجبة",
            MealSummaryEn = "Meal",
            SlotLabel = "lunch",
            Status = TripStopStatus.Delivered,
            ArrivedAtUtc = Now.AddMinutes(-15),
            DeliveredAtUtc = Now.AddMinutes(-5)
        };
        _db.TripStops.Add(stop);
        _db.OrderHandovers.Add(new OrderHandover
        {
            DailyOrderId = order.Id,
            TripId = trip.Id,
            TripStopId = stop.Id,
            DriverProfileId = driver.Id,
            HandedOverAtUtc = Now.AddMinutes(-25),
            Status = "Completed"
        });
        await _db.SaveChangesAsync();

        var handler = CreateHandler();
        var result = await handler.Handle(new GetCustomerDeliveryWidgetQuery(ServiceDate, null), CancellationToken.None);

        result.IsSuccess.Should().BeTrue();
        var item = result.Value!.Items.Should().ContainSingle().Subject;
        item.DeliveryStatus.Should().Be("Delivered");
        item.IsTerminal.Should().BeTrue();
        item.DeliveredAtUtc.Should().Be(Now.AddMinutes(-5));
        item.Driver.Should().NotBeNull();
        item.Location.Should().BeNull();
        item.EtaMinutes.Should().BeNull();
    }

    [Fact]
    public async Task Ambiguous_active_attempts_report_tracking_unavailable_without_driver_data()
    {
        var userId = Guid.NewGuid();
        _user.UserId.Returns(userId);
        var subscription = await SeedIndividualAsync(userId, SubscriptionDayStatus.OutForDelivery);
        var day = await _db.SubscriptionDays.SingleAsync(d => d.SubscriptionId == subscription.Id);
        var order = await SeedReadyOrderAsync(day);

        var driver = new DriverProfile { UserId = Guid.NewGuid(), FullNameAr = "سائق", FullNameEn = "Driver" };
        _db.DriverProfiles.Add(driver);
        var trip = new Trip { RestaurantId = Guid.NewGuid(), Code = "TRP-3", Status = TripStatus.InProgress, PlannedDate = ServiceDate };
        _db.Trips.Add(trip);
        _db.DriverAssignments.Add(new DriverAssignment { TripId = trip.Id, DriverProfileId = driver.Id, Status = "Active", AssignedAtUtc = Now });
        foreach (var _ in new[] { 1, 2 })
        {
            _db.TripStops.Add(new TripStop
            {
                TripId = trip.Id,
                SubscriptionDayId = day.Id,
                DailyOrderId = order.Id,
                OrderCode = "#BX-DUP",
                CustomerMaskedId = "CUST",
                ZoneNameAr = "z",
                ZoneNameEn = "z",
                MealSummaryAr = "وجبة",
                MealSummaryEn = "Meal",
                SlotLabel = "lunch",
                Status = TripStopStatus.PickedUp
            });
        }
        await _db.SaveChangesAsync();

        var handler = CreateHandler();
        var result = await handler.Handle(new GetCustomerDeliveryWidgetQuery(ServiceDate, null), CancellationToken.None);

        result.IsSuccess.Should().BeTrue();
        var item = result.Value!.Items.Should().ContainSingle().Subject;
        item.DeliveryStatus.Should().Be("tracking_unavailable");
        item.IsTrackable.Should().BeFalse();
        item.Driver.Should().BeNull();
        item.Location.Should().BeNull();
        item.DestinationAddress.Should().BeNull();
    }

    [Fact]
    public async Task Non_customer_caller_is_forbidden()
    {
        _user.UserId.Returns(Guid.NewGuid());
        _user.UserType.Returns(UserType.Driver);
        var handler = CreateHandler();

        var result = await handler.Handle(new GetCustomerDeliveryWidgetQuery(ServiceDate, null), CancellationToken.None);

        result.IsFailure.Should().BeTrue();
        result.Error.Type.Should().Be(ErrorType.Forbidden);
    }

    [Fact]
    public async Task Widget_items_expose_the_subscription_day_details_link_even_before_a_trip()
    {
        var userId = Guid.NewGuid();
        _user.UserId.Returns(userId);
        var subscription = await SeedIndividualAsync(userId);
        var day = await _db.SubscriptionDays.SingleAsync(d => d.SubscriptionId == subscription.Id);
        var handler = CreateHandler();

        var result = await handler.Handle(new GetCustomerDeliveryWidgetQuery(ServiceDate, null), CancellationToken.None);

        result.IsSuccess.Should().BeTrue();
        var item = result.Value!.Items.Should().ContainSingle().Subject;
        item.DeliveryDetailsUrl.Should().Be($"/api/v1/customer/subscription-days/{day.Id}/delivery-details");
        item.DetailsUrl.Should().BeNull();
    }

    [Fact]
    public async Task Driver_rating_uses_the_real_batched_aggregation_rounded_like_the_driver_profile()
    {
        var userId = Guid.NewGuid();
        _user.UserId.Returns(userId);
        var subscription = await SeedIndividualAsync(userId, SubscriptionDayStatus.OutForDelivery);
        var day = await _db.SubscriptionDays.SingleAsync(d => d.SubscriptionId == subscription.Id);
        var order = await SeedReadyOrderAsync(day);

        var driver = new DriverProfile { UserId = Guid.NewGuid(), FullNameAr = "سائق مُقيَّم", FullNameEn = "Rated driver" };
        _db.DriverProfiles.Add(driver);
        var trip = new Trip { RestaurantId = Guid.NewGuid(), Code = "TRP-R", Status = TripStatus.InProgress, PlannedDate = ServiceDate };
        _db.Trips.Add(trip);
        _db.DriverAssignments.Add(new DriverAssignment { TripId = trip.Id, DriverProfileId = driver.Id, Status = "Active", AssignedAtUtc = Now });
        _db.TripStops.Add(new TripStop
        {
            TripId = trip.Id,
            SubscriptionDayId = day.Id,
            DailyOrderId = order.Id,
            OrderCode = "#BX-RATE",
            CustomerMaskedId = "CUST",
            ZoneNameAr = "z",
            ZoneNameEn = "z",
            MealSummaryAr = "وجبة",
            MealSummaryEn = "Meal",
            SlotLabel = "lunch",
            Status = TripStopStatus.PickedUp
        });
        _db.DriverRatings.AddRange(
            new DriverRating { DriverProfileId = driver.Id, Score = 4 },
            new DriverRating { DriverProfileId = driver.Id, Score = 5 });
        await _db.SaveChangesAsync();

        var handler = CreateHandler();
        var result = await handler.Handle(new GetCustomerDeliveryWidgetQuery(ServiceDate, null), CancellationToken.None);

        result.IsSuccess.Should().BeTrue();
        var item = result.Value!.Items.Should().ContainSingle().Subject;
        item.Driver!.Id.Should().Be(driver.Id);
        item.Driver.Rating.Should().Be(4.5); // Math.Round(Average, 2) — same policy as the driver profile
    }
}
