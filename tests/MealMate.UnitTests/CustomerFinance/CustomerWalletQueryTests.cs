using FluentAssertions;
using MealMate.Application.Common.Abstractions;
using MealMate.Application.Features.CustomerFinance;
using MealMate.Domain.CustomerFinance;
using MealMate.Domain.Enums;
using MealMate.Domain.ValueObjects;
using MealMate.Persistence;
using Microsoft.EntityFrameworkCore;
using NSubstitute;
using Xunit;

namespace MealMate.UnitTests.CustomerFinance;

public class CustomerWalletQueryTests
{
    private static (AppDbContext Db, ICurrentUser CurrentUser) Create(Guid customerUserId)
    {
        var db = new AppDbContext(
            new DbContextOptionsBuilder<AppDbContext>().UseInMemoryDatabase(Guid.NewGuid().ToString()).Options,
            Substitute.For<IDomainEventDispatcher>());
        var currentUser = Substitute.For<ICurrentUser>();
        currentUser.IsAuthenticated.Returns(true);
        currentUser.UserId.Returns(customerUserId);
        return (db, currentUser);
    }

    private static CustomerCashWallet CashWallet(Guid customerUserId, decimal balance, DateTime modifiedAtUtc)
        => new()
        {
            CustomerUserId = customerUserId,
            Balance = new Money(balance, "KWD"),
            IsActive = true,
            CreatedAtUtc = modifiedAtUtc,
            ModifiedAtUtc = modifiedAtUtc,
        };

    private static CustomerPromotionalWallet PromotionalWallet(Guid customerUserId, decimal balance, DateTime modifiedAtUtc)
        => new()
        {
            CustomerUserId = customerUserId,
            Balance = new Money(balance, "KWD"),
            IsActive = true,
            CreatedAtUtc = modifiedAtUtc,
            ModifiedAtUtc = modifiedAtUtc,
        };

    private static WalletTransaction Transaction(Guid customerUserId, WalletType walletType, DateTime occurredAtUtc,
        decimal amount = 1.5m, string? referenceType = "ReferralReward")
        => new()
        {
            WalletId = Guid.NewGuid(),
            WalletType = walletType,
            CustomerUserId = customerUserId,
            Type = WalletTransactionType.Credit,
            Amount = new Money(amount, "KWD"),
            BalanceAfter = new Money(amount, "KWD"),
            ReferenceType = referenceType,
            ReferenceId = Guid.NewGuid(),
            OccurredAtUtc = occurredAtUtc,
            Description = "desc",
        };

    [Fact]
    public async Task Summary_WithRealWallets_ReturnsBalancesWithoutCreatingRows()
    {
        var customerUserId = Guid.NewGuid();
        var (db, currentUser) = Create(customerUserId);
        db.CustomerCashWallets.Add(CashWallet(customerUserId, 2.5m, new DateTime(2026, 10, 10, 0, 0, 0, DateTimeKind.Utc)));
        db.CustomerPromotionalWallets.Add(PromotionalWallet(customerUserId, 3.138m, new DateTime(2026, 10, 12, 0, 0, 0, DateTimeKind.Utc)));
        db.CustomerPromotionalWallets.Add(PromotionalWallet(Guid.NewGuid(), 99m, DateTime.UtcNow));
        await db.SaveChangesAsync();

        var handler = new GetCustomerWalletSummaryQueryHandler(db, currentUser);
        var summary = await handler.Handle(new GetCustomerWalletSummaryQuery(), CancellationToken.None);

        summary.IsSuccess.Should().BeTrue();
        summary.Value.CashBalanceKwd.Should().Be(2.5m);
        summary.Value.PromotionalBalanceKwd.Should().Be(3.138m);
        summary.Value.CurrencyCode.Should().NotBeNullOrEmpty();
        summary.Value.UpdatedAtUtc.Should().Be(new DateTime(2026, 10, 12, 0, 0, 0, DateTimeKind.Utc));

        db.CustomerCashWallets.Count().Should().Be(1);
        db.CustomerPromotionalWallets.Count().Should().Be(2);
    }

    [Fact]
    public async Task Summary_WithoutWallets_ReturnsZeroes_NoRowsCreated()
    {
        var customerUserId = Guid.NewGuid();
        var (db, currentUser) = Create(customerUserId);
        await db.SaveChangesAsync();

        var handler = new GetCustomerWalletSummaryQueryHandler(db, currentUser);
        var summary = await handler.Handle(new GetCustomerWalletSummaryQuery(), CancellationToken.None);

        summary.Value.CashBalanceKwd.Should().Be(0m);
        summary.Value.PromotionalBalanceKwd.Should().Be(0m);
        db.CustomerCashWallets.Count().Should().Be(0);
        db.CustomerPromotionalWallets.Count().Should().Be(0);
    }

    [Fact]
    public async Task Transactions_PagedDescending_FilteredByWalletType()
    {
        var customerUserId = Guid.NewGuid();
        var (db, currentUser) = Create(customerUserId);
        db.WalletTransactions.Add(Transaction(customerUserId, WalletType.Promotional, new DateTime(2026, 10, 1, 0, 0, 0, DateTimeKind.Utc)));
        db.WalletTransactions.Add(Transaction(customerUserId, WalletType.Cash, new DateTime(2026, 10, 2, 0, 0, 0, DateTimeKind.Utc)));
        db.WalletTransactions.Add(Transaction(customerUserId, WalletType.Promotional, new DateTime(2026, 10, 3, 0, 0, 0, DateTimeKind.Utc)));
        db.WalletTransactions.Add(Transaction(customerUserId, WalletType.Promotional, new DateTime(2026, 10, 4, 0, 0, 0, DateTimeKind.Utc)));
        db.WalletTransactions.Add(Transaction(Guid.NewGuid(), WalletType.Promotional, new DateTime(2026, 10, 5, 0, 0, 0, DateTimeKind.Utc)));
        await db.SaveChangesAsync();

        var handler = new GetCustomerWalletTransactionsQueryHandler(db, currentUser);
        var page = await handler.Handle(new GetCustomerWalletTransactionsQuery("promotional", Page: 1, PageSize: 2), CancellationToken.None);

        page.IsSuccess.Should().BeTrue();
        page.Value.TotalCount.Should().Be(3);
        page.Value.Transactions.Select(t => t.Id)
            .Should().Equal(db.WalletTransactions.Local
                .Where(t => t.CustomerUserId == customerUserId && t.WalletType == WalletType.Promotional)
                .OrderByDescending(t => t.OccurredAtUtc)
                .Take(2).Select(t => t.Id));
    }

    [Fact]
    public async Task Transactions_EmptyWallet_ReturnsEmptyList()
    {
        var customerUserId = Guid.NewGuid();
        var (db, currentUser) = Create(customerUserId);
        await db.SaveChangesAsync();

        var handler = new GetCustomerWalletTransactionsQueryHandler(db, currentUser);
        var page = await handler.Handle(new GetCustomerWalletTransactionsQuery("cash"), CancellationToken.None);

        page.Value.TotalCount.Should().Be(0);
        page.Value.Transactions.Should().BeEmpty();
    }

    [Fact]
    public async Task Transactions_UnknownWalletType_IsRejected()
    {
        var customerUserId = Guid.NewGuid();
        var (db, currentUser) = Create(customerUserId);
        await db.SaveChangesAsync();

        var handler = new GetCustomerWalletTransactionsQueryHandler(db, currentUser);
        var page = await handler.Handle(new GetCustomerWalletTransactionsQuery("savings"), CancellationToken.None);

        page.IsFailure.Should().BeTrue();
    }
}
