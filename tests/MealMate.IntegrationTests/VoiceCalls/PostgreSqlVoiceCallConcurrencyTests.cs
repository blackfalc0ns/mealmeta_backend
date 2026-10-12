using FluentAssertions;
using MealMate.Application.Common.Abstractions;
using MealMate.Domain.CalendarMeals;
using MealMate.Domain.DriverDelivery;
using MealMate.Domain.Enums;
using MealMate.Domain.IdentityAccess;
using MealMate.Domain.ProgramsSubscriptions;
using MealMate.Domain.VoiceCalls;
using MealMate.Persistence;
using MealMate.Persistence.VoiceCalls;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using NSubstitute;
using Testcontainers.PostgreSql;
using Xunit;
using Xunit.Sdk;

namespace MealMate.IntegrationTests.VoiceCalls;

public sealed class PostgreSqlVoiceCallConcurrencyTests : IAsyncLifetime
{
    private PostgreSqlContainer _container = null!;

    public async Task InitializeAsync()
    {
        try
        {
            _container = new PostgreSqlBuilder()
                .WithImage("postgres:16-alpine")
                .Build();

            await _container.StartAsync();

            await using var db = CreateDb();
            await db.Database.EnsureCreatedAsync();
        }
        catch (Exception ex)
        {
            throw new XunitException("Docker/Postgres container failed to start for voice call concurrency tests.", ex);
        }
    }

    public async Task DisposeAsync()
    {
        if (_container != null)
        {
            await _container.DisposeAsync();
        }
    }

    private AppDbContext CreateDb()
    {
        var options = new DbContextOptionsBuilder<AppDbContext>()
            .UseNpgsql(_container.GetConnectionString())
            .ConfigureWarnings(w => w.Ignore(Microsoft.EntityFrameworkCore.Diagnostics.RelationalEventId.PendingModelChangesWarning))
            .Options;

        return new AppDbContext(options, Substitute.For<IDomainEventDispatcher>());
    }

    private static MealMate.Application.Features.CustomerPush.RegisterCustomerDeviceTokenCommandHandler
        CreateTokenHandler(AppDbContext db, Guid userId)
    {
        var currentUser = Substitute.For<MealMate.Application.Common.Abstractions.ICurrentUser>();
        currentUser.IsAuthenticated.Returns(true);
        currentUser.UserId.Returns(userId);
        var clock = Substitute.For<MealMate.Application.Common.Abstractions.IDateTimeProvider>();
        clock.UtcNow.Returns(DateTime.UtcNow);
        return new(db, currentUser, clock);
    }

