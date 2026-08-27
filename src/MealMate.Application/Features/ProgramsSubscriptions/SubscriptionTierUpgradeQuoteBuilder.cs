using System.Text.Json;
using MealMate.Application.Common.Abstractions;
using MealMate.Application.Common.Storage;
using MealMate.Application.Features.CustomerCheckout;
using MealMate.Application.Features.ProgramsSubscriptions.Pricing;
using MealMate.Domain.Enums;
using MealMate.Domain.ProgramsSubscriptions;
using Microsoft.EntityFrameworkCore;

namespace MealMate.Application.Features.ProgramsSubscriptions;

public sealed record SubscriptionTierUpgradeOptionQuote(
    RestaurantTier TargetTier,
    string TierNameAr,
    string TierNameEn,
    string? LogoUrl,
    bool IsCurrent,
    bool Selectable,
    int AccessibleRestaurantCount,
    decimal CurrentRemainingCredit,
    decimal TargetRemainingPrice,
    decimal GatewayRecovery,
    decimal PriceDifference,
    string CurrencyCode,
    int RemainingServiceDays,
    int TotalServiceDays);

public sealed record SubscriptionTierUpgradeQuoteBundle(
    int RemainingServiceDays,
    int TotalServiceDays,
    IReadOnlyList<SubscriptionTierUpgradeOptionQuote> Options,
    string? PricingUnavailableReason);

public interface ISubscriptionTierUpgradeQuoteBuilder
{
    Task<SubscriptionTierUpgradeQuoteBundle> BuildAsync(Subscription subscription, CancellationToken cancellationToken = default);
    Task<SubscriptionTierUpgradeOptionQuote?> BuildForTargetAsync(Subscription subscription, RestaurantTier targetTier, CancellationToken cancellationToken = default);
}

public sealed class SubscriptionTierUpgradeQuoteBuilder : ISubscriptionTierUpgradeQuoteBuilder
{
    private readonly IAppDbContext _db;
    private readonly IDateTimeProvider _clock;
    private readonly IRestaurantTierClassificationService _classification;
    private readonly ISubscriptionScheduleService _schedule;

    public SubscriptionTierUpgradeQuoteBuilder(
        IAppDbContext db,
        IDateTimeProvider clock,
        IRestaurantTierClassificationService classification,
        ISubscriptionScheduleService schedule)
        => (_db, _clock, _classification, _schedule) = (db, clock, classification, schedule);

    public async Task<SubscriptionTierUpgradeQuoteBundle> BuildAsync(Subscription subscription, CancellationToken cancellationToken = default)
    {
        var (remaining, total) = await ResolveServiceDaysAsync(subscription, cancellationToken);
        var pricingRow = await _db.SystemSettings.AsNoTracking()
            .FirstOrDefaultAsync(x => x.Key == SubscriptionPricingSettingsDefaults.Key && x.IsActive, cancellationToken);
        var pricing = SubscriptionPricingSettingsReader.Deserialize(pricingRow?.ValueJson);
        var labels = pricing.TierLabels;

        var classification = await _classification.ResolveAsync(subscription.ProgramId, subscription.BundleId, eligibleOnly: true, cancellationToken);
        var fridayDays = CountFridayDays(subscription);
        var currency = subscription.PaidAmount.CurrencyCode;

        string? pricingUnavailable = null;
        var options = new List<SubscriptionTierUpgradeOptionQuote>();
        foreach (var tier in new[] { RestaurantTier.Basic, RestaurantTier.Platinum, RestaurantTier.Elite })
        {
            if (tier < subscription.SelectedTier) continue;

            var (nameAr, nameEn, logoUrl) = ResolveTierInfo(tier, labels);
            var accessibleCount = classification.Assignments.Count(a => RestaurantTierAccess.CanSelect(tier, a.Tier));
            var isCurrent = tier == subscription.SelectedTier;

            if (isCurrent)
            {
                options.Add(new SubscriptionTierUpgradeOptionQuote(
                    tier, nameAr, nameEn, logoUrl, true, false, accessibleCount,
                    0m, 0m, 0m, 0m, currency, remaining, total));
                continue;
            }

            var preview = await TryPreviewAsync(subscription, tier, fridayDays, cancellationToken);
            if (preview is null)
            {
                pricingUnavailable ??= "pricing.unavailable";
                options.Add(new SubscriptionTierUpgradeOptionQuote(
                    tier, nameAr, nameEn, logoUrl, false, false, accessibleCount,
                    0m, 0m, 0m, 0m, currency, remaining, total));
                continue;
            }

            var quote = SubscriptionTierUpgradeQuote.Calculate(
                subscription.PaidAmount.Amount, remaining, total, preview);
            options.Add(new SubscriptionTierUpgradeOptionQuote(
                tier, nameAr, nameEn, logoUrl, false, remaining > 0,
                accessibleCount,
                quote.CurrentRemainingCredit,
                quote.TargetRemainingPrice,
                quote.GatewayRecovery,
                quote.TotalDueNow,
                currency,
                remaining,
                total));
        }

        return new SubscriptionTierUpgradeQuoteBundle(remaining, total, options, pricingUnavailable);
    }

