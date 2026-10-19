using MealMate.Application.Common.Abstractions;
using MealMate.Application.Common.Messaging;
using MealMate.Application.Common.Models;
using MealMate.Domain.CustomerFinance;
using MealMate.Domain.Enums;
using MealMate.Domain.ValueObjects;
using Microsoft.EntityFrameworkCore;

namespace MealMate.Application.Features.CustomerFinance;

public sealed record GetCustomerWalletSummaryQuery : IQuery<CustomerWalletSummaryDto>;

/// <summary>Spec F6: both wallets are reported; missing wallet rows read as zero and never fabricated.</summary>
public sealed record CustomerWalletSummaryDto(
    decimal CashBalanceKwd,
    decimal PromotionalBalanceKwd,
    string CurrencyCode,
    DateTime? UpdatedAtUtc);

public sealed record GetCustomerWalletTransactionsQuery(
    string WalletType,
    int Page = 1,
    int PageSize = 20) : IQuery<CustomerWalletTransactionListDto>;

public sealed record CustomerWalletTransactionListDto(
    int TotalCount,
    int Page,
    int PageSize,
    IReadOnlyList<CustomerWalletTransactionDto> Transactions);

public sealed record CustomerWalletTransactionDto(
    Guid Id,
    string WalletType,
    string Type,
    decimal AmountKwd,
    decimal BalanceAfterKwd,
    string? ReferenceType,
    Guid? ReferenceId,
    DateTime OccurredAtUtc,
    string? Description);

public sealed class GetCustomerWalletSummaryQueryHandler(
    IAppDbContext db,
    ICurrentUser currentUser)
    : IQueryHandler<GetCustomerWalletSummaryQuery, CustomerWalletSummaryDto>
{
    public async Task<Result<CustomerWalletSummaryDto>> Handle(GetCustomerWalletSummaryQuery request, CancellationToken cancellationToken)
    {
        if (!currentUser.IsAuthenticated || !currentUser.UserId.HasValue)
            return Error.Unauthorized("Auth.Unauthorized", "User is not authenticated.");
        var customerUserId = currentUser.UserId.Value;

        var cash = await db.CustomerCashWallets.AsNoTracking()
            .FirstOrDefaultAsync(x => x.CustomerUserId == customerUserId, cancellationToken);
        var promotional = await db.CustomerPromotionalWallets.AsNoTracking()
            .FirstOrDefaultAsync(x => x.CustomerUserId == customerUserId, cancellationToken);
        var currencyCode = cash?.Balance?.CurrencyCode
            ?? promotional?.Balance?.CurrencyCode
            ?? "KWD";
        var updatedAtUtc = new[]
        {
            cash?.ModifiedAtUtc ?? cash?.CreatedAtUtc,
            promotional?.ModifiedAtUtc ?? promotional?.CreatedAtUtc,
        }
        .Where(t => t.HasValue)
        .Select(t => t!.Value)
        .DefaultIfEmpty(DateTime.MinValue)
        .Max();

        return Result.Success(new CustomerWalletSummaryDto(
            CashBalanceKwd: cash?.Balance?.Amount ?? 0m,
            PromotionalBalanceKwd: promotional?.Balance?.Amount ?? 0m,
            CurrencyCode: currencyCode,
            UpdatedAtUtc: updatedAtUtc == DateTime.MinValue ? null : updatedAtUtc));
    }
}

public sealed class GetCustomerWalletTransactionsQueryHandler(
    IAppDbContext db,
    ICurrentUser currentUser)
    : IQueryHandler<GetCustomerWalletTransactionsQuery, CustomerWalletTransactionListDto>
{
    public async Task<Result<CustomerWalletTransactionListDto>> Handle(GetCustomerWalletTransactionsQuery request, CancellationToken cancellationToken)
    {
        if (!currentUser.IsAuthenticated || !currentUser.UserId.HasValue)
            return Error.Unauthorized("Auth.Unauthorized", "User is not authenticated.");
        var customerUserId = currentUser.UserId.Value;

        var walletType = request.WalletType?.Trim().ToUpperInvariant() switch
        {
            "CASH" => WalletType.Cash,
            "PROMOTIONAL" => WalletType.Promotional,
            _ => (WalletType?)null,
        };
        if (walletType is null)
            return Error.Validation("customer.wallet_type_invalid", "walletType must be 'cash' or 'promotional'.");

        var page = Math.Max(1, request.Page);
        var pageSize = Math.Clamp(request.PageSize, 1, 100);
        var query = db.WalletTransactions.AsNoTracking()
            .Where(x => x.CustomerUserId == customerUserId && x.WalletType == walletType)
            .OrderByDescending(x => x.OccurredAtUtc);
        var totalCount = await query.CountAsync(cancellationToken);
        var rows = await query
            .Skip((page - 1) * pageSize)
            .Take(pageSize)
            .ToListAsync(cancellationToken);

        var transactions = rows.Select(x => new CustomerWalletTransactionDto(
            x.Id,
            x.WalletType.ToString(),
            x.Type.ToString(),
            x.Amount.Amount,
            x.BalanceAfter.Amount,
            x.ReferenceType,
            x.ReferenceId,
            x.OccurredAtUtc,
            x.Description)).ToList();

        return Result.Success(new CustomerWalletTransactionListDto(totalCount, page, pageSize, transactions));
    }
}
