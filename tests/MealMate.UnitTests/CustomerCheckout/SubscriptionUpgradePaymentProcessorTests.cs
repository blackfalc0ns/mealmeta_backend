using MealMate.Application.Common.Abstractions;
using MealMate.Application.Features.CustomerCheckout;
using MealMate.Application.Features.ProgramsSubscriptions;
using MealMate.Domain.Enums;
using MealMate.Domain.ProgramsSubscriptions;
using MealMate.Domain.ValueObjects;
using MealMate.Persistence;
using Microsoft.EntityFrameworkCore;
using NSubstitute;

namespace MealMate.UnitTests.CustomerCheckout;

public sealed class SubscriptionUpgradePaymentProcessorTests
{
    [Fact]
    public async Task Upgrade_payment_applies_tier_and_keeps_subscription_active()
    {
        await using var db = CreateDb();
        var clock = FixedClock();
        var subscription = SeedActiveSubscription(db);
        var upgrade = new SubscriptionTierUpgrade
        {
            SubscriptionId = subscription.Id,
            FromTier = RestaurantTier.Basic,
            ToTier = RestaurantTier.Platinum,
            PriceDifference = new Money(12.5m, "KWD"),
            QuotedRemainingDays = 10,
            QuotedTotalDue = new Money(12.5m, "KWD"),
            PaymentId = "UPG-PAID-1",
            Status = VerificationStatus.Submitted,
        };
        db.SubscriptionTierUpgrades.Add(upgrade);
        await db.SaveChangesAsync();

        var verifiedAt = clock.UtcNow;
        var gateway = Substitute.For<IMyFatoorahPaymentService>();
        gateway.GetPaymentStatusAsync("UPG-PAID-1", "PaymentId", Arg.Any<CancellationToken>()).Returns(
            new MyFatoorahPaymentStatusResponse(
                "INV-UPG-1", "Paid", 12.5m, "Customer", "c@example.com", "50000000", "TX-UPG-1", "KNET",
                verifiedAt, subscription.Id.ToString(), "KWD", "UPG-PAID-1"));

        var processor = CreateProcessor(db, clock, gateway);
        var result = await processor.VerifyAndProcessAsync(subscription.Id, "UPG-PAID-1");

        Assert.True(result.IsAccepted);
        Assert.Equal(SubscriptionStatus.Active, result.SubscriptionStatus);
        Assert.Equal("Completed", result.FulfillmentStatus);

        var tracked = await db.Subscriptions.FindAsync(subscription.Id);
        Assert.Equal(RestaurantTier.Platinum, tracked!.SelectedTier);
        Assert.Equal(SubscriptionStatus.Active, tracked.Status);

        var trackedUpgrade = await db.SubscriptionTierUpgrades.FindAsync(upgrade.Id);
        Assert.Equal(VerificationStatus.Approved, trackedUpgrade!.Status);
        Assert.NotNull(trackedUpgrade.EffectiveAtUtc);
        Assert.True(await db.SubscriptionOperations.AnyAsync(x => x.SubscriptionId == subscription.Id && x.OperationType == "UPGRADE_PAID"));
    }

    [Fact]
    public async Task Failed_upgrade_payment_rejects_upgrade_without_marking_subscription_payment_failed()
    {
        await using var db = CreateDb();
        var clock = FixedClock();
        var subscription = SeedActiveSubscription(db);
        var upgrade = new SubscriptionTierUpgrade
        {
            SubscriptionId = subscription.Id,
            FromTier = RestaurantTier.Basic,
            ToTier = RestaurantTier.Platinum,
            PriceDifference = new Money(12.5m, "KWD"),
            QuotedRemainingDays = 10,
            QuotedTotalDue = new Money(12.5m, "KWD"),
            PaymentId = "UPG-FAIL-1",
            Status = VerificationStatus.Submitted,
        };
        db.SubscriptionTierUpgrades.Add(upgrade);
        await db.SaveChangesAsync();

        var verifiedAt = clock.UtcNow;
        var gateway = Substitute.For<IMyFatoorahPaymentService>();
        gateway.GetPaymentStatusAsync("UPG-FAIL-1", "PaymentId", Arg.Any<CancellationToken>()).Returns(
            new MyFatoorahPaymentStatusResponse(
                "INV-UPG-F", "Failed", 12.5m, "Customer", "c@example.com", "50000000", "TX-UPG-F", "KNET",
                verifiedAt, subscription.Id.ToString(), "KWD", "UPG-FAIL-1"));

        var processor = CreateProcessor(db, clock, gateway);
        var result = await processor.VerifyAndReleaseFailedAsync(subscription.Id, "UPG-FAIL-1");

        Assert.True(result.IsAccepted);
        Assert.Equal(SubscriptionStatus.Active, result.SubscriptionStatus);

        var tracked = await db.Subscriptions.FindAsync(subscription.Id);
        Assert.Equal(SubscriptionStatus.Active, tracked!.Status);
        Assert.Equal(RestaurantTier.Basic, tracked.SelectedTier);

        var trackedUpgrade = await db.SubscriptionTierUpgrades.FindAsync(upgrade.Id);
        Assert.Equal(VerificationStatus.Rejected, trackedUpgrade!.Status);
    }

