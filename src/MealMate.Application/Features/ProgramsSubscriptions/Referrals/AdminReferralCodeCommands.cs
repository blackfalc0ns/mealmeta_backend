using MealMate.Application.Common.Abstractions;
using MealMate.Application.Common.Messaging;
using MealMate.Application.Common.Models;
using MealMate.Domain.ProgramsSubscriptions;
using MealMate.Domain.ValueObjects;
using Microsoft.EntityFrameworkCore;

namespace MealMate.Application.Features.ProgramsSubscriptions.Referrals;

public sealed record AdminReferralCodeDto(
    Guid Id,
    string Code,
    Guid OwnerUserId,
    decimal RewardPercent,
    int? MaxUses,
    int UsedCount,
    bool IsActive,
    DateTime CreatedAtUtc);

public sealed record CreateAdminReferralCodeCommand(
    string Code,
    Guid OwnerUserId,
    decimal RewardPercent,
    int? MaxUses = null) : ICommand<AdminReferralCodeDto>;

public sealed record SetReferralCodeStatusCommand(
    Guid Id,
    bool IsActive) : ICommand<AdminReferralCodeDto>;

public sealed record GetAdminReferralCodesListQuery(
    int Page = 1,
    int PageSize = 20,
    string? Q = null) : IQuery<PagedResult<AdminReferralCodeDto>>;

public sealed record ReferralAttributionAdminDto(
    Guid AttributionId,
    string Code,
    Guid ReferrerUserId,
    Guid ReferredCustomerUserId,
    DateTime AttributedAtUtc,
    decimal? RewardPercentSnapshot,
    DateTime? RewardedAtUtc);

public sealed record GetAdminReferralAttributionsQuery(
    Guid ReferralCodeId,
    int Page = 1,
    int PageSize = 20) : IQuery<PagedResult<ReferralAttributionAdminDto>>;

public sealed class AdminReferralCodeCommandsHandler(
    IAppDbContext db,
    IAuditLogger audit,
    IDateTimeProvider clock)
    : ICommandHandler<CreateAdminReferralCodeCommand, AdminReferralCodeDto>,
      ICommandHandler<SetReferralCodeStatusCommand, AdminReferralCodeDto>
{
    public async Task<Result<AdminReferralCodeDto>> Handle(CreateAdminReferralCodeCommand request, CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(request.Code))
            return Error.Validation("referral.code_required", "Code is required.");

        if (request.OwnerUserId == Guid.Empty)
            return Error.Validation("referral.owner_required", "Owner user ID is required.");

        if (request.RewardPercent < 0m || request.RewardPercent > 100m)
            return Error.Validation("referral.invalid_percent", "Reward percent must be between 0 and 100.");

        var codeNormalized = request.Code.Trim().ToUpperInvariant();
        var exists = await db.ReferralCodes.AsNoTracking().AnyAsync(x => x.Code == codeNormalized, cancellationToken);
        if (exists)
            return Error.Validation("referral.code_duplicate", $"Referral code '{codeNormalized}' already exists.");

        var referral = new ReferralCode
        {
            Code = codeNormalized,
            OwnerUserId = request.OwnerUserId,
            RewardPercent = Percentage.FromPercent(request.RewardPercent),
            MaxUses = request.MaxUses,
            UsedCount = 0,
            IsActive = true,
            CreatedAtUtc = clock.UtcNow,
        };

        db.ReferralCodes.Add(referral);

        await audit.LogAsync(new AuditEntry(
            Action: "ReferralCodeCreated",
            EntityType: nameof(ReferralCode),
            EntityId: referral.Id,
            After: referral,
            SourceChannel: "admin-dashboard"), cancellationToken);

        await db.SaveChangesAsync(cancellationToken);

        return Result.Success(new AdminReferralCodeDto(
            referral.Id,
            referral.Code,
            referral.OwnerUserId,
            referral.RewardPercent?.AsPercent ?? 0m,
            referral.MaxUses,
            referral.UsedCount,
            referral.IsActive,
            referral.CreatedAtUtc));
    }

    public async Task<Result<AdminReferralCodeDto>> Handle(SetReferralCodeStatusCommand request, CancellationToken cancellationToken)
    {
        var referral = await db.ReferralCodes.FirstOrDefaultAsync(x => x.Id == request.Id, cancellationToken);
        if (referral is null)
            return Error.NotFound("referral.not_found", $"Referral code with ID {request.Id} was not found.");

        var previousStatus = referral.IsActive;
        referral.IsActive = request.IsActive;
        referral.ModifiedAtUtc = clock.UtcNow;

        await audit.LogAsync(new AuditEntry(
            Action: "ReferralCodeStatusChanged",
            EntityType: nameof(ReferralCode),
            EntityId: referral.Id,
            Before: previousStatus,
            After: request.IsActive,
            SourceChannel: "admin-dashboard"), cancellationToken);

        await db.SaveChangesAsync(cancellationToken);

        return Result.Success(new AdminReferralCodeDto(
            referral.Id,
            referral.Code,
            referral.OwnerUserId,
            referral.RewardPercent?.AsPercent ?? 0m,
            referral.MaxUses,
            referral.UsedCount,
            referral.IsActive,
            referral.CreatedAtUtc));
    }
}

