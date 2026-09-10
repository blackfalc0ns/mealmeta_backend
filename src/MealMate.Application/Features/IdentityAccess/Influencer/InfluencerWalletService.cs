using MealMate.Application.Common.Abstractions;
using MealMate.Application.Common.Localization;
using MealMate.Application.Common.Models;
using MealMate.Domain.Enums;
using MealMate.Domain.Influencers;
using MealMate.Domain.ValueObjects;
using Microsoft.EntityFrameworkCore;

namespace MealMate.Application.Features.IdentityAccess.Influencer;

public sealed class InfluencerWalletService : IInfluencerWalletService
{
    private readonly IAppDbContext _db;
    private readonly IDateTimeProvider _clock;

    public InfluencerWalletService(IAppDbContext db, IDateTimeProvider clock)
    {
        _db = db;
        _clock = clock;
    }

    public async Task<Result<InfluencerWalletDto>> GetWalletAsync(Guid userId, CancellationToken ct = default)
    {
        var profile = await _db.InfluencerProfiles
            .AsNoTracking()
            .FirstOrDefaultAsync(x => (x.UserId == userId || x.LinkedCustomerUserId == userId) && x.IsActive, ct);

        if (profile is null)
        {
            return Error.NotFound(MessageCodes.Influencers.ProfileNotFound);
        }

        var calculations = await _db.InfluencerCommissionCalculations
            .AsNoTracking()
            .Where(x => x.InfluencerProfileId == profile.Id)
            .ToListAsync(ct);

        var withdrawals = await _db.InfluencerWithdrawalRequests
            .AsNoTracking()
            .Where(x => x.InfluencerProfileId == profile.Id)
            .OrderByDescending(x => x.RequestedAtUtc)
            .ToListAsync(ct);

        var lifetimeEarnings = calculations.Sum(x => x.CommissionAmount.Amount);

        var totalWithdrawnOrPending = withdrawals
            .Where(w => w.Status != WithdrawalStatus.Rejected && w.Status != WithdrawalStatus.Cancelled)
            .Sum(w => w.Amount.Amount);

        var available = Math.Max(0m, lifetimeEarnings - totalWithdrawnOrPending);

        var hasActiveWithdrawal = withdrawals.Any(w =>
            w.Status == WithdrawalStatus.Requested ||
            w.Status == WithdrawalStatus.UnderReview ||
            w.Status == WithdrawalStatus.Approved);

        var now = _clock.UtcNow;
        var startOfMonth = new DateTime(now.Year, now.Month, 1, 0, 0, 0, DateTimeKind.Utc);
        var thisMonthEarned = calculations.Where(c => c.CalculatedAtUtc >= startOfMonth).Sum(c => c.CommissionAmount.Amount);
        var thisMonthWithdrawn = withdrawals.Where(w => w.RequestedAtUtc >= startOfMonth && w.Status == WithdrawalStatus.Paid).Sum(w => w.Amount.Amount);

        var transactions = new List<WalletTransactionDto>();

        foreach (var w in withdrawals.Take(5))
        {
            transactions.Add(new WalletTransactionDto(
                w.ReferenceNumber,
                "Withdrawal",
                w.ReferenceNumber,
                w.Amount.Amount,
                w.Amount.CurrencyCode,
                IsCredit: false,
                Status: w.Status.ToString(),
                CreatedAtUtc: w.RequestedAtUtc
            ));
        }

        foreach (var c in calculations.Take(5))
        {
            transactions.Add(new WalletTransactionDto(
                c.Id.ToString(),
                "Commission",
                c.CodeId?.ToString() ?? profile.InfluencerCode ?? string.Empty,
                c.CommissionAmount.Amount,
                c.CommissionAmount.CurrencyCode,
                IsCredit: true,
                Status: "Completed",
                CreatedAtUtc: c.CalculatedAtUtc
            ));
        }

        var sortedTransactions = transactions.OrderByDescending(t => t.CreatedAtUtc).Take(10).ToList();

        return new InfluencerWalletDto(
            AvailableBalance: new CurrencyAmountDto(available, "KWD"),
            PendingBalance: new CurrencyAmountDto(0m, "KWD"),
            LifetimeEarnings: new CurrencyAmountDto(lifetimeEarnings, "KWD"),
            ThisMonthEarned: new CurrencyAmountDto(thisMonthEarned, "KWD"),
            ThisMonthWithdrawn: new CurrencyAmountDto(thisMonthWithdrawn, "KWD"),
            HasActiveWithdrawalRequest: hasActiveWithdrawal,
            MinimumWithdrawalAmount: 10.0m,
            RecentTransactions: sortedTransactions);
    }

