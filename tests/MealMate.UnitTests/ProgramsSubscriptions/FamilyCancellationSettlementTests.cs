using System.Text.Json;
using MealMate.Application.Common.Abstractions;
using MealMate.Application.Features.ProgramsSubscriptions.Cancellation;
using MealMate.Domain.Enums;
using MealMate.Domain.ProgramsSubscriptions;
using MealMate.Domain.ValueObjects;
using MealMate.Persistence;
using MealMate.Persistence.Common;
using MealMate.Application.Features.FamilyManager;
using MealMate.Application.Common.Localization;
using MealMate.Application.Features.AdminDashboard.OperationalPolicies;
using MealMate.Application.Features.ProgramsSubscriptions.Freeze;
using MealMate.Domain.AdminDashboard;
using MealMate.Domain.CalendarMeals;
using MealMate.Domain.CustomerFinance;
using Microsoft.Extensions.Caching.Memory;
using Microsoft.EntityFrameworkCore;
using NSubstitute;
using Xunit;

namespace MealMate.UnitTests.ProgramsSubscriptions;

public sealed class FamilyCancellationSettlementTests
{
    [Fact]
    public async Task Partial_cancellation_maps_paid_upgrade_dates_through_recorded_freeze_replacements()
    {
        await using var fixture = await SeedPaidFamilyUpgradeAsync(malformedUpgradeDates: false);

        var quote = await fixture.Service.GetQuoteAsync(fixture.SubscriptionId, fixture.ManagerId, UserType.Customer);

        Assert.True(quote.IsSuccess, quote.IsFailure ? quote.Error.Code : null);
        Assert.Equal(6, quote.Value.TotalServiceDays);
        Assert.Equal(5, quote.Value.RefundableDays);
        Assert.Equal(1500m, quote.Value.RefundAmount); // 5/6 of the base 1,200 plus the other payer's 600 upgrade.
        Assert.Equal(2, quote.Value.Beneficiaries.Count);
        Assert.Contains(quote.Value.Beneficiaries, x => x.ConfirmedPaidAmount == 1200m && x.PayerCustomerUserId == fixture.ManagerId);
        Assert.Contains(quote.Value.Beneficiaries, x => x.ConfirmedPaidAmount == 600m && x.PayerCustomerUserId == fixture.UpgradePayerId);
    }

    [Fact]
    public async Task Duplicate_paid_upgrade_service_dates_require_financial_review()
    {
        await using var fixture = await SeedPaidFamilyUpgradeAsync(malformedUpgradeDates: true);

        var quote = await fixture.Service.GetQuoteAsync(fixture.SubscriptionId, fixture.ManagerId, UserType.Customer);

        Assert.True(quote.IsFailure);
        Assert.Equal("family.financial_review_required", quote.Error.Code);
    }

    [Fact]
    public async Task Legacy_approved_upgrade_with_positive_price_and_zero_quote_requires_financial_review()
    {
        await using var fixture = await SeedPaidFamilyUpgradeAsync(malformedUpgradeDates: false);
        fixture.Db.SubscriptionTierUpgrades.Add(new SubscriptionTierUpgrade
        {
            SubscriptionId = fixture.SubscriptionId,
            FromTier = RestaurantTier.Basic,
            ToTier = RestaurantTier.Platinum,
            PriceDifference = new Money(300m, "KWD"),
            QuotedTotalDue = new Money(0m, "KWD"),
            QuotedRemainingDays = 6,
            Status = VerificationStatus.Approved,
            PaymentId = "legacy-unknown-payment",
        });
        await fixture.Db.SaveChangesAsync();

        var quote = await fixture.Service.GetQuoteAsync(fixture.SubscriptionId, fixture.ManagerId, UserType.Customer);

        Assert.True(quote.IsFailure);
        Assert.Equal("family.financial_review_required", quote.Error.Code);
    }

