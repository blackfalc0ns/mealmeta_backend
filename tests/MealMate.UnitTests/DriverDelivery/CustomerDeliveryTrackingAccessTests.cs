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
/// Ownership and evidence-scope tests for GET /api/v1/customer/orders/{id}/live-tracking.
/// Authentication alone must never expose another customer's address, driver, location or proof.
/// </summary>
public sealed class CustomerDeliveryTrackingAccessTests
{
    private static readonly DateTime Now = new(2026, 10, 10, 9, 0, 0, DateTimeKind.Utc);
    private static readonly DateOnly ServiceDate = new(2026, 10, 10);

    private sealed record Fixture(
        AppDbContext Db,
        Guid OwnerUserId,
        Guid ReaderUserId,
        Guid OwnerSubscriptionId,
        Guid StopId);

    [Fact]
    public async Task Owner_can_read_own_tracking_with_real_timeline_sources()
    {
        var fx = await SeedAsync();
        var handler = BuildHandler(fx.Db, fx.OwnerUserId);

        var result = await handler.Handle(new GetCustomerLiveOrderTrackingQuery(fx.StopId), CancellationToken.None);

        result.IsSuccess.Should().BeTrue($"diagnostic: {result.Error?.Code} / {result.Error?.Description}");
        var dto = result.Value;
        dto!.Status.Should().Be("OutForDelivery");
        dto.Timeline.PickedUpAtUtc.Should().Be(Now.AddMinutes(-25)); // current handover, not trip start
        dto.Timeline.ArrivedAtCustomerAtUtc.Should().BeNull();       // arrival ≠ delivered
        dto.Timeline.DeliveredAtUtc.Should().BeNull();
        dto.Address.AddressShort.Should().Be("شارع البحر، السالمية، الدور 2");
        ((double?)dto.Driver!.Rating).Should().BeNull(); // no real rating source exists yet
        dto.Driver.AvatarUrl.Should().StartWith("/api/v1/uploads/content?key="); // never a raw storage key
    }

    [Fact]
    public async Task Outsider_gets_not_found_without_customer_data()
    {
        var fx = await SeedAsync();
        var handler = BuildHandler(fx.Db, Guid.NewGuid()); // authenticated, but unrelated customer

        var result = await handler.Handle(new GetCustomerLiveOrderTrackingQuery(fx.StopId), CancellationToken.None);

        result.IsFailure.Should().BeTrue();
        result.Error.Type.Should().Be(ErrorType.NotFound);
    }

    [Fact]
    public async Task Family_linked_member_with_read_access_can_read()
    {
        var fx = await SeedAsync();

        var group = new FamilyGroup
        {
            ManagerCustomerUserId = fx.OwnerUserId,
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
            MemberSubscriptionId = fx.OwnerSubscriptionId
        };
        (await fx.Db.Subscriptions.SingleAsync(x => x.Id == fx.OwnerSubscriptionId)).FamilyGroupId = group.Id;
        fx.Db.AddRange(group, member, new FamilyMemberAccess
        {
            FamilyMemberId = member.Id,
            LinkedCustomerUserId = fx.ReaderUserId
        });
        await fx.Db.SaveChangesAsync();

        var handler = BuildHandler(fx.Db, fx.ReaderUserId);
        var result = await handler.Handle(new GetCustomerLiveOrderTrackingQuery(fx.StopId), CancellationToken.None);

        result.IsSuccess.Should().BeTrue();
        result.Value!.Driver.Should().NotBeNull();
    }

    [Fact]
    public async Task Removed_family_member_gets_not_found()
    {
        var fx = await SeedAsync();

        var group = new FamilyGroup
        {
            ManagerCustomerUserId = fx.OwnerUserId,
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
            MemberSubscriptionId = fx.OwnerSubscriptionId
        };
        fx.Db.AddRange(group, member, new FamilyMemberAccess
        {
            FamilyMemberId = member.Id,
            LinkedCustomerUserId = fx.ReaderUserId,
            RemovedAtUtc = Now.AddHours(-1)
        });
        await fx.Db.SaveChangesAsync();

        var handler = BuildHandler(fx.Db, fx.ReaderUserId);
        var result = await handler.Handle(new GetCustomerLiveOrderTrackingQuery(fx.StopId), CancellationToken.None);

        result.IsFailure.Should().BeTrue();
        result.Error.Type.Should().Be(ErrorType.NotFound);
    }

