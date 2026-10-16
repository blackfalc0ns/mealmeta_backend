using System.Text.Json;
using MealMate.Application.Common.Abstractions;
using MealMate.Application.Features.DriverDelivery.CustomerTracking;
using MealMate.Domain.CalendarMeals;
using MealMate.Domain.Common;
using MealMate.Domain.DriverDelivery;
using MealMate.Domain.Enums;
using MealMate.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using NSubstitute;
using Xunit;

namespace MealMate.UnitTests.DriverDelivery;

public sealed class CustomerDeliveryChangeOutboxCaptureTests
{
    [Fact]
    public async Task SaveChanges_StagesOneInvalidationPerChangedDay_AndNoInvalidationForNoOpSave()
    {
        var now = new DateTime(2026, 10, 16, 12, 30, 0, DateTimeKind.Utc);
        await using var db = CreateContext(now);
        var day = NewDay();
        var trip = NewTrip();
        db.SubscriptionDays.Add(day);
        db.Trips.Add(trip);
        await db.SaveChangesAsync();
        Assert.Empty(await db.OutboxMessages.ToListAsync());

        day.Status = SubscriptionDayStatus.Preparing;
        db.TripStops.Add(NewPendingStop(day.Id, trip.Id));
        await db.SaveChangesAsync();
        await db.SaveChangesAsync();

        var first = await db.OutboxMessages.SingleAsync();
        Assert.Equal(CustomerDeliveryChangedV1.OutboxEventType, first.EventType);
        Assert.Equal(day.Id.ToString("D"), first.AggregateId);
        Assert.Equal(now, first.CreatedAtUtc);
        var firstPayload = JsonSerializer.Deserialize<CustomerDeliveryChangedV1>(first.PayloadJson, new JsonSerializerOptions(JsonSerializerDefaults.Web));
        Assert.NotNull(firstPayload);
        Assert.Equal(first.Id, firstPayload.EventId);
        Assert.Equal(day.Id, firstPayload.SubscriptionDayId);
        Assert.Equal(now, firstPayload.OccurredAtUtc);
        Assert.Equal(1, firstPayload.SchemaVersion);

        day.Status = SubscriptionDayStatus.OutForDelivery;
        await db.SaveChangesAsync();

        var messages = await db.OutboxMessages.OrderBy(message => message.CreatedAtUtc).ToListAsync();
        Assert.Equal(2, messages.Count);
        Assert.Equal(2, messages.Select(message => message.Id).Distinct().Count());
    }

    [Fact]
    public async Task SaveChanges_EmitsAgainForSecondAttemptWithSameStopStatus()
    {
        await using var db = CreateContext(DateTime.UtcNow);
        var day = NewDay();
        var firstTrip = NewTrip();
        db.SubscriptionDays.Add(day);
        db.Trips.Add(firstTrip);
        await db.SaveChangesAsync();

        var firstAttempt = NewPendingStop(day.Id, firstTrip.Id);
        db.TripStops.Add(firstAttempt);
        await db.SaveChangesAsync();
        firstAttempt.Status = TripStopStatus.Failed;
        await db.SaveChangesAsync();
        var secondTrip = NewTrip();
        db.Trips.Add(secondTrip);
        await db.SaveChangesAsync();
        db.TripStops.Add(NewPendingStop(day.Id, secondTrip.Id));
        await db.SaveChangesAsync();

        var messages = await db.OutboxMessages.ToListAsync();
        Assert.Equal(3, messages.Count);
        Assert.Equal(3, messages.Select(message => message.Id).Distinct().Count());
        Assert.All(messages, message => Assert.Equal(day.Id.ToString("D"), message.AggregateId));
    }

    [Fact]
    public async Task FailedSave_RemovesOnlyTheInvalidationStagedForThatAttempt()
    {
        var failure = new SaveFailureInterceptor();
        await using var db = CreateContext(DateTime.UtcNow, failure: failure);
        var day = NewDay();
        db.SubscriptionDays.Add(day);
        await db.SaveChangesAsync();

        failure.FailNextSave = true;
        day.Status = SubscriptionDayStatus.Preparing;
        await Assert.ThrowsAsync<InvalidOperationException>(() => db.SaveChangesAsync());
        Assert.Empty(db.OutboxMessages.Local);

        await db.SaveChangesAsync();

        Assert.Single(await db.OutboxMessages.ToListAsync());
    }

