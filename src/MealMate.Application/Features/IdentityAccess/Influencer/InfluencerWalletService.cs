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
    private readonly IUserAccountService? _users;
    private readonly IAuditLogger? _audit;

    public InfluencerWalletService(
        IAppDbContext db,
        IDateTimeProvider clock,
        IUserAccountService? users = null,
        IAuditLogger? audit = null)
    {
        _db = db;
        _clock = clock;
        _users = users;
        _audit = audit;
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

        var minAmount = 10.0m;
        var hasBank = !string.IsNullOrWhiteSpace(profile.Iban) && !string.IsNullOrWhiteSpace(profile.BankName);
        var canWithdraw = available >= minAmount && !hasActiveWithdrawal && hasBank;
        string? disabledReason = null;
        if (hasActiveWithdrawal) disabledReason = "PendingWithdrawalExists";
        else if (!hasBank) disabledReason = "NoBankAccountConfigured";
        else if (available < minAmount) disabledReason = "InsufficientBalance";

        var policy = new InfluencerWithdrawalPolicyDto(
            MinimumAmount: minAmount,
            MaximumAmount: available,
            CurrencyCode: "KWD",
            CanWithdraw: canWithdraw,
            DisabledReason: disabledReason);

        InfluencerBankInfoDto? bankInfo = hasBank
            ? new InfluencerBankInfoDto(profile.AccountHolderName ?? profile.DisplayName, profile.BankName!, profile.Iban!, MaskIban(profile.Iban!), true)
            : null;

        return new InfluencerWalletDto(
            AvailableBalance: new CurrencyAmountDto(available, "KWD"),
            PendingBalance: new CurrencyAmountDto(0m, "KWD"),
            LifetimeEarnings: new CurrencyAmountDto(lifetimeEarnings, "KWD"),
            ThisMonthEarned: new CurrencyAmountDto(thisMonthEarned, "KWD"),
            ThisMonthWithdrawn: new CurrencyAmountDto(thisMonthWithdrawn, "KWD"),
            CurrencyCode: "KWD",
            HasActiveWithdrawalRequest: hasActiveWithdrawal,
            WithdrawalPolicy: policy,
            BankInfo: bankInfo,
            RecentTransactions: sortedTransactions);
    }

    public async Task<Result<PagedResultDto<WithdrawalRecordDto>>> GetWithdrawalsAsync(
        Guid userId,
        string? status,
        int page = 1,
        int pageSize = 20,
        CancellationToken ct = default)
    {
        page = Math.Max(1, page);
        pageSize = Math.Clamp(pageSize, 1, 100);

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

        var totalCount = await query.CountAsync(ct);
        var totalPages = (int)Math.Ceiling(totalCount / (double)pageSize);

        var list = await query
            .OrderByDescending(x => x.RequestedAtUtc)
            .Skip((page - 1) * pageSize)
            .Take(pageSize)
            .ToListAsync(ct);

        var items = list.Select(w => new WithdrawalRecordDto(
            w.Id,
            w.ReferenceNumber,
            new CurrencyAmountDto(w.Amount.Amount, w.Amount.CurrencyCode),
            new CurrencyAmountDto(0m, "KWD"),
            new CurrencyAmountDto(w.Amount.Amount, w.Amount.CurrencyCode),
            w.Status.ToString(),
            w.WithdrawalMethod,
            w.Amount.CurrencyCode,
            new InfluencerBankInfoDto(w.AccountHolderName ?? string.Empty, w.BankName ?? string.Empty, w.Iban ?? string.Empty, MaskIban(w.Iban), !string.IsNullOrWhiteSpace(w.Iban)),
            w.RequestedAtUtc,
            w.ReviewedAtUtc,
            null,
            w.RejectionReason,
            1,
            3
        )).ToList();

        return new PagedResultDto<WithdrawalRecordDto>(
            items,
            page,
            pageSize,
            totalCount,
            totalPages,
            HasPreviousPage: page > 1,
            HasNextPage: page < totalPages);
    }

    public async Task<Result<WithdrawalRecordDto>> CancelWithdrawalAsync(Guid userId, Guid withdrawalId, CancellationToken ct = default)
    {
        var profile = await _db.InfluencerProfiles
            .FirstOrDefaultAsync(x => (x.UserId == userId || x.LinkedCustomerUserId == userId) && x.IsActive, ct);

        if (profile is null)
        {
            return Error.NotFound(MessageCodes.Influencers.ProfileNotFound);
        }

        var w = await _db.InfluencerWithdrawalRequests
            .FirstOrDefaultAsync(x => x.Id == withdrawalId && x.InfluencerProfileId == profile.Id, ct);

        if (w is null)
        {
            return Error.NotFound(MessageCodes.Influencers.WithdrawalNotFound);
        }

        if (w.Status != WithdrawalStatus.Requested && w.Status != WithdrawalStatus.UnderReview)
        {
            return Error.Validation(MessageCodes.Influencers.CannotCancelWithdrawal);
        }

        w.Status = WithdrawalStatus.Cancelled;
        w.RejectionReason = "Cancelled by influencer";
        w.ReviewedAtUtc = _clock.UtcNow;

        await _db.SaveChangesAsync(ct);

        return new WithdrawalRecordDto(
            w.Id,
            w.ReferenceNumber,
            new CurrencyAmountDto(w.Amount.Amount, w.Amount.CurrencyCode),
            new CurrencyAmountDto(0m, "KWD"),
            new CurrencyAmountDto(w.Amount.Amount, w.Amount.CurrencyCode),
            w.Status.ToString(),
            w.WithdrawalMethod,
            w.Amount.CurrencyCode,
            new InfluencerBankInfoDto(w.AccountHolderName ?? string.Empty, w.BankName ?? string.Empty, w.Iban ?? string.Empty, MaskIban(w.Iban), !string.IsNullOrWhiteSpace(w.Iban)),
            w.RequestedAtUtc,
            w.ReviewedAtUtc,
            null,
            w.RejectionReason,
            1,
            3);
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
            w.Amount.CurrencyCode,
            new InfluencerBankInfoDto(w.AccountHolderName ?? string.Empty, w.BankName ?? string.Empty, w.Iban ?? string.Empty, MaskIban(w.Iban), !string.IsNullOrWhiteSpace(w.Iban)),
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

        // 3. Resolve bank details (from command or fallback to profile)
        var bankName = !string.IsNullOrWhiteSpace(command.BankName) ? command.BankName.Trim() : profile.BankName;
        var accountHolderName = !string.IsNullOrWhiteSpace(command.AccountHolderName) ? command.AccountHolderName.Trim() : profile.AccountHolderName;
        var iban = !string.IsNullOrWhiteSpace(command.Iban) ? command.Iban.Replace(" ", "").Replace("-", "").Trim().ToUpperInvariant() : profile.Iban;

        if (string.IsNullOrWhiteSpace(bankName) || string.IsNullOrWhiteSpace(accountHolderName) || string.IsNullOrWhiteSpace(iban))
        {
            return Error.Validation(MessageCodes.Influencers.BankAccountRequired);
        }

        // Update default bank info on profile if custom details were passed and requested to save
        if (command.SaveAsDefaultBank && !string.IsNullOrWhiteSpace(command.Iban))
        {
            profile.BankName = bankName;
            profile.AccountHolderName = accountHolderName;
            profile.Iban = iban;
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
            BankName = bankName,
            AccountHolderName = accountHolderName,
            Iban = iban
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
            withdrawal.Amount.CurrencyCode,
            new InfluencerBankInfoDto(withdrawal.AccountHolderName ?? string.Empty, withdrawal.BankName ?? string.Empty, withdrawal.Iban ?? string.Empty, MaskIban(withdrawal.Iban), true),
            withdrawal.RequestedAtUtc,
            null,
            null,
            null,
            1,
            3);
    }

    public async Task<Result<PagedResult<AdminWithdrawalItemDto>>> ListAdminWithdrawalsAsync(
        string? status,
        string? search,
        int page,
        int pageSize,
        CancellationToken ct = default)
    {
        var query = _db.InfluencerWithdrawalRequests
            .AsNoTracking()
            .Include(x => x.Approvals)
            .AsQueryable();

        if (!string.IsNullOrWhiteSpace(status) && !status.Equals("All", StringComparison.OrdinalIgnoreCase))
        {
            if (Enum.TryParse<WithdrawalStatus>(status, true, out var parsedStatus))
            {
                query = query.Where(x => x.Status == parsedStatus);
            }
        }

        if (!string.IsNullOrWhiteSpace(search))
        {
            var s = search.Trim();
            query = query.Where(x =>
                x.ReferenceNumber.Contains(s) ||
                (x.Iban != null && x.Iban.Contains(s)) ||
                (x.AccountHolderName != null && x.AccountHolderName.Contains(s)) ||
                (x.BankName != null && x.BankName.Contains(s)));
        }

        var total = await query.CountAsync(ct);
        var normalizedPage = Math.Max(1, page);
        var normalizedPageSize = Math.Clamp(pageSize, 1, 100);

        var list = await query
            .OrderByDescending(x => x.RequestedAtUtc)
            .Skip((normalizedPage - 1) * normalizedPageSize)
            .Take(normalizedPageSize)
            .ToListAsync(ct);

        var results = new List<AdminWithdrawalItemDto>(list.Count);
        foreach (var w in list)
        {
            var profile = await _db.InfluencerProfiles
                .AsNoTracking()
                .FirstOrDefaultAsync(p => p.Id == w.InfluencerProfileId, ct);

            string influencerName = profile?.DisplayName ?? "Unknown";
            string? influencerCode = profile?.InfluencerCode;
            string? phone = null;
            string? adminName = null;

            if (_users != null && profile != null)
            {
                var user = await _users.FindByIdAsync(profile.UserId, ct);
                phone = user?.PhoneNumber;

                if (w.ReviewedByUserId.HasValue)
                {
                    var adminUser = await _users.FindByIdAsync(w.ReviewedByUserId.Value, ct);
                    adminName = adminUser?.FullName ?? adminUser?.Email;
                }
            }

            var latestApproval = w.Approvals.OrderByDescending(a => a.DecidedAtUtc).FirstOrDefault();

            results.Add(new AdminWithdrawalItemDto(
                w.Id,
                w.ReferenceNumber,
                w.InfluencerProfileId,
                influencerName,
                influencerCode,
                phone,
                w.Amount.Amount,
                w.Amount.CurrencyCode,
                w.Status.ToString(),
                w.BankName,
                w.AccountHolderName,
                w.Iban,
                w.RequestedAtUtc,
                w.ReviewedAtUtc,
                adminName,
                null,
                w.RejectionReason,
                latestApproval?.Notes));
        }

        return new PagedResult<AdminWithdrawalItemDto>(results, total, normalizedPage, normalizedPageSize);
    }

    public async Task<Result<AdminWithdrawalItemDto>> ApproveWithdrawalAsync(
        Guid withdrawalId,
        Guid adminUserId,
        string transferRef,
        DateTime? transferDate,
        string? notes,
        CancellationToken ct = default)
    {
        var w = await _db.InfluencerWithdrawalRequests
            .Include(x => x.Approvals)
            .FirstOrDefaultAsync(x => x.Id == withdrawalId, ct);

        if (w is null)
        {
            return Error.NotFound(MessageCodes.Influencers.WithdrawalNotFound, "Withdrawal request not found.");
        }

        if (w.Status != WithdrawalStatus.Requested && w.Status != WithdrawalStatus.UnderReview)
        {
            return Error.Conflict(MessageCodes.Validation.InvalidStateTransition, "Withdrawal request is already processed.");
        }

        var now = _clock.UtcNow;
        w.Status = WithdrawalStatus.Paid;
        w.ReviewedAtUtc = transferDate ?? now;
        w.ReviewedByUserId = adminUserId;

        var noteContent = string.IsNullOrWhiteSpace(notes)
            ? $"Ref: {transferRef.Trim()}"
            : $"Ref: {transferRef.Trim()} | {notes.Trim()}";

        var approval = new InfluencerWithdrawalApproval
        {
            WithdrawalRequestId = w.Id,
            ApprovedByUserId = adminUserId,
            Decision = VerificationStatus.Approved,
            Notes = noteContent,
            DecidedAtUtc = now
        };

        _db.InfluencerWithdrawalApprovals.Add(approval);

        if (_audit != null)
        {
            await _audit.LogAsync(new AuditEntry(
                "InfluencerWithdrawalApproved",
                nameof(InfluencerWithdrawalRequest),
                w.Id,
                After: new { w.ReferenceNumber, TransferRef = transferRef, Amount = w.Amount.Amount },
                SourceChannel: "admin"), ct);
        }

        await _db.SaveChangesAsync(ct);

        var profile = await _db.InfluencerProfiles.AsNoTracking().FirstOrDefaultAsync(p => p.Id == w.InfluencerProfileId, ct);

        return new AdminWithdrawalItemDto(
            w.Id,
            w.ReferenceNumber,
            w.InfluencerProfileId,
            profile?.DisplayName ?? "Unknown",
            profile?.InfluencerCode,
            null,
            w.Amount.Amount,
            w.Amount.CurrencyCode,
            w.Status.ToString(),
            w.BankName,
            w.AccountHolderName,
            w.Iban,
            w.RequestedAtUtc,
            w.ReviewedAtUtc,
            null,
            transferRef,
            null,
            noteContent);
    }

    public async Task<Result<AdminWithdrawalItemDto>> RejectWithdrawalAsync(
        Guid withdrawalId,
        Guid adminUserId,
        string rejectionReason,
        CancellationToken ct = default)
    {
        var w = await _db.InfluencerWithdrawalRequests
            .Include(x => x.Approvals)
            .FirstOrDefaultAsync(x => x.Id == withdrawalId, ct);

        if (w is null)
        {
            return Error.NotFound(MessageCodes.Influencers.WithdrawalNotFound, "Withdrawal request not found.");
        }

        if (w.Status != WithdrawalStatus.Requested && w.Status != WithdrawalStatus.UnderReview)
        {
            return Error.Conflict(MessageCodes.Validation.InvalidStateTransition, "Withdrawal request is already processed.");
        }

        var now = _clock.UtcNow;
        w.Status = WithdrawalStatus.Rejected;
        w.RejectionReason = rejectionReason.Trim();
        w.ReviewedAtUtc = now;
        w.ReviewedByUserId = adminUserId;

        var approval = new InfluencerWithdrawalApproval
        {
            WithdrawalRequestId = w.Id,
            ApprovedByUserId = adminUserId,
            Decision = VerificationStatus.Rejected,
            Notes = rejectionReason.Trim(),
            DecidedAtUtc = now
        };

        _db.InfluencerWithdrawalApprovals.Add(approval);

        if (_audit != null)
        {
            await _audit.LogAsync(new AuditEntry(
                "InfluencerWithdrawalRejected",
                nameof(InfluencerWithdrawalRequest),
                w.Id,
                After: new { w.ReferenceNumber, RejectionReason = rejectionReason },
                SourceChannel: "admin"), ct);
        }

        await _db.SaveChangesAsync(ct);

        var profile = await _db.InfluencerProfiles.AsNoTracking().FirstOrDefaultAsync(p => p.Id == w.InfluencerProfileId, ct);

        return new AdminWithdrawalItemDto(
            w.Id,
            w.ReferenceNumber,
            w.InfluencerProfileId,
            profile?.DisplayName ?? "Unknown",
            profile?.InfluencerCode,
            null,
            w.Amount.Amount,
            w.Amount.CurrencyCode,
            w.Status.ToString(),
            w.BankName,
            w.AccountHolderName,
            w.Iban,
            w.RequestedAtUtc,
            w.ReviewedAtUtc,
            null,
            null,
            w.RejectionReason,
            rejectionReason.Trim());
    }

    private static string MaskIban(string? iban)
    {
        if (string.IsNullOrWhiteSpace(iban) || iban.Length < 8) return iban ?? string.Empty;
        var prefix = iban[..4];
        var suffix = iban[^4..];
        return $"{prefix} •••• •••• {suffix}";
    }
}