    public async Task<Result<IReadOnlyList<WithdrawalRecordDto>>> GetWithdrawalsAsync(Guid userId, string? status, CancellationToken ct = default)
    {
        var profile = await _db.InfluencerProfiles
            .AsNoTracking()
            .FirstOrDefaultAsync(x => (x.UserId == userId || x.LinkedCustomerUserId == userId) && x.IsActive, ct);

        if (profile is null)
        {
            return Error.NotFound(MessageCodes.Influencers.ProfileNotFound);
        }

        var query = _db.InfluencerWithdrawalRequests
            .AsNoTracking()
            .Where(x => x.InfluencerProfileId == profile.Id);

        if (!string.IsNullOrWhiteSpace(status) && !status.Equals("All", StringComparison.OrdinalIgnoreCase))
        {
            if (Enum.TryParse<WithdrawalStatus>(status, true, out var parsedStatus))
            {
                query = query.Where(x => x.Status == parsedStatus);
            }
        }

        var list = await query
            .OrderByDescending(x => x.RequestedAtUtc)
            .ToListAsync(ct);

        var results = list.Select(w => new WithdrawalRecordDto(
            w.Id,
            w.ReferenceNumber,
            new CurrencyAmountDto(w.Amount.Amount, w.Amount.CurrencyCode),
            new CurrencyAmountDto(0m, "KWD"),
            new CurrencyAmountDto(w.Amount.Amount, w.Amount.CurrencyCode),
            w.Status.ToString(),
            w.WithdrawalMethod,
            new InfluencerBankInfoDto(w.AccountHolderName ?? string.Empty, w.BankName ?? string.Empty, w.Iban ?? string.Empty),
            w.RequestedAtUtc,
            w.ReviewedAtUtc,
            null,
            w.RejectionReason,
            1,
            3
        )).ToList();

        return results;
    }

    public async Task<Result<WithdrawalRecordDto>> GetWithdrawalDetailsAsync(Guid userId, Guid withdrawalId, CancellationToken ct = default)
    {
        var profile = await _db.InfluencerProfiles
            .AsNoTracking()
            .FirstOrDefaultAsync(x => (x.UserId == userId || x.LinkedCustomerUserId == userId) && x.IsActive, ct);

        if (profile is null)
        {
            return Error.NotFound(MessageCodes.Influencers.ProfileNotFound);
        }

        var w = await _db.InfluencerWithdrawalRequests
            .AsNoTracking()
            .FirstOrDefaultAsync(x => x.Id == withdrawalId && x.InfluencerProfileId == profile.Id, ct);

        if (w is null)
        {
            return Error.NotFound(MessageCodes.Influencers.WithdrawalNotFound);
        }

        return new WithdrawalRecordDto(
            w.Id,
            w.ReferenceNumber,
            new CurrencyAmountDto(w.Amount.Amount, w.Amount.CurrencyCode),
            new CurrencyAmountDto(0m, "KWD"),
            new CurrencyAmountDto(w.Amount.Amount, w.Amount.CurrencyCode),
            w.Status.ToString(),
            w.WithdrawalMethod,
            new InfluencerBankInfoDto(w.AccountHolderName ?? string.Empty, w.BankName ?? string.Empty, w.Iban ?? string.Empty),
            w.RequestedAtUtc,
            w.ReviewedAtUtc,
            null,
            w.RejectionReason,
            1,
            3);
    }

