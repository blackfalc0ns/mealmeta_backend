using MealMate.Application.Common.Abstractions;
using MealMate.Domain.Enums;
using MealMate.Domain.RestaurantOperations;
using MealMate.Persistence;
using MealMate.Persistence.CustomerCheckout;
using Microsoft.EntityFrameworkCore;
using NSubstitute;
using Testcontainers.PostgreSql;
using Xunit.Sdk;

namespace MealMate.IntegrationTests.CustomerCheckout;

public sealed class CapacityReservationConcurrencyPostgresTests : IAsyncLifetime
{
    private PostgreSqlContainer _container = null!;

    public async Task InitializeAsync()
    {
        try
        {
            _container = new PostgreSqlBuilder().WithImage("postgres:16-alpine").Build();
            await _container.StartAsync();
            await using var db = CreateDb();
            await db.Database.MigrateAsync();
        }
        catch (Exception ex)
        {
            throw new XunitException("Docker is required for checkout concurrency integration tests.", ex);
        }
    }

    public async Task DisposeAsync() => await _container.DisposeAsync();

    [Fact]
    public async Task Two_checkouts_for_last_capacity_allow_exactly_one_hold()
    {
        var restaurantId = Guid.NewGuid();
        var serviceDate = new DateOnly(2026, 8, 20);
        var menuVersionId = Guid.NewGuid();
        var start = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);

        async Task<bool> ReserveAsync()
        {
            await using var db = CreateDb();
            var transaction = new PostgresCheckoutTransactionExecutor(db);
            var capacityLock = new PostgresCapacityConcurrencyLock(db);
            await start.Task;
            return await transaction.ExecuteAsync(async ct =>
            {
                await capacityLock.AcquireAsync(restaurantId, [serviceDate], ct);
                var reserved = await db.CapacityHolds.CountAsync(x => x.RestaurantId == restaurantId && x.ServiceDate == serviceDate
                    && ((x.Status == CapacityHoldStatus.PendingPayment && x.ExpiresAtUtc > DateTime.UtcNow)
                        || x.Status == CapacityHoldStatus.PaymentConfirmed), ct);
                if (reserved >= 1) return false;
                db.CapacityHolds.Add(new CapacityHold
                {
                    RestaurantId = restaurantId,
                    SubscriptionId = Guid.NewGuid(),
                    MenuPlanVersionId = menuVersionId,
                    ServiceDate = serviceDate,
                    Quantity = 1,
                    Status = CapacityHoldStatus.PendingPayment,
                    ExpiresAtUtc = DateTime.UtcNow.AddMinutes(15)
                });
                await db.SaveChangesAsync(ct);
                return true;
            });
        }

        var first = ReserveAsync();
        var second = ReserveAsync();
        start.SetResult();
        var results = await Task.WhenAll(first, second);

        Assert.Single(results, x => x);
        await using var verificationDb = CreateDb();
        Assert.Equal(1, await verificationDb.CapacityHolds.CountAsync(x => x.RestaurantId == restaurantId && x.ServiceDate == serviceDate));
    }

    private AppDbContext CreateDb()
        => new(new DbContextOptionsBuilder<AppDbContext>().UseNpgsql(_container.GetConnectionString()).Options, Substitute.For<IDomainEventDispatcher>());
}
