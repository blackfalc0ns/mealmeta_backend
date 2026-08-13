using System.Security.Cryptography;
using System.Text;
using MealMate.Application.Common.Abstractions;
using MealMate.Application.Features.CustomerCheckout;
using MealMate.Domain.Enums;
using MealMate.Domain.ProgramsSubscriptions;
using MealMate.Domain.RestaurantOperations;
using MealMate.Domain.ValueObjects;
using MealMate.Persistence;
using Microsoft.EntityFrameworkCore;
using NSubstitute;

namespace MealMate.UnitTests.CustomerCheckout;

public sealed class SubscriptionFlowGapTests
{
    [Fact]
    public void Schedule_uses_Kuwait_cutoff_and_bundle_slot_json_supports_counts()
    {
        var clock = Substitute.For<IDateTimeProvider>();
        clock.UtcNow.Returns(new DateTime(2026, 8, 13, 14, 59, 59, DateTimeKind.Utc));
        var schedule = new SubscriptionScheduleService(clock);

        Assert.Equal(new DateTime(2026, 8, 13, 15, 0, 0, DateTimeKind.Utc), schedule.GetSelectionDeadlineUtc(new DateOnly(2026, 8, 14)));
        Assert.True(schedule.IsStartAllowed(new DateOnly(2026, 8, 14)));
        var requirements = SlotRequirementJson.Parse("[{\"slot\":\"lunch\",\"count\":2},{\"slot\":\"snack\",\"count\":1}]");
        Assert.Equal(2, requirements.Single(x => x.Slot == "lunch").Count);
        Assert.Equal(1, requirements.Single(x => x.Slot == "snack").Count);
    }

    [Fact]
    public async Task Fulfillment_requires_every_payment_confirmed_hold_and_frozen_day_snapshot()
    {
        await using var db = CreateDb();
        var clock = FixedClock();
        var subscription = SeedSubscription(db, days: 2);
        var restaurantId = Guid.NewGuid();
        SeedPool(db, subscription, restaurantId, snapshotDays: 1, holdDays: 2);
        await db.SaveChangesAsync();
        var service = CreateFulfillment(db, clock);

        var result = await service.TryFulfillAsync(subscription.Id);

        Assert.False(result.IsSuccess);
        Assert.Equal("fulfillment.hold_or_snapshot_mismatch", result.ErrorCode);
        Assert.Equal(SubscriptionStatus.Paid, subscription.Status);
        Assert.Equal(1, subscription.FulfillmentAttemptCount);
        Assert.Empty(await db.SubscriptionDays.Where(x => x.SubscriptionId == subscription.Id).ToListAsync());
        Assert.All(await db.CapacityHolds.Where(x => x.SubscriptionId == subscription.Id).ToListAsync(), x => Assert.Equal(CapacityHoldStatus.PaymentConfirmed, x.Status));
    }

    [Fact]
    public async Task Fulfillment_atomically_copies_pool_snapshots_and_confirms_capacity()
    {
        await using var db = CreateDb();
        var clock = FixedClock();
        var subscription = SeedSubscription(db, days: 2);
        var restaurantId = Guid.NewGuid();
        SeedPool(db, subscription, restaurantId, snapshotDays: 2, holdDays: 2);
        await db.SaveChangesAsync();
        var service = CreateFulfillment(db, clock);

        var result = await service.TryFulfillAsync(subscription.Id);

        Assert.True(result.IsSuccess);
        Assert.Equal(SubscriptionStatus.Scheduled, subscription.Status);
        Assert.NotNull(subscription.FulfillmentCompletedAtUtc);
        Assert.Equal(2, await db.SubscriptionDays.CountAsync(x => x.SubscriptionId == subscription.Id));
        Assert.Equal(2, await db.SubscriptionDayMealSnapshots.CountAsync(x => x.SubscriptionId == subscription.Id));
        Assert.All(await db.CapacityHolds.Where(x => x.SubscriptionId == subscription.Id).ToListAsync(), x => Assert.Equal(CapacityHoldStatus.Confirmed, x.Status));
    }

    [Fact]
    public async Task Payment_verification_rejects_amount_mismatch_without_confirming_holds()
    {
        await using var db = CreateDb();
        var clock = FixedClock();
        var subscription = SeedSubscription(db, days: 1);
        subscription.Status = SubscriptionStatus.PendingPayment;
        subscription.PaymentId = null;
        db.PaymentWebhookReceipts.RemoveRange(db.PaymentWebhookReceipts.Local);
        await db.SaveChangesAsync();
        var gateway = Substitute.For<IMyFatoorahPaymentService>();
        var verifiedAt = clock.UtcNow;
        gateway.GetPaymentStatusAsync("PAY-WRONG", "PaymentId", Arg.Any<CancellationToken>()).Returns(
            new MyFatoorahPaymentStatusResponse("INV-1", "Paid", 24m, "Customer", "c@example.com", "50000000", "TX-1", "KNET", verifiedAt,
                subscription.Id.ToString(), "KWD", "PAY-WRONG"));
        var fulfillment = Substitute.For<ISubscriptionFulfillmentService>();
        var processor = new SubscriptionPaymentProcessor(db, gateway, new InlineTransactionExecutor(), new NoOpCapacityLock(), new SubscriptionScheduleService(clock), fulfillment, clock);

        var result = await processor.VerifyAndProcessAsync(subscription.Id, "PAY-WRONG");

        Assert.False(result.IsAccepted);
        Assert.Equal("payment.amount_mismatch", result.ErrorCode);
        Assert.Equal(SubscriptionStatus.PendingPayment, subscription.Status);
        await fulfillment.DidNotReceiveWithAnyArgs().TryFulfillAsync(default);
    }

