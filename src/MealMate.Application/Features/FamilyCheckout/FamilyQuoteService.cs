using MealMate.Application.Common.Abstractions;
using MealMate.Application.Common.Models;
using MealMate.Application.Features.CustomerCheckout;
using MealMate.Application.Features.ProgramsSubscriptions;
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
    Task<Result<FamilyQuoteResult>> QuoteAsync(FamilyGroup group, CancellationToken ct);
}

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
    string PackageSelectionMode = "selected");

public sealed class FamilyQuoteService : IFamilyQuoteService
{
    private readonly ISender _sender;
    private readonly IFamilyPricingSettingsReader _settingsReader;
    private readonly ISubscriptionScheduleService _schedule;

    public FamilyQuoteService(
        ISender sender,
        IFamilyPricingSettingsReader settingsReader,
        ISubscriptionScheduleService schedule)
    {
        _sender = sender;
        _settingsReader = settingsReader;
        _schedule = schedule;
    }

    public async Task<Result<FamilyQuoteResult>> QuoteAsync(FamilyGroup group, CancellationToken ct)
    {
        var settings = await _settingsReader.GetAsync(ct);
        var lines = new List<FamilyQuoteLine>();
        var currency = "KWD";

        foreach (var member in group.Members.Where(FamilyMemberPlanRules.HasPricablePlan))
        {
            if (member.MemberSubscriptionId is not null && group.ConvertedFromSubscriptionId is null)
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
            currency = result.CurrencyCode;
            var prepaidManager = group.ConvertedFromSubscriptionId is not null
                                 && member.Role == FamilyMemberRole.Manager;
            lines.Add(new FamilyQuoteLine(
                member.Id,
                member.DisplayName,
                prepaidManager ? 0m : result.FinalCustomerPrice,
                prepaidManager ? 0m : result.CampaignDiscountApplied,
                prepaidManager ? 0m : result.PromoCodeDiscountApplied,
                prepaidManager ? 0m : result.GatewayCostRecoveryAmount));
        }

        // Payable = non-prepaid lines (converted manager is already paid).
        var payableLines = lines.Where(x => x.MemberPrice > 0m).ToList();
        var subtotal = payableLines.Sum(x => x.MemberPrice);
        var eligibleSubscriptionIds = group.Members
            .Where(m => m.MemberSubscriptionId is not null
                        && m.MemberSubscription is not null
                        && IsEligiblePaidStatus(m.MemberSubscription.Status))
            .Select(m => m.MemberSubscriptionId!.Value)
            .ToHashSet();
        var eligiblePaidCount = eligibleSubscriptionIds.Count;
        var policy = FamilyPackagePolicy.Resolve(settings, group, eligiblePaidCount, payableLines.Count);
        if (policy.IsFailure)
            return policy.Error;

        var package = policy.Value.EffectivePackage;
        var withinPackageRange = policy.Value.ParticipantCount >= package.MinMembers
                                 && policy.Value.ParticipantCount <= package.MaxMembers;
        var discountApplied = withinPackageRange && payableLines.Count > 0;
        var discountPercent = package.DiscountPercent;
        var familyDiscountAmount = discountApplied
            ? Math.Round(subtotal * discountPercent, 3, MidpointRounding.AwayFromZero)
            : 0m;
        var finalPrice = subtotal - familyDiscountAmount;

        return new FamilyQuoteResult(
            lines,
            subtotal,
            discountPercent,
            familyDiscountAmount,
            finalPrice,
            currency,
            discountApplied && familyDiscountAmount > 0m,
            package.Id,
            package.MinMembers,
            package.MaxMembers,
            policy.Value.ParticipantCount,
            policy.Value.PackageSelectionMode);
    }

    private static bool IsEligiblePaidStatus(SubscriptionStatus status)
        => status is SubscriptionStatus.Paid
            or SubscriptionStatus.Scheduled
            or SubscriptionStatus.Active
            or SubscriptionStatus.Frozen
            or SubscriptionStatus.SuspendedForReview;
}