public sealed class GetAdminReferralCodesListQueryHandler(IAppDbContext db)
    : IQueryHandler<GetAdminReferralCodesListQuery, PagedResult<AdminReferralCodeDto>>
{
    public async Task<Result<PagedResult<AdminReferralCodeDto>>> Handle(GetAdminReferralCodesListQuery request, CancellationToken cancellationToken)
    {
        var page = Math.Max(1, request.Page);
        var pageSize = Math.Clamp(request.PageSize, 1, 100);

        var query = db.ReferralCodes.AsNoTracking();

        if (!string.IsNullOrWhiteSpace(request.Q))
        {
            var q = request.Q.Trim().ToUpperInvariant();
            query = query.Where(x => x.Code.Contains(q));
        }

        var totalCount = await query.CountAsync(cancellationToken);

        var items = await query
            .OrderByDescending(x => x.CreatedAtUtc)
            .Skip((page - 1) * pageSize)
            .Take(pageSize)
            .Select(x => new AdminReferralCodeDto(
                x.Id,
                x.Code,
                x.OwnerUserId,
                x.RewardPercent != null ? x.RewardPercent.Value * 100m : 0m,
                x.MaxUses,
                x.UsedCount,
                x.IsActive,
                x.CreatedAtUtc))
            .ToListAsync(cancellationToken);

        return Result.Success(new PagedResult<AdminReferralCodeDto>(items, totalCount, page, pageSize));
    }
}

public sealed class GetAdminReferralAttributionsQueryHandler(IAppDbContext db)
    : IQueryHandler<GetAdminReferralAttributionsQuery, PagedResult<ReferralAttributionAdminDto>>
{
    public async Task<Result<PagedResult<ReferralAttributionAdminDto>>> Handle(GetAdminReferralAttributionsQuery request, CancellationToken cancellationToken)
    {
        var referralCode = await db.ReferralCodes.AsNoTracking().FirstOrDefaultAsync(x => x.Id == request.ReferralCodeId, cancellationToken);
        if (referralCode is null)
            return Error.NotFound("referral.not_found", $"Referral code with ID {request.ReferralCodeId} was not found.");

        var page = Math.Max(1, request.Page);
        var pageSize = Math.Clamp(request.PageSize, 1, 100);

        var query = db.ReferralAttributions.AsNoTracking()
            .Where(x => x.ReferralCodeId == request.ReferralCodeId || x.Code == referralCode.Code);

        var totalCount = await query.CountAsync(cancellationToken);

        var items = await query
            .OrderByDescending(x => x.AttributedAtUtc)
            .Skip((page - 1) * pageSize)
            .Take(pageSize)
            .Select(x => new ReferralAttributionAdminDto(
                x.Id,
                x.Code,
                x.ReferrerUserId,
                x.ReferredCustomerUserId,
                x.AttributedAtUtc,
                x.RewardPercentSnapshot,
                x.RewardedAtUtc))
            .ToListAsync(cancellationToken);

        return Result.Success(new PagedResult<ReferralAttributionAdminDto>(items, totalCount, page, pageSize));
    }
}

