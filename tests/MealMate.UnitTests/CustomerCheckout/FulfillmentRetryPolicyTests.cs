using MealMate.Application.Common.Abstractions;
using MealMate.Application.Features.CustomerCheckout;
using MealMate.Domain.Enums;
using MealMate.Domain.ProgramsSubscriptions;
using MealMate.Domain.ValueObjects;
using MealMate.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using NSubstitute;
using Xunit;

namespace MealMate.UnitTests.CustomerCheckout;

public sealed class FulfillmentRetryPolicyTests
{
    [Fact]
    public async Task TryFulfillAsync_AttemptFourWithFiveMax_KeepsSubscriptionPaidAndPendingRetry()
    {
        using var db = CreateDb();
        var clock = FixedClock();
        var subscription = SeedSubscription(db, initialAttempts: 3);
        await db.SaveChangesAsync();

        var options = Options.Create(new FulfillmentOptions { MaxAutomaticAttempts = 5 });
        var service = CreateFulfillmentService(db, clock, options);

        var result = await service.TryFulfillAsync(subscription.Id);

        Assert.False(result.IsSuccess);
        var updated = await db.Subscriptions.SingleAsync(x => x.Id == subscription.Id);
        Assert.Equal(4, updated.FulfillmentAttemptCount);
        Assert.Equal(SubscriptionStatus.Paid, updated.Status);
    }

    [Fact]
    public async Task TryFulfillAsync_AttemptFiveWithFiveMax_SuspendsSubscriptionForReview()
    {
        using var db = CreateDb();
        var clock = FixedClock();
        var subscription = SeedSubscription(db, initialAttempts: 4);
        await db.SaveChangesAsync();

        var options = Options.Create(new FulfillmentOptions { MaxAutomaticAttempts = 5 });
        var service = CreateFulfillmentService(db, clock, options);

        var result = await service.TryFulfillAsync(subscription.Id);

        Assert.False(result.IsSuccess);
        var updated = await db.Subscriptions.SingleAsync(x => x.Id == subscription.Id);
        Assert.Equal(5, updated.FulfillmentAttemptCount);
        Assert.Equal(SubscriptionStatus.SuspendedForReview, updated.Status);
    }

    [Fact]
    public async Task TryFulfillAsync_CustomMaxAttemptsConfigured_SuspendsWhenThresholdReached()
    {
        using var db = CreateDb();
        var clock = FixedClock();
        var subscription = SeedSubscription(db, initialAttempts: 2);
        await db.SaveChangesAsync();

        var options = Options.Create(new FulfillmentOptions { MaxAutomaticAttempts = 3 });
        var service = CreateFulfillmentService(db, clock, options);

        var result = await service.TryFulfillAsync(subscription.Id);

        Assert.False(result.IsSuccess);
        var updated = await db.Subscriptions.SingleAsync(x => x.Id == subscription.Id);
        Assert.Equal(3, updated.FulfillmentAttemptCount);
        Assert.Equal(SubscriptionStatus.SuspendedForReview, updated.Status);
    }

    private static SubscriptionFulfillmentService CreateFulfillmentService(
        AppDbContext db,
        IDateTimeProvider clock,
        IOptions<FulfillmentOptions> options)
        => new(db, new InlineTransactionExecutor(), new NoOpCapacityLock(), new SubscriptionScheduleService(clock), clock, null, options);

    private static Subscription SeedSubscription(AppDbContext db, int initialAttempts)
    {
        var subscription = new Subscription
        {
            CustomerUserId = Guid.NewGuid(),
            ProgramId = Guid.NewGuid(),
            BundleId = Guid.NewGuid(),
            SelectedTier = RestaurantTier.Basic,
            DurationDays = 5,
            IncludeFriday = false,
            StartDate = new DateOnly(2026, 9, 1),
            EndDate = new DateOnly(2026, 9, 5),
            Status = SubscriptionStatus.Paid,
            PaidAmount = new Money(25m, "KWD"),
            PaymentId = "PAY-RETRY-TEST",
            FulfillmentAttemptCount = initialAttempts
        };
        db.Subscriptions.Add(subscription);
        return subscription;
    }

    private static IDateTimeProvider FixedClock()
    {
        var clock = Substitute.For<IDateTimeProvider>();
        clock.UtcNow.Returns(new DateTime(2026, 8, 28, 12, 0, 0, DateTimeKind.Utc));
        return clock;
    }

    private static AppDbContext CreateDb()
        => new(new DbContextOptionsBuilder<AppDbContext>().UseInMemoryDatabase(Guid.NewGuid().ToString()).Options, Substitute.For<IDomainEventDispatcher>());

    private sealed class InlineTransactionExecutor : ICheckoutTransactionExecutor
    {
        public Task<T> ExecuteAsync<T>(Func<CancellationToken, Task<T>> operation, CancellationToken cancellationToken = default) => operation(cancellationToken);
    }

    private sealed class NoOpCapacityLock : ICapacityConcurrencyLock
    {
        public Task AcquirePaymentAsync(string paymentId, CancellationToken cancellationToken = default) => Task.CompletedTask;
        public Task AcquireSubscriptionAsync(Guid subscriptionId, CancellationToken cancellationToken = default) => Task.CompletedTask;
        public Task AcquireAsync(Guid restaurantId, IReadOnlyList<DateOnly> dates, CancellationToken cancellationToken = default) => Task.CompletedTask;
    }
}
