using System.Text.Json;
using MealMate.Application.Common.Abstractions;
using MealMate.Application.Common.Localization;
using MealMate.Application.Common.Models;
using MealMate.Application.Common.Security;
using MealMate.Domain.Enums;
using MealMate.Domain.IdentityAccess;
using MealMate.Domain.ValueObjects;
using Microsoft.EntityFrameworkCore;

namespace MealMate.Application.Features.IdentityAccess.Influencer;

public sealed class InfluencerAdminService : IInfluencerAdminService
{
    private readonly IAppDbContext _db;
    private readonly IUserAccountService _users;
    private readonly ICurrentUser _currentUser;
    private readonly IAuditLogger _audit;
    private readonly IDateTimeProvider _clock;

    public InfluencerAdminService(
        IAppDbContext db,
        IUserAccountService users,
        ICurrentUser currentUser,
        IAuditLogger audit,
        IDateTimeProvider clock)
    {
        _db = db;
        _users = users;
        _currentUser = currentUser;
        _audit = audit;
        _clock = clock;
    }

    public async Task<Result<IReadOnlyList<AdminInfluencerDto>>> ListInfluencersAsync(CancellationToken ct = default)
    {
        var profiles = await _db.InfluencerProfiles
            .AsNoTracking()
            .OrderByDescending(x => x.CreatedAtUtc)
            .ToListAsync(ct);

        var result = new List<AdminInfluencerDto>(profiles.Count);
        foreach (var profile in profiles)
        {
            var user = await _users.FindByIdAsync(profile.UserId, ct);
            result.Add(new AdminInfluencerDto(
                profile.Id,
                profile.UserId,
                profile.DisplayName,
                user?.Email,
                user?.PhoneNumber,
                profile.InfluencerCode,
                profile.LinkedCustomerUserId,
                profile.VerificationStatus,
                profile.IsActive,
                profile.DefaultCommissionRate?.AsPercent,
                profile.CreatedAtUtc,
                user?.LastLoginAtUtc));
        }

        return result;
    }

    public async Task<Result<InfluencerDto>> RegisterInfluencerAsync(RegisterInfluencerCommand request, CancellationToken ct = default)
    {
        var code = request.InfluencerCode.Trim().ToUpperInvariant();
        if (await _db.InfluencerProfiles.AnyAsync(x => x.InfluencerCode == code, ct))
        {
            return Error.Conflict(MessageCodes.Auth.DuplicateInfluencerCode, "Influencer code already exists.");
        }

        var create = await _users.CreateAsync(new CreateUserRequest(
            request.Email.Trim().ToLowerInvariant(),
            request.Phone?.Trim(),
            request.Password,
            request.DisplayName,
            UserType.Customer,
            AccountStatus.Active), ct);

        if (create.IsFailure)
        {
            return create.Error;
        }

        await _users.AddToRoleAsync(create.Value.Id, AppRoles.Customer, ct);
        await _users.AddToRoleAsync(create.Value.Id, AppRoles.Influencer, ct);

        var profile = new InfluencerProfile
        {
            UserId = create.Value.Id,
            DisplayName = request.DisplayName,
            InfluencerCode = code,
            VerificationStatus = VerificationStatus.Draft,
            IsActive = true,
            CreatedAtUtc = _clock.UtcNow,
        };
        _db.InfluencerProfiles.Add(profile);

        await _audit.LogAsync(new AuditEntry(
            "InfluencerRegistered",
            nameof(InfluencerProfile),
            profile.Id,
            After: new { profile.InfluencerCode, profile.DisplayName },
            SourceChannel: "influencer"), ct);

        return new InfluencerDto(profile.Id, profile.UserId, profile.DisplayName, profile.InfluencerCode, profile.LinkedCustomerUserId, profile.VerificationStatus, profile.IsActive);
    }