    [Fact]
    public async Task Active_subscription_without_pending_upgrade_still_short_circuits_duplicate_success()
    {
        await using var db = CreateDb();
        var clock = FixedClock();
        var subscription = SeedActiveSubscription(db);
        await db.SaveChangesAsync();

        var verifiedAt = clock.UtcNow;
        var gateway = Substitute.For<IMyFatoorahPaymentService>();
        gateway.GetPaymentStatusAsync("PAY-ACTIVE-1", "PaymentId", Arg.Any<CancellationToken>()).Returns(
            new MyFatoorahPaymentStatusResponse(
                "INV-ACTIVE", "Paid", subscription.PaidAmount.Amount, "Customer", "c@example.com", "50000000", "TX-A", "KNET",
                verifiedAt, subscription.Id.ToString(), "KWD", "PAY-ACTIVE-1"));

        var processor = CreateProcessor(db, clock, gateway);
        var result = await processor.VerifyAndProcessAsync(subscription.Id, "PAY-ACTIVE-1");

        Assert.True(result.IsAccepted);
        Assert.True(result.IsDuplicate);
        Assert.Equal(SubscriptionStatus.Active, result.SubscriptionStatus);
        Assert.Equal("Completed", result.FulfillmentStatus);
        Assert.Empty(await db.SubscriptionTierUpgrades.Where(x => x.SubscriptionId == subscription.Id).ToListAsync());
    }

    private static SubscriptionPaymentProcessor CreateProcessor(AppDbContext db, IDateTimeProvider clock, IMyFatoorahPaymentService gateway)
    {
        var fulfillment = Substitute.For<ISubscriptionFulfillmentService>();
        fulfillment.TryFulfillAsync(Arg.Any<Guid>(), Arg.Any<CancellationToken>())
            .Returns(new FulfillmentResult(true, true));
        return new SubscriptionPaymentProcessor(
            db,
            gateway,
            new InlineTransactionExecutor(),
            new NoOpCapacityLock(),
            new SubscriptionScheduleService(clock),
            fulfillment,
            clock);
    }

    private static Subscription SeedActiveSubscription(AppDbContext db)
    {
        var subscription = new Subscription
        {
            CustomerUserId = Guid.NewGuid(),
            ProgramId = Guid.NewGuid(),
            BundleId = Guid.NewGuid(),
            SelectedTier = RestaurantTier.Basic,
            Status = SubscriptionStatus.Active,
            DurationDays = 26,
            StartDate = new DateOnly(2026, 8, 1),
            EndDate = new DateOnly(2026, 8, 27),
            PaidAmount = new Money(100m, "KWD"),
            PaymentId = "ORIGINAL-PAY",
        };
        db.Subscriptions.Add(subscription);
        return subscription;
    }

    private static IDateTimeProvider FixedClock()
    {
        var clock = Substitute.For<IDateTimeProvider>();
        clock.UtcNow.Returns(new DateTime(2026, 8, 13, 9, 0, 0, DateTimeKind.Utc));
        return clock;
    }

    private static AppDbContext CreateDb()
        => new(new DbContextOptionsBuilder<AppDbContext>().UseInMemoryDatabase(Guid.NewGuid().ToString()).Options,
            Substitute.For<IDomainEventDispatcher>());

    private sealed class InlineTransactionExecutor : ICheckoutTransactionExecutor
    {
        public Task<T> ExecuteAsync<T>(Func<CancellationToken, Task<T>> operation, CancellationToken cancellationToken = default)
            => operation(cancellationToken);
    }

    private sealed class NoOpCapacityLock : ICapacityConcurrencyLock
    {
        public Task AcquirePaymentAsync(string paymentId, CancellationToken cancellationToken = default) => Task.CompletedTask;
        public Task AcquireSubscriptionAsync(Guid subscriptionId, CancellationToken cancellationToken = default) => Task.CompletedTask;
        public Task AcquireAsync(Guid restaurantId, IReadOnlyList<DateOnly> dates, CancellationToken cancellationToken = default) => Task.CompletedTask;
    }
}