    public async Task<Result<WithdrawalRecordDto>> SubmitWithdrawalAsync(Guid userId, SubmitInfluencerWithdrawalCommand command, CancellationToken ct = default)
    {
        var profile = await _db.InfluencerProfiles
            .FirstOrDefaultAsync(x => (x.UserId == userId || x.LinkedCustomerUserId == userId) && x.IsActive, ct);

        if (profile is null)
        {
            return Error.NotFound(MessageCodes.Influencers.ProfileNotFound);
        }

        // 1. Guard against active request
        var hasActive = await _db.InfluencerWithdrawalRequests
            .AnyAsync(w => w.InfluencerProfileId == profile.Id &&
                          (w.Status == WithdrawalStatus.Requested ||
                           w.Status == WithdrawalStatus.UnderReview ||
                           w.Status == WithdrawalStatus.Approved), ct);

        if (hasActive)
        {
            return Error.Conflict(MessageCodes.Influencers.ActiveWithdrawalExists);
        }

        // 2. Check balance
        var totalEarnings = await _db.InfluencerCommissionCalculations
            .Where(x => x.InfluencerProfileId == profile.Id)
            .SumAsync(x => x.CommissionAmount.Amount, ct);

        var totalWithdrawn = await _db.InfluencerWithdrawalRequests
            .Where(x => x.InfluencerProfileId == profile.Id &&
                        x.Status != WithdrawalStatus.Rejected &&
                        x.Status != WithdrawalStatus.Cancelled)
            .SumAsync(x => x.Amount.Amount, ct);

        var available = Math.Max(0m, totalEarnings - totalWithdrawn);
        if (command.Amount > available)
        {
            return Error.Validation(MessageCodes.Influencers.InsufficientBalance);
        }

        // 3. Update default bank info if requested
        if (command.SaveAsDefaultBank)
        {
            profile.BankName = command.BankName.Trim();
            profile.AccountHolderName = command.AccountHolderName.Trim();
            profile.Iban = command.Iban.Trim().ToUpperInvariant();
        }

        // 4. Generate reference number
        var now = _clock.UtcNow;
        var seq = await _db.InfluencerWithdrawalRequests.CountAsync(ct) + 1;
        var refNumber = $"WD-{now:yyyy-MM-dd}-{seq:D4}";

        var withdrawal = new InfluencerWithdrawalRequest
        {
            InfluencerProfileId = profile.Id,
            Amount = new Money(command.Amount, "KWD"),
            Status = WithdrawalStatus.Requested,
            RequestedAtUtc = now,
            ReferenceNumber = refNumber,
            WithdrawalMethod = command.WithdrawalMethod,
            BankName = command.BankName.Trim(),
            AccountHolderName = command.AccountHolderName.Trim(),
            Iban = command.Iban.Trim().ToUpperInvariant()
        };

        _db.InfluencerWithdrawalRequests.Add(withdrawal);
        await _db.SaveChangesAsync(ct);

        return new WithdrawalRecordDto(
            withdrawal.Id,
            withdrawal.ReferenceNumber,
            new CurrencyAmountDto(withdrawal.Amount.Amount, withdrawal.Amount.CurrencyCode),
            new CurrencyAmountDto(0m, "KWD"),
            new CurrencyAmountDto(withdrawal.Amount.Amount, withdrawal.Amount.CurrencyCode),
            withdrawal.Status.ToString(),
            withdrawal.WithdrawalMethod,
            new InfluencerBankInfoDto(withdrawal.AccountHolderName ?? string.Empty, withdrawal.BankName ?? string.Empty, withdrawal.Iban ?? string.Empty),
            withdrawal.RequestedAtUtc,
            null,
            null,
            null,
            1,
            3);
    }
}