    public async Task<SubscriptionTierUpgradeOptionQuote?> BuildForTargetAsync(
        Subscription subscription,
        RestaurantTier targetTier,
        CancellationToken cancellationToken = default)
    {
        var bundle = await BuildAsync(subscription, cancellationToken);
        return bundle.Options.FirstOrDefault(x => x.TargetTier == targetTier && x.Selectable);
    }

    public static string SerializeQuoteJson(SubscriptionTierUpgradeOptionQuote option) =>
        JsonSerializer.Serialize(new
        {
            option.CurrentRemainingCredit,
            option.TargetRemainingPrice,
            option.GatewayRecovery,
            option.PriceDifference,
            option.RemainingServiceDays,
            option.TotalServiceDays,
            option.AccessibleRestaurantCount,
        });

    private async Task<(int Remaining, int Total)> ResolveServiceDaysAsync(Subscription subscription, CancellationToken ct)
    {
        var days = await _db.SubscriptionDays.AsNoTracking()
            .Where(x => x.SubscriptionId == subscription.Id)
            .Select(x => x.Status)
            .ToListAsync(ct);
        var total = days.Count > 0 ? days.Count : Math.Max(1, subscription.DurationDays);
        var completed = days.Count(x => x == SubscriptionDayStatus.Delivered);
        var remaining = Math.Max(0, total - completed);
        return (remaining, total);
    }

    private int CountFridayDays(Subscription subscription)
    {
        if (!subscription.IncludeFriday) return 0;
        var dates = _schedule.GetServiceDates(subscription.StartDate, subscription.DurationDays, true);
        return _schedule.CountFridays(dates);
    }

    private async Task<PricingEngineResult?> TryPreviewAsync(
        Subscription subscription,
        RestaurantTier tier,
        int fridayDays,
        CancellationToken cancellationToken)
    {
        var activePrices = await _db.RestaurantBundlePricings
            .AsNoTracking()
            .Where(x => x.ProgramId == subscription.ProgramId
                && x.BundleId == subscription.BundleId
                && x.EffectiveFromUtc <= _clock.UtcNow
                && (x.EffectiveToUtc == null || x.EffectiveToUtc > _clock.UtcNow)
                && (x.LockStatus == PriceLockStatus.Locked
                    || x.LockStatus == PriceLockStatus.Approved
                    || x.LockStatus == PriceLockStatus.ApprovedWithEffectiveDate))
            .Select(x => x.ReferencePrice26.Amount)
            .ToListAsync(cancellationToken);
        if (activePrices.Count == 0) return null;
        if (!await _db.PricingRuleVersions.AsNoTracking().AnyAsync(x => x.IsActive, cancellationToken))
            return null;

        var meanA_t = SubscriptionPackagePricingCalculator.ResolveAverageDailyPrice(activePrices, tier);
        var settingsRow = await _db.SystemSettings.AsNoTracking()
            .FirstOrDefaultAsync(x => x.Key == SubscriptionPricingSettingsDefaults.Key && x.IsActive, cancellationToken);
        var settings = SubscriptionPricingSettingsReader.Deserialize(settingsRow?.ValueJson);
        var profile = settings.Mode == "per_bundle" && settings.PerBundle.TryGetValue(subscription.BundleId, out var customProfile)
            ? customProfile
            : settings.Global;
        var durationRate = profile.ResolveRate(subscription.DurationDays);

        return PricingEngine.Calculate(new PricingEngineRequest(
            MeanReferenceDailyPrice: meanA_t,
            DurationDays: subscription.DurationDays,
            CampaignDiscountKwd: 0m,
            PromoCodeDiscountKwd: 0m,
            DurationRatesPct: new Dictionary<int, decimal> { [subscription.DurationDays] = durationRate },
            FridayDays: fridayDays,
            FridayRatePct: profile.FridayRate));
    }

    private static (string NameAr, string NameEn, string? LogoUrl) ResolveTierInfo(RestaurantTier tier, TierLabelsSettings labels) => tier switch
    {
        RestaurantTier.Basic => (labels.BasicAr, labels.BasicEn, CatalogImageStorage.ToContentUrl(labels.BasicLogoStorageKey)),
        RestaurantTier.Platinum => (labels.PlatinumAr, labels.PlatinumEn, CatalogImageStorage.ToContentUrl(labels.PlatinumLogoStorageKey)),
        RestaurantTier.Elite => (labels.EliteAr, labels.EliteEn, CatalogImageStorage.ToContentUrl(labels.EliteLogoStorageKey)),
        _ => (tier.ToString(), tier.ToString(), null),
    };
}
