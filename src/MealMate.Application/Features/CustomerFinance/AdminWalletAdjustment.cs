using MealMate.Application.Common.Abstractions;
using MealMate.Application.Common.Messaging;
using MealMate.Application.Common.Models;
using MealMate.Domain.CustomerFinance;
using MealMate.Domain.Enums;
using MealMate.Domain.ValueObjects;
using Microsoft.EntityFrameworkCore;

namespace MealMate.Application.Features.CustomerFinance;

public sealed record AdjustWalletBalanceCommand(
    Guid CustomerId,
    WalletType WalletType,
    WalletTransactionType TransactionType,
    string Reason,
    decimal Amount) : ICommand<AdjustWalletResult>;

public sealed record AdjustWalletResult(
    Guid WalletId,
    WalletType WalletType,
    decimal PreviousBalance,
    decimal NewBalance,
    string CurrencyCode,
    Guid TransactionId);

public sealed record GetCustomerWalletsQuery(Guid CustomerId) : IQuery<CustomerWalletsAdminDto>;

public sealed record CustomerWalletsAdminDto(
    Guid CustomerId,
    CustomerWalletDetailDto CashWallet,
    CustomerWalletDetailDto PromotionalWallet,
    IReadOnlyList<CustomerWalletTransactionDto> RecentTransactions);

public sealed record CustomerWalletDetailDto(
    Guid? WalletId,
    WalletType WalletType,
    decimal Balance,
    string CurrencyCode,
    bool IsActive,
    DateTime? UpdatedAtUtc);

public sealed class AdjustWalletBalanceCommandHandler(
    IAppDbContext db,
    IAuditLogger audit,
    IDateTimeProvider clock)
    : ICommandHandler<AdjustWalletBalanceCommand, AdjustWalletResult>
{
    private const string CurrencyCode = "KWD";

    public async Task<Result<AdjustWalletResult>> Handle(AdjustWalletBalanceCommand request, CancellationToken cancellationToken)
    {
        if (request.Amount <= 0m)
            return Error.Validation("wallet.amount_invalid", "Adjustment amount must be positive.");

        if (string.IsNullOrWhiteSpace(request.Reason))
            return Error.Validation("wallet.reason_required", "Adjustment reason is required.");

        if (request.TransactionType is not (WalletTransactionType.Credit or WalletTransactionType.Debit))
            return Error.Validation("wallet.transaction_type_invalid", "Transaction type must be Credit or Debit.");

        var customerExists = await db.CustomerProfiles.AsNoTracking().AnyAsync(x => x.UserId == request.CustomerId, cancellationToken);
        if (!customerExists)
            return Error.NotFound("customer.not_found", $"Customer {request.CustomerId} was not found.");

        Guid walletId;
        decimal previousBalance;
        decimal newBalance;
        string entityName;

        if (request.WalletType == WalletType.Cash)
        {
            entityName = nameof(CustomerCashWallet);
            var wallet = await db.CustomerCashWallets.FirstOrDefaultAsync(x => x.CustomerUserId == request.CustomerId, cancellationToken);
            if (wallet is null)
            {
                wallet = new CustomerCashWallet
                {
                    CustomerUserId = request.CustomerId,
                    Balance = new Money(0m, CurrencyCode),
                    IsActive = true,
                    CreatedAtUtc = clock.UtcNow,
                };
                db.CustomerCashWallets.Add(wallet);
            }

            previousBalance = wallet.Balance?.Amount ?? 0m;
            if (request.TransactionType == WalletTransactionType.Debit)
            {
                newBalance = previousBalance - request.Amount;
                if (newBalance < 0m)
                    return Error.Validation("wallet.insufficient_balance", "Debit adjustment would result in a negative balance.");
            }
            else
            {
                newBalance = previousBalance + request.Amount;
            }

            wallet.Balance = new Money(newBalance, CurrencyCode);
            wallet.ModifiedAtUtc = clock.UtcNow;
            walletId = wallet.Id;
        }
        else if (request.WalletType == WalletType.Promotional)
        {
            entityName = nameof(CustomerPromotionalWallet);
            var wallet = await db.CustomerPromotionalWallets.FirstOrDefaultAsync(x => x.CustomerUserId == request.CustomerId, cancellationToken);
            if (wallet is null)
            {
                wallet = new CustomerPromotionalWallet
                {
                    CustomerUserId = request.CustomerId,
                    Balance = new Money(0m, CurrencyCode),
                    IsActive = true,
                    CreatedAtUtc = clock.UtcNow,
                };
                db.CustomerPromotionalWallets.Add(wallet);
            }

            previousBalance = wallet.Balance?.Amount ?? 0m;
            if (request.TransactionType == WalletTransactionType.Debit)
            {
                newBalance = previousBalance - request.Amount;
                if (newBalance < 0m)
                    return Error.Validation("wallet.insufficient_balance", "Debit adjustment would result in a negative balance.");
            }
            else
            {
                newBalance = previousBalance + request.Amount;
            }

            wallet.Balance = new Money(newBalance, CurrencyCode);
            wallet.ModifiedAtUtc = clock.UtcNow;
            walletId = wallet.Id;
        }
        else
        {
            return Error.Validation("wallet.type_invalid", "Wallet type is invalid.");
        }

        var transaction = new WalletTransaction
        {
            WalletId = walletId,
            WalletType = request.WalletType,
            CustomerUserId = request.CustomerId,
            Type = request.TransactionType,
            Amount = new Money(request.Amount, CurrencyCode),
            BalanceAfter = new Money(newBalance, CurrencyCode),
            ReferenceType = "AdminAdjustment",
            OccurredAtUtc = clock.UtcNow,
            Description = request.Reason.Trim(),
        };
        db.WalletTransactions.Add(transaction);

        await audit.LogAsync(new AuditEntry(
            Action: "WalletAdjustment",
            EntityType: entityName,
            EntityId: walletId,
            Before: previousBalance,
            After: newBalance,
            Reason: request.Reason.Trim(),
            SourceChannel: "admin-dashboard"), cancellationToken);

        await db.SaveChangesAsync(cancellationToken);

        return Result.Success(new AdjustWalletResult(
            walletId,
            request.WalletType,
            previousBalance,
            newBalance,
            CurrencyCode,
            transaction.Id));
    }
}

