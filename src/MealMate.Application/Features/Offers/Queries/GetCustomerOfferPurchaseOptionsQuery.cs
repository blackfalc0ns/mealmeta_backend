using MealMate.Application.Common.Abstractions;
using MealMate.Application.Common.Messaging;
using MealMate.Application.Common.Models;
using MealMate.Application.Common.Storage;
using MealMate.Application.Features.CustomerCheckout;
using MealMate.Application.Features.IdentityAccess.Customers;
using MealMate.Application.Features.Offers.DTOs;
using MealMate.Application.Features.Offers.Services;
using MealMate.Application.Features.ProgramsSubscriptions;
using MealMate.Application.Features.ProgramsSubscriptions.Flow;
using MealMate.Application.Features.ProgramsSubscriptions.Pricing;
using MealMate.Domain.Enums;
using MealMate.Domain.Offers;
using MealMate.Domain.Offers.Enums;
using Microsoft.EntityFrameworkCore;

namespace MealMate.Application.Features.Offers.Queries;

public sealed record GetCustomerOfferPurchaseOptionsQuery(
    Guid OfferId,
    Guid CustomerId,
    DateOnly? StartDate = null,
    bool IncludeFriday = false
) : IQuery<CustomerOfferPurchaseOptionsResponseDto>;

public sealed class GetCustomerOfferPurchaseOptionsQueryHandler
    : IQueryHandler<GetCustomerOfferPurchaseOptionsQuery, CustomerOfferPurchaseOptionsResponseDto>
{
    private readonly IAppDbContext _db;
    private readonly IOfferEligibilityEngine _eligibilityEngine;
    private readonly IDateTimeProvider _clock;
    private readonly ISubscriptionScheduleService _schedule;
    private readonly IAllowedSubscriptionDurationService _allowedDurations;
    private readonly ICustomerDeliveryAddressService _deliveryAddresses;
    private readonly ICustomerIndividualSubscriptionGate? _individualGate;
    private readonly ISubscriptionRestaurantAvailabilityService? _availability;

    public GetCustomerOfferPurchaseOptionsQueryHandler(
        IAppDbContext db,
        IOfferEligibilityEngine eligibilityEngine,
        IDateTimeProvider clock,
        ISubscriptionScheduleService schedule,
        IAllowedSubscriptionDurationService allowedDurations,
        ICustomerDeliveryAddressService deliveryAddresses,
        ICustomerIndividualSubscriptionGate? individualGate = null,
        ISubscriptionRestaurantAvailabilityService? availability = null)
    {
        _db = db;
        _eligibilityEngine = eligibilityEngine;
        _clock = clock;
        _schedule = schedule;
        _allowedDurations = allowedDurations;
        _deliveryAddresses = deliveryAddresses;
        _individualGate = individualGate;
        _availability = availability;
    }

    public async Task<Result<CustomerOfferPurchaseOptionsResponseDto>> Handle(
        GetCustomerOfferPurchaseOptionsQuery request,
        CancellationToken cancellationToken)
    {
        var now = _clock.UtcNow;

        var offer = await _db.Offers.AsNoTracking()
            .Include(o => o.EligiblePrograms)
            .Include(o => o.EligibleBundles)
            .Include(o => o.PackageTargets)
            .Include(o => o.EligibleRestaurants)
            .FirstOrDefaultAsync(o => o.Id == request.OfferId, cancellationToken);

        if (offer is null || offer.Status != OfferStatus.Active || offer.StartsAtUtc > now || offer.EndsAtUtc < now)
        {
            return Error.NotFound("offers.customer.unavailable", string.Empty);
        }

        var priorSubsCount = await _db.Subscriptions
            .CountAsync(s => s.CustomerUserId == request.CustomerId, cancellationToken);
        var userRedemptions = await _db.OfferRedemptions
            .CountAsync(r => r.OfferId == offer.Id && r.CustomerId == request.CustomerId, cancellationToken);

        var evalCtx = new OfferEligibilityContext(
            request.CustomerId,
            priorSubsCount,
            userRedemptions,
            Purpose: OfferEvaluationPurpose.Discovery);

        var eval = _eligibilityEngine.Evaluate(offer, evalCtx, now);
        if (!eval.IsEligible)
        {
            return Error.NotFound("offers.customer.unavailable", string.Empty);
        }

        var isFamilyOnly = offer.PackageTargets.Count > 0
            && offer.PackageTargets.All(t => string.Equals(t.SubscriptionType, "Family", StringComparison.OrdinalIgnoreCase));

        if (isFamilyOnly)
        {
            var (prereqs, blockers) = await EvaluatePrerequisitesAsync(request.CustomerId, cancellationToken);
            var familyBlocker = new MobileBlockingReasonDto(
                "offers.family_only",
                "هذا العرض متاح للاشتراكات العائلية فقط.",
                "This offer is available for family subscriptions only.",
                Advice: new MobileRetryAdviceDto(false));

            var combinedBlockers = new List<MobileBlockingReasonDto>(blockers) { familyBlocker };

            return new CustomerOfferPurchaseOptionsResponseDto(
                offer.Id,
                "Family",
                now,
                prereqs,
                combinedBlockers,
                Array.Empty<OfferPurchaseOptionDto>(),
                FamilyFlowDestination: $"/family-subscription?offerId={offer.Id}");
        }

        var (prerequisites, generalBlockers) = await EvaluatePrerequisitesAsync(request.CustomerId, cancellationToken);

        if (!_schedule.TryResolveCheckoutStart(request.StartDate, request.IncludeFriday, out var resolvedStartDate, out var startError))
        {
            return Error.Validation(startError ?? "checkout.start_date_invalid");
        }

        var settings = await _allowedDurations.GetSettingsAsync(cancellationToken);
        var activePresets = settings.Durations
            .Where(d => !d.IsCustom && d.IsActive)
            .Select(d => d.Days)
            .ToList();
        if (activePresets.Count == 0)
        {
            activePresets = [6, 12, 26];
        }

        // Candidate tuples: (ProgramId, BundleId, Tier, DurationDays)
        var candidates = new List<(Guid ProgramId, Guid BundleId, RestaurantTier Tier, int DurationDays)>();

        if (offer.PackageTargets.Count > 0)
        {
            var individualTargets = offer.PackageTargets
                .Where(t => string.Equals(t.SubscriptionType, "Individual", StringComparison.OrdinalIgnoreCase))
                .ToList();

            foreach (var target in individualTargets)
            {
                var durations = target.AllowedDurationDays.Length > 0
                    ? target.AllowedDurationDays.ToList()
                    : activePresets;

                foreach (var d in durations)
                {
                    if (offer.MinSubscriptionDays.HasValue && d < offer.MinSubscriptionDays.Value) continue;
                    if (offer.MaxSubscriptionDays.HasValue && d > offer.MaxSubscriptionDays.Value) continue;

                    candidates.Add((target.ProgramId, target.BundleId, target.Tier, d));
                }
            }
        }
        else
        {
            // Unrestricted or legacy scoped offer: discover from active catalog
            var programQuery = _db.Programs.AsNoTracking()
                .Where(p => p.IsActive && p.Status == ProgramStatus.Active);
            if (offer.EligiblePrograms.Count > 0)
            {
                var programIds = offer.EligiblePrograms.Select(p => p.ProgramId).ToList();
                programQuery = programQuery.Where(p => programIds.Contains(p.Id));
            }
            var candidateProgramIds = await programQuery.Select(p => p.Id).ToListAsync(cancellationToken);

            var bundleQuery = _db.Bundles.AsNoTracking()
                .Where(b => candidateProgramIds.Contains(b.ProgramId) && b.IsActive && b.Status == ProgramStatus.Active);
            if (offer.EligibleBundles.Count > 0)
            {
                var bundleIds = offer.EligibleBundles.Select(b => b.BundleId).ToList();
                bundleQuery = bundleQuery.Where(b => bundleIds.Contains(b.Id));
            }
            var catalogBundles = await bundleQuery.Select(b => new { b.ProgramId, b.Id }).ToListAsync(cancellationToken);

            var tiers = new[] { RestaurantTier.Basic, RestaurantTier.Platinum, RestaurantTier.Elite };
            var durations = activePresets
                .Where(d => (!offer.MinSubscriptionDays.HasValue || d >= offer.MinSubscriptionDays.Value)
                    && (!offer.MaxSubscriptionDays.HasValue || d <= offer.MaxSubscriptionDays.Value))
                .ToList();

            foreach (var cb in catalogBundles)
            {
                foreach (var t in tiers)
                {
                    foreach (var d in durations)
                    {
                        candidates.Add((cb.ProgramId, cb.Id, t, d));
                    }
                }
            }
        }

        if (candidates.Count == 0)
        {
            var noEligiblePackagesBlocker = new MobileBlockingReasonDto(
                "offers.no_eligible_packages",
                "لا توجد باقات متاحة متوافقة مع هذا العرض حالياً.",
                "No eligible packages are currently available for this offer.",
                Advice: new MobileRetryAdviceDto(false));
            var emptyBlockers = new List<MobileBlockingReasonDto>(generalBlockers) { noEligiblePackagesBlocker };

            return new CustomerOfferPurchaseOptionsResponseDto(
                offer.Id,
                "Individual",
                now,
                prerequisites,
                emptyBlockers,
                Array.Empty<OfferPurchaseOptionDto>());
        }

        var distinctProgramIds = candidates.Select(c => c.ProgramId).Distinct().ToList();
        var distinctBundleIds = candidates.Select(c => c.BundleId).Distinct().ToList();

        var programs = await _db.Programs.AsNoTracking()
            .Where(p => distinctProgramIds.Contains(p.Id) && p.IsActive && p.Status == ProgramStatus.Active)
            .ToDictionaryAsync(p => p.Id, cancellationToken);

        var bundles = await _db.Bundles.AsNoTracking()
            .Where(b => distinctBundleIds.Contains(b.Id) && b.IsActive && b.Status == ProgramStatus.Active)
            .ToDictionaryAsync(b => b.Id, cancellationToken);

        var activePricings = await _db.RestaurantBundlePricings.AsNoTracking()
            .Where(x => distinctBundleIds.Contains(x.BundleId)
                && x.EffectiveFromUtc <= now
                && (x.EffectiveToUtc == null || x.EffectiveToUtc > now)
                && (x.LockStatus == PriceLockStatus.Locked
                    || x.LockStatus == PriceLockStatus.Approved
                    || x.LockStatus == PriceLockStatus.ApprovedWithEffectiveDate))
            .ToListAsync(cancellationToken);

        var hasPricingRuleVersion = await _db.PricingRuleVersions.AsNoTracking()
            .AnyAsync(x => x.IsActive, cancellationToken);

        var activeAddress = await _deliveryAddresses.ResolveActiveDeliveryAddressAsync(request.CustomerId, cancellationToken);

        List<Guid> coveredRestaurantIds = [];
        List<(Guid RestaurantId, Guid ProgramId, Guid BundleId)> offeringRows = [];

        if (activeAddress is not null)
        {
            coveredRestaurantIds = await _db.RestaurantServiceRegions.AsNoTracking()
                .Where(x => x.RegionId == activeAddress.RegionId && x.IsActive)
                .Select(x => x.RestaurantId)
                .Distinct()
                .ToListAsync(cancellationToken);

            var rawOfferings = await _db.RestaurantProgramBundleOffers.AsNoTracking()
                .Where(x => distinctBundleIds.Contains(x.BundleId) && x.IsActive)
                .Select(x => new { x.RestaurantId, x.ProgramId, x.BundleId })
                .ToListAsync(cancellationToken);

            offeringRows = rawOfferings
                .Select(x => (x.RestaurantId, x.ProgramId, x.BundleId))
                .ToList();
        }

        var tierLabels = settings.TierLabels;
        var optionsList = new List<OfferPurchaseOptionDto>();

        foreach (var (programId, bundleId, tier, durationDays) in candidates)
        {
            if (!programs.TryGetValue(programId, out var program)) continue;
            if (!bundles.TryGetValue(bundleId, out var bundle)) continue;

            var (tierNameAr, tierNameEn) = TierDisplayName(tierLabels, tier);
            var mealsPerDay = bundle.MealsPerDay > 0
                ? bundle.MealsPerDay
                : Math.Max(1, bundle.BreakfastCount + bundle.LunchCount + bundle.DinnerCount + bundle.SnackCount);

            var imageUrl = CatalogImageStorage.ToContentUrl(bundle.ImageStorageKey);

            var dates = _schedule.GetServiceDates(resolvedStartDate, durationDays, request.IncludeFriday);
            var totalServiceDays = dates.Count;
            var endDate = dates.Last();
            var fridayDays = _schedule.CountFridays(dates);

            var optionBlockers = new List<MobileBlockingReasonDto>(generalBlockers);

            // Coverage check
            var isAddressCovered = activeAddress is not null
                && coveredRestaurantIds.Count > 0
                && offeringRows.Any(o => coveredRestaurantIds.Contains(o.RestaurantId)
                    && o.ProgramId == programId
                    && o.BundleId == bundleId);

            if (activeAddress is not null && !isAddressCovered)
            {
                optionBlockers.Add(new MobileBlockingReasonDto(
                    SubscriptionCheckoutGateCodes.NoRestaurantsForAddress,
                    "لا توجد مطاعم توصل إلى عنوانك المحدد لهذه الباقة.",
                    "No restaurants deliver to your address for this package.",
                    Advice: new MobileRetryAdviceDto(false)));
            }

            // Pricing check
            var bundlePrices = activePricings
                .Where(x => x.ProgramId == programId && x.BundleId == bundleId)
                .Select(x => x.ReferencePrice26.Amount)
                .ToList();

            var isPricingAvailable = hasPricingRuleVersion && bundlePrices.Count > 0;
            if (!isPricingAvailable)
            {
                optionBlockers.Add(new MobileBlockingReasonDto(
                    "pricing.unavailable",
                    "التسعير غير متوفر لهذه الباقة حالياً.",
                    "Pricing is currently unavailable for this package.",
                    Advice: new MobileRetryAdviceDto(false)));
            }

            // If prerequisites incomplete, address uncovered, or pricing unavailable: return null prices
            if (!prerequisites.IsComplete || !isAddressCovered || !isPricingAvailable)
            {
                optionsList.Add(new OfferPurchaseOptionDto(
                    program.Id,
                    program.NameAr,
                    program.NameEn,
                    bundle.Id,
                    bundle.NameAr,
                    bundle.NameEn,
                    imageUrl,
                    tier,
                    tierNameAr,
                    tierNameEn,
                    durationDays,
                    resolvedStartDate,
                    endDate,
                    request.IncludeFriday,
                    totalServiceDays,
                    mealsPerDay,
                    BasePrice: null,
                    CampaignDiscount: null,
                    PromoDiscount: null,
                    WalletCredit: null,
                    GatewayFee: null,
                    FinalPrice: null,
                    CurrencyCode: "KWD",
                    CanCheckout: false,
                    BlockingReasons: optionBlockers,
                    Discount: null
                ));
                continue;
            }

            // Authoritative server pricing
            var meanDailyPrice = SubscriptionPackagePricingCalculator.ResolveAverageDailyPrice(bundlePrices, tier);
            var profile = settings.Mode == "per_bundle" && settings.PerBundle.TryGetValue(bundle.Id, out var cp)
                ? cp
                : settings.Global;
            var durationRate = profile.ResolveRate(durationDays);

            var pricingReq = new PricingEngineRequest(
                MeanReferenceDailyPrice: meanDailyPrice,
                DurationDays: durationDays,
                DurationRatesPct: new Dictionary<int, decimal> { [durationDays] = durationRate },
                FridayDays: request.IncludeFriday ? fridayDays : 0,
                FridayRatePct: profile.FridayRate);

            var grossResult = PricingEngine.Calculate(pricingReq);

            var offerEligibilityCtx = new OfferEligibilityContext(
                request.CustomerId,
                priorSubsCount,
                userRedemptions,
                CartAmount: grossResult.UnroundedCorePrice,
                DurationDays: durationDays,
                DeliveryGovernorate: activeAddress?.Governorate,
                ProgramId: program.Id,
                BundleId: bundle.Id,
                Tier: tier,
                SubscriptionType: "Individual",
                Purpose: OfferEvaluationPurpose.Checkout);

            var offerEval = _eligibilityEngine.Evaluate(offer, offerEligibilityCtx, now);
            var campaignDiscount = offerEval.IsEligible ? offerEval.CalculatedDiscountAmount : 0m;

            var pricedResult = PricingEngine.Calculate(pricingReq with { CampaignDiscountKwd = campaignDiscount });

            if (!offerEval.IsEligible)
            {
                optionBlockers.Add(new MobileBlockingReasonDto(
                    "checkout.selection_not_eligible",
                    "الباقة المحددة غير متوافقة مع هذا العرض.",
                    "The selected package is not eligible for this offer.",
                    Advice: new MobileRetryAdviceDto(false)));
            }

            var canCheckout = optionBlockers.Count == 0;

            optionsList.Add(new OfferPurchaseOptionDto(
                program.Id,
                program.NameAr,
                program.NameEn,
                bundle.Id,
                bundle.NameAr,
                bundle.NameEn,
                imageUrl,
                tier,
                tierNameAr,
                tierNameEn,
                durationDays,
                resolvedStartDate,
                endDate,
                request.IncludeFriday,
                totalServiceDays,
                mealsPerDay,
                BasePrice: pricedResult.UnroundedCorePrice,
                CampaignDiscount: pricedResult.CampaignDiscountApplied,
                PromoDiscount: 0m,
                WalletCredit: pricedResult.WalletCreditApplied,
                GatewayFee: pricedResult.GatewayCostRecoveryAmount,
                FinalPrice: pricedResult.FinalCustomerPrice,
                CurrencyCode: pricedResult.CurrencyCode,
                CanCheckout: canCheckout,
                BlockingReasons: optionBlockers,
                Discount: pricedResult.CampaignDiscountApplied
            ));
        }

        if (optionsList.Count == 0)
        {
            var noEligiblePackagesBlocker = new MobileBlockingReasonDto(
                "offers.no_eligible_packages",
                "لا توجد باقات متاحة متوافقة مع هذا العرض حالياً.",
                "No eligible packages are currently available for this offer.",
                Advice: new MobileRetryAdviceDto(false));
            generalBlockers.Add(noEligiblePackagesBlocker);
        }

        return new CustomerOfferPurchaseOptionsResponseDto(
            offer.Id,
            "Individual",
            now,
            prerequisites,
            generalBlockers,
            optionsList);
    }

    private async Task<(CustomerSubscriptionPrerequisitesDto Dto, List<MobileBlockingReasonDto> Blockers)> EvaluatePrerequisitesAsync(
        Guid customerUserId, CancellationToken ct)
    {
        var profile = await _db.CustomerProfiles.AsNoTracking()
            .Where(x => x.UserId == customerUserId)
            .Select(x => new { x.AllergiesCompletedAtUtc, x.DietaryPreferencesCompletedAtUtc })
            .FirstOrDefaultAsync(ct);

        var activeAddress = await _deliveryAddresses.ResolveActiveDeliveryAddressAsync(customerUserId, ct);
        var hasAddress = activeAddress is not null;
        var hasAllergies = profile?.AllergiesCompletedAtUtc.HasValue == true;
        var hasDietary = profile?.DietaryPreferencesCompletedAtUtc.HasValue == true;
        var isComplete = hasAddress && hasAllergies && hasDietary;

        var prerequisites = new CustomerSubscriptionPrerequisitesDto(
            isComplete,
            hasAddress,
            hasAllergies,
            hasDietary,
            activeAddress?.RegionId ?? Guid.Empty);

        var blockers = new List<MobileBlockingReasonDto>();
        if (!hasAddress)
        {
            blockers.Add(new MobileBlockingReasonDto(
                "checkout.address_required",
                "يرجى إضافة عنوان توصيل للمتابعة.",
                "Please add a delivery address to continue.",
                "address",
                new MobileRetryAdviceDto(false, RefreshSummaryRequired: true)));
        }
        if (!hasAllergies)
        {
            blockers.Add(new MobileBlockingReasonDto(
                "checkout.allergies_required",
                "يرجى تأكيد الحساسية للمتابعة.",
                "Please confirm allergies to continue.",
                "allergies",
                new MobileRetryAdviceDto(false, RefreshSummaryRequired: true)));
        }
        if (!hasDietary)
        {
            blockers.Add(new MobileBlockingReasonDto(
                "checkout.dietary_preferences_required",
                "يرجى تحديد التفضيلات الغذائية للمتابعة.",
                "Please specify dietary preferences to continue.",
                "dietary_preferences",
                new MobileRetryAdviceDto(false, RefreshSummaryRequired: true)));
        }

        if (_individualGate is not null)
        {
            var hasBlocking = await _individualGate.HasBlockingAsync(customerUserId, ct);
            if (hasBlocking)
            {
                blockers.Add(new MobileBlockingReasonDto(
                    SubscriptionCheckoutGateCodes.ExistingSubscription,
                    "لديك اشتراك نشط بالفعل.",
                    "You already have an active subscription.",
                    Advice: new MobileRetryAdviceDto(false)));
            }
        }

        return (prerequisites, blockers);
    }

    private static (string NameAr, string NameEn) TierDisplayName(TierLabelsSettings? labels, RestaurantTier tier) => tier switch
    {
        RestaurantTier.Basic => (labels?.BasicAr ?? "أساسي", labels?.BasicEn ?? "Basic"),
        RestaurantTier.Platinum => (labels?.PlatinumAr ?? "بلاتينيوم", labels?.PlatinumEn ?? "Platinum"),
        RestaurantTier.Elite => (labels?.EliteAr ?? "إيليت", labels?.EliteEn ?? "Elite"),
        _ => (tier.ToString(), tier.ToString())
    };
}
