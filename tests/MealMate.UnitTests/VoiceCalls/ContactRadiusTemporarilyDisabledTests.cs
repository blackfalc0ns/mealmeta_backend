using FluentAssertions;
using MealMate.Application.Common.Abstractions;
using MealMate.Application.Common.Models;
using MealMate.Application.Features.VoiceCalls.Abstractions;
using MealMate.Application.Features.VoiceCalls.BackgroundJobs;
using MealMate.Application.Features.VoiceCalls.Commands;
using MealMate.Application.Features.VoiceCalls.Dtos;
using MealMate.Application.Features.VoiceCalls.Errors;
using MealMate.Application.Features.VoiceCalls.Options;
using MealMate.Application.Features.VoiceCalls.Queries;
using MealMate.Application.Features.VoiceCalls.Services;
using MealMate.Application.Features.VoiceCalls.V2.Commands;
using MealMate.Domain.CalendarMeals;
using MealMate.Domain.DriverDelivery;
using MealMate.Domain.Enums;
using MealMate.Domain.IdentityAccess;
using MealMate.Domain.ProgramsSubscriptions;
using MealMate.Domain.VoiceCalls;
using MealMate.Infrastructure.Push;
using MealMate.Infrastructure.VoiceCalls;
using MediatR;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using NSubstitute;
using Xunit;

namespace MealMate.UnitTests.VoiceCalls;

public sealed class ContactRadiusTemporarilyDisabledTests
{
    private static MealMate.Persistence.AppDbContext CreateInMemoryDbContext()
    {
        var options = new DbContextOptionsBuilder<MealMate.Persistence.AppDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString())
            .Options;