    /// <summary>
    /// Regression for the nullable-OR proof predicate: a scan whose OrderId is null matched
    /// a stop whose DailyOrderId is null (null == null), leaking another customer's proof.
    /// The owned scan documents the correct identifiers, so it must be the one returned.
    /// </summary>
    [Fact]
    public async Task Foreign_scan_is_never_returned_by_the_nullable_or_predicate()
    {
        var fx = await SeedAsync();
        var handler = BuildHandler(fx.Db, fx.OwnerUserId);

        var result = await handler.Handle(new GetCustomerLiveOrderTrackingQuery(fx.StopId), CancellationToken.None);

        result.IsSuccess.Should().BeTrue();
        var proofUrl = result.Value!.Timeline.ProofPhotoStorageKey;
        proofUrl.Should().NotBeNullOrEmpty();
        proofUrl.Should().Contain("customer-a");
        proofUrl.Should().NotContain("customer-b");
    }

    private static CustomerLiveTrackingQueryHandler BuildHandler(AppDbContext db, Guid callerUserId)
    {
        var user = Substitute.For<ICurrentUser>();
        user.IsAuthenticated.Returns(true);
        user.UserId.Returns(callerUserId);
        user.UserType.Returns(UserType.Customer);

        var messages = Substitute.For<ILocalizedMessageProvider>();
        messages.CurrentCulture.Returns(System.Globalization.CultureInfo.InvariantCulture);
        messages.GetMessage(Arg.Any<string>(), Arg.Any<string>())
            .Returns(callInfo => callInfo.ArgAt<string>(1));

        return new CustomerLiveTrackingQueryHandler(
            db,
            messages,
            new FamilyCalendarAccessGate(db, user));
    }

