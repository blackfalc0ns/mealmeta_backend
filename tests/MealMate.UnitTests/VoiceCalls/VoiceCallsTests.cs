using FluentAssertions;
using MealMate.Application.Common.Abstractions;
using MealMate.Application.Features.VoiceCalls.Commands;
using MealMate.Application.Features.VoiceCalls.Queries;
using MealMate.Domain.CalendarMeals;
using MealMate.Domain.DriverDelivery;
using MealMate.Domain.Enums;
using MealMate.Domain.IdentityAccess;
using MealMate.Domain.ProgramsSubscriptions;
using MealMate.Domain.VoiceCalls;
using Microsoft.EntityFrameworkCore;
using NSubstitute;
using Xunit;

namespace MealMate.UnitTests.VoiceCalls;

public sealed class VoiceCallsTests
{
    private static async Task<(TripStop stop, Guid driverUserId, Guid customerUserId)> SeedDeliveryContext(MealMate.Persistence.AppDbContext db)
    {
        var driverUserId = Guid.NewGuid();
        var customerUserId = Guid.NewGuid();

        var driverProfile = new DriverProfile
        {
            UserId = driverUserId,
            FullNameAr = "أحمد السائق",
            VerificationStatus = VerificationStatus.Approved,
            FullNameEn = "Ahmed Driver"
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
            FullName = "أحمد السائق",
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
            NameAr = "برنامج دايت",
            NameEn = "Diet Program"
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
            CustomerMaskedId = "CUST-001",
            CustomerName = "Fatima Customer",
            ZoneNameAr = "حولي",
            ZoneNameEn = "Hawally",
            MealSummaryAr = "وجبة غداء صحية",
            MealSummaryEn = "Healthy Lunch Meal",
            SlotLabel = "Lunch",
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

    
    private sealed class FixedClock : MealMate.Application.Common.Abstractions.IDateTimeProvider
    {
        public DateTime UtcNow => DateTime.UtcNow;
        public DateOnly TodayUtc => DateOnly.FromDateTime(DateTime.UtcNow);
    }

    private static MealMate.Application.Features.VoiceCalls.Commands.InitiateCallCommandHandler CreateInitiateHandler(
        MealMate.Persistence.AppDbContext db,
        MealMate.Application.Common.Abstractions.ICurrentUser currentUser,
        MealMate.Application.Features.VoiceCalls.Abstractions.ICallSignalingNotifier? notifier = null,
        MealMate.Application.Features.VoiceCalls.Abstractions.IVoiceCallPushNotifier? pushNotifier = null,
        MealMate.Application.Features.VoiceCalls.Abstractions.IVoiceCallParticipantLockService? lockService = null) =>
        new(
            db,
            currentUser,
            notifier,
            pushNotifier,
            lockService,
            new MealMate.Application.Features.VoiceCalls.Services.DeliveryContactEligibilityService(db, new FixedClock()));

    [Fact]
    public async Task InitiateCall_WhenValidCustomerToDriver_ShouldSucceedAndRing()
    {
        // Arrange
        var db = CreateInMemoryDbContext();
        var (stop, driverUserId, customerUserId) = await SeedDeliveryContext(db);

        var currentUser = Substitute.For<ICurrentUser>();
        currentUser.UserId.Returns(customerUserId);

        var handler = CreateInitiateHandler(db, currentUser);
        var clientRequestId = Guid.NewGuid();

        // Act
        var result = await handler.Handle(new InitiateCallCommand(stop.Id, clientRequestId), CancellationToken.None);

        // Assert
        result.IsSuccess.Should().BeTrue("got {0}: {1}", result.Error?.Code, result.Error?.Description);
        result.Value.CallerUserId.Should().Be(customerUserId);
        result.Value.CallerType.Should().Be("Customer");
        result.Value.ReceiverUserId.Should().Be(driverUserId);
        result.Value.ReceiverType.Should().Be("Driver");
        result.Value.Status.Should().Be("Ringing");

        var createdSession = await db.CallSessions.FirstOrDefaultAsync(c => c.Id == result.Value.CallId);
        createdSession.Should().NotBeNull();
        createdSession!.Status.Should().Be(CallStatus.Ringing);
        createdSession.RingingAtUtc.Should().NotBeNull();
    }

    [Fact]
    public async Task InitiateCall_WhenValidDriverToCustomer_ShouldSucceedAndRing()
    {
        // Arrange
        var db = CreateInMemoryDbContext();
        var (stop, driverUserId, customerUserId) = await SeedDeliveryContext(db);

        var currentUser = Substitute.For<ICurrentUser>();
        currentUser.UserId.Returns(driverUserId);

        var handler = CreateInitiateHandler(db, currentUser);
        var clientRequestId = Guid.NewGuid();

        // Act
        var result = await handler.Handle(new InitiateCallCommand(stop.Id, clientRequestId), CancellationToken.None);

        // Assert
        result.IsSuccess.Should().BeTrue();
        result.Value.CallerUserId.Should().Be(driverUserId);
        result.Value.CallerType.Should().Be("Driver");
        result.Value.ReceiverUserId.Should().Be(customerUserId);
        result.Value.ReceiverType.Should().Be("Customer");
        result.Value.Status.Should().Be("Ringing");
    }

    [Fact]
    public async Task InitiateCall_WhenUnauthorized_ShouldReturnUnauthorized()
    {
        // Arrange
        var db = CreateInMemoryDbContext();
        var (stop, _, _) = await SeedDeliveryContext(db);

        var currentUser = Substitute.For<ICurrentUser>();
        currentUser.UserId.Returns((Guid?)null);

        var handler = CreateInitiateHandler(db, currentUser);

        // Act
        var result = await handler.Handle(new InitiateCallCommand(stop.Id, Guid.NewGuid()), CancellationToken.None);

        // Assert
        result.IsFailure.Should().BeTrue();
        result.Error.Code.Should().Be("calls.unauthorized");
    }

    [Fact]
    public async Task InitiateCall_WhenTripStopNotFound_ShouldReturnNotFound()
    {
        // Arrange
        var db = CreateInMemoryDbContext();
        var currentUser = Substitute.For<ICurrentUser>();
        currentUser.UserId.Returns(Guid.NewGuid());

        var handler = CreateInitiateHandler(db, currentUser);

        // Act
        var result = await handler.Handle(new InitiateCallCommand(Guid.NewGuid(), Guid.NewGuid()), CancellationToken.None);

        // Assert
        result.IsFailure.Should().BeTrue();
        result.Error.Code.Should().Be("calls.trip_stop_not_found");
    }

    [Fact]
    public async Task InitiateCall_WhenTripStopDelivered_ShouldReturnDeliveryNotActive()
    {
        // Arrange
        var db = CreateInMemoryDbContext();
        var (stop, _, customerUserId) = await SeedDeliveryContext(db);
        stop.Status = TripStopStatus.Delivered;
        await db.SaveChangesAsync();

        var currentUser = Substitute.For<ICurrentUser>();
        currentUser.UserId.Returns(customerUserId);

        var handler = CreateInitiateHandler(db, currentUser);

        // Act
        var result = await handler.Handle(new InitiateCallCommand(stop.Id, Guid.NewGuid()), CancellationToken.None);

        // Assert
        result.IsFailure.Should().BeTrue();
        result.Error.Code.Should().Be("contact.stop_not_active");
    }

    [Fact]
    public async Task InitiateCall_WhenUnrelatedUserAttemptsCall_ShouldReturnForbidden()
    {
        // Arrange
        var db = CreateInMemoryDbContext();
        var (stop, _, _) = await SeedDeliveryContext(db);
        var unrelatedUserId = Guid.NewGuid();

        var currentUser = Substitute.For<ICurrentUser>();
        currentUser.UserId.Returns(unrelatedUserId);

        var handler = CreateInitiateHandler(db, currentUser);

        // Act
        var result = await handler.Handle(new InitiateCallCommand(stop.Id, Guid.NewGuid()), CancellationToken.None);

        // Assert
        result.IsFailure.Should().BeTrue();
        result.Error.Code.Should().Be("calls.trip_stop_not_found");
    }

    [Fact]
    public async Task InitiateCall_WhenCallerIsAlreadyBusy_ShouldReturnConflict()
    {
        // Arrange
        var db = CreateInMemoryDbContext();
        var (stop, _, customerUserId) = await SeedDeliveryContext(db);

        // Existing active call for customer
        var busySession = new CallSession(
            tripStopId: stop.Id,
            callerUserId: customerUserId,
            callerType: CallParticipantType.Customer,
            receiverUserId: Guid.NewGuid(),
            receiverType: CallParticipantType.Driver,
            clientRequestId: Guid.NewGuid(),
            startedAtUtc: DateTime.UtcNow);
        busySession.MarkRinging(DateTime.UtcNow);
        busySession.Accept(DateTime.UtcNow);
        busySession.MarkConnecting();
        busySession.MarkConnected(DateTime.UtcNow);

        db.CallSessions.Add(busySession);
        await db.SaveChangesAsync();

        var currentUser = Substitute.For<ICurrentUser>();
        currentUser.UserId.Returns(customerUserId);

        var handler = CreateInitiateHandler(db, currentUser);

        // Act
        var result = await handler.Handle(new InitiateCallCommand(stop.Id, Guid.NewGuid()), CancellationToken.None);

        // Assert
        result.IsFailure.Should().BeTrue();
        result.Error.Code.Should().Be("calls.caller_busy");
    }

    [Fact]
    public async Task InitiateCall_WhenReceiverIsAlreadyBusy_ShouldReturnConflict()
    {
        // Arrange
        var db = CreateInMemoryDbContext();
        var (stop, driverUserId, customerUserId) = await SeedDeliveryContext(db);

        // Existing active call for driver
        var busySession = new CallSession(
            tripStopId: stop.Id,
            callerUserId: Guid.NewGuid(),
            callerType: CallParticipantType.Customer,
            receiverUserId: driverUserId,
            receiverType: CallParticipantType.Driver,
            clientRequestId: Guid.NewGuid(),
            startedAtUtc: DateTime.UtcNow);
        busySession.MarkRinging(DateTime.UtcNow);
        busySession.Accept(DateTime.UtcNow);
        busySession.MarkConnecting();
        busySession.MarkConnected(DateTime.UtcNow);

        db.CallSessions.Add(busySession);
        await db.SaveChangesAsync();

        var currentUser = Substitute.For<ICurrentUser>();
        currentUser.UserId.Returns(customerUserId);

        var handler = CreateInitiateHandler(db, currentUser);

        // Act
        var result = await handler.Handle(new InitiateCallCommand(stop.Id, Guid.NewGuid()), CancellationToken.None);

        // Assert
        result.IsFailure.Should().BeTrue();
        result.Error.Code.Should().Be("calls.recipient_busy");
    }

    [Fact]
    public async Task InitiateCall_WhenClientRequestIdIsDuplicate_ShouldReturnExistingCallIdempotently()
    {
        // Arrange
        var db = CreateInMemoryDbContext();
        var (stop, _, customerUserId) = await SeedDeliveryContext(db);
        var clientRequestId = Guid.NewGuid();

        var currentUser = Substitute.For<ICurrentUser>();
        currentUser.UserId.Returns(customerUserId);

        var handler = CreateInitiateHandler(db, currentUser);

        // First call
        var firstResult = await handler.Handle(new InitiateCallCommand(stop.Id, clientRequestId), CancellationToken.None);
        firstResult.IsSuccess.Should().BeTrue();

        // Act: Second call with identical clientRequestId
        var secondResult = await handler.Handle(new InitiateCallCommand(stop.Id, clientRequestId), CancellationToken.None);

        // Assert
        secondResult.IsSuccess.Should().BeTrue();
        secondResult.Value.CallId.Should().Be(firstResult.Value.CallId);

        var count = await db.CallSessions.CountAsync(c => c.ClientRequestId == clientRequestId);
        count.Should().Be(1);
    }

    [Fact]
    public async Task AnswerCall_WhenReceiver_ShouldTransitionToAccepted()
    {
        // Arrange
        var db = CreateInMemoryDbContext();
        var callerId = Guid.NewGuid();
        var receiverId = Guid.NewGuid();

        var session = new CallSession(
            tripStopId: Guid.NewGuid(),
            callerUserId: callerId,
            callerType: CallParticipantType.Driver,
            receiverUserId: receiverId,
            receiverType: CallParticipantType.Customer,
            clientRequestId: Guid.NewGuid(),
            startedAtUtc: DateTime.UtcNow.AddSeconds(-5));
        session.MarkRinging(DateTime.UtcNow.AddSeconds(-4));

        db.CallSessions.Add(session);
        await db.SaveChangesAsync();

        var currentUser = Substitute.For<ICurrentUser>();
        currentUser.UserId.Returns(receiverId);

        var handler = new AnswerCallCommandHandler(db, currentUser);

        // Act
        var result = await handler.Handle(new AnswerCallCommand(session.Id), CancellationToken.None);

        // Assert
        result.IsSuccess.Should().BeTrue();
        result.Value.Status.Should().Be("Accepted");
        result.Value.AcceptedAtUtc.Should().NotBeNull();

        var updated = await db.CallSessions.FindAsync(session.Id);
        updated!.Status.Should().Be(CallStatus.Accepted);
    }

    [Fact]
    public async Task AnswerCall_WhenCallerAttemptsToAnswer_ShouldReturnForbidden()
    {
        // Arrange
        var db = CreateInMemoryDbContext();
        var callerId = Guid.NewGuid();
        var receiverId = Guid.NewGuid();

        var session = new CallSession(
            tripStopId: Guid.NewGuid(),
            callerUserId: callerId,
            callerType: CallParticipantType.Driver,
            receiverUserId: receiverId,
            receiverType: CallParticipantType.Customer,
            clientRequestId: Guid.NewGuid(),
            startedAtUtc: DateTime.UtcNow.AddSeconds(-5));
        session.MarkRinging(DateTime.UtcNow.AddSeconds(-4));
        db.CallSessions.Add(session);
        await db.SaveChangesAsync();

        var currentUser = Substitute.For<ICurrentUser>();
        currentUser.UserId.Returns(callerId); // Caller trying to answer

        var handler = new AnswerCallCommandHandler(db, currentUser);

        // Act
        var result = await handler.Handle(new AnswerCallCommand(session.Id), CancellationToken.None);

        // Assert
        result.IsFailure.Should().BeTrue();
        result.Error.Code.Should().Be("calls.forbidden");
    }

    [Fact]
    public async Task RejectCall_WhenReceiver_ShouldTransitionToRejected()
    {
        // Arrange
        var db = CreateInMemoryDbContext();
        var callerId = Guid.NewGuid();
        var receiverId = Guid.NewGuid();

        var session = new CallSession(
            tripStopId: Guid.NewGuid(),
            callerUserId: callerId,
            callerType: CallParticipantType.Driver,
            receiverUserId: receiverId,
            receiverType: CallParticipantType.Customer,
            clientRequestId: Guid.NewGuid(),
            startedAtUtc: DateTime.UtcNow.AddSeconds(-5));
        session.MarkRinging(DateTime.UtcNow.AddSeconds(-4));
        db.CallSessions.Add(session);
        await db.SaveChangesAsync();

        var currentUser = Substitute.For<ICurrentUser>();
        currentUser.UserId.Returns(receiverId);

        var handler = new RejectCallCommandHandler(db, currentUser);

        // Act
        var result = await handler.Handle(new RejectCallCommand(session.Id, "Declined"), CancellationToken.None);

        // Assert
        result.IsSuccess.Should().BeTrue();
        result.Value.Status.Should().Be("Rejected");

        var updated = await db.CallSessions.FindAsync(session.Id);
        updated!.Status.Should().Be(CallStatus.Rejected);
        updated.EndReason.Should().Be(CallEndReason.ReceiverRejected);
    }

    [Fact]
    public async Task CancelCall_WhenCaller_ShouldTransitionToCancelled()
    {
        // Arrange
        var db = CreateInMemoryDbContext();
        var callerId = Guid.NewGuid();
        var receiverId = Guid.NewGuid();

        var session = new CallSession(
            tripStopId: Guid.NewGuid(),
            callerUserId: callerId,
            callerType: CallParticipantType.Driver,
            receiverUserId: receiverId,
            receiverType: CallParticipantType.Customer,
            clientRequestId: Guid.NewGuid(),
            startedAtUtc: DateTime.UtcNow.AddSeconds(-5));
        session.MarkRinging(DateTime.UtcNow.AddSeconds(-4));
        db.CallSessions.Add(session);
        await db.SaveChangesAsync();

        var currentUser = Substitute.For<ICurrentUser>();
        currentUser.UserId.Returns(callerId);

        var handler = new CancelCallCommandHandler(db, currentUser);

        // Act
        var result = await handler.Handle(new CancelCallCommand(session.Id, "Hangup"), CancellationToken.None);

        // Assert
        result.IsSuccess.Should().BeTrue();
        result.Value.Status.Should().Be("Cancelled");

        var updated = await db.CallSessions.FindAsync(session.Id);
        updated!.Status.Should().Be(CallStatus.Cancelled);
        updated.EndReason.Should().Be(CallEndReason.CallerCancelled);
    }

    [Fact]
    public async Task MarkCallConnectingAndConnected_ShouldTransitionToActiveAndSetConnectedTimestamp()
    {
        // Arrange
        var db = CreateInMemoryDbContext();
        var callerId = Guid.NewGuid();
        var receiverId = Guid.NewGuid();

        var session = new CallSession(
            tripStopId: Guid.NewGuid(),
            callerUserId: callerId,
            callerType: CallParticipantType.Driver,
            receiverUserId: receiverId,
            receiverType: CallParticipantType.Customer,
            clientRequestId: Guid.NewGuid(),
            startedAtUtc: DateTime.UtcNow.AddSeconds(-10));
        session.MarkRinging(DateTime.UtcNow.AddSeconds(-8));
        session.Accept(DateTime.UtcNow.AddSeconds(-6));
        db.CallSessions.Add(session);
        await db.SaveChangesAsync();

        var currentUser = Substitute.For<ICurrentUser>();
        currentUser.UserId.Returns(receiverId);

        var connectingHandler = new MarkCallConnectingCommandHandler(db, currentUser);
        var connectedHandler = new MarkCallConnectedCommandHandler(db, currentUser);

        // Act 1: Connecting
        var connectingResult = await connectingHandler.Handle(new MarkCallConnectingCommand(session.Id), CancellationToken.None);
        connectingResult.IsSuccess.Should().BeTrue();
        connectingResult.Value.Status.Should().Be("Connecting");

        // Act 2: Connected/Active
        var connectedResult = await connectedHandler.Handle(new MarkCallConnectedCommand(session.Id), CancellationToken.None);

        // Assert
        connectedResult.IsSuccess.Should().BeTrue();
        connectedResult.Value.Status.Should().Be("Active");
        connectedResult.Value.ConnectedAtUtc.Should().NotBeNull();

        var updated = await db.CallSessions.FindAsync(session.Id);
        updated!.Status.Should().Be(CallStatus.Active);
        updated.ConnectedAtUtc.Should().NotBeNull();
    }

    [Fact]
    public async Task EndCall_WhenActive_ShouldCalculateDurationStrictlyFromConnectedAtUtc()
    {
        // Arrange
        var db = CreateInMemoryDbContext();
        var callerId = Guid.NewGuid();
        var receiverId = Guid.NewGuid();
        var connectedAt = DateTime.UtcNow.AddSeconds(-60);

        var session = new CallSession(
            tripStopId: Guid.NewGuid(),
            callerUserId: callerId,
            callerType: CallParticipantType.Driver,
            receiverUserId: receiverId,
            receiverType: CallParticipantType.Customer,
            clientRequestId: Guid.NewGuid(),
            startedAtUtc: connectedAt.AddSeconds(-20));
        session.MarkRinging(connectedAt.AddSeconds(-15));
        session.Accept(connectedAt.AddSeconds(-10));
        session.MarkConnecting();
        session.MarkConnected(connectedAt);
        db.CallSessions.Add(session);
        await db.SaveChangesAsync();

        var currentUser = Substitute.For<ICurrentUser>();
        currentUser.UserId.Returns(callerId);

        var handler = new EndCallCommandHandler(db, currentUser);

        // Act
        var result = await handler.Handle(new EndCallCommand(session.Id, "Finished"), CancellationToken.None);

        // Assert
        result.IsSuccess.Should().BeTrue();
        result.Value.Status.Should().Be("Ended");
        result.Value.DurationSeconds.Should().BeGreaterOrEqualTo(55);

        var updated = await db.CallSessions.FindAsync(session.Id);
        updated!.Status.Should().Be(CallStatus.Ended);
        updated.EndReason.Should().Be(CallEndReason.CallerEnded);
    }

    [Fact]
    public async Task EndCall_WhenAlreadyEnded_ShouldBeIdempotent()
    {
        // Arrange
        var db = CreateInMemoryDbContext();
        var callerId = Guid.NewGuid();
        var receiverId = Guid.NewGuid();
        var connectedAt = DateTime.UtcNow.AddSeconds(-30);

        var session = new CallSession(
            tripStopId: Guid.NewGuid(),
            callerUserId: callerId,
            callerType: CallParticipantType.Driver,
            receiverUserId: receiverId,
            receiverType: CallParticipantType.Customer,
            clientRequestId: Guid.NewGuid(),
            startedAtUtc: connectedAt.AddSeconds(-10));
        session.MarkRinging(connectedAt.AddSeconds(-8));
        session.Accept(connectedAt.AddSeconds(-6));
        session.MarkConnecting();
        session.MarkConnected(connectedAt);
        session.End(DateTime.UtcNow, CallEndReason.CallerEnded);
        db.CallSessions.Add(session);
        await db.SaveChangesAsync();

        var currentUser = Substitute.For<ICurrentUser>();
        currentUser.UserId.Returns(callerId);

        var handler = new EndCallCommandHandler(db, currentUser);

        // Act
        var result = await handler.Handle(new EndCallCommand(session.Id), CancellationToken.None);

        // Assert
        result.IsSuccess.Should().BeTrue();
        result.Value.Status.Should().Be("Ended");
    }

    [Fact]
    public async Task GetCallById_WhenParticipant_ShouldReturnCallSessionDto()
    {
        // Arrange
        var db = CreateInMemoryDbContext();
        var callerId = Guid.NewGuid();
        var receiverId = Guid.NewGuid();

        var session = new CallSession(
            tripStopId: Guid.NewGuid(),
            callerUserId: callerId,
            callerType: CallParticipantType.Driver,
            receiverUserId: receiverId,
            receiverType: CallParticipantType.Customer,
            clientRequestId: Guid.NewGuid(),
            startedAtUtc: DateTime.UtcNow);
        db.CallSessions.Add(session);
        await db.SaveChangesAsync();

        var currentUser = Substitute.For<ICurrentUser>();
        currentUser.UserId.Returns(callerId);

        var handler = new GetCallByIdQueryHandler(db, currentUser);

        // Act
        var result = await handler.Handle(new GetCallByIdQuery(session.Id), CancellationToken.None);

        // Assert
        result.IsSuccess.Should().BeTrue();
        result.Value.Id.Should().Be(session.Id);
    }

    [Fact]
    public async Task GetCallById_WhenUnrelatedUser_ShouldReturnNotFoundToPreventEnumeration()
    {
        // Arrange
        var db = CreateInMemoryDbContext();
        var callerId = Guid.NewGuid();
        var receiverId = Guid.NewGuid();

        var session = new CallSession(
            tripStopId: Guid.NewGuid(),
            callerUserId: callerId,
            callerType: CallParticipantType.Driver,
            receiverUserId: receiverId,
            receiverType: CallParticipantType.Customer,
            clientRequestId: Guid.NewGuid(),
            startedAtUtc: DateTime.UtcNow);
        db.CallSessions.Add(session);
        await db.SaveChangesAsync();

        var currentUser = Substitute.For<ICurrentUser>();
        currentUser.UserId.Returns(Guid.NewGuid()); // Unrelated user

        var handler = new GetCallByIdQueryHandler(db, currentUser);

        // Act
        var result = await handler.Handle(new GetCallByIdQuery(session.Id), CancellationToken.None);

        // Assert
        result.IsFailure.Should().BeTrue();
        result.Error.Code.Should().Be("calls.not_found");
    }

    [Fact]
    public async Task GetActiveCall_WhenUserHasOngoingCall_ShouldReturnCall()
    {
        // Arrange
        var db = CreateInMemoryDbContext();
        var callerId = Guid.NewGuid();
        var receiverId = Guid.NewGuid();

        var session = new CallSession(
            tripStopId: Guid.NewGuid(),
            callerUserId: callerId,
            callerType: CallParticipantType.Driver,
            receiverUserId: receiverId,
            receiverType: CallParticipantType.Customer,
            clientRequestId: Guid.NewGuid(),
            startedAtUtc: DateTime.UtcNow);
        session.MarkRinging(DateTime.UtcNow);
        db.CallSessions.Add(session);
        await db.SaveChangesAsync();

        var currentUser = Substitute.For<ICurrentUser>();
        currentUser.UserId.Returns(receiverId);

        var handler = new GetActiveCallQueryHandler(db, currentUser);

        // Act
        var result = await handler.Handle(new GetActiveCallQuery(), CancellationToken.None);

        // Assert
        result.IsSuccess.Should().BeTrue();
        result.Value.Should().NotBeNull();
        result.Value!.Id.Should().Be(session.Id);
    }

    [Fact]
    public async Task GetActiveCall_WhenUserHasNoOngoingCall_ShouldReturnNull()
    {
        // Arrange
        var db = CreateInMemoryDbContext();
        var callerId = Guid.NewGuid();
        var receiverId = Guid.NewGuid();

        var session = new CallSession(
            tripStopId: Guid.NewGuid(),
            callerUserId: callerId,
            callerType: CallParticipantType.Driver,
            receiverUserId: receiverId,
            receiverType: CallParticipantType.Customer,
            clientRequestId: Guid.NewGuid(),
            startedAtUtc: DateTime.UtcNow);
        session.MarkRinging(DateTime.UtcNow);
        session.Reject(DateTime.UtcNow); // Terminal state
        db.CallSessions.Add(session);
        await db.SaveChangesAsync();

        var currentUser = Substitute.For<ICurrentUser>();
        currentUser.UserId.Returns(receiverId);

        var handler = new GetActiveCallQueryHandler(db, currentUser);

        // Act
        var result = await handler.Handle(new GetActiveCallQuery(), CancellationToken.None);

        // Assert
        result.IsSuccess.Should().BeTrue();
        result.Value.Should().BeNull();
    }

    private static MealMate.Persistence.AppDbContext CreateInMemoryDbContext()
    {
        var options = new DbContextOptionsBuilder<MealMate.Persistence.AppDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString())
            .Options;

        var dispatcher = Substitute.For<MealMate.Application.Common.Abstractions.IDomainEventDispatcher>();
        return new MealMate.Persistence.AppDbContext(options, dispatcher);
    }
}
