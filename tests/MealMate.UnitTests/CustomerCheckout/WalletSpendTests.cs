using FluentAssertions;
using MealMate.Application.Common.Abstractions;
using MealMate.Application.Features.CustomerCheckout;
using MealMate.Application.Features.CustomerFinance;
using MealMate.Application.Features.Offers.Services;
using MealMate.Application.Features.ProgramsSubscriptions;
using MealMate.Application.Features.ProgramsSubscriptions.Pricing;
using MealMate.Domain.CustomerFinance;
using MealMate.Domain.Enums;
using MealMate.Domain.ValueObjects;
using MealMate.Persistence;
using Microsoft.EntityFrameworkCore;
using NSubstitute;
using Xunit;

namespace MealMate.UnitTests.CustomerCheckout;

public class WalletSpendTests
{
    private (AppDbContext Db, WalletSpendingService Service, IDateTimeProvider Clock) Create()
    {
        var db = new AppDbContext(
            new DbContextOptionsBuilder<AppDbContext>().UseInMemoryDatabase(Guid.NewGuid().ToString()).Options,
            Substitute.For<IDomainEventDispatcher>());
        var now = DateTime.UtcNow;
        var clock = Substitute.For<IDateTimeProvider>();
        clock.UtcNow.Returns(now);
        return (db, new WalletSpendingService(db, clock, Substitute.For<IApplicationLockManager>()), clock);
    }

    private static CustomerPromotionalWallet Wallet(Guid customerUserId, decimal balance)
        => new()
        {
            CustomerUserId = customerUserId,
            Balance = new Money(balance, "KWD"),
            IsActive = true,
        };

    [Fact]
    public async Task Hold_CreatesHoldRow_BalanceUnchanged()
    {
        var (db, service, _) = Create();
        var customer = Guid.NewGuid();
        var subscriptionId = Guid.NewGuid();
        db.CustomerPromotionalWallets.Add(Wallet(customer, 4m));
        await db.SaveChangesAsync();

        await service.CreateCheckoutHoldAsync(subscriptionId, customer, 1.5m, "KWD");
        await db.SaveChangesAsync();

        var tx = await db.WalletTransactions.SingleAsync();
        tx.Type.Should().Be(WalletTransactionType.Hold);
        tx.WalletType.Should().Be(WalletType.Promotional);
        tx.CustomerUserId.Should().Be(customer);
        tx.ReferenceType.Should().Be("SubscriptionCheckout");
        tx.ReferenceId.Should().Be(subscriptionId);
        tx.Amount.Amount.Should().Be(1.5m);
        tx.BalanceAfter.Amount.Should().Be(4m);

        (await db.CustomerPromotionalWallets.SingleAsync(x => x.CustomerUserId == customer)).Balance.Amount.Should().Be(4m);
    }

    [Fact]
    public async Task Hold_WithZeroOrMissingWallet_NoRows()
    {
        var (db, service, _) = Create();
        var customer = Guid.NewGuid();
        await db.SaveChangesAsync();

        await service.CreateCheckoutHoldAsync(Guid.NewGuid(), customer, 0m, "KWD");
        await service.CreateCheckoutHoldAsync(Guid.NewGuid(), customer, 2m, "KWD");

        db.WalletTransactions.Should().BeEmpty();
        db.CustomerPromotionalWallets.Should().BeEmpty();
    }

    [Fact]
    public async Task Release_CreatesReleaseRow_BalanceUnchanged()
    {
        var (db, service, _) = Create();
        var customer = Guid.NewGuid();
        var subscriptionId = Guid.NewGuid();
        db.CustomerPromotionalWallets.Add(Wallet(customer, 4m));
        await db.SaveChangesAsync();
        await service.CreateCheckoutHoldAsync(subscriptionId, customer, 1.5m, "KWD");
        await db.SaveChangesAsync();

        await service.ReleaseCheckoutHoldAsync(subscriptionId, customer);
        await db.SaveChangesAsync();

        var release = await db.WalletTransactions.SingleAsync(x => x.Type == WalletTransactionType.Release);
        release.ReferenceType.Should().Be("SubscriptionCheckout");
        release.ReferenceId.Should().Be(subscriptionId);
        release.Amount.Amount.Should().Be(1.5m);
        (await db.CustomerPromotionalWallets.SingleAsync(x => x.CustomerUserId == customer)).Balance.Amount.Should().Be(4m);
    }

    [Fact]
    public async Task Release_WithoutHoldRows_NoChange()
    {
        var (db, service, _) = Create();
        var customer = Guid.NewGuid();
        db.CustomerPromotionalWallets.Add(Wallet(customer, 4m));
        await db.SaveChangesAsync();

        await service.ReleaseCheckoutHoldAsync(Guid.NewGuid(), customer);
        await db.SaveChangesAsync();

        db.WalletTransactions.Should().BeEmpty();
    }