    [Fact]
    public async Task SaveChanges_CapturesReadinessTimestampWhenDayStatusDoesNotChange()
    {
        var databaseName = Guid.NewGuid().ToString("N");
        await using (var seed = CreateContext(DateTime.UtcNow, databaseName))
        {
            var day = NewDay(SubscriptionDayStatus.Preparing);
            seed.SubscriptionDays.Add(day);
            seed.RestaurantDailyOrders.Add(NewDailyOrder(day.Id));
            await seed.SaveChangesAsync();
            await ClearOutboxAsync(seed);
        }

        await using var db = CreateContext(DateTime.UtcNow, databaseName);
        var order = await db.RestaurantDailyOrders.SingleAsync();
        order.Status = OrderStatus.ReadyForPickup;
        order.ReadyForPickupAtUtc = DateTime.UtcNow;
        await db.SaveChangesAsync();

        Assert.Single(await db.OutboxMessages.ToListAsync());
        Assert.Equal(SubscriptionDayStatus.Preparing, (await db.SubscriptionDays.SingleAsync()).Status);
    }

    [Fact]
    public async Task SaveChanges_CapturesActiveAssignmentReplacementUsingPersistedUntrackedStops()
    {
        var databaseName = Guid.NewGuid().ToString("N");
        Guid tripId;
        Guid dayId;
        await using (var seed = CreateContext(DateTime.UtcNow, databaseName))
        {
            var day = NewDay();
            var trip = NewTrip();
            tripId = trip.Id;
            dayId = day.Id;
            seed.AddRange(day, trip, NewPendingStop(day.Id, trip.Id), NewAssignment(trip.Id));
            await seed.SaveChangesAsync();
            await ClearOutboxAsync(seed);
        }

        await using var db = CreateContext(DateTime.UtcNow, databaseName);
        var oldAssignment = await db.DriverAssignments.SingleAsync();
        oldAssignment.Status = "Revoked";
        db.DriverAssignments.Add(NewAssignment(tripId));
        Assert.Empty(db.ChangeTracker.Entries<TripStop>());
        await db.SaveChangesAsync();

        var message = await db.OutboxMessages.SingleAsync();
        Assert.Equal(dayId.ToString("D"), message.AggregateId);
    }

    [Fact]
    public async Task SaveChanges_CapturesCustomerProjectionEditsOnAnExistingStop()
    {
        var databaseName = Guid.NewGuid().ToString("N");
        Guid dayId;
        Guid stopId;
        await using (var seed = CreateContext(DateTime.UtcNow, databaseName))
        {
            var day = NewDay();
            var trip = NewTrip();
            var stop = NewPendingStop(day.Id, trip.Id);
            dayId = day.Id;
            stopId = stop.Id;
            seed.AddRange(day, trip, stop);
            await seed.SaveChangesAsync();
            await ClearOutboxAsync(seed);
        }

        await using var db = CreateContext(DateTime.UtcNow, databaseName);
        var trackedStop = await db.TripStops.SingleAsync(item => item.Id == stopId);
        trackedStop.SequenceNumber = 4;
        trackedStop.OrderCode = "updated-order-code";
        trackedStop.FullAddress = "Updated delivery address";
        trackedStop.Latitude = 29.3759m;
        trackedStop.Longitude = 47.9774m;
        await db.SaveChangesAsync();

        var message = await db.OutboxMessages.SingleAsync();
        Assert.Equal(dayId.ToString("D"), message.AggregateId);
    }

    [Fact]
    public async Task SaveChanges_CapturesHandoverPickupAndRevocationUsingDailyOrderDay()
    {
        var databaseName = Guid.NewGuid().ToString("N");
        Guid dayId;
        await using (var seed = CreateContext(DateTime.UtcNow, databaseName))
        {
            var day = NewDay();
            dayId = day.Id;
            var order = NewDailyOrder(day.Id);
            seed.AddRange(day, order);
            await seed.SaveChangesAsync();
            seed.OrderHandovers.Add(NewHandover(order.Id));
            await seed.SaveChangesAsync();
            await ClearOutboxAsync(seed);
        }

        await using var db = CreateContext(DateTime.UtcNow, databaseName);
        var handover = await db.OrderHandovers.SingleAsync();
        handover.HandedOverAtUtc = DateTime.UtcNow;
        await db.SaveChangesAsync();
        Assert.Single(await db.OutboxMessages.ToListAsync());
        await ClearOutboxAsync(db);

        handover.RevokedAtUtc = DateTime.UtcNow.AddMinutes(1);
        handover.Status = "Revoked";
        await db.SaveChangesAsync();
        var revoked = await db.OutboxMessages.SingleAsync();
        Assert.Equal(dayId.ToString("D"), revoked.AggregateId);
        Assert.Equal(nameof(SubscriptionDay), revoked.AggregateType);
    }

