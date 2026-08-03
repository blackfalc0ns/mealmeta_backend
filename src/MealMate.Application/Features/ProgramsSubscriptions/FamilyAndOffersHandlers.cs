using FluentValidation;
using MealMate.Application.Common.Abstractions;
using MealMate.Application.Common.Localization;
using MealMate.Application.Common.Messaging;
using MealMate.Application.Common.Models;
using MealMate.Domain.Enums;
using MealMate.Domain.IdentityAccess;
using MealMate.Domain.ProgramsSubscriptions;
using MealMate.Domain.ValueObjects;
using Microsoft.EntityFrameworkCore;

namespace MealMate.Application.Features.ProgramsSubscriptions;

// Validators
public sealed class AddFamilyMemberCommandValidator : AbstractValidator<AddFamilyMemberCommand>
{
    public AddFamilyMemberCommandValidator()
    {
        RuleFor(x => x.PrimarySubscriptionId).NotEmpty();
        RuleFor(x => x.MemberCustomerUserId).NotEmpty();
        RuleFor(x => x.MaxMealsAllowedPerDay).GreaterThan(0);
        RuleFor(x => x.DisplayName).MaximumLength(200).When(x => x.DisplayName is not null);
        RuleFor(x => x.Relationship).MaximumLength(100).When(x => x.Relationship is not null);
    }
}

public sealed class CreatePromoCodeCommandValidator : AbstractValidator<CreatePromoCodeCommand>
{
    public CreatePromoCodeCommandValidator()
    {
        RuleFor(x => x.Code).NotEmpty().MaximumLength(50);
        RuleFor(x => x).Must(x => x.DiscountPercent.HasValue || x.DiscountAmountKwd.HasValue)
            .WithMessage("Either DiscountPercent or DiscountAmountKwd must be provided.");
        When(x => x.DiscountPercent.HasValue, () => RuleFor(x => x.DiscountPercent!.Value).InclusiveBetween(0.01m, 1.0m));
        When(x => x.DiscountAmountKwd.HasValue, () => RuleFor(x => x.DiscountAmountKwd!.Value).GreaterThan(0m));
    }
}

