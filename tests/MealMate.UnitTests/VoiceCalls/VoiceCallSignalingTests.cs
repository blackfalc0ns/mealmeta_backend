using System.Security.Cryptography;
using System.Text;
using FluentAssertions;
using MealMate.Application.Common.Abstractions;
using MealMate.Application.Features.VoiceCalls.Abstractions;
using MealMate.Application.Features.VoiceCalls.Commands;
using MealMate.Application.Features.VoiceCalls.Dtos;
using MealMate.Application.Features.VoiceCalls.Queries;
using MealMate.Domain.CalendarMeals;
using MealMate.Domain.DriverDelivery;
using MealMate.Domain.Enums;
using MealMate.Domain.IdentityAccess;
using MealMate.Domain.ProgramsSubscriptions;
using MealMate.Domain.VoiceCalls;
using MealMate.Infrastructure.VoiceCalls;
using MealMate.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using NSubstitute;
using Xunit;

namespace MealMate.UnitTests.VoiceCalls;

public sealed class VoiceCallSignalingTests
{
    private static AppDbContext CreateInMemoryDb()
    {
        var options = new DbContextOptionsBuilder<AppDbContext>()
            .UseInMemoryDatabase(databaseName: $"MealMate_VoiceSignaling_Tests_{Guid.NewGuid():N}")
            .Options;

        var dispatcher = Substitute.For<IDomainEventDispatcher>();
        return new AppDbContext(options, dispatcher);
    }

    // =========================================================================
    // 1. COTURN CREDENTIAL SERVICE TESTS
    // =========================================================================

    
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
    public void TurnCredentialService_GeneratesValidCredentials_WithExpectedHmacSha1AndExpiry()
    {
        // Arrange
        const string secret = "my_super_secret_key_123";
        const string host = "turn.example.com";
        var options = Options.Create(new CoturnOptions
        {
            Enabled = true,
            ServerHost = host,
            SharedSecret = secret,
            TtlSeconds = 3600,
            EnableTls = true,
            StunPort = 3478,
            TurnPort = 3478,
            TurnsPort = 5349
        });

        var service = new TurnCredentialService(options);
        var userId = Guid.NewGuid().ToString();

        // Act
        var result = service.GenerateCredentials(userId);

        // Assert
        result.Should().NotBeNull();
        result.Username.Should().EndWith($":{userId}");
        result.TtlSeconds.Should().Be(3600);
        result.ExpiresAtUtc.Should().BeAfter(DateTime.UtcNow.AddMinutes(50));

        // Verify HMAC-SHA1 calculation
        var secretBytes = Encoding.UTF8.GetBytes(secret);
        var usernameBytes = Encoding.UTF8.GetBytes(result.Username);
        using var hmac = new HMACSHA1(secretBytes);
        var expectedHash = Convert.ToBase64String(hmac.ComputeHash(usernameBytes));

        result.Password.Should().Be(expectedHash);

        // Shared secret must NEVER appear anywhere in the result
        result.Password.Should().NotContain(secret);
        result.Username.Should().NotContain(secret);

        // Verify ICE server URLs
        result.IceServers.Should().Contain(s => s.Urls.Any(u => u.StartsWith($"stun:{host}:3478")));
        result.IceServers.Should().Contain(s => s.Urls.Any(u => u.StartsWith($"turn:{host}:3478?transport=udp")));
        result.IceServers.Should().Contain(s => s.Urls.Any(u => u.StartsWith($"turn:{host}:3478?transport=tcp")));
        result.IceServers.Should().Contain(s => s.Urls.Any(u => u.StartsWith($"turns:{host}:5349?transport=tcp")));
    }

    [Fact]
    public void TurnCredentialService_WhenTlsDisabled_OmitsTurnsServer()
    {
        // Arrange
        var options = Options.Create(new CoturnOptions
        {
            Enabled = true,
            EnableTls = false,
            ServerHost = "turn.example.com"
        });
        var service = new TurnCredentialService(options);

        // Act
        var result = service.GenerateCredentials("user-1");

        // Assert
        result.IceServers.Should().NotContain(s => s.Urls.Any(u => u.StartsWith("turns:")));
    }

    [Fact]
    public void TurnCredentialService_WhenDisabled_OmitsTurnServersAndKeepsStun()
    {
        // Arrange
        var options = Options.Create(new CoturnOptions
        {
            Enabled = false,
            ServerHost = "turn.example.com"
        });
        var service = new TurnCredentialService(options);

        // Act
        var result = service.GenerateCredentials("user-1");

        // Assert
        result.IceServers.Should().Contain(s => s.Urls.Any(u => u.StartsWith("stun:")));
        result.IceServers.Should().NotContain(s => s.Urls.Any(u => u.StartsWith("turn:")));
        result.IceServers.Should().NotContain(s => s.Urls.Any(u => u.StartsWith("turns:")));
    }

