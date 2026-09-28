using FluentAssertions;
using MealMate.Application.Common.Abstractions;
using MealMate.Domain.Common;
using MealMate.Domain.DriverDelivery;
using MealMate.Domain.Enums;
using MealMate.Persistence;
using MealMate.Persistence.Common;
using Microsoft.EntityFrameworkCore;
using NSubstitute;
using Testcontainers.PostgreSql;
using Xunit;
using Xunit.Sdk;

namespace MealMate.IntegrationTests.Infrastructure;

public sealed class PostgresApplicationLockManagerTests : IAsyncLifetime
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
            await db.Database.MigrateAsync();
        }
        catch (Exception ex)
        {
            throw new XunitException("Docker is required for PostgresApplicationLockManagerTests.", ex);
        }
    }

    public async Task DisposeAsync() => await _container.DisposeAsync();

    private AppDbContext CreateDb()
    {
        var options = new DbContextOptionsBuilder<AppDbContext>()
            .UseNpgsql(_container.GetConnectionString())
            .Options;
        return new AppDbContext(options, Substitute.For<IDomainEventDispatcher>());
    }

    [Fact]
    public async Task LockPickupBoxAsync_BlocksConcurrentModification_UntilTransactionCompletes()
    {
        var subscriptionDayId = Guid.NewGuid();
        Guid tripId;

        await using (var seedDb = CreateDb())
        {
            var trip = new Trip
            {
                RestaurantId = Guid.NewGuid(),
                PlannedDate = DateOnly.FromDateTime(DateTime.UtcNow),
                Code = "TRIP-TEST-001",
                Status = TripStatus.Planned
            };
            tripId = trip.Id;
            seedDb.Trips.Add(trip);

            var stop = new TripStop
            {
                TripId = tripId,
                SubscriptionDayId = subscriptionDayId,
                OrderCode = "BX-TEST-001",
                CustomerMaskedId = "CUST-001",
                ZoneNameAr = "حولي",
                ZoneNameEn = "Hawally",
                MealSummaryAr = "وجبة",
                MealSummaryEn = "Meal",
                SlotLabel = "12:00 PM",
                Status = TripStopStatus.Pending
            };
            seedDb.TripStops.Add(stop);
            await seedDb.SaveChangesAsync();
        }

        await using var db1 = CreateDb();
        await using var db2 = CreateDb();

        var lockManager1 = new PostgresApplicationLockManager(db1);
        var lockManager2 = new PostgresApplicationLockManager(db2);

        var tx1Started = new TaskCompletionSource<bool>();
        var tx2AttemptingLock = new TaskCompletionSource<bool>();
        var tx1CanCommit = new TaskCompletionSource<bool>();
        var tx2Completed = false;

        // Tx1 locks the pickup box
        var tx1Task = Task.Run(async () =>
        {
            await using var tx1 = await db1.Database.BeginTransactionAsync();
            await lockManager1.LockPickupBoxAsync(subscriptionDayId);
            tx1Started.SetResult(true);

            // Wait for signal before committing
            await tx1CanCommit.Task;
            await tx1.CommitAsync();
        });

        await tx1Started.Task;

        // Tx2 tries to lock the same pickup box - should be blocked while Tx1 is open
        var tx2Task = Task.Run(async () =>
        {
            tx2AttemptingLock.SetResult(true);
            await using var tx2 = await db2.Database.BeginTransactionAsync();
            await lockManager2.LockPickupBoxAsync(subscriptionDayId);
            var stopInTx2 = await db2.TripStops.SingleAsync(s => s.SubscriptionDayId == subscriptionDayId);
            stopInTx2.Status = TripStopStatus.PickedUp;
            await db2.SaveChangesAsync();
            await tx2.CommitAsync();
            tx2Completed = true;
        });

        await tx2AttemptingLock.Task;

        // Short wait to ensure tx2 had a chance to block
        await Task.Delay(300);
        tx2Completed.Should().BeFalse("Tx2 must be blocked by Tx1's lock on the pickup box");

        // Allow Tx1 to commit
        tx1CanCommit.SetResult(true);
        await tx1Task;
        await tx2Task;

        tx2Completed.Should().BeTrue("Tx2 should complete after Tx1 commits");

        await using var verifyDb = CreateDb();
        var reloadedStop = await verifyDb.TripStops.SingleAsync(s => s.SubscriptionDayId == subscriptionDayId);
        reloadedStop.Status.Should().Be(TripStopStatus.PickedUp);
    }
}