    [Fact]
    public async Task SaveChanges_CapturesArrivalTimestampWithoutStatusChange_AndIgnoresUnrelatedAuditChanges()
    {
        var databaseName = Guid.NewGuid().ToString("N");
        Guid dayId;
        Guid stopId;
        await using (var seed = CreateContext(DateTime.UtcNow, databaseName))
        {
            var seedDay = NewDay();
            var trip = NewTrip();
            var stop = NewPendingStop(seedDay.Id, trip.Id);
            dayId = seedDay.Id;
            stopId = stop.Id;
            seed.AddRange(seedDay, trip, stop);
            await seed.SaveChangesAsync();
            await ClearOutboxAsync(seed);
        }

        await using (var db = CreateContext(DateTime.UtcNow, databaseName))
        {
            var stop = await db.TripStops.SingleAsync(item => item.Id == stopId);
            stop.ArrivedAtUtc = DateTime.UtcNow;
            await db.SaveChangesAsync();
            Assert.Single(await db.OutboxMessages.ToListAsync());
            Assert.Equal(TripStopStatus.Pending, stop.Status);
            await ClearOutboxAsync(db);
        }

        await using var auditDb = CreateContext(DateTime.UtcNow, databaseName);
        var day = await auditDb.SubscriptionDays.SingleAsync(item => item.Id == dayId);
        day.ModifiedAtUtc = DateTime.UtcNow;
        day.SelectionDeadlineUtc = DateTime.UtcNow.AddDays(1);
        await auditDb.SaveChangesAsync();
        Assert.Empty(await auditDb.OutboxMessages.ToListAsync());
    }

    private static AppDbContext CreateContext(DateTime now, string? databaseName = null, SaveFailureInterceptor? failure = null)
    {
        var builder = new DbContextOptionsBuilder<AppDbContext>()
            .UseInMemoryDatabase(databaseName ?? Guid.NewGuid().ToString("N"));
        if (failure is not null) builder.AddInterceptors(failure);
        var options = builder.Options;
        return new AppDbContext(options, Substitute.For<IDomainEventDispatcher>(), new FixedDateTimeProvider(now));
    }

    private static SubscriptionDay NewDay(SubscriptionDayStatus status = SubscriptionDayStatus.Open)
        => new()
        {
            SubscriptionId = Guid.NewGuid(),
            ServiceDate = new DateOnly(2026, 10, 16),
            Status = status
        };

    private static TripStop NewPendingStop(Guid dayId, Guid? tripId = null)
        => new()
        {
            TripId = tripId ?? Guid.NewGuid(),
            SubscriptionDayId = dayId,
            OrderCode = "order-code",
            CustomerMaskedId = "customer-mask",
            ZoneNameAr = "المنطقة",
            ZoneNameEn = "Zone",
            MealSummaryAr = "وجبة",
            MealSummaryEn = "Meal",
            SlotLabel = "AM"
        };

    private static Trip NewTrip() => new()
    {
        RestaurantId = Guid.NewGuid(),
        Code = "trip-code",
        PlannedDate = new DateOnly(2026, 10, 16)
    };

    private static DriverAssignment NewAssignment(Guid tripId) => new()
    {
        TripId = tripId,
        DriverProfileId = Guid.NewGuid(),
        AssignedAtUtc = DateTime.UtcNow,
        Status = "Active"
    };

    private static MealMate.Domain.RestaurantOperations.RestaurantDailyOrder NewDailyOrder(Guid dayId) => new()
    {
        RestaurantId = Guid.NewGuid(),
        SubscriptionDayId = dayId,
        ServiceDate = new DateOnly(2026, 10, 16),
        Status = OrderStatus.Created,
        BoxCount = 1
    };

    private static MealMate.Domain.RestaurantOperations.OrderHandover NewHandover(Guid dailyOrderId) => new()
    {
        DailyOrderId = dailyOrderId,
        Status = "Active"
    };

    private static async Task ClearOutboxAsync(AppDbContext db)
    {
        db.OutboxMessages.RemoveRange(await db.OutboxMessages.ToListAsync());
        await db.SaveChangesAsync();
    }

    private sealed class FixedDateTimeProvider(DateTime now) : IDateTimeProvider
    {
        public DateTime UtcNow { get; } = now;
        public DateOnly TodayUtc { get; } = DateOnly.FromDateTime(now);
    }

    private sealed class SaveFailureInterceptor : SaveChangesInterceptor
    {
        public bool FailNextSave { get; set; }

        public override ValueTask<InterceptionResult<int>> SavingChangesAsync(
            DbContextEventData eventData,
            InterceptionResult<int> result,
            CancellationToken cancellationToken = default)
        {
            if (FailNextSave)
            {
                FailNextSave = false;
                throw new InvalidOperationException("Injected persistence failure.");
            }

            return ValueTask.FromResult(result);
        }
    }
}