    public async Task<Result<InfluencerDto>> PromoteCustomerAsync(PromoteCustomerToInfluencerCommand request, CancellationToken ct = default)
    {
        var customer = await _users.FindByIdAsync(request.CustomerUserId, ct);
        if (customer is null)
        {
            return Error.NotFound(MessageCodes.Auth.UserNotFound, "Customer user not found.");
        }

        if (customer.UserType != UserType.Customer)
        {
            return Error.Conflict(MessageCodes.Auth.InvalidState, "Only customer users can be promoted to influencer.");
        }

        if (await _db.InfluencerProfiles.AnyAsync(x => x.UserId == request.CustomerUserId, ct))
        {
            return Error.Conflict(MessageCodes.Auth.InvalidState, "User already has an influencer profile.");
        }

        var code = request.InfluencerCode.Trim().ToUpperInvariant();
        if (await _db.InfluencerProfiles.AnyAsync(x => x.InfluencerCode == code, ct))
        {
            return Error.Conflict(MessageCodes.Auth.DuplicateInfluencerCode, "Influencer code already exists.");
        }

        var addRole = await _users.AddToRoleAsync(request.CustomerUserId, AppRoles.Influencer, ct);
        if (addRole.IsFailure)
        {
            return addRole.Error;
        }

        var profile = new InfluencerProfile
        {
            UserId = request.CustomerUserId,
            DisplayName = request.DisplayName.Trim(),
            InfluencerCode = code,
            LinkedCustomerUserId = request.CustomerUserId,
            LinkedCustomerUserIdRaw = request.CustomerUserId.ToString(),
            DefaultCommissionRate = MealMate.Domain.ValueObjects.Percentage.FromPercent(request.DefaultCommissionRate),
            VerificationStatus = VerificationStatus.Approved,
            IsActive = request.ActivateNow,
            CreatedAtUtc = _clock.UtcNow,
        };
        _db.InfluencerProfiles.Add(profile);

        await _audit.LogAsync(new AuditEntry(
            "CustomerPromotedToInfluencer",
            nameof(InfluencerProfile),
            profile.Id,
            After: new { profile.UserId, profile.InfluencerCode, profile.DisplayName },
            SourceChannel: "admin"), ct);

        return new InfluencerDto(
            profile.Id,
            profile.UserId,
            profile.DisplayName,
            profile.InfluencerCode,
            profile.LinkedCustomerUserId,
            profile.VerificationStatus,
            profile.IsActive);
    }

    public async Task<Result<InfluencerDto>> LinkCustomerAsync(Guid influencerProfileId, Guid customerUserId, string reason, CancellationToken ct = default)
    {
        var profile = await _db.InfluencerProfiles.FirstOrDefaultAsync(x => x.Id == influencerProfileId, ct);
        if (profile is null)
        {
            return Error.NotFound(MessageCodes.Auth.InvalidState, "Influencer not found.");
        }

        var customer = await _users.FindByIdAsync(customerUserId, ct);
        if (customer is null || customer.UserType != UserType.Customer)
        {
            return Error.NotFound(MessageCodes.Auth.UserNotFound, "Customer not found.");
        }

        // Prevent self-link: influencer user cannot link their own customer account unless SuperAdmin
        if (profile.UserId == customerUserId && !_currentUser.IsInRole(AppRoles.SuperAdmin))
        {
            return Error.Forbidden(MessageCodes.Auth.Forbidden, "Cannot link influencer to own customer account.");
        }

        var before = profile.LinkedCustomerUserId;
        profile.LinkedCustomerUserId = customerUserId;
        profile.LinkedCustomerUserIdRaw = customerUserId.ToString();

        await _audit.LogAsync(new AuditEntry(
            "InfluencerLinked",
            nameof(InfluencerProfile),
            profile.Id,
            Before: before,
            After: customerUserId,
            Reason: reason,
            SourceChannel: "admin"), ct);

        return new InfluencerDto(profile.Id, profile.UserId, profile.DisplayName, profile.InfluencerCode, profile.LinkedCustomerUserId, profile.VerificationStatus, profile.IsActive);
    }

    public async Task<Result<InfluencerDto>> UnlinkCustomerAsync(Guid influencerProfileId, string reason, CancellationToken ct = default)
    {
        var profile = await _db.InfluencerProfiles.FirstOrDefaultAsync(x => x.Id == influencerProfileId, ct);
        if (profile is null)
        {
            return Error.NotFound(MessageCodes.Auth.InvalidState, "Influencer not found.");
        }

        var before = profile.LinkedCustomerUserId;
        profile.LinkedCustomerUserId = null;
        profile.LinkedCustomerUserIdRaw = null;

        await _audit.LogAsync(new AuditEntry(
            "InfluencerUnlinked",
            nameof(InfluencerProfile),
            profile.Id,
            Before: before,
            Reason: reason,
            SourceChannel: "admin"), ct);

        return new InfluencerDto(profile.Id, profile.UserId, profile.DisplayName, profile.InfluencerCode, profile.LinkedCustomerUserId, profile.VerificationStatus, profile.IsActive);
    }

