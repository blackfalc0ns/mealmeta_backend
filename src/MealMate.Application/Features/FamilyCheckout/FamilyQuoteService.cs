using MealMate.Application.Common.Abstractions;
using MealMate.Application.Common.Models;
using MealMate.Application.Features.CustomerCheckout;
using MealMate.Application.Features.ProgramsSubscriptions;
using MealMate.Application.Features.ProgramsSubscriptions.Flow;
using MealMate.Domain.Enums;
using MealMate.Domain.ProgramsSubscriptions;
using MediatR;
using Microsoft.EntityFrameworkCore;

namespace MealMate.Application.Features.FamilyCheckout;

public interface IFamilyPricingSettingsReader
{
    Task<FamilyPricingSettings> GetAsync(CancellationToken cancellationToken = default);
}

public sealed class FamilyPricingSettingsReader : IFamilyPricingSettingsReader
{
    private readonly IAppDbContext _db;

    public FamilyPricingSettingsReader(IAppDbContext db) => _db = db;

    public async Task<FamilyPricingSettings> GetAsync(CancellationToken cancellationToken = default)
    {
        var row = await _db.SystemSettings.AsNoTracking()
            .FirstOrDefaultAsync(x => x.Key == SubscriptionPricingSettingsDefaults.Key && x.IsActive, cancellationToken);
        return SubscriptionPricingSettingsReader.Deserialize(row?.ValueJson).Family;
    }
}

public interface IFamilyQuoteService
{
    Task<Result<FamilyQuoteResult>> QuoteAsync(FamilyGroup group, CancellationToken ct, FamilyQuoteScope? scope = null);
}

public sealed record FamilyQuoteScope(IReadOnlySet<Guid> ActiveMemberIds, IReadOnlySet<Guid>? SelectedMemberIds,
    string? PromoCode = null, Guid? PayerCustomerUserId = null);

public sealed record FamilyQuoteLine(
    Guid MemberId,
    string DisplayName,
    decimal MemberPrice,
    decimal CampaignDiscount,
    decimal PromoDiscount,
    decimal GatewayFee);

public sealed record FamilyQuoteResult(
    IReadOnlyList<FamilyQuoteLine> Lines,
    decimal Subtotal,
    decimal FamilyDiscountPercent,
    decimal FamilyDiscountAmount,
    decimal FinalPrice,
    string CurrencyCode,
    bool DiscountApplied,
    Guid? EffectiveFamilyPackageId = null,
    int? EffectiveFamilyPackageMinMembers = null,
    int? EffectiveFamilyPackageMaxMembers = null,
    int ParticipantCount = 0,
    string PackageSelectionMode = "selected",
    string? PackageNameAr = null,
    string? PackageNameEn = null,
    int OccupiedSeatCount = 0,
    int AvailableSeatCount = 0,
    int DeferredMemberCount = 0,
    int CurrentPurchaseCount = 0,
    int RequiredParticipantCount = 2,
    bool ManagerSeatExempt = false,
    bool ManagerIndividualIsLive = false,
    Guid? ManagerIndividualSubscriptionId = null,
    bool IsInitialPurchaseOrReactivation = true,
    bool ManagerMustBeFunded = false,
    decimal PromoCodeDiscountAmount = 0m,
    SubscriptionCouponEvaluation? CouponEvaluation = null,
    Guid? CouponTargetMemberId = null,
    Guid? CouponHolderMemberId = null);

public sealed class FamilyQuoteService : IFamilyQuoteService
{
    private readonly ISender _sender;
    private readonly IFamilyPricingSettingsReader _settingsReader;
    private readonly ISubscriptionScheduleService _schedule;
    private readonly IAppDbContext? _db;
    private readonly FamilyParticipationContextReader? _participation;
    private readonly ISubscriptionFlowCouponService? _coupons;

    public FamilyQuoteService(
        ISender sender,
        IFamilyPricingSettingsReader settingsReader,
        ISubscriptionScheduleService schedule,
        IAppDbContext? db = null,
        FamilyParticipationContextReader? participation = null,
        ISubscriptionFlowCouponService? coupons = null)
    {
        _sender = sender;
        _settingsReader = settingsReader;
        _schedule = schedule;
        _db = db;
        _participation = participation;
        _coupons = coupons;
    }

