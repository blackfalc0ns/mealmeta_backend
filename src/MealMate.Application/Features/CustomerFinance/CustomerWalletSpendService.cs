using MealMate.Application.Common.Abstractions;
using MealMate.Domain.CustomerFinance;
using MealMate.Domain.Enums;
using MealMate.Domain.ValueObjects;
using Microsoft.EntityFrameworkCore;

namespace MealMate.Application.Features.CustomerFinance;

public interface IWalletSpendingService
{
    Task CreateCheckoutHoldAsync(Guid subscriptionId, Guid customerUserId, decimal amount, string currencyCode, CancellationToken ct = default);
    Task ReleaseCheckoutHoldAsync(Guid subscriptionId, Guid customerUserId, CancellationToken ct = default);
    Task ConvertHoldToDebitAsync(Guid subscriptionId, Guid customerUserId, CancellationToken ct = default);
}

/// <summary>
/// Promotional-wallet checkout ledger (spec F6): the checkout writes a Hold row and never
/// touches the live balance; a verified payment converts the Hold into a Debit row and reduces
/// the balance; a failed or expired payment writes a Release row (balance stays untouched).
/// Concurrent spends are serialized on the customer wallet advisory lock.
/// </summary>
public sealed class WalletSpendingService(IAppDbContext db, IDateTimeProvider clock, IApplicationLockManager? locks = null)
    : IWalletSpendingService
{
    private const string ReferenceType = "SubscriptionCheckout";

    public async Task CreateCheckoutHoldAsync(Guid subscriptionId, Guid customerUserId, decimal amount, string currencyCode, CancellationToken ct = default)
    {
        if (amount <= 0m)
            return;
        if (locks is not null)
            await locks.LockCustomerWalletAsync(customerUserId, ct);
        var wallet = await db.CustomerPromotionalWallets.FirstOrDefaultAsync(x => x.CustomerUserId == customerUserId, ct);
        if (wallet is null)
            return;
        var currentBalance = wallet.Balance?.Amount ?? 0m;
        db.WalletTransactions.Add(new WalletTransaction
        {
            WalletId = wallet.Id,
            WalletType = WalletType.Promotional,
            CustomerUserId = customerUserId,
            Type = WalletTransactionType.Hold,
            Amount = new Money(amount, currencyCode),
            BalanceAfter = new Money(currentBalance, currencyCode),
            ReferenceType = ReferenceType,
            ReferenceId = subscriptionId,
            OccurredAtUtc = clock.UtcNow,
            Description = "حجز رصيد المحفظة الترويجية / Promotional wallet hold",
        });
    }

    public async Task ReleaseCheckoutHoldAsync(Guid subscriptionId, Guid customerUserId, CancellationToken ct = default)
    {
        if (locks is not null)
            await locks.LockCustomerWalletAsync(customerUserId, ct);
        var wallet = await db.CustomerPromotionalWallets.FirstOrDefaultAsync(x => x.CustomerUserId == customerUserId, ct);
        if (wallet is null)
            return;
        var holds = await db.WalletTransactions
            .Where(x => x.ReferenceId == subscriptionId && x.ReferenceType == ReferenceType
                && x.CustomerUserId == customerUserId && x.Type == WalletTransactionType.Hold)
            .ToListAsync(ct);
        foreach (var hold in holds)
        {
            db.WalletTransactions.Add(new WalletTransaction
            {
                WalletId = wallet.Id,
                WalletType = WalletType.Promotional,
                CustomerUserId = customerUserId,
                Type = WalletTransactionType.Release,
                Amount = new Money(hold.Amount.Amount, hold.Amount.CurrencyCode),
                BalanceAfter = new Money(hold.BalanceAfter.Amount, hold.BalanceAfter.CurrencyCode),
                ReferenceType = ReferenceType,
                ReferenceId = subscriptionId,
                CorrelationId = hold.CorrelationId,
                OccurredAtUtc = clock.UtcNow,
                Description = "الإفراج عن رصيد محفوظ في المحفظة الترويجية / Promotional wallet hold released",
            });
        }
    }

    public async Task ConvertHoldToDebitAsync(Guid subscriptionId, Guid customerUserId, CancellationToken ct = default)
    {
        if (locks is not null)
            await locks.LockCustomerWalletAsync(customerUserId, ct);
        var wallet = await db.CustomerPromotionalWallets.FirstOrDefaultAsync(x => x.CustomerUserId == customerUserId, ct);
        if (wallet is null)
            return;
        var holds = await db.WalletTransactions
            .Where(x => x.ReferenceId == subscriptionId && x.ReferenceType == ReferenceType
                && x.CustomerUserId == customerUserId && x.Type == WalletTransactionType.Hold)
            .ToListAsync(ct);
        if (holds.Count == 0)
            return;
        var currencyCode = wallet.Balance?.CurrencyCode ?? holds[0].Amount.CurrencyCode;
        var balance = wallet.Balance?.Amount ?? 0m;
        foreach (var hold in holds)
        {
            balance = Math.Max(0m, balance - hold.Amount.Amount);
            hold.Type = WalletTransactionType.Debit;
            var debitBalanceAfter = new Money(balance, currencyCode);
            hold.BalanceAfter = debitBalanceAfter;
            hold.Description = "خصم رصيد المحفظة الترويجية / Promotional wallet debit";
        }
        var newBalance = balance;
        wallet.Balance = new Money(newBalance, currencyCode);
        wallet.ModifiedAtUtc = clock.UtcNow;
    }
}
