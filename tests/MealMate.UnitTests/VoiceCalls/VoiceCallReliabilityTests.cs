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

public sealed class VoiceCallReliabilityTests
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

    #region 1. Push Notification Tests

    
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
    public async Task InitiateCall_DispatchesPushToReceiverOnly_NotToCaller()
    {
        // Arrange
        var db = CreateInMemoryDbContext();
        var (stop, driverUserId, customerUserId) = await SeedDeliveryContext(db);

        var currentUser = Substitute.For<ICurrentUser>();
        currentUser.UserId.Returns(customerUserId); // Customer calling Driver

        var notifier = Substitute.For<ICallSignalingNotifier>();
        var pushNotifier = Substitute.For<IVoiceCallPushNotifier>();

        var handler = CreateInitiateHandler(db, currentUser, notifier, pushNotifier);
        var clientRequestId = Guid.NewGuid();

        // Act
        var result = await handler.Handle(new InitiateCallCommand(stop.Id, clientRequestId), CancellationToken.None);

        // Assert
        result.IsSuccess.Should().BeTrue();

        // Push must target driver (receiver), NOT customer (caller)
        await pushNotifier.Received(1).SendIncomingCallPushAsync(
            result.Value.CallId,
            driverUserId,
            customerUserId,
            "Customer",
            stop.Id,
            Arg.Any<CancellationToken>());

        // Caller must never be targeted by incoming call push
        await pushNotifier.DidNotReceive().SendIncomingCallPushAsync(
            Arg.Any<Guid>(),
            customerUserId,
            Arg.Any<Guid>(),
            Arg.Any<string>(),
            Arg.Any<Guid?>(),
            Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task VoiceCallPushNotifier_SendsToAllActiveTokens_AndDeactivatesInvalidTokens()
    {
        // Arrange
        var db = CreateInMemoryDbContext();
        var receiverUserId = Guid.NewGuid();

        var activeToken = new CustomerDeviceToken
        {
            UserId = receiverUserId,
            Token = "fcm_token_valid_123",
            IsActive = true,
            LastSeenAtUtc = DateTime.UtcNow.AddDays(-1)
        };
        var invalidToken = new CustomerDeviceToken
        {
            UserId = receiverUserId,
            Token = "fcm_token_expired_456",
            IsActive = true,
            LastSeenAtUtc = DateTime.UtcNow.AddDays(-2)
        };
        db.CustomerDeviceTokens.AddRange(activeToken, invalidToken);
        await db.SaveChangesAsync();

        var fcmClient = Substitute.For<IFcmMessagingClient>();
        fcmClient.SendAsync(
            "fcm_token_valid_123",
            Arg.Any<string>(),
            Arg.Any<string>(),
            Arg.Any<IReadOnlyDictionary<string, string>>(),
            Arg.Any<CancellationToken>())
            .Returns(FcmSendResult.Success());

        fcmClient.SendAsync(
            "fcm_token_expired_456",
            Arg.Any<string>(),
            Arg.Any<string>(),
            Arg.Any<IReadOnlyDictionary<string, string>>(),
            Arg.Any<CancellationToken>())
            .Returns(FcmSendResult.InvalidToken("Device token unregistered"));

        var logger = Substitute.For<ILogger<VoiceCallPushNotifier>>();
        var pushNotifier = new VoiceCallPushNotifier(db, fcmClient, logger);

        var callId = Guid.NewGuid();
        var callerUserId = Guid.NewGuid();
        var tripStopId = Guid.NewGuid();

        // Act
        await pushNotifier.SendIncomingCallPushAsync(
            callId,
            receiverUserId,
            callerUserId,
            "Driver",
            tripStopId,
            CancellationToken.None);

        // Assert
        // Token 1 was valid and remains active
        var reloadedValid = await db.CustomerDeviceTokens.FirstAsync(t => t.Token == "fcm_token_valid_123");
        reloadedValid.IsActive.Should().BeTrue();

        // Token 2 was identified as invalid and deactivated
        var reloadedInvalid = await db.CustomerDeviceTokens.FirstAsync(t => t.Token == "fcm_token_expired_456");
        reloadedInvalid.IsActive.Should().BeFalse();
        reloadedInvalid.ModifiedAtUtc.Should().NotBeNull();

        // Verify push payload fields
        await fcmClient.Received().SendAsync(
            "fcm_token_valid_123",
            "مكالمة صوتية واردة",
            Arg.Any<string>(),
            Arg.Is<IReadOnlyDictionary<string, string>>(d =>
                d["type"] == "incoming_call" &&
                d["callId"] == callId.ToString() &&
                d["callerType"] == "Driver" &&
                d["tripStopId"] == tripStopId.ToString() &&
                !d.ContainsKey("sdp") &&
                !d.ContainsKey("ice") &&
                !d.ContainsKey("password")),
            Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task InitiateCall_WhenPushFails_DoesNotRollbackCall()
    {
        // Arrange
        var db = CreateInMemoryDbContext();
        var (stop, driverUserId, customerUserId) = await SeedDeliveryContext(db);

        var currentUser = Substitute.For<ICurrentUser>();
        currentUser.UserId.Returns(customerUserId);

        var notifier = Substitute.For<ICallSignalingNotifier>();
        var pushNotifier = Substitute.For<IVoiceCallPushNotifier>();
        pushNotifier
            .When(p => p.SendIncomingCallPushAsync(Arg.Any<Guid>(), Arg.Any<Guid>(), Arg.Any<Guid>(), Arg.Any<string>(), Arg.Any<Guid?>(), Arg.Any<CancellationToken>()))
            .Do(_ => throw new HttpRequestException("FCM service unavailable"));

        var handler = CreateInitiateHandler(db, currentUser, notifier, pushNotifier);

        // Act
        var result = await handler.Handle(new InitiateCallCommand(stop.Id, Guid.NewGuid()), CancellationToken.None);

        // Assert: Call creation must still succeed despite push transport failure
        result.IsSuccess.Should().BeTrue();
        var created = await db.CallSessions.FirstOrDefaultAsync(c => c.Id == result.Value.CallId);
        created.Should().NotBeNull();
        created!.Status.Should().Be(CallStatus.Ringing);
    }

    [Fact]
    public async Task CancelCall_DispatchesTeardownPush()
    {
        // Arrange
        var db = CreateInMemoryDbContext();
        var (stop, driverUserId, customerUserId) = await SeedDeliveryContext(db);

        var currentUser = Substitute.For<ICurrentUser>();
        currentUser.UserId.Returns(customerUserId);

        var call = new CallSession(stop.Id, customerUserId, CallParticipantType.Customer, driverUserId, CallParticipantType.Driver, Guid.NewGuid(), DateTime.UtcNow);
        call.MarkRinging(DateTime.UtcNow);
        db.CallSessions.Add(call);
        await db.SaveChangesAsync();

        var pushNotifier = Substitute.For<IVoiceCallPushNotifier>();
        var handler = new CancelCallCommandHandler(db, currentUser, null, pushNotifier);

        // Act
        var result = await handler.Handle(new CancelCallCommand(call.Id), CancellationToken.None);

        // Assert
        result.IsSuccess.Should().BeTrue();
        await pushNotifier.Received(1).SendCallCancelledPushAsync(call.Id, driverUserId, Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task MarkCallMissed_DispatchesTeardownPush()
    {
        // Arrange
        var db = CreateInMemoryDbContext();
        var (stop, driverUserId, customerUserId) = await SeedDeliveryContext(db);

        var call = new CallSession(stop.Id, customerUserId, CallParticipantType.Customer, driverUserId, CallParticipantType.Driver, Guid.NewGuid(), DateTime.UtcNow);
        call.MarkRinging(DateTime.UtcNow);
        db.CallSessions.Add(call);
        await db.SaveChangesAsync();

        var pushNotifier = Substitute.For<IVoiceCallPushNotifier>();
        var handler = new MarkCallMissedCommandHandler(db, null, pushNotifier);

        // Act
        var result = await handler.Handle(new MarkCallMissedCommand(call.Id), CancellationToken.None);

        // Assert
        result.IsSuccess.Should().BeTrue();
        await pushNotifier.Received(1).SendCallMissedPushAsync(call.Id, driverUserId, Arg.Any<CancellationToken>());
    }

    #endregion

    #region 2. Post-Commit Transaction Ordering & Rollback Protection

    [Fact]
    public async Task InitiateCall_WhenLockServiceFails_SendsNoSignalROrPushNotifications()
    {
        // Arrange
        var db = CreateInMemoryDbContext();
        var (stop, driverUserId, customerUserId) = await SeedDeliveryContext(db);

        var currentUser = Substitute.For<ICurrentUser>();
        currentUser.UserId.Returns(customerUserId);

        var notifier = Substitute.For<ICallSignalingNotifier>();
        var pushNotifier = Substitute.For<IVoiceCallPushNotifier>();

        var failingLockService = Substitute.For<IVoiceCallParticipantLockService>();
        failingLockService
            .ExecuteInParticipantLockAsync<Result<CallSession>>(
                Arg.Any<Guid>(),
                Arg.Any<Guid>(),
                Arg.Any<Func<CancellationToken, Task<Result<CallSession>>>>(),
                Arg.Any<Func<CancellationToken, Task<Result<CallSession>?>>>(),
                Arg.Any<CancellationToken>())
            .Returns(Result.Failure<CallSession>(VoiceCallErrors.ConcurrencyConflict()));

        var handler = CreateInitiateHandler(db, currentUser, notifier, pushNotifier, failingLockService);

        // Act
        var result = await handler.Handle(new InitiateCallCommand(stop.Id, Guid.NewGuid()), CancellationToken.None);

        // Assert
        result.IsFailure.Should().BeTrue();

        // NO SignalR notifications sent
        await notifier.DidNotReceive().NotifyIncomingCallAsync(
            Arg.Any<Guid>(), Arg.Any<Guid>(), Arg.Any<Guid>(), Arg.Any<string>(), Arg.Any<Guid>(), Arg.Any<DateTime>(), Arg.Any<DateTime?>(), Arg.Any<CancellationToken>());

        // NO Push notifications sent
        await pushNotifier.DidNotReceive().SendIncomingCallPushAsync(
            Arg.Any<Guid>(), Arg.Any<Guid>(), Arg.Any<Guid>(), Arg.Any<string>(), Arg.Any<Guid?>(), Arg.Any<CancellationToken>());
    }

    #endregion

    #region 3. Missed Call Timeout Worker Tests

    [Fact]
    public async Task TimeoutWorker_MarksExpiredRingingCallAsMissed()
    {
        // Arrange
        var db = CreateInMemoryDbContext();
        var (stop, driverUserId, customerUserId) = await SeedDeliveryContext(db);

        var expiredRingingCall = new CallSession(
            stop.Id,
            customerUserId,
            CallParticipantType.Customer,
            driverUserId,
            CallParticipantType.Driver,
            Guid.NewGuid(),
            DateTime.UtcNow.AddSeconds(-45));
        expiredRingingCall.MarkRinging(DateTime.UtcNow.AddSeconds(-45));
        db.CallSessions.Add(expiredRingingCall);
        await db.SaveChangesAsync();

        var services = new ServiceCollection();
        services.AddSingleton<IAppDbContext>(db);

        var sender = Substitute.For<ISender>();
        sender.Send(Arg.Is<MarkCallMissedCommand>(cmd => cmd.CallId == expiredRingingCall.Id), Arg.Any<CancellationToken>())
            .Returns(async _ =>
            {
                var cmdHandler = new MarkCallMissedCommandHandler(db);
                return await cmdHandler.Handle(new MarkCallMissedCommand(expiredRingingCall.Id), CancellationToken.None);
            });
        services.AddScoped<ISender>(_ => sender);

        var serviceProvider = services.BuildServiceProvider();
        var scopeFactory = serviceProvider.GetRequiredService<IServiceScopeFactory>();

        var options = Microsoft.Extensions.Options.Options.Create(new VoiceCallOptions
        {
            RingTimeoutSeconds = 30,
            TimeoutWorkerIntervalSeconds = 5,
            MaxBatchSize = 10
        });

        var logger = Substitute.For<ILogger<MissedCallTimeoutJob>>();
        var job = new MissedCallTimeoutJob(scopeFactory, options, logger);

        // Act
        var processed = await job.ProcessExpiredCallsAsync(CancellationToken.None);

        // Assert
        processed.Should().Be(1);
        var reloaded = await db.CallSessions.FindAsync(expiredRingingCall.Id);
        reloaded.Should().NotBeNull();
        reloaded!.Status.Should().Be(CallStatus.Missed);
        reloaded.EndReason.Should().Be(CallEndReason.NoAnswer);
    }

    [Fact]
    public async Task TimeoutWorker_IgnoresYoungRingingCall()
    {
        // Arrange
        var db = CreateInMemoryDbContext();
        var (stop, driverUserId, customerUserId) = await SeedDeliveryContext(db);

        var youngCall = new CallSession(
            stop.Id,
            customerUserId,
            CallParticipantType.Customer,
            driverUserId,
            CallParticipantType.Driver,
            Guid.NewGuid(),
            DateTime.UtcNow.AddSeconds(-10));
        youngCall.MarkRinging(DateTime.UtcNow.AddSeconds(-10));
        db.CallSessions.Add(youngCall);
        await db.SaveChangesAsync();

        var services = new ServiceCollection();
        services.AddSingleton<IAppDbContext>(db);
        var sender = Substitute.For<ISender>();
        services.AddScoped<ISender>(_ => sender);

        var serviceProvider = services.BuildServiceProvider();
        var scopeFactory = serviceProvider.GetRequiredService<IServiceScopeFactory>();

        var options = Microsoft.Extensions.Options.Options.Create(new VoiceCallOptions
        {
            RingTimeoutSeconds = 30,
            MaxBatchSize = 10
        });

        var logger = Substitute.For<ILogger<MissedCallTimeoutJob>>();
        var job = new MissedCallTimeoutJob(scopeFactory, options, logger);

        // Act
        var processed = await job.ProcessExpiredCallsAsync(CancellationToken.None);

        // Assert
        processed.Should().Be(0);
        var reloaded = await db.CallSessions.FindAsync(youngCall.Id);
        reloaded!.Status.Should().Be(CallStatus.Ringing);
    }

    [Fact]
    public async Task TimeoutWorker_DoesNotTouchAcceptedOrActiveCalls()
    {
        // Arrange
        var db = CreateInMemoryDbContext();
        var (stop, driverUserId, customerUserId) = await SeedDeliveryContext(db);

        var acceptedCall = new CallSession(stop.Id, customerUserId, CallParticipantType.Customer, driverUserId, CallParticipantType.Driver, Guid.NewGuid(), DateTime.UtcNow.AddMinutes(-5));
        acceptedCall.MarkRinging(DateTime.UtcNow.AddMinutes(-5));
        acceptedCall.Accept(DateTime.UtcNow.AddMinutes(-4));

        var activeCall = new CallSession(stop.Id, customerUserId, CallParticipantType.Customer, driverUserId, CallParticipantType.Driver, Guid.NewGuid(), DateTime.UtcNow.AddMinutes(-10));
        activeCall.MarkRinging(DateTime.UtcNow.AddMinutes(-10));
        activeCall.Accept(DateTime.UtcNow.AddMinutes(-9));
        activeCall.MarkConnecting();
        activeCall.MarkConnected(DateTime.UtcNow.AddMinutes(-8));

        db.CallSessions.AddRange(acceptedCall, activeCall);
        await db.SaveChangesAsync();

        var services = new ServiceCollection();
        services.AddSingleton<IAppDbContext>(db);
        var sender = Substitute.For<ISender>();
        services.AddScoped<ISender>(_ => sender);

        var serviceProvider = services.BuildServiceProvider();
        var scopeFactory = serviceProvider.GetRequiredService<IServiceScopeFactory>();

        var options = Microsoft.Extensions.Options.Options.Create(new VoiceCallOptions { RingTimeoutSeconds = 30 });
        var logger = Substitute.For<ILogger<MissedCallTimeoutJob>>();
        var job = new MissedCallTimeoutJob(scopeFactory, options, logger);

        // Act
        var processed = await job.ProcessExpiredCallsAsync(CancellationToken.None);

        // Assert
        processed.Should().Be(0);
        (await db.CallSessions.FindAsync(acceptedCall.Id))!.Status.Should().Be(CallStatus.Accepted);
        (await db.CallSessions.FindAsync(activeCall.Id))!.Status.Should().Be(CallStatus.Active);
    }

    [Fact]
    public async Task TimeoutWorker_SurvivesIndividualRowFailure_AndProcessesRemainingBatch()
    {
        // Arrange
        var db = CreateInMemoryDbContext();
        var (stop, driverUserId, customerUserId) = await SeedDeliveryContext(db);

        var call1 = new CallSession(stop.Id, customerUserId, CallParticipantType.Customer, driverUserId, CallParticipantType.Driver, Guid.NewGuid(), DateTime.UtcNow.AddSeconds(-50));
        call1.MarkRinging(DateTime.UtcNow.AddSeconds(-50));

        var call2 = new CallSession(stop.Id, customerUserId, CallParticipantType.Customer, driverUserId, CallParticipantType.Driver, Guid.NewGuid(), DateTime.UtcNow.AddSeconds(-40));
        call2.MarkRinging(DateTime.UtcNow.AddSeconds(-40));

        db.CallSessions.AddRange(call1, call2);
        await db.SaveChangesAsync();

        var services = new ServiceCollection();
        services.AddSingleton<IAppDbContext>(db);

        var sender = Substitute.For<ISender>();
        // First call throws exception
        sender.Send(Arg.Is<MarkCallMissedCommand>(cmd => cmd.CallId == call1.Id), Arg.Any<CancellationToken>())
            .Returns(Task.FromException<Result<CallSessionDto>>(new InvalidOperationException("Simulated transient error")));

        // Second call succeeds
        sender.Send(Arg.Is<MarkCallMissedCommand>(cmd => cmd.CallId == call2.Id), Arg.Any<CancellationToken>())
            .Returns(async _ =>
            {
                var cmdHandler = new MarkCallMissedCommandHandler(db);
                return await cmdHandler.Handle(new MarkCallMissedCommand(call2.Id), CancellationToken.None);
            });

        services.AddScoped<ISender>(_ => sender);

        var serviceProvider = services.BuildServiceProvider();
        var scopeFactory = serviceProvider.GetRequiredService<IServiceScopeFactory>();

        var options = Microsoft.Extensions.Options.Options.Create(new VoiceCallOptions { RingTimeoutSeconds = 30 });
        var logger = Substitute.For<ILogger<MissedCallTimeoutJob>>();
        var job = new MissedCallTimeoutJob(scopeFactory, options, logger);

        // Act
        var processed = await job.ProcessExpiredCallsAsync(CancellationToken.None);

        // Assert: 1 call succeeded despite the first one throwing
        processed.Should().Be(1);
        (await db.CallSessions.FindAsync(call2.Id))!.Status.Should().Be(CallStatus.Missed);
    }

    #endregion

    #region 4. Answer vs Timeout Concurrency Race

    [Fact]
    public async Task AnswerVsTimeout_WhenReceiverAnswersConcurrently_AnswerWinsAndTimeoutFailsGracefully()
    {
        // Arrange
        var db = CreateInMemoryDbContext();
        var (stop, driverUserId, customerUserId) = await SeedDeliveryContext(db);

        var call = new CallSession(stop.Id, customerUserId, CallParticipantType.Customer, driverUserId, CallParticipantType.Driver, Guid.NewGuid(), DateTime.UtcNow.AddSeconds(-35));
        call.MarkRinging(DateTime.UtcNow.AddSeconds(-35));
        db.CallSessions.Add(call);
        await db.SaveChangesAsync();

        // 1. Receiver answers the call concurrently
        var currentUser = Substitute.For<ICurrentUser>();
        currentUser.UserId.Returns(driverUserId);
        var answerHandler = new AnswerCallCommandHandler(db, currentUser);
        var answerResult = await answerHandler.Handle(new AnswerCallCommand(call.Id), CancellationToken.None);

        answerResult.IsSuccess.Should().BeTrue();
        answerResult.Value.Status.Should().Be("Accepted");

        // 2. Timeout worker now attempts to mark call missed
        var missedHandler = new MarkCallMissedCommandHandler(db);
        var missedResult = await missedHandler.Handle(new MarkCallMissedCommand(call.Id), CancellationToken.None);

        // Assert: Timeout attempt must fail with InvalidState, call remains Accepted
        missedResult.IsFailure.Should().BeTrue();
        var reloaded = await db.CallSessions.FindAsync(call.Id);
        reloaded!.Status.Should().Be(CallStatus.Accepted);
    }

    #endregion

    #region 5. Concurrency & Idempotency Interaction

    [Fact]
    public async Task InitiateCall_SameClientRequestIdRetriedConcurrently_ReturnsSameSessionWithoutBusyConflict()
    {
        // Arrange
        var db = CreateInMemoryDbContext();
        var (stop, driverUserId, customerUserId) = await SeedDeliveryContext(db);

        var currentUser = Substitute.For<ICurrentUser>();
        currentUser.UserId.Returns(customerUserId);

        var handler = CreateInitiateHandler(db, currentUser);
        var clientRequestId = Guid.NewGuid();

        // Act: First request
        var first = await handler.Handle(new InitiateCallCommand(stop.Id, clientRequestId), CancellationToken.None);
        // Act: Second request with exact same ClientRequestId (retry)
        var retry = await handler.Handle(new InitiateCallCommand(stop.Id, clientRequestId), CancellationToken.None);

        // Assert: Both succeed and point to the identical CallSession ID
        first.IsSuccess.Should().BeTrue();
        retry.IsSuccess.Should().BeTrue();
        retry.Value.CallId.Should().Be(first.Value.CallId);
    }

    [Fact]
    public async Task InitiateCall_SameCallerDifferentClientRequestId_ReturnsCallerBusy()
    {
        // Arrange
        var db = CreateInMemoryDbContext();
        var (stop, driverUserId, customerUserId) = await SeedDeliveryContext(db);

        var currentUser = Substitute.For<ICurrentUser>();
        currentUser.UserId.Returns(customerUserId);

        var handler = CreateInitiateHandler(db, currentUser);

        // Act: First call initiated
        var first = await handler.Handle(new InitiateCallCommand(stop.Id, Guid.NewGuid()), CancellationToken.None);
        first.IsSuccess.Should().BeTrue();

        // Act: Second call from same caller while first call is Ringing
        var second = await handler.Handle(new InitiateCallCommand(stop.Id, Guid.NewGuid()), CancellationToken.None);

        // Assert: Second call must be rejected because caller is busy
        second.IsFailure.Should().BeTrue();
        second.Error.Code.Should().Be(VoiceCallErrors.CallerBusy().Code);
    }

    [Fact]
    public void ParticipantLock_DeterministicOrdering_PreventsDeadlockOnBidirectionalCall()
    {
        var guidA = Guid.Parse("00000000-0000-0000-0000-000000000001");
        var guidB = Guid.Parse("00000000-0000-0000-0000-000000000002");

        // When A calls B:
        var (first1, second1) = guidA.CompareTo(guidB) <= 0 ? (guidA, guidB) : (guidB, guidA);

        // When B calls A:
        var (first2, second2) = guidB.CompareTo(guidA) <= 0 ? (guidB, guidA) : (guidA, guidB);

        // Both lock sequences MUST be identical (A then B) to guarantee no deadlock
        first1.Should().Be(guidA);
        second1.Should().Be(guidB);

        first2.Should().Be(guidA);
        second2.Should().Be(guidB);
    }

    #endregion

    #region 6. Coturn TLS Validation Tests

    [Fact]
    public void TurnCredentialService_WhenEnableTlsFalse_DoesNotIncludeTurnsUrl()
    {
        // Arrange
        var options = Microsoft.Extensions.Options.Options.Create(new CoturnOptions
        {
            Enabled = true,
            EnableTls = false,
            TlsCertConfigured = true,
            ServerHost = "turn.example.com",
            SharedSecret = "secret"
        });

        var service = new TurnCredentialService(options);

        // Act
        var creds = service.GenerateCredentials("user-1");

        // Assert
        creds.IceServers.Should().NotContain(s => s.Urls.Any(u => u.StartsWith("turns:")));
    }

    [Fact]
    public void TurnCredentialService_WhenEnableTlsTrueAndValid_IncludesTurnsUrl()
    {
        // Arrange
        var options = Microsoft.Extensions.Options.Options.Create(new CoturnOptions
        {
            Enabled = true,
            EnableTls = true,
            TlsCertConfigured = true,
            ServerHost = "turn.example.com",
            SharedSecret = "secret",
            TurnsPort = 5349
        });

        var service = new TurnCredentialService(options);

        // Act
        var creds = service.GenerateCredentials("user-1");

        // Assert
        creds.IceServers.Should().Contain(s => s.Urls.Any(u => u.StartsWith("turns:turn.example.com:5349")));
    }

    [Fact]
    public void TurnCredentialService_WhenTlsCertConfiguredFalse_DoesNotIncludeTurnsUrlEvenIfEnableTlsIsTrue()
    {
        // Arrange
        var options = Microsoft.Extensions.Options.Options.Create(new CoturnOptions
        {
            Enabled = true,
            EnableTls = true,
            TlsCertConfigured = false, // No cert configured in environment
            ServerHost = "turn.example.com",
            SharedSecret = "secret"
        });

        var service = new TurnCredentialService(options);

        // Act
        var creds = service.GenerateCredentials("user-1");

        // Assert: turns: URL must NOT be advertised when certificates are not configured
        creds.IceServers.Should().NotContain(s => s.Urls.Any(u => u.StartsWith("turns:")));
    }

    #endregion

    #region Part 6 — Production Hardening, Recovery & Lifecycle Tests

    [Fact]
    public async Task RecoveryWorker_RecoversStaleAcceptedCall_ToFailedWithConnectionFailed()
    {
        // Arrange
        var db = CreateInMemoryDbContext();
        var (stop, driverUserId, customerUserId) = await SeedDeliveryContext(db);

        var staleAccepted = new CallSession(
            stop.Id,
            customerUserId,
            CallParticipantType.Customer,
            driverUserId,
            CallParticipantType.Driver,
            Guid.NewGuid(),
            DateTime.UtcNow.AddSeconds(-30));
        staleAccepted.MarkRinging(DateTime.UtcNow.AddSeconds(-25));
        staleAccepted.Accept(DateTime.UtcNow.AddSeconds(-25)); // 25s ago > AcceptedSetupTimeout (20s)
        db.CallSessions.Add(staleAccepted);
        await db.SaveChangesAsync();

        var services = new ServiceCollection();
        services.AddSingleton<IAppDbContext>(db);
        var currentUser = Substitute.For<ICurrentUser>();
        currentUser.UserId.Returns((Guid?)null);
        var notifier = Substitute.For<ICallSignalingNotifier>();
        var handler = new MarkCallFailedCommandHandler(db, currentUser, notifier);

        var sender = Substitute.For<ISender>();
        sender.Send(Arg.Any<MarkCallFailedCommand>(), Arg.Any<CancellationToken>())
            .Returns(call => handler.Handle(call.Arg<MarkCallFailedCommand>(), CancellationToken.None));
        services.AddScoped<ISender>(_ => sender);

        var serviceProvider = services.BuildServiceProvider();
        var scopeFactory = serviceProvider.GetRequiredService<IServiceScopeFactory>();

        var options = Microsoft.Extensions.Options.Options.Create(new VoiceCallOptions
        {
            AcceptedSetupTimeoutSeconds = 20,
            MaxBatchSize = 10
        });

        var logger = Substitute.For<ILogger<VoiceCallRecoveryJob>>();
        var job = new VoiceCallRecoveryJob(scopeFactory, options, NullVoiceCallRecoveryCoordinator.Instance, logger);

        // Act
        var processed = await job.ProcessExpiredAcceptedCallsAsync(db, sender, CancellationToken.None);

        // Assert
        processed.Should().Be(1);
        var reloaded = await db.CallSessions.FindAsync(staleAccepted.Id);
        reloaded.Should().NotBeNull();
        reloaded!.Status.Should().Be(CallStatus.Failed);
        reloaded.EndReason.Should().Be(CallEndReason.ConnectionFailed);
    }

    [Fact]
    public async Task RecoveryWorker_IgnoresFreshAcceptedCall()
    {
        // Arrange
        var db = CreateInMemoryDbContext();
        var (stop, driverUserId, customerUserId) = await SeedDeliveryContext(db);

        var freshAccepted = new CallSession(
            stop.Id,
            customerUserId,
            CallParticipantType.Customer,
            driverUserId,
            CallParticipantType.Driver,
            Guid.NewGuid(),
            DateTime.UtcNow.AddSeconds(-10));
        freshAccepted.MarkRinging(DateTime.UtcNow.AddSeconds(-8));
        freshAccepted.Accept(DateTime.UtcNow.AddSeconds(-5)); // 5s ago < AcceptedSetupTimeout (20s)
        db.CallSessions.Add(freshAccepted);
        await db.SaveChangesAsync();

        var services = new ServiceCollection();
        services.AddSingleton<IAppDbContext>(db);
        var sender = Substitute.For<ISender>();
        services.AddScoped<ISender>(_ => sender);

        var serviceProvider = services.BuildServiceProvider();
        var scopeFactory = serviceProvider.GetRequiredService<IServiceScopeFactory>();

        var options = Microsoft.Extensions.Options.Options.Create(new VoiceCallOptions
        {
            AcceptedSetupTimeoutSeconds = 20,
            MaxBatchSize = 10
        });

        var logger = Substitute.For<ILogger<VoiceCallRecoveryJob>>();
        var job = new VoiceCallRecoveryJob(scopeFactory, options, NullVoiceCallRecoveryCoordinator.Instance, logger);

        // Act
        var processed = await job.ProcessExpiredAcceptedCallsAsync(db, sender, CancellationToken.None);

        // Assert
        processed.Should().Be(0);
        var reloaded = await db.CallSessions.FindAsync(freshAccepted.Id);
        reloaded!.Status.Should().Be(CallStatus.Accepted);
    }

    [Fact]
    public async Task RecoveryWorker_RecoversStaleConnectingCall_ToFailedWithConnectionFailed()
    {
        // Arrange
        var db = CreateInMemoryDbContext();
        var (stop, driverUserId, customerUserId) = await SeedDeliveryContext(db);

        var staleConnecting = new CallSession(
            stop.Id,
            customerUserId,
            CallParticipantType.Customer,
            driverUserId,
            CallParticipantType.Driver,
            Guid.NewGuid(),
            DateTime.UtcNow.AddSeconds(-50));
        staleConnecting.MarkRinging(DateTime.UtcNow.AddSeconds(-45));
        staleConnecting.Accept(DateTime.UtcNow.AddSeconds(-40));
        staleConnecting.MarkConnecting(DateTime.UtcNow.AddSeconds(-35)); // 35s ago > ConnectingTimeout (30s)
        db.CallSessions.Add(staleConnecting);
        await db.SaveChangesAsync();

        var services = new ServiceCollection();
        services.AddSingleton<IAppDbContext>(db);
        var currentUser = Substitute.For<ICurrentUser>();
        currentUser.UserId.Returns((Guid?)null);
        var notifier = Substitute.For<ICallSignalingNotifier>();
        var handler = new MarkCallFailedCommandHandler(db, currentUser, notifier);

        var sender = Substitute.For<ISender>();
        sender.Send(Arg.Any<MarkCallFailedCommand>(), Arg.Any<CancellationToken>())
            .Returns(call => handler.Handle(call.Arg<MarkCallFailedCommand>(), CancellationToken.None));
        services.AddScoped<ISender>(_ => sender);

        var serviceProvider = services.BuildServiceProvider();
        var scopeFactory = serviceProvider.GetRequiredService<IServiceScopeFactory>();

        var options = Microsoft.Extensions.Options.Options.Create(new VoiceCallOptions
        {
            ConnectingTimeoutSeconds = 30,
            MaxBatchSize = 10
        });

        var logger = Substitute.For<ILogger<VoiceCallRecoveryJob>>();
        var job = new VoiceCallRecoveryJob(scopeFactory, options, NullVoiceCallRecoveryCoordinator.Instance, logger);

        // Act
        var processed = await job.ProcessExpiredConnectingCallsAsync(db, sender, CancellationToken.None);

        // Assert
        processed.Should().Be(1);
        var reloaded = await db.CallSessions.FindAsync(staleConnecting.Id);
        reloaded.Should().NotBeNull();
        reloaded!.Status.Should().Be(CallStatus.Failed);
        reloaded.EndReason.Should().Be(CallEndReason.ConnectionFailed);
    }

    [Fact]
    public async Task RecoveryWorker_RecoversAbandonedActiveCall_ToEndedWithTimeout()
    {
        // Arrange
        var db = CreateInMemoryDbContext();
        var (stop, driverUserId, customerUserId) = await SeedDeliveryContext(db);

        var abandonedActive = new CallSession(
            stop.Id,
            customerUserId,
            CallParticipantType.Customer,
            driverUserId,
            CallParticipantType.Driver,
            Guid.NewGuid(),
            DateTime.UtcNow.AddMinutes(-300));
        abandonedActive.MarkRinging(DateTime.UtcNow.AddMinutes(-299));
        abandonedActive.Accept(DateTime.UtcNow.AddMinutes(-298));
        abandonedActive.MarkConnecting(DateTime.UtcNow.AddMinutes(-297));
        abandonedActive.MarkConnected(DateTime.UtcNow.AddMinutes(-250)); // 250m ago > MaximumCallDurationMinutes (240m)
        db.CallSessions.Add(abandonedActive);
        await db.SaveChangesAsync();

        var services = new ServiceCollection();
        services.AddSingleton<IAppDbContext>(db);
        var currentUser = Substitute.For<ICurrentUser>();
        currentUser.UserId.Returns((Guid?)null);
        var notifier = Substitute.For<ICallSignalingNotifier>();
        var handler = new EndCallCommandHandler(db, currentUser, notifier);

        var sender = Substitute.For<ISender>();
        sender.Send(Arg.Any<EndCallCommand>(), Arg.Any<CancellationToken>())
            .Returns(call => handler.Handle(call.Arg<EndCallCommand>(), CancellationToken.None));
        services.AddScoped<ISender>(_ => sender);

        var serviceProvider = services.BuildServiceProvider();
        var scopeFactory = serviceProvider.GetRequiredService<IServiceScopeFactory>();

        var options = Microsoft.Extensions.Options.Options.Create(new VoiceCallOptions
        {
            MaximumCallDurationMinutes = 240,
            MaxBatchSize = 10
        });

        var logger = Substitute.For<ILogger<VoiceCallRecoveryJob>>();
        var job = new VoiceCallRecoveryJob(scopeFactory, options, NullVoiceCallRecoveryCoordinator.Instance, logger);

        // Act
        var processed = await job.ProcessAbandonedActiveCallsAsync(db, sender, CancellationToken.None);

        // Assert
        processed.Should().Be(1);
        var reloaded = await db.CallSessions.FindAsync(abandonedActive.Id);
        reloaded.Should().NotBeNull();
        reloaded!.Status.Should().Be(CallStatus.Ended);
        reloaded.EndReason.Should().Be(CallEndReason.Timeout);
    }

    [Fact]
    public async Task MarkCallConnected_WhenAlreadyActive_IsIdempotentAndPreservesConnectedAtUtc()
    {
        // Arrange
        var db = CreateInMemoryDbContext();
        var (stop, driverUserId, customerUserId) = await SeedDeliveryContext(db);

        var originalConnectedAtUtc = DateTime.UtcNow.AddSeconds(-45);
        var session = new CallSession(
            stop.Id,
            customerUserId,
            CallParticipantType.Customer,
            driverUserId,
            CallParticipantType.Driver,
            Guid.NewGuid(),
            DateTime.UtcNow.AddMinutes(-2));
        session.MarkRinging(DateTime.UtcNow.AddMinutes(-2));
        session.Accept(DateTime.UtcNow.AddSeconds(-60));
        session.MarkConnecting(DateTime.UtcNow.AddSeconds(-50));
        session.MarkConnected(originalConnectedAtUtc);
        db.CallSessions.Add(session);
        await db.SaveChangesAsync();

        var currentUser = Substitute.For<ICurrentUser>();
        currentUser.UserId.Returns(customerUserId);
        var notifier = Substitute.For<ICallSignalingNotifier>();
        var handler = new MarkCallConnectedCommandHandler(db, currentUser, notifier);

        // Act: Repeated ReportConnected callback from mobile WebRTC client
        var result = await handler.Handle(new MarkCallConnectedCommand(session.Id), CancellationToken.None);

        // Assert: Must succeed idempotently and preserve original ConnectedAtUtc
        result.IsSuccess.Should().BeTrue();
        result.Value.Status.Should().Be("Active");
        result.Value.ConnectedAtUtc.Should().Be(originalConnectedAtUtc);

        var reloaded = await db.CallSessions.FindAsync(session.Id);
        reloaded!.ConnectedAtUtc.Should().Be(originalConnectedAtUtc);
        await notifier.DidNotReceive().NotifyCallConnectedAsync(
            Arg.Any<Guid>(), Arg.Any<Guid>(), Arg.Any<Guid>(), Arg.Any<DateTime>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task MarkCallConnecting_WhenAlreadyActive_DoesNotRegressToConnecting()
    {
        // Arrange
        var db = CreateInMemoryDbContext();
        var (stop, driverUserId, customerUserId) = await SeedDeliveryContext(db);

        var session = new CallSession(
            stop.Id,
            customerUserId,
            CallParticipantType.Customer,
            driverUserId,
            CallParticipantType.Driver,
            Guid.NewGuid(),
            DateTime.UtcNow.AddMinutes(-2));
        session.MarkRinging(DateTime.UtcNow.AddMinutes(-2));
        session.Accept(DateTime.UtcNow.AddSeconds(-60));
        session.MarkConnecting(DateTime.UtcNow.AddSeconds(-50));
        session.MarkConnected(DateTime.UtcNow.AddSeconds(-40));
        db.CallSessions.Add(session);
        await db.SaveChangesAsync();

        var currentUser = Substitute.For<ICurrentUser>();
        currentUser.UserId.Returns(customerUserId);
        var notifier = Substitute.For<ICallSignalingNotifier>();
        var handler = new MarkCallConnectingCommandHandler(db, currentUser, notifier);

        // Act: Stale or out-of-order ReportConnecting received during Active
        var result = await handler.Handle(new MarkCallConnectingCommand(session.Id), CancellationToken.None);

        // Assert: Succeeds safely without corrupting Active status
        result.IsSuccess.Should().BeTrue();
        result.Value.Status.Should().Be("Active");
        var reloaded = await db.CallSessions.FindAsync(session.Id);
        reloaded!.Status.Should().Be(CallStatus.Active);
    }

    [Fact]
    public async Task MultiDeviceAnswer_WhenDeviceAAnswers_DeviceBReconcilesIdempotently()
    {
        // Arrange
        var db = CreateInMemoryDbContext();
        var (stop, driverUserId, customerUserId) = await SeedDeliveryContext(db);

        var session = new CallSession(
            stop.Id,
            customerUserId,
            CallParticipantType.Customer,
            driverUserId,
            CallParticipantType.Driver,
            Guid.NewGuid(),
            DateTime.UtcNow);
        session.MarkRinging(DateTime.UtcNow);
        db.CallSessions.Add(session);
        await db.SaveChangesAsync();

        var currentUser = Substitute.For<ICurrentUser>();
        currentUser.UserId.Returns(driverUserId); // Receiver account
        var notifier = Substitute.For<ICallSignalingNotifier>();
        var handler = new AnswerCallCommandHandler(db, currentUser, notifier);

        // Act 1: Device A answers
        var resultDeviceA = await handler.Handle(new AnswerCallCommand(session.Id), CancellationToken.None);

        // Act 2: Device B of the same receiver user answers milliseconds later
        var resultDeviceB = await handler.Handle(new AnswerCallCommand(session.Id), CancellationToken.None);

        // Assert: Both succeed, call is Accepted, receiver group received notification
        resultDeviceA.IsSuccess.Should().BeTrue();
        resultDeviceB.IsSuccess.Should().BeTrue();
        resultDeviceB.Value.Status.Should().Be("Accepted");

        var reloaded = await db.CallSessions.FindAsync(session.Id);
        reloaded!.Status.Should().Be(CallStatus.Accepted);

        // Verify receiverUserId was passed so other devices stop ringing
        await notifier.Received(1).NotifyCallAnsweredAsync(
            session.Id,
            session.CallerUserId,
            Arg.Any<DateTime>(),
            driverUserId,
            Arg.Any<CancellationToken>());
    }

    [Fact]
    public void CallStatePolicy_CorrectlyClassifiesAllStatuses()
    {
        // Ongoing
        CallStatePolicy.IsOngoing(CallStatus.Created).Should().BeTrue();
        CallStatePolicy.IsOngoing(CallStatus.Ringing).Should().BeTrue();
        CallStatePolicy.IsOngoing(CallStatus.Accepted).Should().BeTrue();
        CallStatePolicy.IsOngoing(CallStatus.Connecting).Should().BeTrue();
        CallStatePolicy.IsOngoing(CallStatus.Active).Should().BeTrue();
        CallStatePolicy.IsOngoing(CallStatus.Ended).Should().BeFalse();
        CallStatePolicy.IsOngoing(CallStatus.Failed).Should().BeFalse();

        // Terminal
        CallStatePolicy.IsTerminal(CallStatus.Rejected).Should().BeTrue();
        CallStatePolicy.IsTerminal(CallStatus.Cancelled).Should().BeTrue();
        CallStatePolicy.IsTerminal(CallStatus.Missed).Should().BeTrue();
        CallStatePolicy.IsTerminal(CallStatus.Failed).Should().BeTrue();
        CallStatePolicy.IsTerminal(CallStatus.Ended).Should().BeTrue();
        CallStatePolicy.IsTerminal(CallStatus.Active).Should().BeFalse();

        // Signaling allowed (Accepted, Connecting, Active for ICE restart)
        CallStatePolicy.CanSignalWebRtc(CallStatus.Accepted).Should().BeTrue();
        CallStatePolicy.CanSignalWebRtc(CallStatus.Connecting).Should().BeTrue();
        CallStatePolicy.CanSignalWebRtc(CallStatus.Active).Should().BeTrue();
        CallStatePolicy.CanSignalWebRtc(CallStatus.Ringing).Should().BeFalse();
        CallStatePolicy.CanSignalWebRtc(CallStatus.Ended).Should().BeFalse();
    }

    [Theory]
    [InlineData("IceFailed", CallEndReason.ConnectionFailed)]
    [InlineData("PeerConnectionFailed", CallEndReason.ConnectionFailed)]
    [InlineData("MediaUnavailable", CallEndReason.ConnectionFailed)]
    [InlineData("PermissionDenied", CallEndReason.ConnectionFailed)]
    [InlineData("SignalingTimeout", CallEndReason.Timeout)]
    [InlineData("Unknown", CallEndReason.ConnectionFailed)]
    [InlineData(null, CallEndReason.ConnectionFailed)]
    [InlineData("ArbitraryMaliciousString", CallEndReason.ConnectionFailed)]
    public void ClientConnectionFailureReason_MapsSafelyToCallEndReason(string? clientString, CallEndReason expected)
    {
        var clientReason = ClientConnectionFailureReasonExtensions.FromString(clientString);
        var serverReason = clientReason.ToCallEndReason();
        serverReason.Should().Be(expected);
    }

    [Fact]
    public async Task GetCallHistoryForTripStop_ReturnsPagedResultWithDeterministicOrdering()
    {
        // Arrange
        var db = CreateInMemoryDbContext();
        var (stop, driverUserId, customerUserId) = await SeedDeliveryContext(db);

        var now = DateTime.UtcNow;
        var call1 = new CallSession(stop.Id, customerUserId, CallParticipantType.Customer, driverUserId, CallParticipantType.Driver, Guid.NewGuid(), now.AddMinutes(-30));
        var call2 = new CallSession(stop.Id, customerUserId, CallParticipantType.Customer, driverUserId, CallParticipantType.Driver, Guid.NewGuid(), now.AddMinutes(-20));
        var call3 = new CallSession(stop.Id, customerUserId, CallParticipantType.Customer, driverUserId, CallParticipantType.Driver, Guid.NewGuid(), now.AddMinutes(-10));

        db.CallSessions.AddRange(call1, call2, call3);
        await db.SaveChangesAsync();

        var currentUser = Substitute.For<ICurrentUser>();
        currentUser.UserId.Returns(customerUserId);
        var handler = new GetCallHistoryForTripStopQueryHandler(
            db,
            currentUser,
            new VoiceCallAuthorizationService(db));

        // Act: page 1 with page size 2
        var query = new GetCallHistoryForTripStopQuery(stop.Id, Page: 1, PageSize: 2);
        var result = await handler.Handle(query, CancellationToken.None);

        // Assert: PagedResult returns 2 items, totalCount 3, ordered newest first
        result.IsSuccess.Should().BeTrue();
        var paged = result.Value;
        paged.Items.Count.Should().Be(2);
        paged.TotalCount.Should().Be(3);
        paged.Page.Should().Be(1);
        paged.PageSize.Should().Be(2);
        paged.HasNextPage.Should().BeTrue();
        paged.HasPreviousPage.Should().BeFalse();
    }

    #endregion
}
