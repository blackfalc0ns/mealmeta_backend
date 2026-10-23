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
using SubscriptionProgram = MealMate.Domain.ProgramsSubscriptions.Program;

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

        var program = new SubscriptionProgram
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
    public async Task ContactEligibility_RefreshRequestedTrip_WhenDriverHasNewerActiveTrip()
    {
        await using var db = CreateInMemoryDbContext();
        var (stop, driverUserId, _) = await SeedDeliveryContext(db);
        var driver = await db.DriverProfiles.SingleAsync(d => d.UserId == driverUserId);
        var fix = await db.DeliveryTrackingPoints.SingleAsync();
        fix.CapturedAtUtc = DateTime.UtcNow.AddHours(-2);
        var newerTrip = new Trip { Code = "NEWER", RestaurantId = (await db.Trips.SingleAsync()).RestaurantId, Status = TripStatus.Assigned };
        newerTrip.DriverAssignments.Add(new DriverAssignment
        {
            DriverProfileId = driver.Id, Status = "Active", AssignedAtUtc = DateTime.UtcNow.AddMinutes(1)
        });
        db.Trips.Add(newerTrip);
        await db.SaveChangesAsync();

        var eligibility = new DeliveryContactEligibilityService(db, new FixedClock());
        (await eligibility.EvaluateAsync(stop.Id, driverUserId, CancellationToken.None))
            .Value.ReasonCode.Should().Be("contact.location_stale");
        var user = Substitute.For<ICurrentUser>();
        user.UserId.Returns(driverUserId);
        var update = await new MealMate.Application.Features.DriverDelivery.Dispatcher.UpdateDriverLocationCommandHandler(
            db, user, MealMate.Application.Common.Localization.NullLocalizedMessageProvider.Instance).Handle(
            new MealMate.Application.Features.DriverDelivery.Dispatcher.UpdateDriverLocationCommand(
                DriverId: null, TripId: stop.TripId, Latitude: stop.Latitude!.Value,
                Longitude: stop.Longitude!.Value, AccuracyMeters: 15, CapturedAtUtc: DateTime.UtcNow), CancellationToken.None);
        update.IsSuccess.Should().BeTrue();
        update.Value.TripId.Should().Be(stop.TripId);
        (await eligibility.EvaluateAsync(stop.Id, driverUserId, CancellationToken.None))
            .Value.CanInitiate.Should().BeTrue();
        (await db.DeliveryTrackingPoints.CountAsync(p => p.TripId == newerTrip.Id)).Should().Be(0);

        var foreignTrip = new Trip { Code = "OTHER-DRIVER", Status = TripStatus.InProgress };
        db.Trips.Add(foreignTrip);
        await db.SaveChangesAsync();
        var rejected = await new MealMate.Application.Features.DriverDelivery.Dispatcher.UpdateDriverLocationCommandHandler(
            db, user, MealMate.Application.Common.Localization.NullLocalizedMessageProvider.Instance).Handle(
            new MealMate.Application.Features.DriverDelivery.Dispatcher.UpdateDriverLocationCommand(
                DriverId: null, TripId: foreignTrip.Id, Latitude: stop.Latitude.Value,
                Longitude: stop.Longitude.Value, AccuracyMeters: 15, CapturedAtUtc: DateTime.UtcNow), CancellationToken.None);
        rejected.IsFailure.Should().BeTrue();
        (await db.DeliveryTrackingPoints.CountAsync(p => p.TripId == foreignTrip.Id)).Should().Be(0);
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

    [Theory]
    [InlineData(0, "none", false, false)]
    [InlineData(1, "none", false, false)]
    [InlineData(2, "none", false, false)]
    [InlineData(1, "active", false, false)]
    [InlineData(2, "active", false, false)]
    [InlineData(1, "resumed", true, false)]
    [InlineData(2, "resumed", true, true)]
    public async Task Eligibility_ReturnsCurrentWorkflow_WithHoldTemporarilyHiddenForMobileTesting(
        int misses, string holdState, bool canRetry, bool canReveal)
    {
        await using var db = CreateInMemoryDbContext();
        var (stop, driver, customer) = await SeedDeliveryContext(db);
        var assignment = await db.DriverAssignments.SingleAsync();
        var contactCase = new DeliveryContactCase(stop.Id, assignment.Id, 1, DateTime.UtcNow);
        for (var i = 0; i < misses; i++)
            contactCase.RecordQualifiedMissedAttempt(Guid.NewGuid(), DateTime.UtcNow);
        if (holdState != "none")
        {
            var hold = new DeliveryHold
            {
                TripStopId = stop.Id, ContactCaseId = contactCase.Id, ActorUserId = driver,
                Reason = "CustomerNoAnswer", Status = holdState == "resumed" ? "Released" : "Active",
                HeldAtUtc = DateTime.UtcNow.AddMinutes(-5),
                ResumedAtUtc = holdState == "resumed" ? DateTime.UtcNow.AddMinutes(-2) : null
            };
            db.DeliveryHolds.Add(hold);
            contactCase.AttachHold(hold.Id, DateTime.UtcNow);
        }
        db.DeliveryContactCases.Add(contactCase);
        await db.SaveChangesAsync();
        var clock = new FixedClock();
        var eligibility = new DeliveryContactEligibilityService(db, clock);
        var result = await eligibility.EvaluateAsync(stop.Id, driver, default);
        result.IsSuccess.Should().BeTrue();
        result.Value.ContactCaseId.Should().Be(contactCase.Id);
        result.Value.CanInitiate.Should().BeTrue();
        result.Value.CanHold.Should().BeFalse();
        result.Value.CanRetry.Should().Be(canRetry);
        result.Value.CanRevealPhone.Should().Be(canReveal);
        var caseResult = await new GetDeliveryContactCaseV2QueryHandler(
            new DeliveryContactCaseService(db, clock, eligibility), db)
            .Handle(new(stop.Id, driver), default);
        caseResult.Value.CanHold.Should().BeFalse();
        result.Value.CanRevealPhone.Should().Be(caseResult.Value.CanRevealPhone);

        // Customer participants see the case, never driver-only action rights.
        var customerResult = await eligibility.EvaluateAsync(stop.Id, customer, default);
        customerResult.Value.ContactCaseId.Should().Be(contactCase.Id);
        customerResult.Value.CanHold.Should().BeFalse();
        customerResult.Value.CanRetry.Should().BeFalse();
        customerResult.Value.CanRevealPhone.Should().BeFalse();
        (await eligibility.EvaluateAsync(stop.Id, Guid.NewGuid(), default)).IsFailure.Should().BeTrue();

        // A stale position must not erase persisted workflow or allow retry.
        (await db.DeliveryTrackingPoints.SingleAsync()).CapturedAtUtc = DateTime.UtcNow.AddMinutes(-5);
        await db.SaveChangesAsync();
        var stale = await eligibility.EvaluateAsync(stop.Id, driver, default);
        stale.Value.CanInitiate.Should().BeFalse();
        stale.Value.ReasonCode.Should().Be("contact.location_stale");
        stale.Value.ContactCaseId.Should().Be(contactCase.Id);
        stale.Value.CanHold.Should().BeFalse();
        stale.Value.CanRetry.Should().BeFalse();
        stale.Value.CanRevealPhone.Should().Be(canReveal);

        stop.Status = TripStopStatus.Delivered;
        await db.SaveChangesAsync();
        var inactive = await eligibility.EvaluateAsync(stop.Id, driver, default);
        inactive.Value.ContactCaseId.Should().Be(contactCase.Id);
        inactive.Value.CanHold.Should().BeFalse();
        inactive.Value.CanRetry.Should().BeFalse();
        inactive.Value.CanRevealPhone.Should().BeFalse();
    }

    [Theory]
    [InlineData("absent")]
    [InlineData("closed")]
    [InlineData("previous-assignment")]
    [InlineData("other-stop")]
    public async Task Eligibility_NeverCreatesCase_OrInheritsOtherCycleRights(string scope)
    {
        await using var db = CreateInMemoryDbContext();
        var (stop, driver, _) = await SeedDeliveryContext(db);
        var assignment = await db.DriverAssignments.SingleAsync();
        if (scope != "absent")
        {
            var contactCase = new DeliveryContactCase(scope == "other-stop" ? Guid.NewGuid() : stop.Id,
                scope == "previous-assignment" ? Guid.NewGuid() : assignment.Id, 1, DateTime.UtcNow);
            contactCase.RecordQualifiedMissedAttempt(Guid.NewGuid(), DateTime.UtcNow);
            if (scope == "closed") contactCase.Close(DateTime.UtcNow);
            db.DeliveryContactCases.Add(contactCase);
            await db.SaveChangesAsync();
        }
        var count = await db.DeliveryContactCases.CountAsync();
        var result = await new DeliveryContactEligibilityService(db, new FixedClock())
            .EvaluateAsync(stop.Id, driver, default);
        result.IsSuccess.Should().BeTrue();
        result.Value.CanInitiate.Should().BeTrue();
        result.Value.ContactCaseId.Should().BeNull();
        result.Value.CanHold.Should().BeFalse();
        result.Value.CanRetry.Should().BeFalse();
        result.Value.CanRevealPhone.Should().BeFalse();
        (await db.DeliveryContactCases.CountAsync()).Should().Be(count);
        db.ChangeTracker.HasChanges().Should().BeFalse();
    }

    [Theory]
    [InlineData(5, 30, 20, 30)]
    [InlineData(150, 60, 30, 40)]
    public async Task Initiation_NearExpiryToken_RequiresRefreshWithoutCreatingAttempt_AndRetryKeepsCall(
        int secondsRemaining, int ringSeconds, int acceptedSeconds, int connectingSeconds)
    {
        await using var db = CreateInMemoryDbContext();
        var (stop, driver, _) = await SeedDeliveryContext(db);
        var actor = new VoiceCallDeviceContext(driver, Guid.NewGuid())
            { AccessTokenExpiresAtUtc = DateTime.UtcNow.AddSeconds(secondsRemaining) };
        var clock = new FixedClock();
        var handler = new InitiateVoiceCallV2CommandHandler(db, clock,
            new DeliveryContactEligibilityService(db, clock), options: Options.Create(new VoiceCallOptions
            {
                RingTimeoutSeconds = ringSeconds, AcceptedSetupTimeoutSeconds = acceptedSeconds,
                ConnectingTimeoutSeconds = connectingSeconds
            }));
        var request = new InitiateVoiceCallV2Command(stop.Id, Guid.NewGuid(), actor);
        var blocked = await handler.Handle(request, default);
        blocked.IsFailure.Should().BeTrue();
        blocked.Error.Code.Should().Be("calls.auth_refresh_required");
        blocked.Error.Type.Should().Be(ErrorType.Conflict);
        (await db.CallSessions.CountAsync()).Should().Be(0);
        (await db.VoiceCallBusyParticipants.CountAsync()).Should().Be(0);
        (await db.OutboxMessages.CountAsync(m => m.EventType.StartsWith("voice-call.lifecycle."))).Should().Be(0);
        (await db.IdempotencyRecords.CountAsync()).Should().Be(0);
        (await db.DeliveryContactAttempts.CountAsync()).Should().Be(0);
        request = request with { Actor = actor with { AccessTokenExpiresAtUtc = DateTime.UtcNow.AddHours(1) } };
        var created = await handler.Handle(request, default);
        created.IsSuccess.Should().BeTrue();
        request = request with { Actor = actor };
        var replay = await handler.Handle(request, default);
        replay.IsSuccess.Should().BeTrue();
        replay.Value.CallId.Should().Be(created.Value.CallId);
        (await db.CallSessions.CountAsync()).Should().Be(1);
    }

    [Theory]
    [InlineData("valid", true)]
    [InlineData("expired", false)]
    [InlineData("revoked", false)]
    [InlineData("missing", false)]
    [InlineData("foreign-owner", false)]
    [InlineData("revoked-after-call", true)]
    [InlineData("expired-after-call", true)]
    public async Task MissedQualification_ChecksCallerSessionAtCallEnd_NotOutboxProcessingTime(string state, bool qualifies)
    {
        await using var db = CreateInMemoryDbContext();
        var (stop, driver, customer) = await SeedDeliveryContext(db);
        var ended = DateTime.UtcNow.AddMinutes(-2);
        var started = ended.AddSeconds(-30);
        var device = new VoiceCallDeviceSession
        {
            UserId = state == "foreign-owner" ? Guid.NewGuid() : driver,
            InstallationId = "driver-auth-test", Platform = "android", ControlProofHash = new string('a', 64),
            ExpiresAtUtc = state == "expired" ? ended.AddSeconds(-1)
                : state == "expired-after-call" ? ended.AddSeconds(1) : ended.AddDays(1),
            RevokedAtUtc = state == "revoked" ? ended.AddSeconds(-1)
                : state == "revoked-after-call" ? ended.AddSeconds(1) : null
        };
        if (state != "missing") db.VoiceCallDeviceSessions.Add(device);
        var call = new CallSession(stop.Id, driver, CallParticipantType.Driver,
            customer, CallParticipantType.Customer, Guid.NewGuid(), started);
        call.MarkRingingV2(started, ended, device.Id);
        call.MarkMissed(ended);
        db.CallSessions.Add(call);
        db.VoiceCallDeviceReceipts.Add(new VoiceCallDeviceReceipt(call.Id, Guid.NewGuid(), started));
        await db.SaveChangesAsync();
        var clock = new FixedClock();
        var service = new DeliveryContactCaseService(db, clock, new DeliveryContactEligibilityService(db, clock));
        await service.RecordAttemptForMissedCallAsync(call, default);
        await service.RecordAttemptForMissedCallAsync(call, default);
        var attempt = await db.DeliveryContactAttempts.SingleAsync();
        attempt.QualifiesAsNoAnswer.Should().Be(qualifies);
        (await db.DeliveryContactCases.SingleAsync()).QualifiedMissedCount.Should().Be(qualifies ? 1 : 0);
    }

    private sealed class FixedClock : IDateTimeProvider
    {
        public DateTime UtcNow => DateTime.UtcNow;
        public DateOnly TodayUtc => DateOnly.FromDateTime(UtcNow);
    }
}