    [Fact]
    public void TurnCredentialService_DifferentUsers_ProduceDifferentCredentials()
    {
        // Arrange
        var options = Options.Create(new CoturnOptions());
        var service = new TurnCredentialService(options);

        // Act
        var creds1 = service.GenerateCredentials("user-A");
        var creds2 = service.GenerateCredentials("user-B");

        // Assert
        creds1.Username.Should().NotBe(creds2.Username);
        creds1.Password.Should().NotBe(creds2.Password);
    }

    [Fact]
    public async Task GetTurnCredentialsQuery_AuthenticatedUser_ReturnsCredentials()
    {
        // Arrange
        var options = Options.Create(new CoturnOptions());
        var service = new TurnCredentialService(options);
        var currentUser = Substitute.For<ICurrentUser>();
        var userId = Guid.NewGuid();
        currentUser.UserId.Returns(userId);

        var handler = new GetTurnCredentialsQueryHandler(service, currentUser);

        // Act
        var result = await handler.Handle(new GetTurnCredentialsQuery(), CancellationToken.None);

        // Assert
        result.IsSuccess.Should().BeTrue();
        result.Value.Username.Should().EndWith($":{userId}");
    }

    // =========================================================================
    // 2. CQRS LIFECYCLE NOTIFIER HOOK TESTS
    // =========================================================================

    private static async Task<(TripStop stop, Guid driverUserId, Guid customerUserId)> SeedDeliveryContext(AppDbContext db)
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

