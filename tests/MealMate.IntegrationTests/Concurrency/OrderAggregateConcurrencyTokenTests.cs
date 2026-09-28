using FluentAssertions;
using MealMate.Application.Common.Abstractions;
using MealMate.Domain.DriverDelivery;
using MealMate.Domain.Enums;
using MealMate.Domain.RestaurantOperations;
using MealMate.Persistence;
using Microsoft.EntityFrameworkCore;
using NSubstitute;
using Testcontainers.PostgreSql;
using Xunit;
using Xunit.Sdk;

namespace MealMate.IntegrationTests.Concurrency;

public sealed class OrderAggregateConcurrencyTokenTests : IAsyncLifetime
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
            throw new XunitException("Docker is required for OrderAggregateConcurrencyTokenTests.", ex);
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
    public async Task RestaurantDailyOrder_ConcurrentUpdate_ThrowsDbUpdateConcurrencyException()
    {
        var restaurantId = Guid.NewGuid();
        var subscriptionDayId = Guid.NewGuid();
        Guid orderId;

        await using (var db = CreateDb())
        {
            var order = new RestaurantDailyOrder
            {
                RestaurantId = restaurantId,
                SubscriptionDayId = subscriptionDayId,
                ServiceDate = new DateOnly(2026, 9, 29),
                Status = OrderStatus.Created,
                BoxCount = 1
            };
            db.RestaurantDailyOrders.Add(order);
            await db.SaveChangesAsync();
            orderId = order.Id;
        }

        await using var db1 = CreateDb();
        await using var db2 = CreateDb();

        var orderFromDb1 = await db1.RestaurantDailyOrders.SingleAsync(x => x.Id == orderId);
        var orderFromDb2 = await db2.RestaurantDailyOrders.SingleAsync(x => x.Id == orderId);

        orderFromDb1.Status = OrderStatus.AwaitingRestaurantConfirmation;
        await db1.SaveChangesAsync();

        orderFromDb2.Status = OrderStatus.Cancelled;
        var act = () => db2.SaveChangesAsync();

        await act.Should().ThrowAsync<DbUpdateConcurrencyException>(
            "Stale concurrent write on RestaurantDailyOrder must be rejected by PostgreSQL optimistic concurrency token.");
    }

    [Fact]
    public async Task Trip_ConcurrentUpdate_ThrowsDbUpdateConcurrencyException()
    {
        Guid tripId;

        await using (var db = CreateDb())
        {
            var trip = new Trip
            {
                RestaurantId = Guid.NewGuid(),
                Code = "TRP-CONC-01",
                PlannedDate = new DateOnly(2026, 9, 29),
                Status = TripStatus.Planned
            };
            db.Trips.Add(trip);
            await db.SaveChangesAsync();
            tripId = trip.Id;
        }

        await using var db1 = CreateDb();
        await using var db2 = CreateDb();

        var tripFromDb1 = await db1.Trips.SingleAsync(x => x.Id == tripId);
        var tripFromDb2 = await db2.Trips.SingleAsync(x => x.Id == tripId);

        tripFromDb1.Status = TripStatus.Assigned;
        await db1.SaveChangesAsync();

        tripFromDb2.Status = TripStatus.Cancelled;
        var act = () => db2.SaveChangesAsync();

        await act.Should().ThrowAsync<DbUpdateConcurrencyException>(
            "Stale concurrent write on Trip must be rejected by PostgreSQL optimistic concurrency token.");
    }
}
