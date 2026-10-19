using MealMate.Application.Common.Abstractions;
using MealMate.Application.Common.Messaging;
using MealMate.Application.Common.Models;
using MealMate.Application.Common.Localization;
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
using MediatR;
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
    private readonly ISender? _sender;
    private readonly ILocalizedMessageProvider _messages;

    public GetCustomerOfferPurchaseOptionsQueryHandler(
        IAppDbContext db,
        IOfferEligibilityEngine eligibilityEngine,
        IDateTimeProvider clock,
        ISubscriptionScheduleService schedule,
        IAllowedSubscriptionDurationService allowedDurations,
        ICustomerDeliveryAddressService deliveryAddresses,
        ICustomerIndividualSubscriptionGate? individualGate = null,
        ISubscriptionRestaurantAvailabilityService? availability = null,
        ISender? sender = null,
        ILocalizedMessageProvider? messages = null)
    {
        _db = db;
        _eligibilityEngine = eligibilityEngine;
        _clock = clock;
        _schedule = schedule;
        _allowedDurations = allowedDurations;
        _deliveryAddresses = deliveryAddresses;
        _individualGate = individualGate;
        _availability = availability;
        _sender = sender;
        _messages = messages ?? NullLocalizedMessageProvider.Instance;
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
            var (prereqs, blockers, _) = await EvaluatePrerequisitesAsync(request.CustomerId, cancellationToken);
            var familyBlocker = OfferPurchaseOptionsLocalization.Reason(
                _messages,
                MessageCodes.OfferPurchaseOptions.FamilyOnly,
                advice: new MobileRetryAdviceDto(false));

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

        var (prerequisites, generalBlockers, activeAddress) = await EvaluatePrerequisitesAsync(request.CustomerId, cancellationToken);

        var chosenStartDate = request.StartDate
            ?? _schedule.ResolveFirstDeliveryDate(_schedule.GetKuwaitToday(), request.IncludeFriday);
        if (!_schedule.TryResolveCheckoutStart(chosenStartDate, request.IncludeFriday, out var resolvedStartDate, out var startError))
        {
            return Error.Validation(startError ?? "checkout.start_date_invalid");
        }

        var settings = await _allowedDurations.GetSettingsAsync(cancellationToken);
        var activePresets = settings.Durations
            .Where(d => !d.IsCustom && d.IsActive)
            .Select(d => d.Days)
            .Distinct()
            .OrderBy(d => d)
            .ToList();

        var durationDecisions = new Dictionary<int, AllowedDurationDecision>();
        async Task<bool> IsDurationAllowedAsync(int durationDays)
        {
            if (!durationDecisions.TryGetValue(durationDays, out var decision))
            {
                decision = await _allowedDurations.EvaluateAsync(durationDays, request.IncludeFriday, cancellationToken);
                durationDecisions[durationDays] = decision;
            }

            return decision.IsAllowed;
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
                    if (!await IsDurationAllowedAsync(d)) continue;

                    candidates.Add((target.ProgramId, target.BundleId, target.Tier, d));
                }
            }
        }
        else
        {
            // Discover only package/tier combinations actually published by the customer catalog.
            IReadOnlyList<CustomerSubscriptionCatalogItemDto> catalog = [];
            if (_sender is not null)
            {
                var catalogResult = await _sender.Send(new GetCustomerSubscriptionPricingCatalogQuery(), cancellationToken);
                if (catalogResult.IsFailure) return catalogResult.Error;
                catalog = catalogResult.Value;
            }
            var eligibleProgramIds = offer.EligiblePrograms.Select(p => p.ProgramId).ToHashSet();
            var eligibleBundleIds = offer.EligibleBundles.Select(b => b.BundleId).ToHashSet();
            var durations = activePresets
                .Where(d => (!offer.MinSubscriptionDays.HasValue || d >= offer.MinSubscriptionDays.Value)
                    && (!offer.MaxSubscriptionDays.HasValue || d <= offer.MaxSubscriptionDays.Value))
                .ToList();

            foreach (var item in catalog)
            {
                if (eligibleProgramIds.Count > 0 && !eligibleProgramIds.Contains(item.ProgramId)) continue;
                if (eligibleBundleIds.Count > 0 && !eligibleBundleIds.Contains(item.BundleId)) continue;

                foreach (var d in durations)
                {
                    if (!await IsDurationAllowedAsync(d)) continue;
                    candidates.Add((item.ProgramId, item.BundleId, item.Tier, d));
                }
            }
        }

        candidates = candidates.Distinct().ToList();

        if (candidates.Count == 0)
        {
            var noEligiblePackagesBlocker = OfferPurchaseOptionsLocalization.Reason(
                _messages,
                MessageCodes.OfferPurchaseOptions.NoEligiblePackages,
                advice: new MobileRetryAdviceDto(false));
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

        candidates = candidates.Where(candidate =>
        {
            if (!programs.ContainsKey(candidate.ProgramId)) return false;
            if (!bundles.TryGetValue(candidate.BundleId, out var bundle)) return false;
            if (bundle.ProgramId != candidate.ProgramId) return false;
            var slotTotal = bundle.BreakfastCount + bundle.LunchCount + bundle.DinnerCount + bundle.SnackCount;
            return slotTotal > 0
                && bundle.BreakfastCount >= 0
                && bundle.LunchCount >= 0
                && bundle.DinnerCount >= 0
                && bundle.SnackCount >= 0;
        }).ToList();

        if (candidates.Count == 0)
        {
            generalBlockers.Add(OfferPurchaseOptionsLocalization.Reason(
                _messages, MessageCodes.OfferPurchaseOptions.NoEligiblePackages,
                advice: new MobileRetryAdviceDto(false)));
            return new CustomerOfferPurchaseOptionsResponseDto(
                offer.Id, "Individual", now, prerequisites, generalBlockers, Array.Empty<OfferPurchaseOptionDto>());
        }

        var tierLabels = settings.TierLabels;
        var optionsList = new List<OfferPurchaseOptionDto>();

        foreach (var (programId, bundleId, tier, durationDays) in candidates)
        {
            if (!programs.TryGetValue(programId, out var program)) continue;
            if (!bundles.TryGetValue(bundleId, out var bundle)) continue;

            var mealsPerDay = bundle.BreakfastCount + bundle.LunchCount + bundle.DinnerCount + bundle.SnackCount;
            var imageUrl = CatalogImageStorage.ToContentUrl(bundle.ImageStorageKey);
            var dates = _schedule.GetServiceDates(resolvedStartDate, durationDays, request.IncludeFriday);
            if (dates.Count == 0)
            {
                return Error.Validation("checkout.start_date_invalid");
            }

            var totalServiceDays = dates.Count;
            var endDate = dates[^1];
            var fridayDays = _schedule.CountFridays(dates);
            var optionBlockers = new List<MobileBlockingReasonDto>(generalBlockers);
            IndividualRestaurantAvailabilityReport? availability = null;
            Guid? offerEligibleRestaurantId = null;

            if (!prerequisites.HasDeliveryAddress)
            {
                // The address prerequisite already explains this blocker.
            }
            else if (_availability is null)
            {
                optionBlockers.Add(OfferPurchaseOptionsLocalization.Reason(
                    _messages,
                    MessageCodes.OfferPurchaseOptions.AvailabilityUnavailable,
                    advice: new MobileRetryAdviceDto(false, RefreshAvailabilityRequired: true)));
            }
            else
            {
                availability = await _availability.EvaluateDetailedAsync(
                    request.CustomerId,
                    programId,
                    bundleId,
                    tier,
                    durationDays,
                    request.IncludeFriday,
                    resolvedStartDate,
                    cancellationToken);

                var offerRestaurantIds = offer.EligibleRestaurants
                    .Select(x => x.RestaurantId)
                    .ToHashSet();
                var eligibleRestaurants = offerRestaurantIds.Count == 0
                    ? availability.EligibleRestaurants
                    : availability.EligibleRestaurants.Where(x => offerRestaurantIds.Contains(x.RestaurantId)).ToList();
                offerEligibleRestaurantId = eligibleRestaurants.FirstOrDefault()?.RestaurantId;

                if (eligibleRestaurants.Count == 0)
                {
                    var reasonCode = offerRestaurantIds.Count > 0
                        ? MessageCodes.OfferPurchaseOptions.PeriodUnavailable
                        : availability.Availability.Status == IndividualRestaurantAvailabilityStatus.NoRestaurantsForAddress
                            ? SubscriptionCheckoutGateCodes.NoRestaurantsForAddress
                            : availability.Availability.Status == IndividualRestaurantAvailabilityStatus.NoRestaurantsForSelection
                                ? SubscriptionCheckoutGateCodes.NoRestaurantsForSelection
                                : MessageCodes.OfferPurchaseOptions.PeriodUnavailable;
                    optionBlockers.Add(OfferPurchaseOptionsLocalization.Reason(
                        _messages,
                        reasonCode,
                        advice: new MobileRetryAdviceDto(false, RefreshAvailabilityRequired: true)));
                }
                else if (availability.Availability.Status != IndividualRestaurantAvailabilityStatus.Available)
                {
                    optionBlockers.Add(OfferPurchaseOptionsLocalization.Reason(
                        _messages,
                        MessageCodes.OfferPurchaseOptions.PeriodUnavailable,
                        advice: new MobileRetryAdviceDto(false, RefreshAvailabilityRequired: true)));
                }
            }

            PricingEngineResult? pricing = null;
            if (optionBlockers.Count == 0 && prerequisites.IsComplete)
            {
                if (_sender is null)
                {
                    optionBlockers.Add(OfferPurchaseOptionsLocalization.Reason(
                        _messages, "subscription_flow.pricing_unavailable",
                        advice: new MobileRetryAdviceDto(false)));
                }
                else
                {
                    var pricingResult = await _sender.Send(new PreviewCustomerPricingQuery(
                        programId,
                        bundleId,
                        tier,
                        durationDays,
                        IncludeFriday: request.IncludeFriday,
                        FridayDays: fridayDays,
                        OfferId: offer.Id,
                        CustomerId: request.CustomerId,
                        CustomerSubscriptionType: "Individual"), cancellationToken);

                    if (pricingResult.IsSuccess)
                    {
                        pricing = pricingResult.Value;
                        var offerEligibility = _eligibilityEngine.Evaluate(
                            offer,
                            new OfferEligibilityContext(
                                request.CustomerId,
                                priorSubsCount,
                                userRedemptions,
                                CartAmount: pricing.UnroundedCorePrice,
                                DurationDays: durationDays,
                                DeliveryGovernorate: activeAddress?.Governorate,
                                ProgramId: programId,
                                BundleId: bundleId,
                                RestaurantId: offerEligibleRestaurantId,
                                Tier: tier,
                                SubscriptionType: "Individual",
                                Purpose: OfferEvaluationPurpose.Checkout),
                            now);

                        if (!offerEligibility.IsEligible
                            || pricing.CampaignDiscountApplied != offerEligibility.CalculatedDiscountAmount)
                        {
                            pricing = null;
                            optionBlockers.Add(OfferPurchaseOptionsLocalization.Reason(
                                _messages,
                                "checkout.selection_not_eligible",
                                advice: new MobileRetryAdviceDto(false, RefreshAvailabilityRequired: true)));
                        }
                        else if (pricing.FinalCustomerPrice <= 0m)
                        {
                            pricing = null;
                            optionBlockers.Add(OfferPurchaseOptionsLocalization.Reason(
                                _messages,
                                MessageCodes.Checkout.CouponFullDiscountUnsupported,
                                advice: new MobileRetryAdviceDto(false)));
                        }
                    }
                    else
                    {
                        optionBlockers.Add(OfferPurchaseOptionsLocalization.Reason(
                            _messages,
                            "subscription_flow.pricing_unavailable",
                            advice: new MobileRetryAdviceDto(false)));
                    }
                }
            }

            var canCheckout = pricing is not null && optionBlockers.Count == 0
                && availability?.Availability.Status == IndividualRestaurantAvailabilityStatus.Available;
            var (tierNameAr, tierNameEn) = TierDisplayName(tierLabels, tier);

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
                BasePrice: pricing?.UnroundedCorePrice,
                CampaignDiscount: pricing?.CampaignDiscountApplied,
                PromoDiscount: pricing?.PromoCodeDiscountApplied,
                WalletCredit: pricing?.WalletCreditApplied,
                GatewayFee: pricing?.GatewayCostRecoveryAmount,
                FinalPrice: pricing?.FinalCustomerPrice,
                CurrencyCode: pricing?.CurrencyCode ?? "KWD",
                CanCheckout: canCheckout,
                BlockingReasons: optionBlockers,
                Discount: pricing is null ? null : pricing.CampaignDiscountApplied + pricing.PromoCodeDiscountApplied));
        }

        if (optionsList.Count == 0)
        {
            generalBlockers.Add(OfferPurchaseOptionsLocalization.Reason(
                _messages,
                MessageCodes.OfferPurchaseOptions.NoEligiblePackages,
                advice: new MobileRetryAdviceDto(false)));
        }

        return new CustomerOfferPurchaseOptionsResponseDto(
            offer.Id,
            "Individual",
            now,
            prerequisites,
            generalBlockers,
            optionsList);
    }

    private async Task<(CustomerSubscriptionPrerequisitesDto Dto, List<MobileBlockingReasonDto> Blockers, CustomerActiveDeliveryAddress? ActiveAddress)> EvaluatePrerequisitesAsync(
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
            blockers.Add(OfferPurchaseOptionsLocalization.Reason(
                _messages,
                "checkout.address_required",
                "address",
                new MobileRetryAdviceDto(false, RefreshSummaryRequired: true)));
        }
        if (!hasAllergies)
        {
            blockers.Add(OfferPurchaseOptionsLocalization.Reason(
                _messages,
                "checkout.allergies_required",
                "allergies",
                new MobileRetryAdviceDto(false, RefreshSummaryRequired: true)));
        }
        if (!hasDietary)
        {
            blockers.Add(OfferPurchaseOptionsLocalization.Reason(
                _messages,
                "checkout.dietary_preferences_required",
                "dietary_preferences",
                new MobileRetryAdviceDto(false, RefreshSummaryRequired: true)));
        }

        if (_individualGate is null)
        {
            blockers.Add(OfferPurchaseOptionsLocalization.Reason(
                _messages,
                MessageCodes.OfferPurchaseOptions.ReadinessUnavailable,
                advice: new MobileRetryAdviceDto(true, RefreshSummaryRequired: true)));
        }
        else
        {
            var hasBlocking = await _individualGate.HasBlockingAsync(customerUserId, ct)
                || await _individualGate.HasBlockingFamilyGroupAsync(customerUserId, ct);
            if (hasBlocking)
            {
                blockers.Add(OfferPurchaseOptionsLocalization.Reason(
                    _messages,
                    SubscriptionCheckoutGateCodes.ExistingSubscription,
                    advice: new MobileRetryAdviceDto(false)));
            }
        }

        return (prerequisites, blockers, activeAddress);
    }

    private static (string NameAr, string NameEn) TierDisplayName(TierLabelsSettings? labels, RestaurantTier tier) => tier switch
    {
        RestaurantTier.Basic => (labels?.BasicAr ?? tier.ToString(), labels?.BasicEn ?? tier.ToString()),
        RestaurantTier.Platinum => (labels?.PlatinumAr ?? tier.ToString(), labels?.PlatinumEn ?? tier.ToString()),
        RestaurantTier.Elite => (labels?.EliteAr ?? tier.ToString(), labels?.EliteEn ?? tier.ToString()),
        _ => (tier.ToString(), tier.ToString())
    };
}