    [Fact]
    public async Task Explicitly_recorded_zero_due_upgrade_does_not_add_a_refund_contribution()
    {
        await using var fixture = await SeedPaidFamilyUpgradeAsync(malformedUpgradeDates: false);
        fixture.Db.SubscriptionTierUpgrades.Add(new SubscriptionTierUpgrade
        {
            SubscriptionId = fixture.SubscriptionId,
            FromTier = RestaurantTier.Platinum,
            ToTier = RestaurantTier.Elite,
            PriceDifference = new Money(0m, "KWD"),
            QuotedTotalDue = new Money(0m, "KWD"),
            Status = VerificationStatus.Approved,
            PaymentCreationState = "NotRequired",
            EffectiveAtUtc = new DateTime(2026, 10, 4),
        });
        await fixture.Db.SaveChangesAsync();

        var quote = await fixture.Service.GetQuoteAsync(fixture.SubscriptionId, fixture.ManagerId, UserType.Customer);

        Assert.True(quote.IsSuccess, quote.IsFailure ? quote.Error.Code : null);
        Assert.Equal(1500m, quote.Value.RefundAmount);
        Assert.Equal(2, quote.Value.Beneficiaries.Count);
    }

    [Fact]
    public async Task Legacy_positive_upgrade_with_zero_quote_uses_verified_receipt_contribution()
    {
        await using var fixture = await SeedPaidFamilyUpgradeAsync(malformedUpgradeDates: false);
        var upgrade = await fixture.Db.SubscriptionTierUpgrades.SingleAsync(x => x.SubscriptionId == fixture.SubscriptionId);
        upgrade.QuotedTotalDue = new Money(0m, "KWD");
        await fixture.Db.SaveChangesAsync();

        var quote = await fixture.Service.GetQuoteAsync(fixture.SubscriptionId, fixture.ManagerId, UserType.Customer);

        Assert.True(quote.IsSuccess, quote.IsFailure ? quote.Error.Code : null);
        Assert.Equal(1500m, quote.Value.RefundAmount);
        Assert.Contains(quote.Value.Beneficiaries, x => x.ConfirmedPaidAmount == 600m && x.PayerCustomerUserId == fixture.UpgradePayerId);
    }