    private static async Task<Fixture> SeedAsync()
    {
        var db = new AppDbContext(
            new DbContextOptionsBuilder<AppDbContext>().UseInMemoryDatabase(Guid.NewGuid().ToString()).Options,
            Substitute.For<IDomainEventDispatcher>());

        var ownerUserId = Guid.NewGuid();
        var readerUserId = Guid.NewGuid();
        var restaurantId = Guid.NewGuid();

        var program = new MealMate.Domain.ProgramsSubscriptions.Program
        {
            Code = "WL",
            NameAr = "وزن",
            NameEn = "Weight",
            Status = ProgramStatus.Active,
            IsActive = true
        };
        var bundle = new MealMate.Domain.ProgramsSubscriptions.Bundle
        {
            ProgramId = program.Id,
            Code = "3M",
            NameAr = "3 وجبات",
            NameEn = "3 meals",
            MealsPerDay = 3,
            Status = ProgramStatus.Active,
            IsActive = true
        };
        var ownerSubscription = new Subscription
        {
            CustomerUserId = ownerUserId,
            ProgramId = program.Id,
            BundleId = bundle.Id,
            DurationDays = 6,
            StartDate = ServiceDate,
            EndDate = ServiceDate.AddDays(5),
            Status = SubscriptionStatus.Active,
            FulfillmentCompletedAtUtc = Now
        };
        var readerSubscription = new Subscription
        {
            CustomerUserId = readerUserId,
            ProgramId = program.Id,
            BundleId = bundle.Id,
            DurationDays = 6,
            StartDate = ServiceDate,
            EndDate = ServiceDate.AddDays(5),
            Status = SubscriptionStatus.Active,
            FulfillmentCompletedAtUtc = Now
        };
        var ownerDay = new SubscriptionDay
        {
            SubscriptionId = ownerSubscription.Id,
            ServiceDate = ServiceDate,
            Status = SubscriptionDayStatus.OutForDelivery,
            RestaurantId = restaurantId
        };
        var readerDay = new SubscriptionDay
        {
            SubscriptionId = readerSubscription.Id,
            ServiceDate = ServiceDate,
            Status = SubscriptionDayStatus.OutForDelivery,
            RestaurantId = restaurantId
        };

        var driver = new DriverProfile
        {
            UserId = Guid.NewGuid(),
            FullNameAr = "سائق التوصيل",
            FullNameEn = "Delivery Driver",
            VehicleType = "car",
            VehiclePlate = "AA-1234",
            ProfileImageStatus = VerificationStatus.Approved,
            ProfileImageStorageKey = "driver-profiles/avatars/driver-a.jpg"
        };

        var dailyOrder = new RestaurantDailyOrder
        {
            RestaurantId = restaurantId,
            SubscriptionDayId = ownerDay.Id,
            ServiceDate = ServiceDate,
            Status = OrderStatus.OnTheWay,
            BoxCount = 1,
            ReadyForPickupAtUtc = Now.AddMinutes(-40)
        };

        var trip = new Trip
        {
            RestaurantId = restaurantId,
            Code = "TRP-2026",
            Status = TripStatus.InProgress,
            PlannedDate = ServiceDate,
            StartedAtUtc = Now.AddMinutes(-30)
        };

        var stop = new TripStop
        {
            TripId = trip.Id,
            SubscriptionDayId = ownerDay.Id,
            DailyOrderId = null, // legacy shape exercised by the regression scan below
            OrderCode = "#BX-2026",
            CustomerMaskedId = "CUST-****",
            ZoneNameAr = "السالمية",
            ZoneNameEn = "Salmiya",
            FullAddress = "شارع البحر، السالمية، الدور 2",
            MealSummaryAr = "وجبة غداء",
            MealSummaryEn = "Lunch box",
            SlotLabel = "lunch",
            Status = TripStopStatus.PickedUp,
            Latitude = 29.33m,
            Longitude = 48.07m
        };

        db.AddRange(program, bundle, ownerSubscription, readerSubscription, ownerDay, readerDay, driver, dailyOrder, trip, stop);
        db.DriverAssignments.Add(new DriverAssignment
        {
            TripId = trip.Id,
            DriverProfileId = driver.Id,
            Status = "Active",
            AssignedAtUtc = Now.AddMinutes(-35)
        });
        db.OrderHandovers.Add(new OrderHandover
        {
            DailyOrderId = dailyOrder.Id,
            TripId = trip.Id,
            TripStopId = stop.Id,
            DriverProfileId = driver.Id,
            HandedOverAtUtc = Now.AddMinutes(-25),
            Status = "Completed"
        });

        // Nullable-OR trap: scan.OrderId null + stop.DailyOrderId null matched in the old
        // predicate, while the scan's SubscriptionDayId points at customer B's day.
        db.DeliveryConfirmationScans.Add(new DeliveryConfirmationScan
        {
            OrderId = null,
            SubscriptionDayId = readerDay.Id,
            DriverProfileId = driver.Id,
            BarcodeValue = "FOREIGN",
            PhotoPath = "delivery-proof/customer-b/foreign.jpg",
            ScannedAtUtc = Now.AddMinutes(-5)
        });
        db.DeliveryConfirmationScans.Add(new DeliveryConfirmationScan
        {
            OrderId = dailyOrder.Id,
            SubscriptionDayId = ownerDay.Id,
            DriverProfileId = driver.Id,
            BarcodeValue = "#BX-2026",
            PhotoPath = "delivery-proof/customer-a/own.jpg",
            ScannedAtUtc = Now.AddMinutes(-2)
        });

        await db.SaveChangesAsync();

        return new Fixture(db, ownerUserId, readerUserId, ownerSubscription.Id, stop.Id);
    }
}