    public async Task<Result<FamilyQuoteResult>> QuoteAsync(FamilyGroup group, CancellationToken ct, FamilyQuoteScope? scope = null)
    {
        scope ??= _db is null
            ? new FamilyQuoteScope(group.Members.Select(x => x.Id).ToHashSet(), new HashSet<Guid>())
            : new FamilyQuoteScope(await FamilyCheckoutGate.ActiveAccessMemberIdsAsync(_db, group, ct),
                await FamilyCheckoutSelectionService.GetSelectedMemberIdsOrNullAsync(_db, group.Id, ct));
        var participation = _participation is null
            ? null
            : await _participation.GetAsync(group, scope.SelectedMemberIds, ct);
        var settings = await _settingsReader.GetAsync(ct);
        var lines = new List<FamilyQuoteLine>();
        string? currency = null;

        foreach (var member in group.Members.Where(m => scope.ActiveMemberIds.Contains(m.Id)
            && scope.SelectedMemberIds?.Contains(m.Id) == true && FamilyMemberPlanRules.HasPricablePlan(m)))
        {
            var isManagerIndividual = member.Role == FamilyMemberRole.Manager && member.MemberSubscriptionId is not null
                && (member.MemberSubscriptionId == participation?.ManagerIndividualSubscriptionId
                    || member.MemberSubscriptionId == group.ConvertedFromSubscriptionId
                    || (participation is null && group.ConvertedFromSubscriptionId is not null));
            if (member.MemberSubscriptionId is not null && !isManagerIndividual
                && participation?.PurchasableMemberIds.Contains(member.Id) != true)
                continue;
            if (member.MemberSubscriptionId is not null && participation is null && !isManagerIndividual)
                continue;
            if (isManagerIndividual && participation is null)
                continue;
            if (isManagerIndividual && participation?.ManagerSeatExempt == true && participation.ManagerIndividualIsLive)
                continue;

            // Price plan-complete members; payment opens without requiring Ready (address/age).
            var days = member.DurationDays!.Value;
            var fridayDays = member.IncludeFriday ? _schedule.GetFridayAddonDays(days) : 0;
            var pricing = await _sender.Send(
                new PreviewCustomerPricingQuery(
                    member.ProgramId!.Value,
                    member.BundleId!.Value,
                    member.SelectedTier!.Value,
                    days,
                    IncludeFriday: member.IncludeFriday,
                    FridayDays: fridayDays),
                ct);

            if (pricing.IsFailure)
                return pricing.Error;

            var result = pricing.Value;
            if (currency is not null && !string.Equals(currency, result.CurrencyCode, StringComparison.OrdinalIgnoreCase))
                return Error.Conflict("family.currency_mismatch");
            currency ??= result.CurrencyCode;
            lines.Add(new FamilyQuoteLine(
                member.Id,
                member.DisplayName,
                result.FinalCustomerPrice,
                result.CampaignDiscountApplied,
                result.PromoCodeDiscountApplied,
                result.GatewayCostRecoveryAmount));
        }

        // Payable = non-prepaid lines (converted manager is already paid).
        var payableLines = lines.Where(x => x.MemberPrice > 0m).ToList();
        var subtotal = payableLines.Sum(x => x.MemberPrice);
        var eligibleSubscriptionIds = group.Members
            .Where(m => scope.ActiveMemberIds.Contains(m.Id) && m.MemberSubscriptionId is not null
                        && m.MemberSubscription is not null
                        && m.MemberSubscriptionId != participation?.ManagerIndividualSubscriptionId
                        && m.MemberSubscriptionId != group.ConvertedFromSubscriptionId
                        && IsEligiblePaidStatus(m.MemberSubscription.Status))
            .Select(m => m.MemberSubscriptionId!.Value)
            .ToHashSet();
        var legacyConvertedManagerCount = participation is null && group.ConvertedFromSubscriptionId is Guid convertedId
            && group.Members.Any(x => x.Role == FamilyMemberRole.Manager && x.MemberSubscriptionId == convertedId) ? 1 : 0;
        var eligiblePaidCount = participation is null
            ? eligibleSubscriptionIds.Count + legacyConvertedManagerCount
            : Math.Max(0, participation.QualifyingParticipantCount - payableLines.Count);
        currency ??= group.CurrencyCode;
        if (payableLines.Count == 0)
            return new FamilyQuoteResult([], 0m, 0m, 0m, 0m, currency, false, ParticipantCount: participation?.QualifyingParticipantCount ?? eligiblePaidCount,
                OccupiedSeatCount: participation?.OccupiedSeats ?? 0,
                AvailableSeatCount: participation?.AvailableSeats ?? 0,
                DeferredMemberCount: participation?.DeferredMemberCount ?? 0,
                CurrentPurchaseCount: participation?.CurrentPurchaseCount ?? 0,
                RequiredParticipantCount: participation?.IsInitialPurchaseOrReactivation == false ? 1 : 2,
                ManagerSeatExempt: participation?.ManagerSeatExempt ?? group.ManagerIndividualSeatExempt ?? false,
                ManagerIndividualIsLive: participation?.ManagerIndividualIsLive ?? false,
                ManagerIndividualSubscriptionId: participation?.ManagerIndividualSubscriptionId,
                IsInitialPurchaseOrReactivation: participation?.IsInitialPurchaseOrReactivation ?? group.Status == FamilyGroupStatus.Draft,
                ManagerMustBeFunded: participation?.ManagerMustBeFunded ?? false);
        var policy = FamilyPackagePolicy.Resolve(settings, group, eligiblePaidCount, payableLines.Count);
        if (policy.IsFailure)
            return policy.Error;

        var package = policy.Value.EffectivePackage;
        var fundedCount = participation?.QualifyingParticipantCount ?? policy.Value.ParticipantCount;
        var withinPackageRange = (group.Status == FamilyGroupStatus.Active || fundedCount >= 2)
                                 && (participation?.OccupiedSeats ?? policy.Value.ParticipantCount) <= package.MaxMembers;
        var discountApplied = withinPackageRange && payableLines.Count > 0;
        var discountPercent = group.Status == FamilyGroupStatus.Active
            ? group.DiscountPercentSnapshot
            : package.DiscountPercent;
        var familyDiscountAmount = discountApplied
            ? Math.Round(subtotal * discountPercent, FamilyPaymentAllocationService.CurrencyMinorDigits(currency), MidpointRounding.AwayFromZero)
            : 0m;
        SubscriptionCouponEvaluation? couponEvaluation = null;
        Guid? couponTargetMemberId = null;
        Guid? couponHolderMemberId = null;
        decimal promoDiscount = 0m;
        var familyEligibleMemberIds = new HashSet<Guid>();
        var familyEligibleSubtotal = 0m;
        if (!string.IsNullOrWhiteSpace(scope.PromoCode))
        {
            if (_db is null || _coupons is null || scope.PayerCustomerUserId is not Guid payerId)
                return Error.Validation("family.coupon_context_unavailable");
            var targetedPromo = await _db.PromoCodes.AsNoTracking()
                .FirstOrDefaultAsync(x => x.Code.ToUpper() == scope.PromoCode.Trim().ToUpper(), ct);
            var target = ReadTargeting(targetedPromo?.TargetingJson);
            // A Family coupon applies once to the aggregate eligible set. Program/bundle
            // targets limit that set, so unrelated selected members never receive a discount.
            var familyEligibleMembers = group.Members.Where(m => scope.SelectedMemberIds?.Contains(m.Id) == true
                    && scope.ActiveMemberIds.Contains(m.Id) && m.MemberSubscriptionId is null
                    && FamilyMemberPlanRules.HasPricablePlan(m)
                    && (target?.ProgramId.HasValue != true || target.ProgramId == m.ProgramId)
                    && (target?.BundleId.HasValue != true || target.BundleId == m.BundleId))
                .ToList();
            familyEligibleMemberIds = familyEligibleMembers.Select(x => x.Id).ToHashSet();
            familyEligibleSubtotal = payableLines.Where(l => familyEligibleMemberIds.Contains(l.MemberId)).Sum(x => x.MemberPrice);
            foreach (var member in familyEligibleMembers)
            {
                var familyEvaluation = await _coupons.EvaluateAsync(scope.PromoCode, payerId, member.ProgramId!.Value,
                    member.BundleId!.Value, member.DurationDays!.Value, familyEligibleSubtotal, ct, subscriptionType: "Family");
                if (familyEvaluation.IsValid)
                {
                    couponEvaluation = familyEvaluation;
                    couponHolderMemberId = member.Id;
                    break;
                }
            }
            // A FamilyBundle coupon is restricted to the matching actual member bundle line.
            if (couponEvaluation is null || !couponEvaluation.IsValid)
            {
                foreach (var member in group.Members.Where(m => scope.SelectedMemberIds?.Contains(m.Id) == true
                             && scope.ActiveMemberIds.Contains(m.Id) && m.MemberSubscriptionId is null
                             && FamilyMemberPlanRules.HasPricablePlan(m)))
                {
                    var line = payableLines.FirstOrDefault(x => x.MemberId == member.Id);
                    if (line is null) continue;
                    var evaluation = await _coupons.EvaluateAsync(scope.PromoCode, payerId, member.ProgramId!.Value,
                        member.BundleId!.Value, member.DurationDays!.Value, line.MemberPrice, ct,
                        subscriptionType: "FamilyBundle");
                    if (evaluation.IsValid)
                    {
                        couponEvaluation = evaluation;
                        couponTargetMemberId = member.Id;
                        couponHolderMemberId = member.Id;
                        break;
                    }
                }
            }
            if (couponEvaluation is null || !couponEvaluation.IsValid)
                return Error.Validation("family.coupon_rejected", couponEvaluation?.MessageEn ?? "Coupon is not eligible for the selected family members.");

            promoDiscount = couponTargetMemberId.HasValue
                ? Math.Min(payableLines.First(x => x.MemberId == couponTargetMemberId.Value).MemberPrice, couponEvaluation.DiscountAmount)
                : Math.Min(familyEligibleSubtotal, couponEvaluation.DiscountAmount);
            // Campaign/package and coupon reductions do not stack; keep the larger saving.
            if (promoDiscount > familyDiscountAmount)
            {
                familyDiscountAmount = 0m;
                discountApplied = false;
            }
            else { promoDiscount = 0m; couponEvaluation = null; couponTargetMemberId = null; couponHolderMemberId = null; }
        }
        if (promoDiscount > 0m)
        {
            if (couponTargetMemberId is { } oneMember)
            {
                var index = lines.FindIndex(x => x.MemberId == oneMember);
                if (index >= 0) lines[index] = lines[index] with { PromoDiscount = promoDiscount };
            }
            else
            {
                var applicable = lines.Where(x => familyEligibleMemberIds.Contains(x.MemberId)).ToList();
                var remaining = promoDiscount;
                for (var index = 0; index < lines.Count; index++)
                {
                    var line = lines[index];
                    if (!applicable.Any(x => x.MemberId == line.MemberId)) continue;
                    var allocation = line.MemberId == applicable[^1].MemberId
                        ? remaining
                        : Math.Min(remaining, Math.Round(promoDiscount * line.MemberPrice / familyEligibleSubtotal,
                            FamilyPaymentAllocationService.CurrencyMinorDigits(currency), MidpointRounding.AwayFromZero));
                    lines[index] = line with { PromoDiscount = allocation };
                    remaining -= allocation;
                }
            }
        }
        var finalPrice = subtotal - familyDiscountAmount - promoDiscount;

        return new FamilyQuoteResult(
            lines,
            subtotal,
            discountPercent,
            familyDiscountAmount,
            finalPrice,
            currency,
            (discountApplied && familyDiscountAmount > 0m) || promoDiscount > 0m,
            package.Id,
            package.MinMembers,
            package.MaxMembers,
            policy.Value.ParticipantCount,
            policy.Value.PackageSelectionMode, package.NameAr, package.NameEn,
            participation?.OccupiedSeats ?? policy.Value.ParticipantCount,
            participation?.AvailableSeats ?? Math.Max(0, package.MaxMembers - policy.Value.ParticipantCount),
            participation?.DeferredMemberCount ?? 0,
            participation?.CurrentPurchaseCount ?? payableLines.Count,
            participation is null ? package.MinMembers : participation.IsInitialPurchaseOrReactivation ? 2 : 1,
            participation?.ManagerSeatExempt ?? group.ManagerIndividualSeatExempt ?? false,
            participation?.ManagerIndividualIsLive ?? false,
            participation?.ManagerIndividualSubscriptionId,
            participation?.IsInitialPurchaseOrReactivation ?? group.Status == FamilyGroupStatus.Draft,
            participation?.ManagerMustBeFunded ?? false,
            promoDiscount, couponEvaluation, promoDiscount > 0m ? couponTargetMemberId : null,
            promoDiscount > 0m ? couponHolderMemberId : null);
    }

    private static bool IsEligiblePaidStatus(SubscriptionStatus status)
        => status is SubscriptionStatus.Paid
            or SubscriptionStatus.Scheduled
            or SubscriptionStatus.Active
            or SubscriptionStatus.Frozen
            or SubscriptionStatus.SuspendedForReview;

    private static PromoTargeting? ReadTargeting(string? json)
    {
        if (string.IsNullOrWhiteSpace(json)) return null;
        try { return System.Text.Json.JsonSerializer.Deserialize<PromoTargeting>(json); }
        catch (System.Text.Json.JsonException) { return null; }
    }

    private sealed record PromoTargeting(Guid? CustomerUserId, Guid? ProgramId, Guid? BundleId, string? SubscriptionType);
}