    private static async Task<(TripStop stop, Guid driverUserId, Guid customerUserId)> SeedContextAsync(AppDbContext db)
    {
        var driverUserId = Guid.NewGuid();
        var customerUserId = Guid.NewGuid();

        var trip = new Trip
        {
            RestaurantId = Guid.NewGuid(),
            Code = $"TRIP-{Guid.NewGuid():N}"[..10],
            Status = TripStatus.InProgress,
            PlannedDate = DateOnly.FromDateTime(DateTime.UtcNow)
        };
        db.Trips.Add(trip);

        var stop = new TripStop
        {
            TripId = trip.Id,
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
        await db.SaveChangesAsync();

        return (stop, driverUserId, customerUserId);
    }

    [Fact]
    public async Task PostgreSql_UniqueConstraint_PreventsDuplicateClientRequestId()
    {
        // Arrange
        await using var db = CreateDb();
        var (stop, driverUserId, customerUserId) = await SeedContextAsync(db);

        var clientRequestId = Guid.NewGuid();
        var call1 = new CallSession(
            stop.Id, customerUserId, CallParticipantType.Customer,
            driverUserId, CallParticipantType.Driver,
            clientRequestId, DateTime.UtcNow);

        db.CallSessions.Add(call1);
        await db.SaveChangesAsync();

        // Act: Attempt to insert second call with identical CallerUserId and ClientRequestId
        await using var db2 = CreateDb();
        var call2 = new CallSession(
            stop.Id, customerUserId, CallParticipantType.Customer,
            driverUserId, CallParticipantType.Driver,
            clientRequestId, DateTime.UtcNow);

        db2.CallSessions.Add(call2);

        // Assert: Must throw DbUpdateException due to PostgreSQL unique index constraint
        var act = async () => await db2.SaveChangesAsync();
        await act.Should().ThrowAsync<DbUpdateException>();
    }

    [Fact]
    public async Task PostgreSql_XminConcurrency_ThrowsOnConcurrentModifications()
    {
        // Arrange
        await using var db1 = CreateDb();
        var (stop, driverUserId, customerUserId) = await SeedContextAsync(db1);

        var session = new CallSession(
            stop.Id, customerUserId, CallParticipantType.Customer,
            driverUserId, CallParticipantType.Driver,
            Guid.NewGuid(), DateTime.UtcNow);
        session.MarkRinging(DateTime.UtcNow);
        db1.CallSessions.Add(session);
        await db1.SaveChangesAsync();

        // Act: Load session in two separate contexts
        await using var db2 = CreateDb();
        var sessionInDb1 = await db1.CallSessions.FirstAsync(c => c.Id == session.Id);
        var sessionInDb2 = await db2.CallSessions.FirstAsync(c => c.Id == session.Id);

        // First context updates and commits
        sessionInDb1.Accept(DateTime.UtcNow);
        await db1.SaveChangesAsync();

        // Second context tries to cancel based on stale xmin
        sessionInDb2.Cancel(DateTime.UtcNow);

        // Assert: DbUpdateConcurrencyException occurs in PostgreSQL
        var act = async () => await db2.SaveChangesAsync();
        await act.Should().ThrowAsync<DbUpdateConcurrencyException>();
    }

    [Fact]
    public async Task PostgresVoiceCallRecoveryCoordinator_AdvisoryLock_AllowsOnlyOneConcurrentInstance()
    {
        // Arrange: Two independent DbContexts simulating Instance 1 and Instance 2
        await using var db1 = CreateDb();
        await using var db2 = CreateDb();

        var logger1 = Substitute.For<ILogger<PostgresVoiceCallRecoveryCoordinator>>();
        var logger2 = Substitute.For<ILogger<PostgresVoiceCallRecoveryCoordinator>>();

        var coordinator1 = new PostgresVoiceCallRecoveryCoordinator(db1, logger1);
        var coordinator2 = new PostgresVoiceCallRecoveryCoordinator(db2, logger2);

        var instance1Entered = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var barrier = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);

        var instance1Acquired = false;
        var instance2Acquired = false;

        // Act: Instance 1 starts cycle and holds lock while barrier is unreleased
        var task1 = Task.Run(async () =>
        {
            await coordinator1.TryExecuteRecoveryCycleAsync(async ct =>
            {
                instance1Acquired = true;
                instance1Entered.SetResult();
                await barrier.Task;
            }, CancellationToken.None);
        });

        // Wait until instance1 actually acquires lock and enters execution
        await instance1Entered.Task;

        // Instance 2 attempts recovery while Instance 1 holds the advisory transaction lock
        var instance2Result = await coordinator2.TryExecuteRecoveryCycleAsync(async ct =>
        {
            instance2Acquired = true;
            await Task.CompletedTask;
        }, CancellationToken.None);

        // Release barrier so Instance 1 completes
        barrier.SetResult();
        await task1;

        // Assert: Instance 1 executed, Instance 2 cleanly skipped execution without error
        instance1Acquired.Should().BeTrue();
        instance2Acquired.Should().BeFalse();
        instance2Result.Should().BeFalse();
    }

    [Fact]
    public async Task Concurrent_token_registrations_leave_an_active_owned_matching_route()
    {
        // Arrange: one live voice session bound to the account's current route.
        var user = Guid.NewGuid();
        var installation = "install-race-" + Guid.NewGuid().ToString("N")[..8];

        await using (var seedDb = CreateDb())
        {
            var seedToken = new CustomerDeviceToken
            {
                UserId = user,
                Token = "seed-fcm-" + Guid.NewGuid().ToString("N"),
                Platform = MobilePlatform.Android,
                IsActive = true,
                LastSeenAtUtc = DateTime.UtcNow
            };
            seedDb.CustomerDeviceTokens.Add(seedToken);
            seedDb.VoiceCallDeviceSessions.Add(new VoiceCallDeviceSession
            {
                UserId = user,
                InstallationId = installation,
                Platform = "android",
                FcmDeviceTokenId = seedToken.Id,
                ControlProofHash = new string('a', 64),
                ExpiresAtUtc = DateTime.UtcNow.AddDays(1)
            });
            await seedDb.SaveChangesAsync();
        }

        static MealMate.Application.Features.CustomerPush.RegisterCustomerDeviceTokenCommandHandler
            CreateHandler(AppDbContext db, Guid userId)
        {
            var currentUser = Substitute.For<MealMate.Application.Common.Abstractions.ICurrentUser>();
            currentUser.IsAuthenticated.Returns(true);
            currentUser.UserId.Returns(userId);
            var clock = Substitute.For<MealMate.Application.Common.Abstractions.IDateTimeProvider>();
            clock.UtcNow.Returns(DateTime.UtcNow);
            return new(db, currentUser, clock);
        }

        // Act: two installations-refresh registrations race on the same installation.
        // The commit that lands last owns the binding; the test asserts the final
        // committed route, never the capture order of the racing FCM tokens.
        var taskA = Task.Run(async () =>
        {
            await using var db = CreateDb();
            return await CreateHandler(db, user).Handle(
                new MealMate.Application.Features.CustomerPush.RegisterCustomerDeviceTokenCommand(
                    "race-token-a", MobilePlatform.Android, installation), CancellationToken.None);
        });
        var taskB = Task.Run(async () =>
        {
            await using var db = CreateDb();
            return await CreateHandler(db, user).Handle(
                new MealMate.Application.Features.CustomerPush.RegisterCustomerDeviceTokenCommand(
                    "race-token-b", MobilePlatform.Android, installation), CancellationToken.None);
        });
        await Task.WhenAll(taskA, taskB);

        // Assert: both registrations succeeded, and the surviving binding is active,
        // owned by the caller and matched to the installation.
        taskA.Result.IsSuccess.Should().BeTrue();
        taskB.Result.IsSuccess.Should().BeTrue();

        await using var verify = CreateDb();
        var session = await verify.VoiceCallDeviceSessions.SingleAsync(s => s.UserId == user && s.InstallationId == installation);
        session.RevokedAtUtc.Should().BeNull();
        session.FcmDeviceTokenId.Should().NotBeNull();
        var route = await verify.CustomerDeviceTokens.SingleAsync(t => t.Id == session.FcmDeviceTokenId);
        route.UserId.Should().Be(user);
        route.IsActive.Should().BeTrue();
    }