        var dispatcher = Substitute.For<IDomainEventDispatcher>();
        return new MealMate.Persistence.AppDbContext(options, dispatcher);
    }

    private static async Task<(TripStop stop, Guid driverUserId, Guid customerUserId)> SeedDeliveryContext(MealMate.Persistence.AppDbContext db)
    {
        var driverUserId = Guid.NewGuid();
        var customerUserId = Guid.NewGuid();

        var driverProfile = new DriverProfile
        {
            UserId = driverUserId,
            FullNameAr = "علي السائق",
            VerificationStatus = VerificationStatus.Approved,
            FullNameEn = "Ali Driver"
        };
        db.DriverProfiles.Add(driverProfile);

        // Contact eligibility checks account status, so both participants need real
        // (active) application-user rows in the seed.
        db.Users.Add(new MealMate.Persistence.Identity.ApplicationUser
        {
            Id = driverUserId,
            UserName = "drv_" + driverUserId.ToString("N")[..10],
            NormalizedUserName = ("DRV_" + driverUserId.ToString("N")[..10]).ToUpperInvariant(),
            UserType = UserType.Driver,
            FullName = "علي السائق",
            SecurityStamp = Guid.NewGuid().ToString("D"),
            EmailConfirmed = true,
            AccountStatus = AccountStatus.Active
        });
        db.Users.Add(new MealMate.Persistence.Identity.ApplicationUser
        {
            Id = customerUserId,
            UserName = "cust_" + customerUserId.ToString("N")[..10],
            NormalizedUserName = ("CUST_" + customerUserId.ToString("N")[..10]).ToUpperInvariant(),
            UserType = UserType.Customer,
            FullName = "عميل السائق",
            SecurityStamp = Guid.NewGuid().ToString("D"),
            EmailConfirmed = true,
            AccountStatus = AccountStatus.Active
        });

        var trip = new Trip
        {
            Code = $"TRIP-{Guid.NewGuid():N}"[..10],
            RestaurantId = Guid.NewGuid(),
            PlannedDate = DateOnly.FromDateTime(DateTime.UtcNow),
            Status = TripStatus.InProgress
        };
        db.Trips.Add(trip);

        var driverAssignment = new DriverAssignment
        {
            TripId = trip.Id,
            DriverProfileId = driverProfile.Id,
            AssignedAtUtc = DateTime.UtcNow,
            Status = "Active"
        };
        trip.DriverAssignments.Add(driverAssignment);

        var program = new Program
        {
            Code = $"PROG-{Guid.NewGuid():N}"[..10],
            NameAr = "برنامج صحي",
            NameEn = "Healthy Program"
        };
        db.Programs.Add(program);

        var subscription = new Subscription
        {
            ProgramId = program.Id,
            CustomerUserId = customerUserId,
            StartDate = DateOnly.FromDateTime(DateTime.UtcNow),
            EndDate = DateOnly.FromDateTime(DateTime.UtcNow.AddDays(30)),
            DurationDays = 30
        };
        db.Subscriptions.Add(subscription);

        var subscriptionDay = new SubscriptionDay
        {
            SubscriptionId = subscription.Id,
            ServiceDate = DateOnly.FromDateTime(DateTime.UtcNow)
        };
        db.SubscriptionDays.Add(subscriptionDay);

        var stop = new TripStop
        {
            TripId = trip.Id,
            Trip = trip,
            SubscriptionDayId = subscriptionDay.Id,
            OrderCode = $"ORD-{Guid.NewGuid():N}"[..10],
            CustomerMaskedId = "CUST-002",
            CustomerName = "Sara Customer",
            ZoneNameAr = "السالمية",
            ZoneNameEn = "Salmiya",
            MealSummaryAr = "وجبة عشاء",
            MealSummaryEn = "Dinner Meal",
            SlotLabel = "Dinner",
            Status = TripStopStatus.PickedUp
        };
        db.TripStops.Add(stop);
        db.SaveChanges();

        // Contact-eligibility context: stop coordinates and a fresh, accurate fix owned
        // by the assigned driver on this trip.
        stop.Latitude = 29.3983m;
        stop.Longitude = 47.9774m;

        db.DeliveryTrackingPoints.Add(new DeliveryTrackingPoint
        {
            TripId = stop.TripId,
            DriverProfileId = driverProfile.Id,
            Latitude = 29.3974m,
            Longitude = 47.9774m,
            AccuracyMeters = 20,
            CapturedAtUtc = DateTime.UtcNow,
            ReceivedAtUtc = DateTime.UtcNow,
            RecordedAtUtc = DateTime.UtcNow
        });

        await db.SaveChangesAsync();

        return (stop, driverUserId, customerUserId);
    }

    [Fact]
    public async Task ContactEligibility_OutsideThreeKilometers_AllowsCallWhileRadiusIsTemporarilyDisabled()
    {
        await using var db = CreateInMemoryDbContext();
        var (stop, driverUserId, _) = await SeedDeliveryContext(db);
        var fix = await db.DeliveryTrackingPoints.SingleAsync();
        fix.Latitude = stop.Latitude!.Value + 1m;
        await db.SaveChangesAsync();

        var result = await new DeliveryContactEligibilityService(db, new FixedClock())
            .EvaluateAsync(stop.Id, driverUserId, CancellationToken.None);

        result.IsSuccess.Should().BeTrue();
        result.Value.CanInitiate.Should().BeTrue();
        result.Value.DistanceMeters.Should().BeGreaterThan(100_000);
    }

    [Theory]
    [InlineData(120, 20, "contact.location_stale")]
    [InlineData(0, 150, "contact.location_accuracy_low")]
    public async Task ContactEligibility_OutsideThreeKilometers_StillRejectsInvalidLocationQuality(
        int ageSeconds, double accuracyMeters, string reason)
    {
        await using var db = CreateInMemoryDbContext();
        var (stop, driverUserId, _) = await SeedDeliveryContext(db);
        var fix = await db.DeliveryTrackingPoints.SingleAsync();
        fix.Latitude = stop.Latitude!.Value + 1m;
        fix.CapturedAtUtc = DateTime.UtcNow.AddSeconds(-ageSeconds);
        fix.AccuracyMeters = accuracyMeters;
        await db.SaveChangesAsync();

        var result = await new DeliveryContactEligibilityService(db, new FixedClock())
            .EvaluateAsync(stop.Id, driverUserId, CancellationToken.None);

        result.IsSuccess.Should().BeTrue();
        result.Value.CanInitiate.Should().BeFalse();
        result.Value.ReasonCode.Should().Be(reason);
    }

    [Fact]
    public async Task ResumeHold_OutsideThreeKilometers_ReleasesHoldWhileRadiusIsTemporarilyDisabled()
    {
        await using var db = CreateInMemoryDbContext();
        var (stop, driverUserId, _) = await SeedDeliveryContext(db);
        var fix = await db.DeliveryTrackingPoints.SingleAsync();
        fix.Latitude = stop.Latitude!.Value + 1m;
        var assignment = await db.DriverAssignments.SingleAsync();
        var contactCase = new DeliveryContactCase(stop.Id, assignment.Id, 1, DateTime.UtcNow);
        var hold = new DeliveryHold
        {
            TripId = stop.TripId,
            TripStopId = stop.Id,
            ContactCaseId = contactCase.Id,
            ActorUserId = driverUserId,
            Reason = "Customer did not answer",
            HeldAtUtc = DateTime.UtcNow,
            Status = "Active"
        };
        contactCase.AttachHold(hold.Id, DateTime.UtcNow);
        db.DeliveryContactCases.Add(contactCase);
        db.DeliveryHolds.Add(hold);
        await db.SaveChangesAsync();
        var clock = new FixedClock();
        var eligibility = new DeliveryContactEligibilityService(db, clock);
        var cases = new DeliveryContactCaseService(db, clock, eligibility);

        var result = await new ResumeDeliveryHoldV2CommandHandler(cases, db, clock)
            .Handle(new ResumeDeliveryHoldV2Command(stop.Id, driverUserId), CancellationToken.None);

        result.IsSuccess.Should().BeTrue();
        var savedHold = await db.DeliveryHolds.SingleAsync();
        savedHold.Status.Should().Be("Released");
        savedHold.ResumedAtUtc.Should().NotBeNull();
    }

    private sealed class FixedClock : IDateTimeProvider
    {
        public DateTime UtcNow => DateTime.UtcNow;
        public DateOnly TodayUtc => DateOnly.FromDateTime(UtcNow);
    }
}
