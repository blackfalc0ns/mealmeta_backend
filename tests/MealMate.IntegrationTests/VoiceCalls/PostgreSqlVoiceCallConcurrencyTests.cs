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
}