    private static SubscriptionFulfillmentService CreateFulfillment(AppDbContext db, IDateTimeProvider clock)
        => new(db, new InlineTransactionExecutor(), new NoOpCapacityLock(), new SubscriptionScheduleService(clock), clock);

    private static Subscription SeedSubscription(AppDbContext db, int days)
    {
        var subscription = new Subscription
        {
            CustomerUserId = Guid.NewGuid(),
            ProgramId = Guid.NewGuid(),
            BundleId = Guid.NewGuid(),
            SelectedTier = RestaurantTier.Basic,
            DurationDays = days,
            IncludeFriday = true,
            StartDate = new DateOnly(2026, 8, 20),
            EndDate = new DateOnly(2026, 8, 20).AddDays(days - 1),
            Status = SubscriptionStatus.Paid,
            PaidAmount = new Money(25m, "KWD"),
            PaymentId = "PAY-TEST"
        };
        db.Subscriptions.Add(subscription);
        db.PaymentWebhookReceipts.Add(new PaymentWebhookReceipt
        {
            Gateway = "MyFatoorah", EventKey = "PAY-TEST", PaymentId = "PAY-TEST", SubscriptionId = subscription.Id,
            Status = "PaymentVerified", ProcessedAtUtc = DateTime.UtcNow
        });
        return subscription;
    }

    private static void SeedPool(AppDbContext db, Subscription subscription, Guid restaurantId, int snapshotDays, int holdDays)
    {
        var pool = new SubscriptionRestaurantPoolSnapshot { SubscriptionId = subscription.Id, RestaurantCount = 1, EligibilityCheckedAtUtc = DateTime.UtcNow };
        var restaurant = new SubscriptionRestaurantSnapshot
        {
            PoolSnapshotId = pool.Id, SubscriptionId = subscription.Id, RestaurantId = restaurantId,
            ProgramId = subscription.ProgramId, BundleId = subscription.BundleId, RegionId = Guid.NewGuid(), Tier = subscription.SelectedTier,
            NameAr = "مطعم", NameEn = "Restaurant", IsInitiallySelected = true, OfferingSnapshotJson = "{}", CapturedAtUtc = DateTime.UtcNow
        };
        db.SubscriptionRestaurantPoolSnapshots.Add(pool);
        db.SubscriptionRestaurantSnapshots.Add(restaurant);
        const string slots = "[{\"slot\":\"lunch\",\"count\":1}]";
        const string meals = "[{\"mealId\":\"00000000-0000-0000-0000-000000000001\",\"nameAr\":\"وجبة\",\"nameEn\":\"Meal\",\"slot\":\"lunch\",\"sortOrder\":0,\"presentAllergenCodes\":[],\"mayContainAllergenCodes\":[],\"dietaryPreferenceCodes\":[],\"ingredients\":[]}]";
        var hash = Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(slots + "\n" + meals))).ToLowerInvariant();
        for (var index = 0; index < snapshotDays; index++)
        {
            var date = subscription.StartDate.AddDays(index);
            db.SubscriptionRestaurantDaySnapshots.Add(new SubscriptionRestaurantDaySnapshot
            {
                RestaurantSnapshotId = restaurant.Id, SubscriptionId = subscription.Id, RestaurantId = restaurantId,
                ServiceDate = date, MenuPlanVersionId = Guid.NewGuid(), SlotRequirementsJson = slots, MealsJson = meals,
                ContentHash = hash, CapturedAtUtc = DateTime.UtcNow
            });
        }
        for (var index = 0; index < holdDays; index++)
        {
            var date = subscription.StartDate.AddDays(index);
            var version = db.SubscriptionRestaurantDaySnapshots.Local.SingleOrDefault(x => x.SubscriptionId == subscription.Id && x.ServiceDate == date)?.MenuPlanVersionId ?? Guid.NewGuid();
            db.CapacityHolds.Add(new CapacityHold
            {
                SubscriptionId = subscription.Id, RestaurantId = restaurantId, ServiceDate = date, MenuPlanVersionId = version,
                Quantity = 1, Status = CapacityHoldStatus.PaymentConfirmed, ExpiresAtUtc = DateTime.UtcNow.AddMinutes(10)
            });
        }
    }

    private static IDateTimeProvider FixedClock()
    {
        var clock = Substitute.For<IDateTimeProvider>();
        clock.UtcNow.Returns(new DateTime(2026, 8, 13, 9, 0, 0, DateTimeKind.Utc));
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
