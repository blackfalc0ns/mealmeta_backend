using FluentAssertions;
using MealMate.Application.Common.Abstractions;
using MealMate.Application.Common.Security;
using MealMate.Application.Features.CustomerFinance;
using MealMate.Domain.CustomerFinance;
using MealMate.Domain.Enums;
using MealMate.Domain.IdentityAccess;
using MealMate.Domain.ValueObjects;
using MealMate.Persistence;
using Microsoft.EntityFrameworkCore;
using NSubstitute;
using Xunit;

namespace MealMate.UnitTests.CustomerFinance;

public class WalletAdjustmentFlowTests
{
    private static DbContextOptions<AppDbContext> CreateInMemoryOptions() =>
        new DbContextOptionsBuilder<AppDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString())
            .Options;

    private static (AppDbContext Db, IAuditLogger Audit, IDateTimeProvider Clock) Create()
    {
        var db = new AppDbContext(CreateInMemoryOptions(), Substitute.For<IDomainEventDispatcher>());
        var audit = Substitute.For<IAuditLogger>();
        var clock = Substitute.For<IDateTimeProvider>();
        clock.UtcNow.Returns(DateTime.UtcNow);
        return (db, audit, clock);
    }

    [Fact]
    public async Task Credit_IncreasesBalance_AndCreatesTransaction_AndAudits()
    {
        var (db, audit, clock) = Create();
        var customerId = Guid.NewGuid();
        db.CustomerProfiles.Add(new CustomerProfile { UserId = customerId });
        db.CustomerCashWallets.Add(new CustomerCashWallet
        {
            CustomerUserId = customerId,
            Balance = new Money(10m, "KWD"),
            IsActive = true,
        });
        await db.SaveChangesAsync();

        var handler = new AdjustWalletBalanceCommandHandler(db, audit, clock);
        var result = await handler.Handle(new AdjustWalletBalanceCommand(
            customerId,
            WalletType.Cash,
            WalletTransactionType.Credit,
            "Goodwill credit for delivery issue",
            5m), CancellationToken.None);

        result.IsSuccess.Should().BeTrue();
        result.Value.PreviousBalance.Should().Be(10m);
        result.Value.NewBalance.Should().Be(15m);
        result.Value.CurrencyCode.Should().Be("KWD");

        var wallet = await db.CustomerCashWallets.SingleAsync(x => x.CustomerUserId == customerId);
        wallet.Balance.Amount.Should().Be(15m);

        var tx = await db.WalletTransactions.SingleAsync(x => x.CustomerUserId == customerId);
        tx.Type.Should().Be(WalletTransactionType.Credit);
        tx.Amount.Amount.Should().Be(5m);
        tx.BalanceAfter.Amount.Should().Be(15m);
        tx.Description.Should().Be("Goodwill credit for delivery issue");

        await audit.Received(1).LogAsync(
            Arg.Is<AuditEntry>(e =>
                e.Action == "WalletAdjustment" &&
                e.SourceChannel == "admin-dashboard" &&
                (decimal)e.Before! == 10m &&
                (decimal)e.After! == 15m),
            Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task Debit_ReducesBalance_WhenSufficient()
    {
        var (db, audit, clock) = Create();
        var customerId = Guid.NewGuid();
        db.CustomerProfiles.Add(new CustomerProfile { UserId = customerId });
        db.CustomerCashWallets.Add(new CustomerCashWallet
        {
            CustomerUserId = customerId,
            Balance = new Money(20m, "KWD"),
            IsActive = true,
        });
        await db.SaveChangesAsync();

        var handler = new AdjustWalletBalanceCommandHandler(db, audit, clock);
        var result = await handler.Handle(new AdjustWalletBalanceCommand(
            customerId,
            WalletType.Cash,
            WalletTransactionType.Debit,
            "Correction of excess credit",
            7.5m), CancellationToken.None);

        result.IsSuccess.Should().BeTrue();
        result.Value.PreviousBalance.Should().Be(20m);
        result.Value.NewBalance.Should().Be(12.5m);

        var wallet = await db.CustomerCashWallets.SingleAsync(x => x.CustomerUserId == customerId);
        wallet.Balance.Amount.Should().Be(12.5m);

        var tx = await db.WalletTransactions.SingleAsync(x => x.CustomerUserId == customerId);
        tx.Type.Should().Be(WalletTransactionType.Debit);
        tx.Amount.Amount.Should().Be(7.5m);
        tx.BalanceAfter.Amount.Should().Be(12.5m);
    }

    [Fact]
    public async Task Debit_Fails_WhenInsufficientBalance()
    {
        var (db, audit, clock) = Create();
        var customerId = Guid.NewGuid();
        db.CustomerProfiles.Add(new CustomerProfile { UserId = customerId });
        db.CustomerCashWallets.Add(new CustomerCashWallet
        {
            CustomerUserId = customerId,
            Balance = new Money(5m, "KWD"),
            IsActive = true,
        });
        await db.SaveChangesAsync();

        var handler = new AdjustWalletBalanceCommandHandler(db, audit, clock);
        var result = await handler.Handle(new AdjustWalletBalanceCommand(
            customerId,
            WalletType.Cash,
            WalletTransactionType.Debit,
            "Over-deduction attempt",
            10m), CancellationToken.None);

        result.IsFailure.Should().BeTrue();
        result.Error.Code.Should().Be("wallet.insufficient_balance");

        var wallet = await db.CustomerCashWallets.SingleAsync(x => x.CustomerUserId == customerId);
        wallet.Balance.Amount.Should().Be(5m);
        db.WalletTransactions.Should().BeEmpty();
    }

    [Fact]
    public async Task Adjust_Fails_WhenCustomerNotFound()
    {
        var (db, audit, clock) = Create();
        var unknownCustomerId = Guid.NewGuid();

        var handler = new AdjustWalletBalanceCommandHandler(db, audit, clock);
        var result = await handler.Handle(new AdjustWalletBalanceCommand(
            unknownCustomerId,
            WalletType.Cash,
            WalletTransactionType.Credit,
            "Credit unknown customer",
            10m), CancellationToken.None);

        result.IsFailure.Should().BeTrue();
        result.Error.Code.Should().Be("customer.not_found");
    }

    [Fact]
    public async Task GetCustomerWallets_ReturnsWalletsAndTransactions()
    {
        var (db, _, clock) = Create();
        var customerId = Guid.NewGuid();
        db.CustomerProfiles.Add(new CustomerProfile { UserId = customerId });
        db.CustomerCashWallets.Add(new CustomerCashWallet
        {
            CustomerUserId = customerId,
            Balance = new Money(25m, "KWD"),
            IsActive = true,
            ModifiedAtUtc = clock.UtcNow,
        });
        db.CustomerPromotionalWallets.Add(new CustomerPromotionalWallet
        {
            CustomerUserId = customerId,
            Balance = new Money(5m, "KWD"),
            IsActive = true,
            ModifiedAtUtc = clock.UtcNow,
        });
        db.WalletTransactions.Add(new WalletTransaction
        {
            WalletId = Guid.NewGuid(),
            WalletType = WalletType.Cash,
            CustomerUserId = customerId,
            Type = WalletTransactionType.Credit,
            Amount = new Money(25m, "KWD"),
            BalanceAfter = new Money(25m, "KWD"),
            ReferenceType = "AdminAdjustment",
            OccurredAtUtc = clock.UtcNow.AddMinutes(-10),
            Description = "Deposit",
        });
        await db.SaveChangesAsync();

        var handler = new GetCustomerWalletsQueryHandler(db);
        var result = await handler.Handle(new GetCustomerWalletsQuery(customerId), CancellationToken.None);

        result.IsSuccess.Should().BeTrue();
        result.Value.CustomerId.Should().Be(customerId);
        result.Value.CashWallet.Balance.Should().Be(25m);
        result.Value.PromotionalWallet.Balance.Should().Be(5m);
        result.Value.RecentTransactions.Should().HaveCount(1);
        result.Value.RecentTransactions[0].Description.Should().Be("Deposit");
    }
}