// Handlers
public sealed class FamilyAndOffersHandlers :
    ICommandHandler<AddFamilyMemberCommand, FamilyMemberDto>,
    ICommandHandler<RemoveFamilyMemberCommand, bool>,
    IQueryHandler<GetFamilyMembersQuery, IReadOnlyList<FamilyMemberDto>>,
    ICommandHandler<CreatePromoCodeCommand, PromoCodeDto>,
    IQueryHandler<ValidateDiscountCodeQuery, DiscountValidationResultDto>,
    IQueryHandler<GetPromoCodesListQuery, PagedResult<PromoCodeDto>>
{
    private readonly IAppDbContext _db;
    private readonly IAuditLogger _audit;
    private readonly IDateTimeProvider _clock;

    public FamilyAndOffersHandlers(IAppDbContext db, IAuditLogger audit, IDateTimeProvider clock)
    {
        _db = db;
        _audit = audit;
        _clock = clock;
    }

    public async Task<Result<FamilyMemberDto>> Handle(AddFamilyMemberCommand request, CancellationToken cancellationToken)
    {
        var primarySub = await _db.Subscriptions.FirstOrDefaultAsync(x => x.Id == request.PrimarySubscriptionId, cancellationToken);
        if (primarySub is null || primarySub.Status is SubscriptionStatus.Cancelled or SubscriptionStatus.Expired)
        {
            return Error.NotFound(MessageCodes.Common.NotFound, $"Primary subscription '{request.PrimarySubscriptionId}' was not found or is inactive.");
        }

        // Limit member count to max 4 family members
        var currentMemberCount = await _db.FamilySubscriptions
            .CountAsync(x => x.PrimarySubscriptionId == request.PrimarySubscriptionId && x.IsActive, cancellationToken);

        if (currentMemberCount >= 4)
        {
            return Error.Validation(MessageCodes.Validation.InclusiveBetween, "Family subscription member limit reached (Maximum 4 members allowed).");
        }

        var alreadyExists = await _db.FamilySubscriptions.AnyAsync(
            x => x.PrimarySubscriptionId == request.PrimarySubscriptionId && x.MemberCustomerUserId == request.MemberCustomerUserId && x.IsActive,
            cancellationToken);

        if (alreadyExists)
        {
            return Error.Conflict(MessageCodes.Validation.Unknown, "User is already an active member of this family subscription.");
        }

        var member = new FamilySubscription
        {
            PrimarySubscriptionId = request.PrimarySubscriptionId,
            MemberCustomerUserId = request.MemberCustomerUserId,
            DisplayName = request.DisplayName?.Trim(),
            Relationship = request.Relationship?.Trim(),
            MaxMealsAllowedPerDay = request.MaxMealsAllowedPerDay,
            IsActive = true,
        };

        _db.FamilySubscriptions.Add(member);
        await _db.SaveChangesAsync(cancellationToken);

        await _audit.LogAsync(new AuditEntry(
            "FamilyMemberAdded",
            nameof(FamilySubscription),
            member.Id,
            After: new { member.PrimarySubscriptionId, member.MemberCustomerUserId, member.DisplayName },
            SourceChannel: "customer-app"), cancellationToken);

        return MapFamilyMemberDto(member);
    }

    public async Task<Result<bool>> Handle(RemoveFamilyMemberCommand request, CancellationToken cancellationToken)
    {
        var member = await _db.FamilySubscriptions.FirstOrDefaultAsync(x => x.Id == request.MemberId, cancellationToken);
        if (member is null)
        {
            return Error.NotFound(MessageCodes.Common.NotFound, $"Family member record with ID '{request.MemberId}' was not found.");
        }

        member.IsActive = false;
        await _db.SaveChangesAsync(cancellationToken);

        await _audit.LogAsync(new AuditEntry(
            "FamilyMemberRemoved",
            nameof(FamilySubscription),
            member.Id,
            After: new { member.PrimarySubscriptionId, member.MemberCustomerUserId },
            SourceChannel: "customer-app"), cancellationToken);

        return true;
    }

    public async Task<Result<IReadOnlyList<FamilyMemberDto>>> Handle(GetFamilyMembersQuery request, CancellationToken cancellationToken)
    {
        var members = await _db.FamilySubscriptions
            .Where(x => x.PrimarySubscriptionId == request.PrimarySubscriptionId && x.IsActive)
            .OrderBy(x => x.CreatedAtUtc)
            .ToListAsync(cancellationToken);

        var dtos = members.Select(MapFamilyMemberDto).ToList();
        return dtos;
    }

    public async Task<Result<PromoCodeDto>> Handle(CreatePromoCodeCommand request, CancellationToken cancellationToken)
    {
        var code = request.Code.Trim().ToUpperInvariant();
        var exists = await _db.PromoCodes.AnyAsync(x => x.Code == code, cancellationToken);
        if (exists)
        {
            return Error.Conflict(MessageCodes.Validation.DuplicateCode, $"Promo code '{code}' already exists.");
        }

        var promo = new PromoCode
        {
            Code = code,
            DiscountPercent = request.DiscountPercent.HasValue ? Percentage.FromFraction(request.DiscountPercent.Value) : null,
            DiscountAmount = request.DiscountAmountKwd.HasValue ? new Money(request.DiscountAmountKwd.Value, "KWD") : null,
            MaxUses = request.MaxUses,
            UsedCount = 0,
            StartsAtUtc = request.StartsAtUtc ?? _clock.UtcNow,
            EndsAtUtc = request.EndsAtUtc,
            IsActive = true,
        };

        _db.PromoCodes.Add(promo);
        await _db.SaveChangesAsync(cancellationToken);

        await _audit.LogAsync(new AuditEntry(
            "PromoCodeCreated",
            nameof(PromoCode),
            promo.Id,
            After: new { promo.Code, promo.StartsAtUtc, promo.EndsAtUtc, promo.MaxUses },
            SourceChannel: "admin-dashboard"), cancellationToken);

        return MapPromoCodeDto(promo);
    }

    public async Task<Result<DiscountValidationResultDto>> Handle(ValidateDiscountCodeQuery request, CancellationToken cancellationToken)
    {
        var code = request.Code.Trim().ToUpperInvariant();
        var now = _clock.UtcNow;

        // 1. Check PromoCode
        var promo = await _db.PromoCodes.FirstOrDefaultAsync(x => x.Code == code, cancellationToken);
        if (promo is not null)
        {
            if (!promo.IsActive)
            {
                return new DiscountValidationResultDto(false, code, "PromoCode", 0m, "Promo code is inactive.");
            }
            if (promo.StartsAtUtc > now || (promo.EndsAtUtc.HasValue && promo.EndsAtUtc.Value < now))
            {
                return new DiscountValidationResultDto(false, code, "PromoCode", 0m, "Promo code has expired or is not yet valid.");
            }
            if (promo.MaxUses.HasValue && promo.UsedCount >= promo.MaxUses.Value)
            {
                return new DiscountValidationResultDto(false, code, "PromoCode", 0m, "Promo code usage limit reached.");
            }

            decimal discountAmount = 0m;
            if (promo.DiscountPercent is not null)
            {
                discountAmount = request.TotalAmountKwd * promo.DiscountPercent.Value;
            }
            else if (promo.DiscountAmount is not null)
            {
                discountAmount = Math.Min(request.TotalAmountKwd, promo.DiscountAmount.Amount);
            }

            return new DiscountValidationResultDto(true, code, "PromoCode", Math.Round(discountAmount, 3), null);
        }

        // 2. Check ReferralCode
        var referral = await _db.ReferralCodes.FirstOrDefaultAsync(x => x.Code == code, cancellationToken);
        if (referral is not null)
        {
            if (!referral.IsActive)
            {
                return new DiscountValidationResultDto(false, code, "ReferralCode", 0m, "Referral code is inactive.");
            }
            if (referral.MaxUses.HasValue && referral.UsedCount >= referral.MaxUses.Value)
            {
                return new DiscountValidationResultDto(false, code, "ReferralCode", 0m, "Referral code usage limit reached.");
            }

            var rewardPct = referral.RewardPercent?.Value ?? 0.10m; // Default 10% referral reward
            var discountAmount = Math.Round(request.TotalAmountKwd * rewardPct, 3);

            return new DiscountValidationResultDto(true, code, "ReferralCode", discountAmount, null);
        }

        return new DiscountValidationResultDto(false, code, "Unknown", 0m, $"Discount code '{code}' was not found.");
    }

    public async Task<Result<PagedResult<PromoCodeDto>>> Handle(GetPromoCodesListQuery request, CancellationToken cancellationToken)
    {
        var query = _db.PromoCodes.AsNoTracking().AsQueryable();

        var totalCount = await query.CountAsync(cancellationToken);
        var pageReq = new PageRequest { Page = request.Page, PageSize = request.PageSize };

        var items = await query
            .OrderByDescending(x => x.CreatedAtUtc)
            .Skip(pageReq.Skip)
            .Take(pageReq.NormalizedPageSize)
            .ToListAsync(cancellationToken);

        var dtos = items.Select(MapPromoCodeDto).ToList();

        return new PagedResult<PromoCodeDto>(dtos, totalCount, pageReq.NormalizedPage, pageReq.NormalizedPageSize);
    }

    private static FamilyMemberDto MapFamilyMemberDto(FamilySubscription m) => new(
        m.Id,
        m.PrimarySubscriptionId,
        m.MemberCustomerUserId,
        m.DisplayName,
        m.Relationship,
        m.MaxMealsAllowedPerDay,
        m.IsActive,
        m.CreatedAtUtc);

    private static PromoCodeDto MapPromoCodeDto(PromoCode p) => new(
        p.Id,
        p.Code,
        p.DiscountPercent?.Value,
        p.DiscountAmount?.Amount,
        p.MaxUses,
        p.UsedCount,
        p.StartsAtUtc,
        p.EndsAtUtc,
        p.IsActive,
        p.CreatedAtUtc);
}
