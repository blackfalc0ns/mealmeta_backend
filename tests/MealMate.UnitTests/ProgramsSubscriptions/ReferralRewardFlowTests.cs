using FluentAssertions;
using MealMate.Application.Common.Abstractions;
using MealMate.Application.Features.CustomerCheckout;
using MealMate.Application.Features.ProgramsSubscriptions.Referrals;
using MealMate.Domain.CustomerFinance;
using MealMate.Domain.Enums;
using MealMate.Domain.ProgramsSubscriptions;
using MealMate.Domain.ValueObjects;
using MealMate.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using NSubstitute;
using Xunit;

namespace MealMate.UnitTests.ProgramsSubscriptions;

public class ReferralRewardFlowTests
{
    private (AppDbContext Db, CustomerReferralRewardsService Service) Create()
    {
        var db = new AppDbContext(
            new DbContextOptionsBuilder<AppDbContext>().UseInMemoryDatabase(Guid.NewGuid().ToString()).Options,
            Substitute.For<IDomainEventDispatcher>());
        var now = DateTime.UtcNow;
        var clock = Substitute.For<IDateTimeProvider>();
        clock.UtcNow.Returns(now);
        return (db, new CustomerReferralRewardsService(db, clock, Substitute.For<IApplicationLockManager>()));
    }

    private static Subscription PaidSubscription(Guid customerUserId, decimal paid)
        => new()
        {
            CustomerUserId = customerUserId,
            BundleId = Guid.NewGuid(),
            Status = SubscriptionStatus.Paid,
            DurationDays = 26,
            StartDate = DateOnly.FromDateTime(DateTime.UtcNow.Date),
            EndDate = DateOnly.FromDateTime(DateTime.UtcNow.Date).AddDays(25),
            PaidAmount = new Money(paid, "KWD"),
        };

    [Fact]
    public async Task PaidReferralCheckout_CreditsReferrerOnce_EnrichesAttributionAndLog()
    {
        var (db, service) = Create();
        var owner = Guid.NewGuid();
        var customer = Guid.NewGuid();
        var code = new ReferralCode
        {
            Code = "REF-PAID",
            OwnerUserId = owner,
            RewardPercent = Percentage.FromFraction(0.1m),
        };
        var subscription = PaidSubscription(customer, 31.375m);
        db.ReferralCodes.Add(code);
        db.Subscriptions.Add(subscription);
        await db.SaveChangesAsync();
        var subscriptionId = subscription.Id;
        db.ReferralAttributions.Add(new ReferralAttribution
        {
            Code = "REF-PAID",
            ReferrerUserId = owner,
            ReferredCustomerUserId = customer,
            AttributedAtUtc = DateTime.UtcNow,
            ReferralCodeId = code.Id,
            RewardPercentSnapshot = 0.1m,
            RewardSubscriptionId = subscriptionId,
        });
        db.SubscriptionSnapshots.Add(new SubscriptionSnapshot
        {
            SubscriptionId = subscriptionId,
            ROfD = Percentage.FromFraction(1m),
            SnapshotJson = "{\"CouponDiscountKwd\":3.137}",
            CapturedAtUtc = DateTime.UtcNow,
        });
        await db.SaveChangesAsync();

        await service.CompleteForPaidSubscriptionAsync(subscription);
        await db.SaveChangesAsync();
        await service.CompleteForPaidSubscriptionAsync(subscription);
        await db.SaveChangesAsync();

        var attribution = await db.ReferralAttributions.IgnoreQueryFilters().SingleAsync();
        attribution.RewardedAtUtc.Should().NotBeNull();
        attribution.RewardPercentSnapshot.Should().Be(0.1m);

        var wallet = await db.CustomerPromotionalWallets.SingleAsync(x => x.CustomerUserId == owner);
        wallet.Balance.Amount.Should().Be(3.138m);

        var tx = await db.WalletTransactions.SingleAsync();
        tx.Type.Should().Be(WalletTransactionType.Credit);
        tx.WalletType.Should().Be(WalletType.Promotional);
        tx.CustomerUserId.Should().Be(owner);
        tx.ReferenceType.Should().Be("ReferralReward");
        tx.ReferenceId.Should().Be(subscriptionId);
        tx.Amount.Amount.Should().Be(3.138m);
        tx.BalanceAfter.Amount.Should().Be(3.138m);

        var log = await db.DiscountsAndReferralLogs.SingleAsync(x => x.SubscriptionId == subscriptionId);
        log.DiscountType.Should().Be("ReferralReward");
        log.Code.Should().Be("REF-PAID");
        log.Amount.Amount.Should().Be(3.137m);

        db.WalletTransactions.Count(x => x.ReferenceType == "ReferralReward").Should().Be(1);
    }