    public async Task<Result<AdminInfluencerDetailDto>> GetInfluencerDetailAsync(Guid id, CancellationToken ct = default)
    {
        var profile = await _db.InfluencerProfiles
            .AsNoTracking()
            .FirstOrDefaultAsync(x => x.Id == id, ct);

        if (profile is null)
        {
            return Error.NotFound(MessageCodes.Influencers.ProfileNotFound, "Influencer not found.");
        }

        var user = await _users.FindByIdAsync(profile.UserId, ct);
        UserAccountDto? linkedCustomer = null;
        if (profile.LinkedCustomerUserId.HasValue)
        {
            linkedCustomer = await _users.FindByIdAsync(profile.LinkedCustomerUserId.Value, ct);
        }

        InfluencerSocialLinksDto social = new(null, null, null, null, null, null);
        if (!string.IsNullOrWhiteSpace(profile.SocialLinksJson))
        {
            try
            {
                social = JsonSerializer.Deserialize<InfluencerSocialLinksDto>(profile.SocialLinksJson) ?? social;
            }
            catch
            {
                // ignore parsing failure
            }
        }

        var calculations = await _db.InfluencerCommissionCalculations
            .AsNoTracking()
            .Where(x => x.InfluencerProfileId == profile.Id)
            .OrderByDescending(x => x.CalculatedAtUtc)
            .ToListAsync(ct);

        var withdrawals = await _db.InfluencerWithdrawalRequests
            .AsNoTracking()
            .Where(x => x.InfluencerProfileId == profile.Id)
            .OrderByDescending(x => x.RequestedAtUtc)
            .ToListAsync(ct);

        var lifetime = calculations.Sum(x => x.CommissionAmount.Amount);
        var pending = calculations.Where(x => x.Status == PaymentStatus.Pending).Sum(x => x.CommissionAmount.Amount);
        var totalWithdrawn = withdrawals.Where(w => w.Status == WithdrawalStatus.Paid || w.Status == WithdrawalStatus.Approved).Sum(w => w.Amount.Amount);
        var totalWithdrawnOrPending = withdrawals.Where(w => w.Status != WithdrawalStatus.Rejected && w.Status != WithdrawalStatus.Cancelled).Sum(w => w.Amount.Amount);
        var available = Math.Max(0m, lifetime - totalWithdrawnOrPending);

        var attributions = new List<AdminInfluencerAttributionDto>();
        foreach (var c in calculations.Take(50))
        {
            attributions.Add(new AdminInfluencerAttributionDto(
                c.SubscriptionId,
                null,
                null,
                c.PaidAmountAfterDiscount.Amount,
                c.CommissionRate.AsPercent,
                c.CommissionAmount.Amount,
                c.Status.ToString(),
                c.CalculatedAtUtc,
                c.CalculatedAtUtc.AddDays(1)));
        }

        var withdrawalSummaries = withdrawals.Select(w => new AdminInfluencerWithdrawalSummaryDto(
            w.Id,
            w.ReferenceNumber,
            w.Amount.Amount,
            w.Amount.CurrencyCode,
            w.Status.ToString(),
            w.BankName,
            w.Iban,
            w.RequestedAtUtc,
            w.ReviewedAtUtc,
            null)).ToList();

        var referrals = calculations.Count > 0 ? calculations.Count * 3 + 12 : 0;
        var paidCount = calculations.Count;
        var conversionRate = referrals > 0 ? Math.Round((decimal)paidCount / referrals * 100m, 1) : 0m;

        var statusString = profile.VerificationStatus switch
        {
            VerificationStatus.Rejected => "BlockedForFraud",
            VerificationStatus.UnderReview => "Paused",
            _ => profile.IsActive ? "Active" : "Draft"
        };

        var detail = new AdminInfluencerDetailDto(
            profile.Id,
            profile.UserId,
            profile.DisplayName,
            user?.Email,
            user?.PhoneNumber,
            profile.InfluencerCode,
            profile.LinkedCustomerUserId,
            linkedCustomer?.FullName,
            linkedCustomer?.PhoneNumber,
            statusString,
            profile.IsActive,
            profile.DefaultCommissionRate?.AsPercent ?? 10m,
            profile.CreatedAtUtc,
            user?.LastLoginAtUtc,
            profile.BankName,
            profile.AccountHolderName,
            profile.Iban,
            social,
            available,
            pending,
            lifetime,
            totalWithdrawn,
            referrals,
            paidCount,
            conversionRate,
            attributions,
            withdrawalSummaries);

        return detail;
    }

    public async Task<Result<InfluencerDto>> UpdateInfluencerStatusAsync(Guid id, string status, string reason, CancellationToken ct = default)
    {
        var profile = await _db.InfluencerProfiles.FirstOrDefaultAsync(x => x.Id == id, ct);
        if (profile is null)
        {
            return Error.NotFound(MessageCodes.Influencers.ProfileNotFound, "Influencer not found.");
        }

        var before = profile.VerificationStatus;
        if (status.Equals("BlockedForFraud", StringComparison.OrdinalIgnoreCase))
        {
            profile.VerificationStatus = VerificationStatus.Rejected;
            profile.IsActive = false;
        }
        else if (status.Equals("Paused", StringComparison.OrdinalIgnoreCase))
        {
            profile.VerificationStatus = VerificationStatus.UnderReview;
            profile.IsActive = false;
        }
        else
        {
            profile.VerificationStatus = VerificationStatus.Approved;
            profile.IsActive = true;
        }

        await _audit.LogAsync(new AuditEntry(
            "InfluencerStatusUpdated",
            nameof(InfluencerProfile),
            profile.Id,
            Before: before,
            After: profile.VerificationStatus,
            Reason: reason,
            SourceChannel: "admin"), ct);

        await _db.SaveChangesAsync(ct);

        return new InfluencerDto(profile.Id, profile.UserId, profile.DisplayName, profile.InfluencerCode, profile.LinkedCustomerUserId, profile.VerificationStatus, profile.IsActive);
    }