    [Fact]
    public async Task Stale_interleaved_rebind_commit_is_rejected_and_recovery_lands_an_active_route()
    {
        // The harmful interleaving: writer A re-registers the existing token, reads it
        // active and prepares the rebind; writer B completes a fresh registration for
        // the same installation (deactivating A's row and rebinding the session); then
        // A resumes. Without the xmin concurrency token A's stale commit lands
        // silently and the session ends up bound to an inactive route.
        var user = Guid.NewGuid();
        var installation = "install-r1-" + Guid.NewGuid().ToString("N")[..8];

        Guid sessionId;
        string tokenAText;
        await using (var seedDb = CreateDb())
        {
            var tokenA = new CustomerDeviceToken
            {
                UserId = user,
                Token = "token-a-" + Guid.NewGuid().ToString("N"),
                Platform = MobilePlatform.Android,
                DeviceId = installation,
                IsActive = true,
                LastSeenAtUtc = DateTime.UtcNow
            };
            var older = new CustomerDeviceToken
            {
                UserId = user,
                Token = "older-fcm-" + Guid.NewGuid().ToString("N"),
                Platform = MobilePlatform.Android,
                DeviceId = installation,
                IsActive = true,
                LastSeenAtUtc = DateTime.UtcNow
            };
            seedDb.CustomerDeviceTokens.AddRange(tokenA, older);
            var session = new VoiceCallDeviceSession
            {
                UserId = user,
                InstallationId = installation,
                Platform = "android",
                FcmDeviceTokenId = older.Id,
                ControlProofHash = new string('a', 64),
                ExpiresAtUtc = DateTime.UtcNow.AddDays(1)
            };
            seedDb.VoiceCallDeviceSessions.Add(session);
            await seedDb.SaveChangesAsync();
            sessionId = session.Id;
            tokenAText = tokenA.Token;
        }

        // Writer A: the read/prepare phase of re-registering the existing token.
        await using (var writerA = CreateDb())
        {
            var trackedA = await writerA.CustomerDeviceTokens.FirstAsync(t => t.Token == tokenAText);
            var session = await writerA.VoiceCallDeviceSessions.FirstAsync(s => s.Id == sessionId);
            trackedA.IsActive = true; // read as active: reactivating is a no-op locally
            trackedA.LastSeenAtUtc = DateTime.UtcNow;
            trackedA.ModifiedAtUtc = DateTime.UtcNow;
            session.FcmDeviceTokenId = trackedA.Id;

            // Writer B completes a full registration for the same installation.
            await using (var writerB = CreateDb())
            {
                var registered = await CreateTokenHandler(writerB, user).Handle(
                    new MealMate.Application.Features.CustomerPush.RegisterCustomerDeviceTokenCommand(
                        "token-b-" + Guid.NewGuid().ToString("N"), MobilePlatform.Android, installation),
                    CancellationToken.None);
                registered.IsSuccess.Should().BeTrue();
            }

            // Writer A resumes: the xmin token must reject the stale commit instead of
            // silently pointing the session at the now-inactive route.
            var staleCommit = async () => await writerA.SaveChangesAsync();
            await staleCommit.Should().ThrowAsync<DbUpdateConcurrencyException>();
        }

        // The real handler's retry path re-reads and lands a live, owned, matching route.
        await using (var recovery = CreateDb())
        {
            var retried = await CreateTokenHandler(recovery, user).Handle(
                new MealMate.Application.Features.CustomerPush.RegisterCustomerDeviceTokenCommand(
                    tokenAText, MobilePlatform.Android, installation), CancellationToken.None);
            retried.IsSuccess.Should().BeTrue();

            var settled = await recovery.VoiceCallDeviceSessions.FirstAsync(s => s.Id == sessionId);
            settled.FcmDeviceTokenId.Should().NotBeNull();
            var route = await recovery.CustomerDeviceTokens.SingleAsync(t => t.Id == settled.FcmDeviceTokenId);
            route.UserId.Should().Be(user);
            route.IsActive.Should().BeTrue();
        }
    }
}
