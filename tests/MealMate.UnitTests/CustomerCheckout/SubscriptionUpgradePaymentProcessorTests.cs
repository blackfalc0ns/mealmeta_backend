using System.Text.Json;
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
    public async Task Paid_upgrade_after_cancellation_records_review_without_applying_tier()
    {
        await using var db = CreateDb(); var clock = FixedClock(); var subscription = SeedActiveSubscription(db);
        subscription.Status = SubscriptionStatus.Cancelled;
        var upgrade = new SubscriptionTierUpgrade { SubscriptionId = subscription.Id, FromTier = RestaurantTier.Basic,
            ToTier = RestaurantTier.Platinum, PriceDifference = new Money(12.5m, "KWD"), QuotedTotalDue = new Money(12.5m, "KWD"),
            PaymentId = "late-upgrade", Status = VerificationStatus.Submitted, PayerCustomerUserId = subscription.CustomerUserId };
        db.SubscriptionTierUpgrades.Add(upgrade); await db.SaveChangesAsync();
        var gateway = Substitute.For<IPaymentGatewayService>();
        var verificationTime = clock.UtcNow;
        gateway.GetPaymentStatusAsync("late-upgrade", "PaymentId", Arg.Any<CancellationToken>()).Returns(new PaymentGatewayStatusResponse(
            "late-invoice", "Paid", 12.5m, "", "", "", "late-tx", "KNET", verificationTime, subscription.Id.ToString(), "KWD", "late-upgrade"));
        var result = await CreateProcessor(db, clock, gateway).VerifyAndProcessAsync(subscription.Id, "late-upgrade");
        Assert.Equal("ReviewRequired", result.FulfillmentStatus);
        Assert.Equal(SubscriptionStatus.Cancelled, subscription.Status);
        Assert.Equal(RestaurantTier.Basic, subscription.SelectedTier);
        Assert.Equal(12.5m, upgrade.VerifiedPaidAmount);
    }

    [Theory]
    [InlineData(SubscriptionDayStatus.Delivered)]
    [InlineData(SubscriptionDayStatus.Locked72h)]
    public async Task Late_paid_family_upgrade_after_funded_day_changes_is_reviewed_without_tier_activation(SubscriptionDayStatus changedStatus)
    {
        await using var db = CreateDb();
        var clock = FixedClock();
        var manager = Guid.NewGuid();
        var group = new FamilyGroup { Name = "Family", ManagerCustomerUserId = manager, Status = FamilyGroupStatus.Active };
        var member = new FamilyMember { FamilyGroupId = group.Id, DisplayName = "Member", RelationshipCode = "child", NutritionalGoalCode = "maintain" };
        var dates = new[] { new DateOnly(2026, 8, 20), new DateOnly(2026, 8, 21) };
        var subscription = new Subscription { CustomerUserId = manager, FamilyGroupId = group.Id, FamilyMemberId = member.Id,
            ProgramId = Guid.NewGuid(), BundleId = Guid.NewGuid(), SelectedTier = RestaurantTier.Basic, Status = SubscriptionStatus.Active,
            DurationDays = dates.Length, StartDate = dates[0], EndDate = dates[^1], PaidAmount = new Money(100m, "KWD") };
        member.MemberSubscriptionId = subscription.Id;
        var upgrade = new SubscriptionTierUpgrade { SubscriptionId = subscription.Id, FromTier = RestaurantTier.Basic,
            ToTier = RestaurantTier.Platinum, PriceDifference = new Money(25m, "KWD"), QuotedTotalDue = new Money(25m, "KWD"),
            QuotedRemainingDays = dates.Length, PaymentId = "family-upgrade-payment", PayerCustomerUserId = manager,
            EffectiveServiceDate = dates[0], EffectiveServiceDatesJson = JsonSerializer.Serialize(dates), Status = VerificationStatus.Submitted };
        var changedDay = new MealMate.Domain.CalendarMeals.SubscriptionDay { SubscriptionId = subscription.Id,
            ServiceDate = dates[0], Status = changedStatus, SelectionDeadlineUtc = clock.UtcNow.AddDays(1),
            LockAtUtc = changedStatus == SubscriptionDayStatus.Locked72h ? clock.UtcNow.AddHours(-1) : null };
        db.FamilyGroups.Add(group); db.FamilyMembers.Add(member); db.Subscriptions.Add(subscription);
        db.SubscriptionTierUpgrades.Add(upgrade); db.SubscriptionDays.AddRange(changedDay,
            new MealMate.Domain.CalendarMeals.SubscriptionDay { SubscriptionId = subscription.Id, ServiceDate = dates[1],
                Status = SubscriptionDayStatus.Open, SelectionDeadlineUtc = clock.UtcNow.AddDays(1) });
        await db.SaveChangesAsync();

        var gateway = Substitute.For<IPaymentGatewayService>();
        var verificationTime = clock.UtcNow;
        gateway.GetPaymentStatusAsync("family-upgrade-payment", "PaymentId", Arg.Any<CancellationToken>()).Returns(
            new PaymentGatewayStatusResponse("family-upgrade-invoice", "Paid", 25m, "Customer", "c@example.com", "50000000",
                "family-upgrade-tx", "KNET", verificationTime, subscription.Id.ToString(), "KWD", "family-upgrade-payment"));
        var result = await CreateProcessor(db, clock, gateway).VerifyAndProcessAsync(subscription.Id, "family-upgrade-payment");

        Assert.Equal("ReviewRequired", result.FulfillmentStatus);
        Assert.Equal("upgrade.service_changed_review_required", result.ErrorCode);
        Assert.Equal(RestaurantTier.Basic, subscription.SelectedTier);
        Assert.Equal(VerificationStatus.Submitted, upgrade.Status);
        Assert.Equal("FinancialReviewRequired", (await db.PaymentWebhookReceipts.SingleAsync(x => x.PaymentId == "family-upgrade-payment")).Status);
        Assert.False(await db.SubscriptionOperations.AnyAsync(x => x.SubscriptionId == subscription.Id && x.OperationType == "UPGRADE_PAID"));
    }

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
        var gateway = Substitute.For<IPaymentGatewayService>();
        gateway.GetPaymentStatusAsync("UPG-PAID-1", "PaymentId", Arg.Any<CancellationToken>()).Returns(
            new PaymentGatewayStatusResponse(
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
        Assert.Equal(12.5m, trackedUpgrade.VerifiedPaidAmount);
        Assert.Equal("KWD", trackedUpgrade.VerifiedCurrencyCode);
        Assert.Equal("INV-UPG-1", trackedUpgrade.VerifiedInvoiceId);
        Assert.True(await db.SubscriptionOperations.AnyAsync(x => x.SubscriptionId == subscription.Id && x.OperationType == "UPGRADE_PAID"));
    }

    [Fact]
    public async Task Upgrade_payment_rejects_unbound_initial_payment_id()
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
            PaymentId = null, // Unbound legacy upgrade cannot acquire a new provider payment.
            Status = VerificationStatus.Submitted,
        };
        db.SubscriptionTierUpgrades.Add(upgrade);
        await db.SaveChangesAsync();

        var verifiedAt = clock.UtcNow;
        var gateway = Substitute.For<IPaymentGatewayService>();
        gateway.GetPaymentStatusAsync("UPG-GATEWAY-123", "PaymentId", Arg.Any<CancellationToken>()).Returns(
            new PaymentGatewayStatusResponse(
                "INV-UPG-2", "Paid", 12.5m, "Customer", "c@example.com", "50000000", "TX-UPG-2", "KNET",
                verifiedAt, subscription.Id.ToString(), "KWD", "UPG-GATEWAY-123"));

        var processor = CreateProcessor(db, clock, gateway);
        var result = await processor.VerifyAndProcessAsync(subscription.Id, "UPG-GATEWAY-123");

        Assert.False(result.IsAccepted); // An unbound legacy upgrade has no provider payment identity to acknowledge.
        Assert.NotEqual("Completed", result.FulfillmentStatus);
        Assert.Equal(SubscriptionStatus.Active, result.SubscriptionStatus);

        var trackedUpgrade = await db.SubscriptionTierUpgrades.FindAsync(upgrade.Id);
        Assert.Equal(VerificationStatus.Submitted, trackedUpgrade!.Status);
        Assert.Null(trackedUpgrade.PaymentId);
    }

    [Fact]
    public async Task Failed_upgrade_payment_rejects_upgrade_without_marking_subscription_payment_failed()
    {
        await using var db = CreateDb();
        var clock = FixedClock();
        var subscription = SeedActiveSubscription(db);
        subscription.FamilyGroupId = Guid.NewGuid(); // Family manager upgrade still binds to subscription reference.
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
        var gateway = Substitute.For<IPaymentGatewayService>();
        gateway.GetPaymentStatusAsync("UPG-FAIL-1", "PaymentId", Arg.Any<CancellationToken>()).Returns(
            new PaymentGatewayStatusResponse(
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
        var gateway = Substitute.For<IPaymentGatewayService>();
        gateway.GetPaymentStatusAsync("ORIGINAL-PAY", "PaymentId", Arg.Any<CancellationToken>()).Returns(
            new PaymentGatewayStatusResponse(
                "INV-ACTIVE", "Paid", subscription.PaidAmount.Amount, "Customer", "c@example.com", "50000000", "TX-A", "KNET",
                verifiedAt, subscription.Id.ToString(), "KWD", "ORIGINAL-PAY"));

        var processor = CreateProcessor(db, clock, gateway);
        var result = await processor.VerifyAndProcessAsync(subscription.Id, "ORIGINAL-PAY");

        Assert.True(result.IsAccepted);
        Assert.True(result.IsDuplicate);
        Assert.Equal(SubscriptionStatus.Active, result.SubscriptionStatus);
        Assert.Equal("Completed", result.FulfillmentStatus);
        Assert.Empty(await db.SubscriptionTierUpgrades.Where(x => x.SubscriptionId == subscription.Id).ToListAsync());
    }

    [Theory]
    [InlineData("Failed")]
    [InlineData("Paid")]
    public async Task Stale_attempt_cannot_change_new_payment_or_release_its_holds(string gatewayStatus)
    {
        await using var db = CreateDb();
        var subscription = SeedActiveSubscription(db);
        subscription.Status = SubscriptionStatus.PendingPayment;
        subscription.PaymentId = "new-payment";
        var hold = new MealMate.Domain.RestaurantOperations.CapacityHold { SubscriptionId = subscription.Id, ExpiresAtUtc = FixedClock().UtcNow.AddMinutes(15) };
        db.CapacityHolds.Add(hold);
        await db.SaveChangesAsync();
        var gateway = Substitute.For<IPaymentGatewayService>();
        gateway.GetPaymentStatusAsync("old-payment", "PaymentId", Arg.Any<CancellationToken>()).Returns(
            new PaymentGatewayStatusResponse("reference", gatewayStatus, 100m, "", "", "", "transaction", "Payzah", new DateTime(2026, 8, 13, 9, 0, 0, DateTimeKind.Utc), subscription.Id.ToString(), "KWD", "old-payment"));
        var processor = CreateProcessor(db, FixedClock(), gateway);
        var result = gatewayStatus == "Paid" ? await processor.VerifyAndProcessAsync(subscription.Id, "old-payment")
            : await processor.VerifyAndReleaseFailedAsync(subscription.Id, "old-payment");
        Assert.False(result.IsAccepted);
        Assert.Equal("payment.stale_attempt_review_required", result.ErrorCode);
        Assert.Equal(SubscriptionStatus.PendingPayment, subscription.Status);
        Assert.Equal(CapacityHoldStatus.PendingPayment, hold.Status);
        Assert.Equal("new-payment", subscription.PaymentId);
    }
    [Theory]
    [InlineData("Paid")]
    [InlineData("Failed")]
    public async Task Replayed_financial_review_receipt_does_not_become_success(string authoritativeStatus)
    {
        await using var db = CreateDb();
        var subscription = SeedActiveSubscription(db);
        db.PaymentWebhookReceipts.Add(new PaymentWebhookReceipt { Gateway = "Payzah", PaymentId = "review-payment", EventKey = "review-payment", SubscriptionId = subscription.Id, Status = "FinancialReviewRequired", LastFulfillmentErrorCode = "payment.hold_expired" });
        await db.SaveChangesAsync();
        var gateway = Substitute.For<IPaymentGatewayService>();
        gateway.GetPaymentStatusAsync("review-payment", "PaymentId", Arg.Any<CancellationToken>()).Returns(
            new PaymentGatewayStatusResponse("reference", authoritativeStatus, 100m, "", "", "", "transaction", "Payzah", new DateTime(2026, 8, 13, 9, 0, 0, DateTimeKind.Utc), subscription.Id.ToString(), "KWD", "review-payment"));
        var processor = CreateProcessor(db, FixedClock(), gateway);
        var result = authoritativeStatus == "Paid" ? await processor.VerifyAndProcessAsync(subscription.Id, "review-payment")
            : await processor.VerifyAndReleaseFailedAsync(subscription.Id, "review-payment");
        Assert.Equal(authoritativeStatus == "Paid", result.IsAccepted);
        Assert.Equal("ReviewRequired", result.FulfillmentStatus);
        Assert.Equal(authoritativeStatus == "Paid" ? "payment.hold_expired" : "payment.receipt_requires_review", result.ErrorCode);
        Assert.Equal(SubscriptionStatus.Active, subscription.Status);
        Assert.Equal(RestaurantTier.Basic, subscription.SelectedTier);
        Assert.Empty(await db.SubscriptionDays.Where(x => x.SubscriptionId == subscription.Id).ToListAsync());
        Assert.Equal("FinancialReviewRequired", (await db.PaymentWebhookReceipts.SingleAsync(x => x.PaymentId == "review-payment")).Status);
    }

    private static SubscriptionPaymentProcessor CreateProcessor(AppDbContext db, IDateTimeProvider clock, IPaymentGatewayService gateway)
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