    [Fact]
    public async Task EarlyAttribution_CreditedOnFirstPaidSubscription_PolicyResolvedFromCode()
    {
        var (db, service) = Create();
        var owner = Guid.NewGuid();
        var customer = Guid.NewGuid();
        var code = new ReferralCode
        {
            Code = "REF-EARLY",
            OwnerUserId = owner,
            RewardPercent = Percentage.FromFraction(0.15m),
        };
        db.ReferralCodes.Add(code);
        await db.SaveChangesAsync();
        db.ReferralAttributions.Add(new ReferralAttribution
        {
            Code = "REF-EARLY",
            ReferrerUserId = owner,
            ReferredCustomerUserId = customer,
            AttributedAtUtc = DateTime.UtcNow,
            ReferralCodeId = code.Id,
        });
        db.Subscriptions.Add(PaidSubscription(customer, 100m));
        await db.SaveChangesAsync();

        var subscription = await db.Subscriptions.SingleAsync();
        await service.CompleteForPaidSubscriptionAsync(subscription);
        await db.SaveChangesAsync();
        await service.CompleteForPaidSubscriptionAsync(subscription);
        await db.SaveChangesAsync();

        var attribution = await db.ReferralAttributions.SingleAsync();
        attribution.RewardSubscriptionId.Should().Be(subscription.Id);
        attribution.RewardPercentSnapshot.Should().Be(0.15m);
        attribution.RewardedAtUtc.Should().NotBeNull();
        (await db.CustomerPromotionalWallets.SingleAsync(x => x.CustomerUserId == owner)).Balance.Amount.Should().Be(15.000m);
        db.WalletTransactions.Count().Should().Be(1);
    }

    [Fact]
    public async Task NotFirstPaidSubscription_EarlyAttribution_NotCredited()
    {
        var (db, service) = Create();
        var owner = Guid.NewGuid();
        var customer = Guid.NewGuid();
        var code = new ReferralCode
        {
            Code = "REF-LATE",
            OwnerUserId = owner,
            RewardPercent = Percentage.FromFraction(0.1m),
        };
        db.ReferralCodes.Add(code);
        await db.SaveChangesAsync();
        db.ReferralAttributions.Add(new ReferralAttribution
        {
            Code = "REF-LATE",
            ReferrerUserId = owner,
            ReferredCustomerUserId = customer,
            AttributedAtUtc = DateTime.UtcNow,
            ReferralCodeId = code.Id,
        });
        db.Subscriptions.Add(PaidSubscription(customer, 50m));
        db.Subscriptions.Add(new Subscription
        {
            CustomerUserId = customer,
            BundleId = Guid.NewGuid(),
            Status = SubscriptionStatus.Active,
            DurationDays = 26,
            StartDate = DateOnly.FromDateTime(DateTime.UtcNow.Date),
            EndDate = DateOnly.FromDateTime(DateTime.UtcNow.Date).AddDays(25),
            PaidAmount = new Money(40m, "KWD"),
        });
        await db.SaveChangesAsync();

        var earlierSubscription = db.Subscriptions.Local.First(x => x.PaidAmount.Amount == 50m);
        await service.CompleteForPaidSubscriptionAsync(earlierSubscription);

        db.CustomerPromotionalWallets.Should().BeEmpty();
        db.WalletTransactions.Should().BeEmpty();
        (await db.ReferralAttributions.SingleAsync()).RewardedAtUtc.Should().BeNull();
    }