    [Fact]
    public async Task ConvertHoldToDebit_ReducesBalance_UpdatesHoldRow()
    {
        var (db, service, _) = Create();
        var customer = Guid.NewGuid();
        var subscriptionId = Guid.NewGuid();
        db.CustomerPromotionalWallets.Add(Wallet(customer, 4m));
        await db.SaveChangesAsync();
        await service.CreateCheckoutHoldAsync(subscriptionId, customer, 1.5m, "KWD");
        await db.SaveChangesAsync();

        await service.ConvertHoldToDebitAsync(subscriptionId, customer);
        await db.SaveChangesAsync();

        (await db.CustomerPromotionalWallets.SingleAsync(x => x.CustomerUserId == customer)).Balance.Amount.Should().Be(2.5m);
        var debit = await db.WalletTransactions.SingleAsync(x => x.ReferenceId == subscriptionId);
        debit.Type.Should().Be(WalletTransactionType.Debit);
        debit.BalanceAfter.Amount.Should().Be(2.5m);
        debit.Amount.Amount.Should().Be(1.5m);
    }

    [Fact]
    public async Task ConvertHoldToDebit_IsIdempotent()
    {
        var (db, service, _) = Create();
        var customer = Guid.NewGuid();
        var subscriptionId = Guid.NewGuid();
        db.CustomerPromotionalWallets.Add(Wallet(customer, 4m));
        await db.SaveChangesAsync();
        await service.CreateCheckoutHoldAsync(subscriptionId, customer, 1.5m, "KWD");
        await db.SaveChangesAsync();

        await service.ConvertHoldToDebitAsync(subscriptionId, customer);
        await db.SaveChangesAsync();
        await service.ConvertHoldToDebitAsync(subscriptionId, customer);
        await db.SaveChangesAsync();

        (await db.CustomerPromotionalWallets.SingleAsync(x => x.CustomerUserId == customer)).Balance.Amount.Should().Be(2.5m);
        db.WalletTransactions.Count(x => x.ReferenceId == subscriptionId).Should().Be(1);
    }

    [Fact]
    public async Task PreviewHandler_AppliesPromotionalWalletCredit_WhenCustomerIdSet()
    {
        var db = new AppDbContext(
            new DbContextOptionsBuilder<AppDbContext>().UseInMemoryDatabase(Guid.NewGuid().ToString()).Options,
            Substitute.For<IDomainEventDispatcher>());
        var programId = Guid.NewGuid();
        var bundleId = Guid.NewGuid();
        var restaurantId = Guid.NewGuid();
        db.PricingRuleVersions.Add(new MealMate.Domain.ProgramsSubscriptions.PricingRuleVersion
        {
            IsActive = true,
            RMax = Percentage.FromFraction(0.3m),
            RMin = Percentage.FromFraction(0.05m),
            DShort = 6, DLong = 26, Gamma = 1, RoundingStepKwd = 0.1m,
        });
        db.RestaurantBundlePricings.Add(new MealMate.Domain.ProgramsSubscriptions.RestaurantBundlePricing
        {
            ProgramId = programId,
            BundleId = bundleId,
            RestaurantId = restaurantId,
            ReferencePrice26 = new Money(104m, "KWD"),
            ContractedSupplyPrice26 = new Money(80m, "KWD"),
            LockStatus = PriceLockStatus.Locked,
            EffectiveFromUtc = DateTime.UtcNow.AddDays(-1),
        });
        var customer = Guid.NewGuid();
        db.CustomerPromotionalWallets.Add(Wallet(customer, 5m));
        await db.SaveChangesAsync();

        var clock = Substitute.For<IDateTimeProvider>();
        clock.UtcNow.Returns(DateTime.UtcNow);
        var handler = new PricingAndClassificationHandlers(
            db,
            Substitute.For<MealMate.Application.Common.Abstractions.IAuditLogger>(),
            clock,
            Substitute.For<MealMate.Application.Features.ProgramsSubscriptions.Pricing.IRestaurantTierClassificationService>(),
            offerEngine: Substitute.For<IOfferEligibilityEngine>());
        var result = await handler.Handle(new PreviewCustomerPricingQuery(programId, bundleId, RestaurantTier.Basic, 26,
            CustomerId: customer), CancellationToken.None);

        var noWallet = await handler.Handle(new PreviewCustomerPricingQuery(programId, bundleId, RestaurantTier.Basic, 26), CancellationToken.None);
        result.Value.WalletCreditApplied.Should().Be(5m);
        result.Value.WalletCreditApplied.Should().BeGreaterThan(0m);
        result.Value.FinalCustomerPrice.Should().BeLessThan(noWallet.Value.FinalCustomerPrice);
    }
}
