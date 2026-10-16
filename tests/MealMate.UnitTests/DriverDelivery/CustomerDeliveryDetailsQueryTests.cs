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
/// GET /api/v1/customer/subscription-days/{id}/delivery-details: one authorized
/// subscription day, usable before any TripStop exists, with server truth only and
/// the same shared projection as the widget. A refusal is the same not-found as a
/// missing day so another customer's box existence is never disclosed.
/// </summary>
public sealed class CustomerDeliveryDetailsQueryTests
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

    public CustomerDeliveryDetailsQueryTests()
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

    private GetCustomerDeliveryDetailsQueryHandler CreateHandler()
    {
        _user.IsAuthenticated.Returns(true);
        return new GetCustomerDeliveryDetailsQueryHandler(
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

    [Fact]
    public async Task Ready_day_without_any_trip_stop_opens_details()
    {
        var userId = Guid.NewGuid();
        _user.UserId.Returns(userId);
        var subscription = await SeedIndividualAsync(userId);
        var day = await _db.SubscriptionDays.SingleAsync(d => d.SubscriptionId == subscription.Id);
        await SeedReadyOrderAsync(day);
        var handler = CreateHandler();

        var result = await handler.Handle(new GetCustomerDeliveryDetailsQuery(day.Id), CancellationToken.None);

        result.IsSuccess.Should().BeTrue();
        result.Value!.ServiceDate.Should().Be(ServiceDate);
        result.Value.RefreshAfterSeconds.Should().Be(30);

        var delivery = result.Value.Delivery;
        delivery.SubscriptionDayId.Should().Be(day.Id);
        delivery.DeliveryStatus.Should().Be(CustomerDeliveryStatusCodes.ReadyForPickup);
        delivery.TripStopId.Should().BeNull();
        delivery.DetailsUrl.Should().BeNull(); // the legacy stop-based link stays null before a trip
        delivery.DeliveryDetailsUrl.Should().Be($"/api/v1/customer/subscription-days/{day.Id}/delivery-details");
        delivery.Driver.Should().BeNull();
        delivery.DestinationAddress.Should().BeNull();
        delivery.EtaMinutes.Should().BeNull();
        delivery.ReadyAtUtc.Should().Be(Now.AddMinutes(-20));
    }

    [Fact]
    public async Task Delivered_day_with_missing_delivery_time_does_not_invent_one()
    {
        var userId = Guid.NewGuid();
        _user.UserId.Returns(userId);
        var subscription = await SeedIndividualAsync(userId, SubscriptionDayStatus.Delivered);
        var day = await _db.SubscriptionDays.SingleAsync(d => d.SubscriptionId == subscription.Id);
        var order = await SeedReadyOrderAsync(day);
        order.Status = OrderStatus.Delivered;
        await _db.SaveChangesAsync();

        var driver = new DriverProfile { UserId = Guid.NewGuid(), FullNameAr = "سائق", FullNameEn = "Driver" };
        _db.DriverProfiles.Add(driver);
        var trip = new Trip { RestaurantId = Guid.NewGuid(), Code = "TRP-D", Status = TripStatus.Completed, PlannedDate = ServiceDate };
        _db.Trips.Add(trip);
        _db.DriverAssignments.Add(new DriverAssignment { TripId = trip.Id, DriverProfileId = driver.Id, Status = "Active", AssignedAtUtc = Now });
        _db.TripStops.Add(new TripStop
        {
            TripId = trip.Id,
            SubscriptionDayId = day.Id,
            DailyOrderId = order.Id,
            OrderCode = "#BX-NOTIME",
            CustomerMaskedId = "CUST",
            ZoneNameAr = "z",
            ZoneNameEn = "z",
            MealSummaryAr = "وجبة",
            MealSummaryEn = "Meal",
            SlotLabel = "lunch",
            Status = TripStopStatus.Delivered,
            ArrivedAtUtc = Now.AddMinutes(-15),
            DeliveredAtUtc = null
        });
        await _db.SaveChangesAsync();

        var handler = CreateHandler();
        var result = await handler.Handle(new GetCustomerDeliveryDetailsQuery(day.Id), CancellationToken.None);

        result.IsSuccess.Should().BeTrue();
        var delivery = result.Value!.Delivery;
        delivery.DeliveryStatus.Should().Be(CustomerDeliveryStatusCodes.Delivered);
        delivery.IsTerminal.Should().BeTrue();
        delivery.DeliveredAtUtc.Should().BeNull(); // no fabricated clock
        delivery.ArrivedAtUtc.Should().Be(Now.AddMinutes(-15));
        delivery.Driver.Should().NotBeNull(); // documented handover identity survives
        delivery.Driver!.MaskedPhone.Should().BeNull(); // missing contact data stays absent
    }

    [Fact]
    public async Task Day_of_another_customer_returns_not_found()
    {
        var strangerSubscription = await SeedIndividualAsync(Guid.NewGuid());
        var strangerDay = await _db.SubscriptionDays.SingleAsync(d => d.SubscriptionId == strangerSubscription.Id);
        _user.UserId.Returns(Guid.NewGuid());
        var handler = CreateHandler();

        var result = await handler.Handle(new GetCustomerDeliveryDetailsQuery(strangerDay.Id), CancellationToken.None);

        result.IsFailure.Should().BeTrue();
        result.Error.Type.Should().Be(ErrorType.NotFound);
    }

    [Fact]
    public async Task Unknown_day_returns_not_found()
    {
        _user.UserId.Returns(Guid.NewGuid());
        var handler = CreateHandler();

        var result = await handler.Handle(new GetCustomerDeliveryDetailsQuery(Guid.NewGuid()), CancellationToken.None);

        result.IsFailure.Should().BeTrue();
        result.Error.Type.Should().Be(ErrorType.NotFound);
    }

    [Fact]
    public async Task Family_read_access_opens_details_and_revocation_closes_them()
    {
        var ownerUserId = Guid.NewGuid();
        var readerUserId = Guid.NewGuid();
        var subscription = await SeedIndividualAsync(ownerUserId);
        var day = await _db.SubscriptionDays.SingleAsync(d => d.SubscriptionId == subscription.Id);

        var group = new FamilyGroup
        {
            ManagerCustomerUserId = ownerUserId,
            Name = "عائلة الاختبار",
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
            MemberSubscriptionId = subscription.Id
        };
        subscription.FamilyGroupId = group.Id;
        subscription.FamilyMemberId = member.Id;
        _db.FamilyMemberPermissions.Add(new FamilyMemberPermissions { FamilyMemberId = member.Id, ViewCalendar = true });
        var access = new FamilyMemberAccess
        {
            FamilyMemberId = member.Id,
            LinkedCustomerUserId = readerUserId,
            HasSeparateAccount = true
        };
        _db.AddRange(group, member, access);
        await _db.SaveChangesAsync();

        _user.UserId.Returns(readerUserId);
        var handler = CreateHandler();

        var allowed = await handler.Handle(new GetCustomerDeliveryDetailsQuery(day.Id), CancellationToken.None);
        allowed.IsSuccess.Should().BeTrue($"diagnostic: {allowed.Error?.Code} / {allowed.Error?.Description}");
        allowed.Value!.Delivery.SubscriptionDayId.Should().Be(day.Id);

        access.RemovedAtUtc = Now;
        await _db.SaveChangesAsync();

        var revoked = await handler.Handle(new GetCustomerDeliveryDetailsQuery(day.Id), CancellationToken.None);
        revoked.IsFailure.Should().BeTrue();
        revoked.Error.Type.Should().Be(ErrorType.NotFound);
    }

    [Fact]
    public async Task Ambiguous_current_attempts_open_details_without_driver_or_destination_data()
    {
        var userId = Guid.NewGuid();
        _user.UserId.Returns(userId);
        var subscription = await SeedIndividualAsync(userId, SubscriptionDayStatus.OutForDelivery);
        var day = await _db.SubscriptionDays.SingleAsync(d => d.SubscriptionId == subscription.Id);
        var order = await SeedReadyOrderAsync(day);

        var driver = new DriverProfile { UserId = Guid.NewGuid(), FullNameAr = "سائق", FullNameEn = "Driver" };
        _db.DriverProfiles.Add(driver);
        var trip = new Trip { RestaurantId = Guid.NewGuid(), Code = "TRP-A", Status = TripStatus.InProgress, PlannedDate = ServiceDate };
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
        var result = await handler.Handle(new GetCustomerDeliveryDetailsQuery(day.Id), CancellationToken.None);

        result.IsSuccess.Should().BeTrue(); // IsTrackable=false never blocks opening details
        var delivery = result.Value!.Delivery;
        delivery.DeliveryStatus.Should().Be(CustomerDeliveryStatusCodes.TrackingUnavailable);
        delivery.Driver.Should().BeNull();
        delivery.DestinationAddress.Should().BeNull();
        delivery.Location.Should().BeNull();
        delivery.DeliveryDetailsUrl.Should().Be($"/api/v1/customer/subscription-days/{day.Id}/delivery-details");
    }

    [Fact]
    public async Task Details_read_never_writes()
    {
        var userId = Guid.NewGuid();
        _user.UserId.Returns(userId);
        var subscription = await SeedIndividualAsync(userId);
        var day = await _db.SubscriptionDays.SingleAsync(d => d.SubscriptionId == subscription.Id);
        await SeedReadyOrderAsync(day);
        var handler = CreateHandler();

        var result = await handler.Handle(new GetCustomerDeliveryDetailsQuery(day.Id), CancellationToken.None);

        result.IsSuccess.Should().BeTrue();
        _db.ChangeTracker.Entries()
            .Where(e => e.State is EntityState.Added or EntityState.Modified or EntityState.Deleted)
            .Should().BeEmpty();
    }

    [Fact]
    public async Task Non_customer_caller_is_forbidden()
    {
        _user.UserId.Returns(Guid.NewGuid());
        _user.UserType.Returns(UserType.Driver);
        var handler = CreateHandler();

        var result = await handler.Handle(new GetCustomerDeliveryDetailsQuery(Guid.NewGuid()), CancellationToken.None);

        result.IsFailure.Should().BeTrue();
        result.Error.Type.Should().Be(ErrorType.Forbidden);
    }
}
