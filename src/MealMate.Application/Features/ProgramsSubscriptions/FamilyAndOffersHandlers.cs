using FluentValidation;
using System.Text.Json;
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
        RuleFor(x => x.MaxUses).GreaterThan(0).When(x => x.MaxUses.HasValue);
        RuleFor(x => x.PerCustomerLimit).GreaterThan(0).When(x => x.PerCustomerLimit.HasValue);
        When(x => x.DiscountPercent.HasValue, () => RuleFor(x => x.DiscountPercent!.Value).InclusiveBetween(0.01m, 1.0m));
        When(x => x.DiscountAmountKwd.HasValue, () => RuleFor(x => x.DiscountAmountKwd!.Value).GreaterThan(0m));
        When(x => x.SubscriptionType is not null, () => RuleFor(x => x.SubscriptionType!).Must(x =>
            x is "Individual" or "Family" or "FamilyBundle"));
    }
}

// Handlers
public sealed class FamilyAndOffersHandlers :
    ICommandHandler<AddFamilyMemberCommand, FamilyMemberDto>,
    ICommandHandler<RemoveFamilyMemberCommand, bool>,
    IQueryHandler<GetFamilyMembersQuery, IReadOnlyList<FamilyMemberDto>>,
    ICommandHandler<CreatePromoCodeCommand, PromoCodeDto>,
    IQueryHandler<ValidateDiscountCodeQuery, DiscountValidationResultDto>,
    IQueryHandler<GetPromoCodesListQuery, PagedResult<PromoCodeDto>>,
    ICommandHandler<SetPromoCodeActiveCommand, PromoCodeDto>
{
    private readonly IAppDbContext _db;
    private readonly IAuditLogger _audit;
    private readonly IDateTimeProvider _clock;
    private readonly ICurrentUser _currentUser;

    public FamilyAndOffersHandlers(IAppDbContext db, IAuditLogger audit, IDateTimeProvider clock, ICurrentUser currentUser)
    {
        _db = db;
        _audit = audit;
        _clock = clock;
        _currentUser = currentUser;
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

        if (request.ProgramId.HasValue && !await _db.Programs.AnyAsync(x => x.Id == request.ProgramId.Value, cancellationToken))
            return Error.Validation("promo_code.program_not_found", "The selected program does not exist.");
        if (request.BundleId.HasValue)
        {
            var bundle = await _db.Bundles.AsNoTracking().Where(x => x.Id == request.BundleId.Value)
                .Select(x => new { x.Id, x.ProgramId }).FirstOrDefaultAsync(cancellationToken);
            if (bundle is null) return Error.Validation("promo_code.bundle_not_found", "The selected bundle does not exist.");
            if (request.ProgramId.HasValue && bundle.ProgramId != request.ProgramId.Value)
                return Error.Validation("promo_code.target_mismatch", "The selected bundle does not belong to the selected program.");
        }

        var promo = new PromoCode
        {
            Code = code,
            DiscountPercent = request.DiscountPercent.HasValue ? Percentage.FromFraction(request.DiscountPercent.Value) : null,
            DiscountAmount = request.DiscountAmountKwd.HasValue ? new Money(request.DiscountAmountKwd.Value, "KWD") : null,
            MaxUses = request.MaxUses,
            PerCustomerLimit = request.PerCustomerLimit,
            UsedCount = 0,
            StartsAtUtc = request.StartsAtUtc ?? _clock.UtcNow,
            EndsAtUtc = request.EndsAtUtc,
            IsActive = true,
            TargetingJson = JsonSerializer.Serialize(new PromoTargeting(
                request.CustomerUserId, request.ProgramId, request.BundleId, request.SubscriptionType)),
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

        // Public validation returns the code's RULE (percent/fixed amount), never an amount derived
        // from a client-supplied total; the binding discount is computed at quote time, server-side.
        // request.SubscriptionId is deliberately ignored: no cross-customer subscription probing.

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

            if (!IsEligibleTarget(promo, request, _currentUser))
            {
                return new DiscountValidationResultDto(false, code, "PromoCode", 0m,
                    "Promo code is not eligible for this customer or subscription.", EligibilityCode: "target_mismatch");
            }

            if (promo.DiscountPercent is not null)
            {
                return new DiscountValidationResultDto(true, code, "PromoCode", 0m, null,
                    DiscountPercent: promo.DiscountPercent.Value,
                    EligibilityCode: "rule_only_amount_set_at_quote");
            }

            if (promo.DiscountAmount is not null)
            {
                // Server-stored fixed rule; reported as the rule value, not a client-derived computation.
                return new DiscountValidationResultDto(true, code, "PromoCode", promo.DiscountAmount.Amount, null,
                    EligibilityCode: "fixed_amount_rule");
            }

            return new DiscountValidationResultDto(false, code, "PromoCode", 0m, "Promo code has no discount rule configured.");
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

            // No implicit reward percentage: an unset policy is an honest failure, never a silent 10%.
            if (referral.RewardPercent is null)
            {
                return new DiscountValidationResultDto(false, code, "ReferralCode", 0m,
                    "Referral reward policy is not configured for this code.",
                    EligibilityCode: "reward_policy_unset");
            }

            return new DiscountValidationResultDto(true, code, "ReferralCode", 0m, null,
                DiscountPercent: referral.RewardPercent.Value,
                EligibilityCode: "rule_only_amount_set_at_quote");
        }

        return new DiscountValidationResultDto(false, code, "Unknown", 0m, $"Discount code '{code}' was not found.");
    }

    private static bool IsEligibleTarget(PromoCode promo, ValidateDiscountCodeQuery request, ICurrentUser currentUser)
    {
        if (string.IsNullOrWhiteSpace(promo.TargetingJson)) return true;
        PromoTargeting? target;
        try { target = JsonSerializer.Deserialize<PromoTargeting>(promo.TargetingJson); }
        catch (JsonException) { return false; }
        if (target is null) return false;
        if (target.CustomerUserId.HasValue)
        {
            if (!currentUser.IsAuthenticated || !currentUser.UserId.HasValue ||
                target.CustomerUserId != currentUser.UserId.Value ||
                (request.CustomerUserId.HasValue && request.CustomerUserId != currentUser.UserId.Value))
                return false;
        }
        if (target.ProgramId.HasValue && target.ProgramId != request.ProgramId) return false;
        if (target.BundleId.HasValue && target.BundleId != request.BundleId) return false;
        if (!string.IsNullOrWhiteSpace(target.SubscriptionType) &&
            !string.Equals(target.SubscriptionType, request.SubscriptionType, StringComparison.Ordinal)) return false;
        return true;
    }

    private sealed record PromoTargeting(Guid? CustomerUserId, Guid? ProgramId, Guid? BundleId, string? SubscriptionType);

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

    public async Task<Result<PromoCodeDto>> Handle(SetPromoCodeActiveCommand request, CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(request.Reason))
            return Error.Validation(MessageCodes.Validation.Required, "A reason is required.");
        var promo = await _db.PromoCodes.FirstOrDefaultAsync(x => x.Id == request.PromoCodeId, cancellationToken);
        if (promo is null) return Error.NotFound(MessageCodes.Common.NotFound, "Promo code was not found.");
        promo.IsActive = request.IsActive;
        await _db.SaveChangesAsync(cancellationToken);
        await _audit.LogAsync(new AuditEntry("PromoCodeActivationChanged", nameof(PromoCode), promo.Id,
            After: new { promo.IsActive, Reason = request.Reason.Trim() }, SourceChannel: "admin-dashboard"), cancellationToken);
        return MapPromoCodeDto(promo);
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
        p.CreatedAtUtc,
        GetTargeting(p).CustomerUserId,
        GetTargeting(p).ProgramId,
        GetTargeting(p).BundleId,
        GetTargeting(p).SubscriptionType,
        p.PerCustomerLimit);

    private static PromoTargeting GetTargeting(PromoCode promo)
    {
        if (string.IsNullOrWhiteSpace(promo.TargetingJson)) return new(null, null, null, null);
        try { return JsonSerializer.Deserialize<PromoTargeting>(promo.TargetingJson) ?? new(null, null, null, null); }
        catch (JsonException) { return new(null, null, null, null); }
    }
}