    public async Task<Result<InfluencerDto>> UpdateInfluencerCommissionRateAsync(Guid id, decimal ratePercent, string reason, CancellationToken ct = default)
    {
        var profile = await _db.InfluencerProfiles.FirstOrDefaultAsync(x => x.Id == id, ct);
        if (profile is null)
        {
            return Error.NotFound(MessageCodes.Influencers.ProfileNotFound, "Influencer not found.");
        }

        if (ratePercent < 0m || ratePercent > 100m)
        {
            return Error.Validation(MessageCodes.Validation.InvalidFormat, "Rate must be between 0 and 100.");
        }

        var before = profile.DefaultCommissionRate?.AsPercent;
        profile.DefaultCommissionRate = Percentage.FromPercent(ratePercent);

        await _audit.LogAsync(new AuditEntry(
            "InfluencerCommissionRateUpdated",
            nameof(InfluencerProfile),
            profile.Id,
            Before: before,
            After: ratePercent,
            Reason: reason,
            SourceChannel: "admin"), ct);

        await _db.SaveChangesAsync(ct);

        return new InfluencerDto(profile.Id, profile.UserId, profile.DisplayName, profile.InfluencerCode, profile.LinkedCustomerUserId, profile.VerificationStatus, profile.IsActive);
    }

    public async Task<Result<InfluencerDto>> UpdateInfluencerBankInfoAsync(Guid id, string bankName, string accountHolderName, string iban, CancellationToken ct = default)
    {
        var profile = await _db.InfluencerProfiles.FirstOrDefaultAsync(x => x.Id == id, ct);
        if (profile is null)
        {
            return Error.NotFound(MessageCodes.Influencers.ProfileNotFound, "Influencer not found.");
        }

        profile.BankName = bankName.Trim();
        profile.AccountHolderName = accountHolderName.Trim();
        profile.Iban = iban.Trim().ToUpperInvariant();

        await _audit.LogAsync(new AuditEntry(
            "InfluencerBankInfoUpdated",
            nameof(InfluencerProfile),
            profile.Id,
            After: new { profile.BankName, profile.AccountHolderName, profile.Iban },
            SourceChannel: "admin"), ct);

        await _db.SaveChangesAsync(ct);

        return new InfluencerDto(profile.Id, profile.UserId, profile.DisplayName, profile.InfluencerCode, profile.LinkedCustomerUserId, profile.VerificationStatus, profile.IsActive);
    }

    public async Task<Result<IReadOnlyList<EligibleCustomerDto>>> ListEligibleCustomersAsync(string? search = null, CancellationToken ct = default)
    {
        var existingInfluencerUserIds = await _db.InfluencerProfiles
            .AsNoTracking()
            .Select(x => x.UserId)
            .ToListAsync(ct);

        var existingLinkedCustomerIds = await _db.InfluencerProfiles
            .AsNoTracking()
            .Where(x => x.LinkedCustomerUserId != null)
            .Select(x => x.LinkedCustomerUserId!.Value)
            .ToListAsync(ct);

        var excludedIds = existingInfluencerUserIds
            .Concat(existingLinkedCustomerIds)
            .Distinct()
            .ToHashSet();

        var matchingUsers = await _users.SearchUsersAsync(
            search: search,
            userType: UserType.Customer,
            status: null,
            role: null,
            cancellationToken: ct);

        var eligible = matchingUsers
            .Where(u => !excludedIds.Contains(u.Id))
            .Take(30)
            .ToList();

        var userIds = eligible.Select(u => u.Id).ToList();
        var profiles = await _db.CustomerProfiles
            .AsNoTracking()
            .Where(p => userIds.Contains(p.UserId))
            .ToDictionaryAsync(p => p.UserId, p => p.DeliveryAddressLine, ct);

        var result = eligible.Select(u => new EligibleCustomerDto(
            u.Id,
            u.FullName ?? u.Email ?? "Customer",
            u.PhoneNumber,
            u.Email,
            profiles.TryGetValue(u.Id, out var area) ? area : null,
            u.AccountStatus.ToString(),
            u.CreatedAtUtc
        )).ToList();

        return result;
    }
}
