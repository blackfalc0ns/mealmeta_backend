using MealMate.Application.Common.Abstractions;
using MealMate.Application.Features.CustomerCheckout;
using MealMate.Domain.Enums;
using MealMate.Domain.ProgramsSubscriptions;
using MealMate.Domain.RestaurantOperations;
using MealMate.Persistence;
using Microsoft.EntityFrameworkCore;
using NSubstitute;

namespace MealMate.UnitTests.CustomerCheckout;

public sealed class CapacityHoldExpiryServiceTests
{
    [Fact]
    public async Task ExpireDueAsync_cancels_pending_payment_when_hold_window_ends()
    {
        var now = new DateTime(2026, 9, 7, 16, 0, 0, DateTimeKind.Utc);
        var (db, clock, service, subscriptionId, holdId) = await SeedPendingAsync(now, holdExpiresAt: now.AddMinutes(-1));

        var result = await service.ExpireDueAsync();

        Assert.Equal(1, result.SubscriptionsExpired);
        Assert.True(result.HoldsExpired >= 1);
        var subscription = await db.Subscriptions.SingleAsync(x => x.Id == subscriptionId);
        Assert.Equal(SubscriptionStatus.Cancelled, subscription.Status);
        var hold = await db.CapacityHolds.SingleAsync(x => x.Id == holdId);
        Assert.Equal(CapacityHoldStatus.Released, hold.Status);
        Assert.Equal(now, hold.ReleasedAtUtc);
    }

    [Fact]
    public async Task ExpireDueAsync_keeps_pending_payment_while_hold_is_active()
    {
        var now = new DateTime(2026, 9, 7, 16, 0, 0, DateTimeKind.Utc);
        var (db, _, service, subscriptionId, holdId) = await SeedPendingAsync(now, holdExpiresAt: now.AddMinutes(10));

        var result = await service.ExpireDueAsync();

        Assert.Equal(0, result.SubscriptionsExpired);
        Assert.Equal(0, result.HoldsExpired);
        var subscription = await db.Subscriptions.SingleAsync(x => x.Id == subscriptionId);
        Assert.Equal(SubscriptionStatus.PendingPayment, subscription.Status);
        var hold = await db.CapacityHolds.SingleAsync(x => x.Id == holdId);
        Assert.Equal(CapacityHoldStatus.PendingPayment, hold.Status);
    }

    [Fact]
    public async Task ExpireDueAsync_cancels_payment_failed_after_fifteen_minutes()
    {
        var now = new DateTime(2026, 9, 7, 16, 0, 0, DateTimeKind.Utc);
        var db = CreateDb();
        var clock = Substitute.For<IDateTimeProvider>();
        clock.UtcNow.Returns(now);
        var subscription = await AddSubscriptionAsync(db, SubscriptionStatus.PaymentFailed, now.AddMinutes(-16));
        var service = CreateService(db, clock);

        var result = await service.ExpireDueAsync();

        Assert.Equal(1, result.SubscriptionsExpired);
        var stored = await db.Subscriptions.SingleAsync(x => x.Id == subscription.Id);
        Assert.Equal(SubscriptionStatus.Cancelled, stored.Status);
    }

    [Fact]
    public async Task ExpireDueAsync_does_not_cancel_active_subscription()
    {
        var now = new DateTime(2026, 9, 7, 16, 0, 0, DateTimeKind.Utc);
        var db = CreateDb();
        var clock = Substitute.For<IDateTimeProvider>();
        clock.UtcNow.Returns(now);
        var subscription = await AddSubscriptionAsync(db, SubscriptionStatus.Active, now.AddDays(-20));
        var service = CreateService(db, clock);

        var result = await service.ExpireDueAsync();

        Assert.Equal(0, result.SubscriptionsExpired);
        var stored = await db.Subscriptions.SingleAsync(x => x.Id == subscription.Id);
        Assert.Equal(SubscriptionStatus.Active, stored.Status);
    }

    private static async Task<(AppDbContext Db, IDateTimeProvider Clock, CapacityHoldExpiryService Service, Guid SubscriptionId, Guid HoldId)> SeedPendingAsync(
        DateTime now,
        DateTime holdExpiresAt)
    {
        var db = CreateDb();
        var clock = Substitute.For<IDateTimeProvider>();
        clock.UtcNow.Returns(now);
        var subscription = await AddSubscriptionAsync(db, SubscriptionStatus.PendingPayment, now.AddMinutes(-15));
        var hold = new CapacityHold
        {
            RestaurantId = Guid.NewGuid(),
            SubscriptionId = subscription.Id,
            MenuPlanVersionId = Guid.NewGuid(),
            ServiceDate = new DateOnly(2026, 9, 10),
            Quantity = 1,
            Status = CapacityHoldStatus.PendingPayment,
            ExpiresAtUtc = holdExpiresAt,
        };
        db.CapacityHolds.Add(hold);
        await db.SaveChangesAsync();
        return (db, clock, CreateService(db, clock), subscription.Id, hold.Id);
    }

    private static async Task<Subscription> AddSubscriptionAsync(AppDbContext db, SubscriptionStatus status, DateTime lastOperationAtUtc)
    {
        var program = new Program { Code = "P", NameAr = "ب", NameEn = "P", Status = ProgramStatus.Active, IsActive = true };
        var bundle = new Bundle
        {
            ProgramId = program.Id,
            Program = program,
            Code = "B",
            NameAr = "ب",
            NameEn = "B",
            MealsPerDay = 1,
            LunchCount = 1,
            Status = ProgramStatus.Active,
            IsActive = true,
        };
        var subscription = new Subscription
        {
            CustomerUserId = Guid.NewGuid(),
            ProgramId = program.Id,
            BundleId = bundle.Id,
            SelectedTier = RestaurantTier.Basic,
            DurationDays = 12,
            Status = status,
            StartDate = new DateOnly(2026, 9, 10),
            EndDate = new DateOnly(2026, 9, 22),
            PaidAmount = new MealMate.Domain.ValueObjects.Money(50m, "KWD"),
            LastOperationAtUtc = lastOperationAtUtc,
        };
        db.AddRange(program, bundle, subscription);
        await db.SaveChangesAsync();
        return subscription;
    }

    private static CapacityHoldExpiryService CreateService(AppDbContext db, IDateTimeProvider clock)
        => new(db, new InlineTransactionExecutor(), new NoopCapacityLock(), clock);

    private static AppDbContext CreateDb()
        => new(new DbContextOptionsBuilder<AppDbContext>().UseInMemoryDatabase(Guid.NewGuid().ToString()).Options,
            Substitute.For<IDomainEventDispatcher>());

    private sealed class InlineTransactionExecutor : ICheckoutTransactionExecutor
    {
        public Task<T> ExecuteAsync<T>(Func<CancellationToken, Task<T>> operation, CancellationToken cancellationToken = default)
            => operation(cancellationToken);
    }

    private sealed class NoopCapacityLock : ICapacityConcurrencyLock
    {
        public Task AcquirePaymentAsync(string paymentId, CancellationToken cancellationToken = default) => Task.CompletedTask;
        public Task AcquireSubscriptionAsync(Guid subscriptionId, CancellationToken cancellationToken = default) => Task.CompletedTask;
        public Task AcquireAsync(Guid restaurantId, IReadOnlyList<DateOnly> dates, CancellationToken cancellationToken = default) => Task.CompletedTask;
    }
}