public sealed class GetCustomerWalletsQueryHandler(IAppDbContext db)
    : IQueryHandler<GetCustomerWalletsQuery, CustomerWalletsAdminDto>
{
    private const string DefaultCurrency = "KWD";

    public async Task<Result<CustomerWalletsAdminDto>> Handle(GetCustomerWalletsQuery request, CancellationToken cancellationToken)
    {
        var customerExists = await db.CustomerProfiles.AsNoTracking().AnyAsync(x => x.UserId == request.CustomerId, cancellationToken);
        if (!customerExists)
            return Error.NotFound("customer.not_found", $"Customer {request.CustomerId} was not found.");

        var cash = await db.CustomerCashWallets.AsNoTracking()
            .FirstOrDefaultAsync(x => x.CustomerUserId == request.CustomerId, cancellationToken);
        var promotional = await db.CustomerPromotionalWallets.AsNoTracking()
            .FirstOrDefaultAsync(x => x.CustomerUserId == request.CustomerId, cancellationToken);

        var cashDetail = new CustomerWalletDetailDto(
            cash?.Id,
            WalletType.Cash,
            cash?.Balance?.Amount ?? 0m,
            cash?.Balance?.CurrencyCode ?? DefaultCurrency,
            cash?.IsActive ?? true,
            cash?.ModifiedAtUtc ?? cash?.CreatedAtUtc);

        var promoDetail = new CustomerWalletDetailDto(
            promotional?.Id,
            WalletType.Promotional,
            promotional?.Balance?.Amount ?? 0m,
            promotional?.Balance?.CurrencyCode ?? DefaultCurrency,
            promotional?.IsActive ?? true,
            promotional?.ModifiedAtUtc ?? promotional?.CreatedAtUtc);

        var transactions = await db.WalletTransactions.AsNoTracking()
            .Where(x => x.CustomerUserId == request.CustomerId)
            .OrderByDescending(x => x.OccurredAtUtc)
            .Take(50)
            .Select(x => new CustomerWalletTransactionDto(
                x.Id,
                x.WalletType.ToString().ToLowerInvariant(),
                x.Type.ToString().ToLowerInvariant(),
                x.Amount.Amount,
                x.BalanceAfter.Amount,
                x.ReferenceType,
                x.ReferenceId,
                x.OccurredAtUtc,
                x.Description))
            .ToListAsync(cancellationToken);

        return Result.Success(new CustomerWalletsAdminDto(
            request.CustomerId,
            cashDetail,
            promoDetail,
            transactions));
    }
}