    [Fact]
    public async Task InitiateCallCommand_DispatchesIncomingAndRingingNotifications()
    {
        // Arrange
        await using var db = CreateInMemoryDb();
        var (stop, driverUserId, customerUserId) = await SeedDeliveryContext(db);

        var currentUser = Substitute.For<ICurrentUser>();
        currentUser.UserId.Returns(customerUserId);
        var notifier = Substitute.For<ICallSignalingNotifier>();

        var handler = CreateInitiateHandler(db, currentUser, notifier);

        // Act
        var result = await handler.Handle(new InitiateCallCommand(stop.Id, Guid.NewGuid()), CancellationToken.None);

        // Assert
        result.IsSuccess.Should().BeTrue();
        await notifier.Received(1).NotifyIncomingCallAsync(
            result.Value.CallId,
            driverUserId,
            customerUserId,
            "Customer",
            stop.Id,
            Arg.Any<DateTime>(),
            Arg.Any<DateTime?>(),
            Arg.Any<CancellationToken>());

        await notifier.Received(1).NotifyCallRingingAsync(
            result.Value.CallId,
            customerUserId,
            Arg.Any<DateTime>(),
            Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task AnswerCallCommand_DispatchesAnsweredNotification()
    {
        // Arrange
        await using var db = CreateInMemoryDb();
        var callerId = Guid.NewGuid();
        var receiverId = Guid.NewGuid();

        var session = new CallSession(
            Guid.NewGuid(), callerId, CallParticipantType.Customer,
            receiverId, CallParticipantType.Driver,
            Guid.NewGuid(), DateTime.UtcNow);
        session.MarkRinging(DateTime.UtcNow);
        db.CallSessions.Add(session);
        await db.SaveChangesAsync();

        var currentUser = Substitute.For<ICurrentUser>();
        currentUser.UserId.Returns(receiverId);
        var notifier = Substitute.For<ICallSignalingNotifier>();

        var handler = new AnswerCallCommandHandler(db, currentUser, notifier);

        // Act
        var result = await handler.Handle(new AnswerCallCommand(session.Id), CancellationToken.None);

        // Assert
        result.IsSuccess.Should().BeTrue();
        await notifier.Received(1).NotifyCallAnsweredAsync(
            session.Id,
            callerId,
            Arg.Any<DateTime>(),
            Arg.Any<Guid?>(),
            Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task RejectCallCommand_DispatchesRejectedNotification()
    {
        // Arrange
        await using var db = CreateInMemoryDb();
        var callerId = Guid.NewGuid();
        var receiverId = Guid.NewGuid();

        var session = new CallSession(
            Guid.NewGuid(), callerId, CallParticipantType.Customer,
            receiverId, CallParticipantType.Driver,
            Guid.NewGuid(), DateTime.UtcNow);
        session.MarkRinging(DateTime.UtcNow);
        db.CallSessions.Add(session);
        await db.SaveChangesAsync();

        var currentUser = Substitute.For<ICurrentUser>();
        currentUser.UserId.Returns(receiverId);
        var notifier = Substitute.For<ICallSignalingNotifier>();

        var handler = new RejectCallCommandHandler(db, currentUser, notifier);

        // Act
        var result = await handler.Handle(new RejectCallCommand(session.Id, "Busy driving"), CancellationToken.None);

        // Assert
        result.IsSuccess.Should().BeTrue();
        await notifier.Received(1).NotifyCallRejectedAsync(
            session.Id,
            callerId,
            "Busy driving",
            Arg.Any<DateTime>(),
            Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task CancelCallCommand_DispatchesCancelledNotification()
    {
        // Arrange
        await using var db = CreateInMemoryDb();
        var callerId = Guid.NewGuid();
        var receiverId = Guid.NewGuid();

        var session = new CallSession(
            Guid.NewGuid(), callerId, CallParticipantType.Customer,
            receiverId, CallParticipantType.Driver,
            Guid.NewGuid(), DateTime.UtcNow);
        session.MarkRinging(DateTime.UtcNow);
        db.CallSessions.Add(session);
        await db.SaveChangesAsync();

        var currentUser = Substitute.For<ICurrentUser>();
        currentUser.UserId.Returns(callerId);
        var notifier = Substitute.For<ICallSignalingNotifier>();

        var handler = new CancelCallCommandHandler(db, currentUser, notifier);

        // Act
        var result = await handler.Handle(new CancelCallCommand(session.Id, "Nevermind"), CancellationToken.None);

        // Assert
        result.IsSuccess.Should().BeTrue();
        await notifier.Received(1).NotifyCallCancelledAsync(
            session.Id,
            receiverId,
            "Nevermind",
            Arg.Any<DateTime>(),
            Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task EndCallCommand_DispatchesEndedNotification()
    {
        // Arrange
        await using var db = CreateInMemoryDb();
        var callerId = Guid.NewGuid();
        var receiverId = Guid.NewGuid();

        var session = new CallSession(
            Guid.NewGuid(), callerId, CallParticipantType.Customer,
            receiverId, CallParticipantType.Driver,
            Guid.NewGuid(), DateTime.UtcNow);
        session.MarkRinging(DateTime.UtcNow);
        session.Accept(DateTime.UtcNow);
        session.MarkConnecting();
        session.MarkConnected(DateTime.UtcNow);
        db.CallSessions.Add(session);
        await db.SaveChangesAsync();

        var currentUser = Substitute.For<ICurrentUser>();
        currentUser.UserId.Returns(callerId);
        var notifier = Substitute.For<ICallSignalingNotifier>();

        var handler = new EndCallCommandHandler(db, currentUser, notifier);

        // Act
        var result = await handler.Handle(new EndCallCommand(session.Id), CancellationToken.None);

        // Assert
        result.IsSuccess.Should().BeTrue();
        await notifier.Received(1).NotifyCallEndedAsync(
            session.Id,
            receiverId,
            Arg.Any<string>(),
            Arg.Any<int?>(),
            Arg.Any<DateTime>(),
            Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task CommandHandlers_WhenNotificationFails_DoesNotRollbackOrThrow()
    {
        // Arrange
        await using var db = CreateInMemoryDb();
        var callerId = Guid.NewGuid();
        var receiverId = Guid.NewGuid();

        var session = new CallSession(
            Guid.NewGuid(), callerId, CallParticipantType.Customer,
            receiverId, CallParticipantType.Driver,
            Guid.NewGuid(), DateTime.UtcNow);
        session.MarkRinging(DateTime.UtcNow);
        db.CallSessions.Add(session);
        await db.SaveChangesAsync();

        var currentUser = Substitute.For<ICurrentUser>();
        currentUser.UserId.Returns(receiverId);

        // Notifier throws exception (simulating transient network error on SignalR)
        var faultyNotifier = Substitute.For<ICallSignalingNotifier>();
        faultyNotifier.NotifyCallAnsweredAsync(Arg.Any<Guid>(), Arg.Any<Guid>(), Arg.Any<DateTime>(), Arg.Any<Guid?>(), Arg.Any<CancellationToken>())
            .Returns(Task.FromException(new InvalidOperationException("SignalR cluster unreachable")));

        var handler = new AnswerCallCommandHandler(db, currentUser, faultyNotifier);

        // Act: Should succeed regardless!
        var result = await handler.Handle(new AnswerCallCommand(session.Id), CancellationToken.None);

        // Assert
        result.IsSuccess.Should().BeTrue();
        result.Value.Status.Should().Be("Accepted");

        var reloaded = await db.CallSessions.FindAsync(session.Id);
        reloaded!.Status.Should().Be(CallStatus.Accepted);
    }
}