    [Fact]
    public async Task ZeroRewardPolicyMissing_MarksAttribution_WithoutLedgerCredit()
    {
        var (db, service) = Create();
        var owner = Guid.NewGuid();
        var customer = Guid.NewGuid();
        var code = new ReferralCode
        {
            Code = "REF-ZERO",
            OwnerUserId = owner,
        };
        var subscription = PaidSubscription(customer, 20m);
        db.ReferralCodes.Add(code);
        db.Subscriptions.Add(subscription);
        await db.SaveChangesAsync();
        db.ReferralAttributions.Add(new ReferralAttribution
        {
            Code = "REF-ZERO",
            ReferrerUserId = owner,
            ReferredCustomerUserId = customer,
            AttributedAtUtc = DateTime.UtcNow,
            RewardPercentSnapshot = 0m,
            RewardSubscriptionId = subscription.Id,
        });
        await db.SaveChangesAsync();

        await service.CompleteForPaidSubscriptionAsync(subscription);

        (await db.ReferralAttributions.IgnoreQueryFilters().SingleAsync()).RewardedAtUtc.Should().NotBeNull();
        db.CustomerPromotionalWallets.Should().BeEmpty();
        db.WalletTransactions.Should().BeEmpty();
    }

    [Fact]
    public async Task GetReferralSummary_RewardedCount_ComesFromRealWalletLedger()
    {
        var db = new AppDbContext(
            new DbContextOptionsBuilder<AppDbContext>().UseInMemoryDatabase(Guid.NewGuid().ToString()).Options,
            Substitute.For<IDomainEventDispatcher>());
        var owner = Guid.NewGuid();
        db.ReferralCodes.Add(new ReferralCode
        {
            Code = "REF-SUM",
            OwnerUserId = owner,
            RewardPercent = Percentage.FromFraction(0.1m),
        });
        db.ReferralAttributions.Add(new ReferralAttribution
        {
            Code = "REF-SUM",
            ReferrerUserId = owner,
            ReferredCustomerUserId = Guid.NewGuid(),
            AttributedAtUtc = DateTime.UtcNow,
        });
        var wallet = new CustomerPromotionalWallet
        {
            CustomerUserId = owner,
            Balance = new Money(5m, "KWD"),
        };
        db.CustomerPromotionalWallets.Add(wallet);
        await db.SaveChangesAsync();
        db.WalletTransactions.Add(new WalletTransaction
        {
            WalletId = wallet.Id,
            WalletType = WalletType.Promotional,
            CustomerUserId = owner,
            Type = WalletTransactionType.Credit,
            Amount = new Money(3m, "KWD"),
            BalanceAfter = new Money(3m, "KWD"),
            ReferenceType = "ReferralReward",
            ReferenceId = Guid.NewGuid(),
            OccurredAtUtc = DateTime.UtcNow,
        });
        db.WalletTransactions.Add(new WalletTransaction
        {
            WalletId = wallet.Id,
            WalletType = WalletType.Promotional,
            CustomerUserId = owner,
            Type = WalletTransactionType.Credit,
            Amount = new Money(2m, "KWD"),
            BalanceAfter = new Money(5m, "KWD"),
            ReferenceType = "ReferralReward",
            ReferenceId = Guid.NewGuid(),
            OccurredAtUtc = DateTime.UtcNow,
        });
        await db.SaveChangesAsync();

        var currentUser = Substitute.For<ICurrentUser>();
        currentUser.IsAuthenticated.Returns(true);
        currentUser.UserId.Returns(owner);
        var configuration = Substitute.For<IConfiguration>();
        configuration["PublicBaseUrl"].Returns((string?)null);
        var handler = new GetReferralSummaryQueryHandler(db, currentUser, configuration);

        var summary = await handler.Handle(new GetReferralSummaryQuery(), CancellationToken.None);

        summary.Value.RewardedCount.Should().Be(2);
        summary.Value.AttributedCount.Should().Be(1);
        summary.Value.RewardPercent.Should().Be(0.1m);
    }
}