    [Theory]
    [InlineData(true, false, 72, SubscriptionStatus.Paid, false, false)]
    [InlineData(false, false, 0, SubscriptionStatus.PendingPayment, false, false)]
    [InlineData(true, true, 0, SubscriptionStatus.Scheduled, false, false)]
    [InlineData(true, false, 0, SubscriptionStatus.PendingPayment, false, false)]
    [InlineData(true, false, 0, SubscriptionStatus.Active, false, false)]
    [InlineData(true, false, 0, SubscriptionStatus.Completed, false, false)]
    [InlineData(true, false, 0, SubscriptionStatus.Expired, false, false)]
    [InlineData(true, false, 0, SubscriptionStatus.Paid, true, false)]
    [InlineData(true, false, 72, SubscriptionStatus.Paid, false, true)]
    public async Task No_calendar_quote_uses_confirmed_net_and_requires_actual_payer(bool paid, bool unknownPayer, decimal expected, SubscriptionStatus subscriptionStatus, bool fulfillmentCompleted, bool fulfillmentAttempted)
    {
        using var db = new AppDbContext(new DbContextOptionsBuilder<AppDbContext>().UseInMemoryDatabase(Guid.NewGuid().ToString()).Options,
            Substitute.For<IDomainEventDispatcher>());
        var manager = Guid.NewGuid();
        var group = new FamilyGroup { Name = "Family", ManagerCustomerUserId = manager, Status = FamilyGroupStatus.Active };
        var member = new FamilyMember { FamilyGroupId = group.Id, DisplayName = "Member", RelationshipCode = "child", NutritionalGoalCode = "balance" };
        var sub = new Subscription { CustomerUserId = Guid.NewGuid(), FamilyGroupId = group.Id, FamilyMemberId = member.Id,
            Status = subscriptionStatus, DurationDays = 26,
            ProgramId = Guid.NewGuid(), BundleId = Guid.NewGuid(), StartDate = new DateOnly(2026, 11, 1), EndDate = new DateOnly(2026, 11, 26),
            PaidAmount = new Money(paid ? 80m : 100m, "KWD"),
            FulfillmentCompletedAtUtc = fulfillmentCompleted ? new DateTime(2026, 10, 8) : null,
            FulfillmentAttemptCount = fulfillmentAttempted ? 1 : 0,
            LastFulfillmentAttemptUtc = fulfillmentAttempted ? new DateTime(2026, 10, 8) : null,
            LastFulfillmentErrorCode = fulfillmentAttempted ? "fulfillment.transient_calendar_error" : null };
        member.MemberSubscriptionId = sub.Id;
        var batch = new FamilyCheckoutBatch { FamilyGroupId = group.Id, PayerCustomerUserId = unknownPayer ? Guid.Empty : manager,
            IdempotencyKey = "purchase", Status = paid ? FamilyCheckoutBatchStatus.Paid : FamilyCheckoutBatchStatus.PaymentPending,
            Subtotal = 100m, FinalPrice = 80m, FamilyDiscountAmount = 20m, GatewayPaymentId = "purchase-payment", GatewayInvoiceId = "purchase-invoice", PaidAtUtc = paid ? new DateTime(2026, 10, 1) : null };
        batch.Lines.Add(new FamilyCheckoutBatchLine { FamilyMemberId = member.Id, SubscriptionId = sub.Id,
            PriceBeforeFamilyDiscount = 100m, FamilyDiscountAllocated = 20m, NetPaidAmount = 80m, DurationDays = 26 });
        if (paid) {
            batch.PaymentAttempts.Add(new FamilyCheckoutPaymentAttempt { AttemptKey = "purchase", GatewayPaymentId = batch.GatewayPaymentId, GatewayInvoiceId = batch.GatewayInvoiceId, VerifiedAtUtc = new DateTime(2026, 10, 1), ResultCode = "Paid" });
            db.PaymentWebhookReceipts.Add(new PaymentWebhookReceipt { Gateway = "Payzah", EventKey = "purchase-payment", PaymentId = batch.GatewayPaymentId, InvoiceId = batch.GatewayInvoiceId, Amount = 80m, CurrencyCode = "KWD", CustomerReference = group.Id.ToString(), Status = fulfillmentCompleted ? "FulfillmentCompleted" : fulfillmentAttempted ? "FulfillmentRetryPending" : "PaymentVerified", VerifiedAtUtc = new DateTime(2026, 10, 1), FulfillmentCompletedAtUtc = fulfillmentCompleted ? new DateTime(2026, 10, 8) : null, FulfillmentAttemptCount = fulfillmentAttempted ? 1 : 0, LastFulfillmentAttemptUtc = fulfillmentAttempted ? new DateTime(2026, 10, 8) : null, LastFulfillmentErrorCode = fulfillmentAttempted ? "fulfillment.transient_calendar_error" : null });
        }
        if (fulfillmentAttempted)
            db.SubscriptionOperations.Add(new SubscriptionOperation { SubscriptionId = sub.Id, OperationType = "FULFILLMENT_RETRY_SCHEDULED", PreviousStatus = SubscriptionStatus.Paid, NewStatus = SubscriptionStatus.Paid, TimestampUtc = new DateTime(2026, 10, 8) });
        db.FamilyGroups.Add(group); db.FamilyMembers.Add(member); db.Subscriptions.Add(sub); db.FamilyCheckoutBatches.Add(batch);
        await db.SaveChangesAsync();
        var clock = Substitute.For<IDateTimeProvider>(); clock.TodayUtc.Returns(new DateOnly(2026, 10, 9)); clock.UtcNow.Returns(new DateTime(2026, 10, 9));
        var result = await new SubscriptionCancellationService(db, clock, familySettlement: new FamilyCancellationSettlement(db, clock,
            new PostgresApplicationTransactionExecutor(db), new PostgresApplicationLockManager(db),
            new FamilySubscriptionAccessService(db, new FamilyActionPolicy(db)), NullLocalizedMessageProvider.Instance,
            new OperationalPolicyService(db, new MemoryCache(new MemoryCacheOptions()), Substitute.For<IAuditLogger>(), clock))).GetQuoteAsync(sub.Id, manager, UserType.Customer);
        if (unknownPayer) { Assert.True(result.IsFailure); Assert.Equal("family.financial_review_required", result.Error.Code); }
        else if (paid && (subscriptionStatus != SubscriptionStatus.Paid || fulfillmentCompleted))
        {
            Assert.True(result.IsFailure);
            Assert.Equal("family.financial_review_required", result.Error.Code);
        }
        else { Assert.True(result.IsSuccess); Assert.Equal(expected, result.Value.NetRefundAmount); }
    }

    private sealed record PaidUpgradeFixture(AppDbContext Db, SubscriptionCancellationService Service,
        Guid ManagerId, Guid UpgradePayerId, Guid SubscriptionId) : IAsyncDisposable
    {
        public ValueTask DisposeAsync() => Db.DisposeAsync();
    }

    private static async Task<PaidUpgradeFixture> SeedPaidFamilyUpgradeAsync(bool malformedUpgradeDates)
    {
        var db = new AppDbContext(new DbContextOptionsBuilder<AppDbContext>().UseInMemoryDatabase(Guid.NewGuid().ToString()).Options,
            Substitute.For<IDomainEventDispatcher>());
        var manager = Guid.NewGuid();
        var upgradePayer = Guid.NewGuid();
        var group = new FamilyGroup { Name = "Family", ManagerCustomerUserId = manager, Status = FamilyGroupStatus.Active };
        var member = new FamilyMember { FamilyGroupId = group.Id, DisplayName = "Member", RelationshipCode = "child", NutritionalGoalCode = "balance" };
        var dates = Enumerable.Range(1, 8).Select(x => new DateOnly(2026, 11, x)).ToList();
        var subscription = new Subscription { CustomerUserId = Guid.NewGuid(), FamilyGroupId = group.Id, FamilyMemberId = member.Id,
            Status = SubscriptionStatus.Active, DurationDays = 6, ProgramId = Guid.NewGuid(), BundleId = Guid.NewGuid(),
            StartDate = dates[0], EndDate = dates[5], PaidAmount = new Money(1200m, "KWD") };
        member.MemberSubscriptionId = subscription.Id;
        var batch = new FamilyCheckoutBatch { FamilyGroupId = group.Id, PayerCustomerUserId = manager, IdempotencyKey = "family-purchase",
            Status = FamilyCheckoutBatchStatus.Paid, Subtotal = 1200m, FinalPrice = 1200m, CurrencyCode = "KWD",
            GatewayPaymentId = "family-payment", GatewayInvoiceId = "family-invoice", PaidAtUtc = new DateTime(2026, 10, 1) };
        batch.Lines.Add(new FamilyCheckoutBatchLine { FamilyMemberId = member.Id, SubscriptionId = subscription.Id, CheckoutSelected = true,
            PriceBeforeFamilyDiscount = 1200m, NetPaidAmount = 1200m, CurrencyCode = "KWD", DurationDays = 6, StartDate = dates[0] });
        batch.PaymentAttempts.Add(new FamilyCheckoutPaymentAttempt { AttemptKey = "family-purchase", GatewayPaymentId = "family-payment",
            GatewayInvoiceId = "family-invoice", VerifiedAtUtc = new DateTime(2026, 10, 1), ResultCode = "Paid" });
        var freeze = new SubscriptionFreezeRequest { SubscriptionId = subscription.Id, FreezeStartDate = dates[2], FreezeEndDate = dates[3],
            FreezeDays = 2, Status = VerificationStatus.Approved, CreatedAtUtc = new DateTime(2026, 10, 2) };
        var outcome = new FreezeAppliedOutcome([dates[2], dates[3]], [dates[6], dates[7]], dates[7]);
        var fundedDates = dates.Take(6).ToList();
        if (malformedUpgradeDates) fundedDates.Add(fundedDates[1]);
        var upgrade = new SubscriptionTierUpgrade { SubscriptionId = subscription.Id, FromTier = RestaurantTier.Basic, ToTier = RestaurantTier.Platinum,
            PriceDifference = new Money(600m, "KWD"), QuotedTotalDue = new Money(600m, "KWD"), QuotedRemainingDays = 6,
            Status = VerificationStatus.Approved, PaymentId = "upgrade-payment", PayerCustomerUserId = upgradePayer,
            VerifiedPaidAmount = 600m, VerifiedCurrencyCode = "KWD", VerifiedInvoiceId = "upgrade-invoice",
            VerifiedPaidAtUtc = new DateTime(2026, 10, 3), EffectiveServiceDate = fundedDates[0],
            EffectiveServiceDatesJson = JsonSerializer.Serialize(fundedDates) };

        db.FamilyGroups.Add(group); db.FamilyMembers.Add(member); db.Subscriptions.Add(subscription); db.FamilyCheckoutBatches.Add(batch);
        db.SubscriptionFreezeRequests.Add(freeze); db.SystemSettings.Add(new SystemSetting { Key = FreezeAppliedOutcome.Prefix + freeze.Id,
            ValueJson = JsonSerializer.Serialize(outcome), CreatedAtUtc = new DateTime(2026, 10, 2) });
        db.SubscriptionTierUpgrades.Add(upgrade);
        db.PaymentWebhookReceipts.AddRange(
            new PaymentWebhookReceipt { Gateway = "Payzah", EventKey = "family-payment", PaymentId = "family-payment", SubscriptionId = subscription.Id,
                InvoiceId = "family-invoice", Amount = 1200m, CurrencyCode = "KWD", CustomerReference = group.Id.ToString(),
                Status = "PaymentVerified", VerifiedAtUtc = new DateTime(2026, 10, 1) },
            new PaymentWebhookReceipt { Gateway = "Payzah", EventKey = "upgrade-payment", PaymentId = "upgrade-payment", SubscriptionId = subscription.Id,
                InvoiceId = "upgrade-invoice", Amount = 600m, CurrencyCode = "KWD", CustomerReference = subscription.Id.ToString(),
                Status = "PaymentVerified", VerifiedAtUtc = new DateTime(2026, 10, 3) });
        for (var i = 0; i < dates.Count; i++)
            db.SubscriptionDays.Add(new SubscriptionDay { SubscriptionId = subscription.Id, ServiceDate = dates[i],
                Status = i == 0 ? SubscriptionDayStatus.Delivered : i is 2 or 3 ? SubscriptionDayStatus.Frozen : SubscriptionDayStatus.Open,
                SelectionDeadlineUtc = new DateTime(2026, 10, 30) });
        await db.SaveChangesAsync();

        var clock = Substitute.For<IDateTimeProvider>(); clock.TodayUtc.Returns(new DateOnly(2026, 10, 9));
        clock.UtcNow.Returns(new DateTime(2026, 10, 9, 9, 0, 0, DateTimeKind.Utc));
        var settlement = new FamilyCancellationSettlement(db, clock, new PostgresApplicationTransactionExecutor(db),
            new PostgresApplicationLockManager(db), new FamilySubscriptionAccessService(db, new FamilyActionPolicy(db)),
            NullLocalizedMessageProvider.Instance, new OperationalPolicyService(db, new MemoryCache(new MemoryCacheOptions()),
                Substitute.For<IAuditLogger>(), clock));
        return new PaidUpgradeFixture(db, new SubscriptionCancellationService(db, clock, familySettlement: settlement),
            manager, upgradePayer, subscription.Id);
    }
}
