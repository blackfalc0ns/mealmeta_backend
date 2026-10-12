using MealMate.Application.Common;
using MealMate.Application.Common.Abstractions;
using MealMate.Application.Common.Models;
using MealMate.Application.Common.Storage;
using MealMate.Application.Features.CustomerCheckout;
using MealMate.Application.Features.IdentityAccess.Customers;
using MealMate.Application.Features.FamilyCheckout;
using MealMate.Application.Features.Offers.Services;
using MealMate.Application.Features.ProgramsSubscriptions.Pricing;
using MealMate.Application.Features.ProgramsSubscriptions.Upgrade;
using MealMate.Domain.Enums;
using MealMate.Domain.IdentityAccess;
using MealMate.Domain.ProgramsSubscriptions;
using MediatR;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Hosting;

namespace MealMate.Application.Features.ProgramsSubscriptions.Flow;

public sealed class SubscriptionFlowQueryService : ISubscriptionFlowQueryService
{
    private static readonly SubscriptionStatus[] CountedSubscriptionStatuses =
    [
        SubscriptionStatus.Paid, SubscriptionStatus.Scheduled, SubscriptionStatus.Active,
        SubscriptionStatus.Frozen, SubscriptionStatus.Completed, SubscriptionStatus.Expired,
        SubscriptionStatus.CancellationPending, SubscriptionStatus.Upgraded,
    ];

    private readonly IAppDbContext _db;
    private readonly IRestaurantEligibilityService _eligibility;
    private readonly IMenuPlanResolutionService _menus;
    private readonly ISubscriptionScheduleService _schedule;
    private readonly IBundleSlotRequirementService _slots;
    private readonly IPaymentGatewayService _payments;
    private readonly IAllowedSubscriptionDurationService _allowedDurations;
    private readonly IRestaurantTierClassificationService _tiers;
    private readonly ISubscriptionCheckoutReadinessService _readiness;
    private readonly ISubscriptionRestaurantAvailabilityService? _availability;
    private readonly ICustomerIndividualSubscriptionGate _individualGate;
    private readonly IRestaurantDiversificationCalculator _diversification;
    private readonly ISubscriptionFlowCouponService _coupons;
    private readonly ISender _sender;
    private readonly IConfiguration? _configuration;
    private readonly IHostEnvironment? _environment;
    private readonly ICustomerProfilePrerequisitesGate _prerequisitesGate;
    private readonly ISubscriptionCatalogPreparationService _preparation;
    private readonly ISubscriptionCatalogAvailabilityService _catalog;

    public SubscriptionFlowQueryService(
        IAppDbContext db,
        IRestaurantEligibilityService eligibility,
        IMenuPlanResolutionService menus,
        ISubscriptionScheduleService schedule,
        IBundleSlotRequirementService slots,
        IPaymentGatewayService payments,
        IAllowedSubscriptionDurationService allowedDurations,
        IRestaurantTierClassificationService tiers,
        ISubscriptionCheckoutReadinessService readiness,
        ICustomerIndividualSubscriptionGate individualGate,
        IRestaurantDiversificationCalculator diversification,
        ISender sender,
        IConfiguration? configuration = null,
        IHostEnvironment? environment = null,
        ISubscriptionFlowCouponService? couponService = null,
        ISubscriptionRestaurantAvailabilityService? availability = null,
        ICustomerProfilePrerequisitesGate? prerequisitesGate = null,
        ISubscriptionCatalogPreparationService? preparation = null,
        ISubscriptionCatalogAvailabilityService? catalogAvailability = null,
        IDateTimeProvider? clock = null)
    {
        _db = db;
        _eligibility = eligibility;
        _menus = menus;
        _schedule = schedule;
        _slots = slots;
        _payments = payments;
        _allowedDurations = allowedDurations;
        _tiers = tiers;
        _readiness = readiness;
        _availability = availability;
        _individualGate = individualGate;
        _diversification = diversification;
        _sender = sender;
        _configuration = configuration;
        _environment = environment;
        _coupons = couponService ?? new SubscriptionFlowCouponService(db, new OfferEligibilityEngine());
        _prerequisitesGate = prerequisitesGate ?? new CustomerProfilePrerequisitesGate(db);
        var resolvedClock = clock ?? new CustomerProfilePrerequisitesGate.SystemDateTimeProvider();
        var deliveryAddresses = new CustomerDeliveryAddressService(db, resolvedClock);
        _preparation = preparation ?? new SubscriptionCatalogPreparationService(db, resolvedClock);
        _catalog = catalogAvailability ?? new SubscriptionCatalogAvailabilityService(db, resolvedClock, deliveryAddresses,
            tiers, allowedDurations, availability ?? new SubscriptionRestaurantAvailabilityService(db, resolvedClock,
                deliveryAddresses, eligibility, tiers), sender, _prerequisitesGate, _preparation, schedule);
    }

    public async Task<Result<IReadOnlyList<SubscriptionTypeOptionDto>>> GetSubscriptionTypesAsync(Guid customerUserId, CancellationToken ct = default)
    {
        var hasIndividual = customerUserId != Guid.Empty && await _individualGate.FindBlockingAsync(customerUserId, ct) is not null;
        var hasFamily = customerUserId != Guid.Empty && await _individualGate.HasBlockingFamilyGroupAsync(customerUserId, ct);
        var individualBlocked = hasIndividual || hasFamily;
        const string conversionOptions = "/api/v1/customer/subscriptions/current/conversion-options";

        var (indNameAr, indNameEn, indDescAr, indDescEn) = SubscriptionFlowLocalization.IndividualType();
        var (famNameAr, famNameEn) = SubscriptionFlowLocalization.FamilyType();
        var famDescAr = SubscriptionFlowLocalization.FamilyDescriptionAr(hasIndividual, hasFamily);
        var famDescEn = SubscriptionFlowLocalization.FamilyDescriptionEn(hasIndividual, hasFamily);

        IReadOnlyList<SubscriptionTypeOptionDto> types =
        [
            new SubscriptionTypeOptionDto(
                "INDIVIDUAL", indNameAr, indNameEn,
                indDescAr, indDescEn,
                1, !individualBlocked, false,
                individualBlocked ? CustomerIndividualSubscriptionGate.ExistingSubscriptionError : null),
            new SubscriptionTypeOptionDto(
                "FAMILY", famNameAr, famNameEn,
                famDescAr, famDescEn,
                4, true, false,
                DisabledReason: null,
                RequiresConversion: hasIndividual,
                ConversionEndpoint: hasIndividual ? conversionOptions : null)
        ];

        return Result.Success(types);
    }

    public async Task<Result<IReadOnlyList<CustomerProgramCardDto>>> GetProgramsAsync(Guid customerUserId, CancellationToken ct = default)
    {
        var catalog = await _catalog.GetProgramsAsync(customerUserId, null, ct);
        if (catalog.IsFailure) return catalog.Error;
        return Result.Success<IReadOnlyList<CustomerProgramCardDto>>(catalog.Value.Items
            .Select(x => x.Option with { Availability = x.Availability }).ToList());
    }

    public async Task<Result<PagedResult<CustomerProgramCardDto>>> GetProgramsPagedAsync(Guid customerUserId, PageRequest page, CancellationToken ct = default)
    {
        var catalog = await _catalog.GetProgramsAsync(customerUserId, page ?? new PageRequest(), ct);
        if (catalog.IsFailure) return catalog.Error;
        var pagination = catalog.Value.Pagination!;
        return new PagedResult<CustomerProgramCardDto>(catalog.Value.Items
            .Select(x => x.Option with { Availability = x.Availability }).ToList(),
            pagination.TotalCount, pagination.Page, pagination.PageSize);
    }

    /// <summary>Offset overflow guard for in-memory slicing of a known result size.</summary>
    private static bool TryGetOverflowError(PageRequest pageRequest, int totalCount, out string? errorCode)
    {
        errorCode = null;
        if (pageRequest.NormalizedPage == 1) return false;
        var maxOffset = (long)(totalCount - 1) * pageRequest.NormalizedPageSize;
        if (maxOffset <= int.MaxValue) return false;
        errorCode = "subscription_flow.page_overflow";
        return true;
    }

    public async Task<Result<CustomerProgramBundlesDto>> GetProgramBundlesAsync(Guid programId, Guid customerUserId, CancellationToken ct = default)
        => await GetProgramBundlesCoreAsync(programId, customerUserId, null, ct);

    public async Task<Result<CustomerProgramBundlesDto>> GetProgramBundlesPagedAsync(Guid programId, Guid customerUserId, PageRequest page, CancellationToken ct = default)
        => await GetProgramBundlesCoreAsync(programId, customerUserId, page ?? new PageRequest(), ct);

    private async Task<Result<CustomerProgramBundlesDto>> GetProgramBundlesCoreAsync(Guid programId, Guid customerUserId, PageRequest? pageRequest, CancellationToken ct)
    {
        var program = await _db.Programs.AsNoTracking().FirstOrDefaultAsync(x => x.Id == programId && x.IsActive && x.Status == ProgramStatus.Active, ct);
        if (program is null) return Error.NotFound("subscription_flow.program_not_found");

        var prerequisites = await _prerequisitesGate.EvaluateAsync(customerUserId, ct);
        var deliveryRegionId = prerequisites.ActiveAddress?.RegionId;
        var hasAddress = prerequisites.HasAddress;
        var hasAllergies = prerequisites.HasAllergies;
        var hasDietary = prerequisites.HasDietaryPreferences;
        var isProfileComplete = prerequisites.IsMet;

        var bundles = await _db.Bundles.AsNoTracking()
            .Where(x => x.ProgramId == programId && x.IsActive && x.Status == ProgramStatus.Active)
            .OrderBy(x => x.SortOrder)
            .ToListAsync(ct);
        var bundleIds = bundles.Select(x => x.Id).ToList();
        var prepared = prerequisites.IsMet
            ? await _preparation.ResolveAsync(customerUserId, deliveryRegionId!.Value, bundleIds, ct)
            : new Dictionary<Guid, HashSet<Guid>>();

        var selectionCounts = await _db.Subscriptions.AsNoTracking()
            .Where(x => x.ProgramId == programId && bundleIds.Contains(x.BundleId) && CountedSubscriptionStatuses.Contains(x.Status))
            .GroupBy(x => x.BundleId)
            .Select(x => new { BundleId = x.Key, Count = x.Count() })
            .ToDictionaryAsync(x => x.BundleId, x => x.Count, ct);

        var mostSelectedBundleId = selectionCounts.Count == 0
            ? (Guid?)null
            : bundles.OrderByDescending(x => selectionCounts.GetValueOrDefault(x.Id))
                .ThenBy(x => x.SortOrder)
                .Where(x => selectionCounts.GetValueOrDefault(x.Id) > 0)
                .Select(x => (Guid?)x.Id)
                .FirstOrDefault();

        var offers = await _db.RestaurantProgramBundleOffers.AsNoTracking()
            .Where(x => x.ProgramId == programId && bundleIds.Contains(x.BundleId) && x.IsActive)
            .Select(x => new { x.BundleId, x.RestaurantId })
            .ToListAsync(ct);
        var offeredRestaurantIds = offers.Select(x => x.RestaurantId).Distinct().ToList();

        var restaurantProfiles = await _db.RestaurantProfiles.AsNoTracking()
            .Where(x => offeredRestaurantIds.Contains(x.Id)
                && x.VerificationStatus == VerificationStatus.Approved
                && x.IsOperationallyActive)
            .OrderBy(x => x.TradeNameAr)
            .Select(x => new { x.Id, x.TradeNameAr, x.TradeNameEn })
            .ToListAsync(ct);

        var coveredRestaurantIds = deliveryRegionId.HasValue
            ? await FilterRestaurantIdsByDeliveryRegionAsync(restaurantProfiles.Select(x => x.Id).ToList(), deliveryRegionId.Value, ct)
            : restaurantProfiles.Select(x => x.Id).ToList();
        var coveredRestaurantSet = coveredRestaurantIds.ToHashSet();
        var activeRestaurantProfiles = restaurantProfiles.Where(x => coveredRestaurantSet.Contains(x.Id)).ToList();
        var activeRestaurantIds = activeRestaurantProfiles.Select(x => x.Id).ToList();
        var logos = await LoadRestaurantLogoUrlsAsync(activeRestaurantIds, ct);
        var restaurantCards = activeRestaurantProfiles.ToDictionary(
            x => x.Id,
            x => new CustomerBundleRestaurantDto(x.Id, logos.GetValueOrDefault(x.Id)));

        var (mostBadgeAr, mostBadgeEn) = SubscriptionFlowLocalization.MostSelectedBadge();

        var bundleCards = bundles.Where(IsBundleSlotConfigurationValid).Select(x =>
        {
            var preparedIds = prepared.GetValueOrDefault(x.Id) ?? [];
            var restaurants = offers.Where(o => o.BundleId == x.Id)
                .Select(o => o.RestaurantId)
                .Distinct()
                .Where(id => prerequisites.IsMet && preparedIds.Contains(id))
                .Select(id => restaurantCards.GetValueOrDefault(id))
                .Where(r => r is not null)
                .Select(r => r!)
                .ToList();
            return new CustomerBundleCardDto(
                x.Id, x.ProgramId, x.Code, x.NameAr, x.NameEn,
                x.Description ?? x.DescriptionEn ?? x.DescriptionAr, CatalogImageStorage.ToContentUrl(x.ImageStorageKey),
                x.MealsPerDay, x.BreakfastCount, x.LunchCount, x.DinnerCount, x.SnackCount,
                ToSlotRequirements(x.BreakfastCount, x.LunchCount, x.DinnerCount, x.SnackCount), x.SortOrder,
                x.DescriptionAr ?? x.NameAr, x.DescriptionEn ?? x.Description ?? x.NameEn,
                ToDisplaySlots(x.BreakfastCount, x.LunchCount, x.DinnerCount, x.SnackCount),
                restaurants.Count, restaurants.Take(8).ToList(), selectionCounts.GetValueOrDefault(x.Id),
                x.Id == mostSelectedBundleId,
                x.Id == mostSelectedBundleId ? new CustomerBundleBadgeDto("MOST_SELECTED", mostBadgeAr, mostBadgeEn) : null,
                SubscriptionCatalogAvailabilityService.ApplyPreparationAvailability(
                    SubscriptionCatalogAvailabilityService.ResolveCoverageAvailability(prerequisites,
                    offers.Where(o => o.BundleId == x.Id && coveredRestaurantSet.Contains(o.RestaurantId))
                        .Select(o => o.RestaurantId).Distinct().Count(),
                    offers.Where(o => o.BundleId == x.Id && restaurantProfiles.Any(r => r.Id == o.RestaurantId))
                        .Select(o => o.RestaurantId).Distinct().Count()), prerequisites, restaurants.Count));
        }).ToList();

        // Selection counts, restaurant counts and preview lists are computed on the full bundle
        // set regardless of paging; pagination only slices the displayed cards.
        IReadOnlyList<CustomerBundleCardDto> pageCards = bundleCards;
        CustomerPaginationMetaDto? pagination = null;
        if (pageRequest is not null)
        {
            if (TryGetOverflowError(pageRequest, bundleCards.Count, out var overflowError))
                return Error.Validation(overflowError!);
            pagination = new CustomerPaginationMetaDto(
                bundleCards.Count,
                pageRequest.NormalizedPage,
                pageRequest.NormalizedPageSize,
                (int)Math.Ceiling(bundleCards.Count / (double)pageRequest.NormalizedPageSize),
                pageRequest.NormalizedPage < (int)Math.Ceiling(bundleCards.Count / (double)pageRequest.NormalizedPageSize),
                pageRequest.NormalizedPage > 1);
            pageCards = bundleCards
                .Skip(pageRequest.Skip)
                .Take(pageRequest.NormalizedPageSize)
                .ToList();
        }

        return new CustomerProgramBundlesDto(
            HasDeliveryAddress: hasAddress,
            Bundles: pageCards,
            HasAllergies: hasAllergies,
            HasDietaryPreferences: hasDietary,
            IsProfileComplete: isProfileComplete,
            Pagination: pagination);
    }

    public async Task<Result<CustomerSubscriptionPrerequisitesDto>> GetPrerequisitesAsync(Guid customerUserId, CancellationToken ct = default)
    {
        if (customerUserId != Guid.Empty && await _individualGate.HasBlockingFamilyGroupAsync(customerUserId, ct))
        {
            return new Error("family.incomplete", null, ErrorType.Conflict,
                [new { error = "family.incomplete", missingStep = "FAMILY", actionEndpoint = "GET /api/v1/customer/subscriptions/uncompleted" }]);
        }

        var (gateError, deliveryRegionId) = await ValidateCustomerProfilePrerequisitesAsync(customerUserId, null, ct);
        if (gateError is not null) return gateError;

        return new CustomerSubscriptionPrerequisitesDto(
            IsComplete: true,
            HasDeliveryAddress: true,
            HasAllergies: true,
            HasDietaryPreferences: true,
            DeliveryRegionId: deliveryRegionId!.Value);
    }

    private async Task<Guid?> ResolveEffectiveBundleIdAsync(Guid programId, Guid? bundleId, CancellationToken ct)
    {
        if (bundleId.HasValue && bundleId.Value != Guid.Empty)
            return bundleId.Value;

        return await _db.Bundles.AsNoTracking()
            .Where(x => x.ProgramId == programId
                && x.IsActive
                && x.Status == ProgramStatus.Active
                && x.Program.IsActive
                && x.Program.Status == ProgramStatus.Active)
            .OrderBy(x => x.SortOrder)
            .Select(x => (Guid?)x.Id)
            .FirstOrDefaultAsync(ct);
    }

    public async Task<Result<IReadOnlyList<CustomerDurationOptionDto>>> GetDurationsAsync(
        Guid programId, Guid? bundleId, Guid customerUserId, bool includeFriday, CancellationToken ct = default)
    {
        var effectiveBundleId = await ResolveEffectiveBundleIdAsync(programId, bundleId, ct);
        if (!effectiveBundleId.HasValue || !await IsActiveBundleAsync(programId, effectiveBundleId.Value, ct))
            return Error.NotFound("subscription_flow.bundle_not_found");
        var targetBundleId = effectiveBundleId.Value;

        var settings = await _allowedDurations.GetSettingsAsync(ct);
        var activeOptions = settings.Durations.Where(x => x.IsActive).OrderBy(x => x.IsCustom).ThenBy(x => x.Days).ToList();
        if (activeOptions.Count == 0) return Error.Conflict("pricing_unavailable");

        var catalog = await _sender.Send(new GetCustomerSubscriptionPricingCatalogQuery(), ct);
        if (catalog.IsFailure) return catalog.Error;
        var pricedTiers = catalog.Value
            .Where(x => x.ProgramId == programId && x.BundleId == targetBundleId)
            .Select(x => x.Tier)
            .Distinct()
            .ToList();
        if (pricedTiers.Count == 0)
            return Error.Conflict("pricing_unavailable");

        var fridayAvailability = await ResolveFridayAvailabilityAsync(programId, targetBundleId, null, ct, pricedTiers);
        var selectionCounts = await _db.Subscriptions.AsNoTracking()
            .Where(x => x.ProgramId == programId && x.BundleId == targetBundleId && CountedSubscriptionStatuses.Contains(x.Status))
            .GroupBy(x => x.DurationDays)
            .Select(x => new { DurationDays = x.Key, Count = x.Count() })
            .ToDictionaryAsync(x => x.DurationDays, x => x.Count, ct);

        var presetRows = activeOptions.Where(x => !x.IsCustom).Select(option =>
        {
            var fridayDays = includeFriday && option.AllowsFriday && fridayAvailability.IsAvailable
                ? _schedule.GetFridayAddonDays(option.Days) : 0;
            var supportsFriday = option.AllowsFriday && fridayAvailability.IsAvailable;
            return new
            {
                Option = option,
                DurationDays = (int?)option.Days,
                FridayDays = fridayDays,
                TotalServiceDays = option.Days + fridayDays,
                SupportsFriday = supportsFriday,
            };
        }).ToList();

        if (includeFriday && presetRows.Count > 0 && presetRows.All(x => !x.SupportsFriday)
            && !(activeOptions.Any(x => x.IsCustom) && activeOptions.First(x => x.IsCustom).AllowsFriday && fridayAvailability.IsAvailable))
        {
            return new Error("subscription_flow.friday_unavailable", null, ErrorType.Conflict,
                [new { error = "subscription_flow.friday_unavailable", fridayAvailability }]);
        }

        var popularDuration = selectionCounts.Count == 0
            ? (int?)null
            : presetRows.OrderByDescending(x => selectionCounts.GetValueOrDefault(x.DurationDays ?? -1))
                .ThenByDescending(x => x.DurationDays)
                .Where(x => selectionCounts.GetValueOrDefault(x.DurationDays ?? -1) > 0)
                .Select(x => x.DurationDays)
                .FirstOrDefault();

        var (popBadgeAr, popBadgeEn) = SubscriptionFlowLocalization.MostPopularBadge();
        var (friOptAr, friOptEn) = SubscriptionFlowLocalization.IncludeFridayOption();

        var result = new List<CustomerDurationOptionDto>();
        foreach (var row in presetRows)
        {
            if (includeFriday && !row.SupportsFriday) continue;
            var option = row.Option;
            result.Add(new CustomerDurationOptionDto(
                row.DurationDays, option.NameAr, option.NameEn,
                selectionCounts.GetValueOrDefault(option.Days),
                row.DurationDays == popularDuration,
                row.DurationDays == popularDuration ? new CustomerBundleBadgeDto("MOST_POPULAR", popBadgeAr, popBadgeEn) : null,
                row.SupportsFriday, includeFriday && row.SupportsFriday, row.FridayDays, row.TotalServiceDays,
                new CustomerFridayOptionDto("INCLUDE_FRIDAY", friOptAr, friOptEn, false, row.SupportsFriday),
                row.SupportsFriday ? fridayAvailability : CreateFridayAvailability(0),
                false, null, null));
        }

        var custom = activeOptions.FirstOrDefault(x => x.IsCustom);
        if (custom is not null)
        {
            var supportsFriday = custom.AllowsFriday && fridayAvailability.IsAvailable;
            if (!includeFriday || supportsFriday)
            {
                result.Add(new CustomerDurationOptionDto(
                    null, custom.NameAr, custom.NameEn, 0, false, null,
                    supportsFriday, includeFriday && supportsFriday, null, null,
                    new CustomerFridayOptionDto("INCLUDE_FRIDAY", friOptAr, friOptEn, false, supportsFriday),
                    supportsFriday ? fridayAvailability : CreateFridayAvailability(0),
                    true, custom.MinDays, custom.MaxDays));
            }
        }

        if (result.Count == 0) return Error.Conflict("pricing_unavailable");
        return Result.Success<IReadOnlyList<CustomerDurationOptionDto>>(result);
    }

    public async Task<Result<IReadOnlyList<CustomerTierOptionDto>>> GetTiersAsync(
        Guid programId, Guid? bundleId, Guid customerUserId, int? durationDays, bool includeFriday, Guid? familyMemberId = null, CancellationToken ct = default, DateOnly? startDate = null)
    {
        var effectiveBundleId = await ResolveEffectiveBundleIdAsync(programId, bundleId, ct);
        if (!effectiveBundleId.HasValue || !await IsActiveBundleAsync(programId, effectiveBundleId.Value, ct))
            return Error.NotFound("subscription_flow.bundle_not_found");
        var targetBundleId = effectiveBundleId.Value;

        var isFamilyFlow = familyMemberId is Guid requestedMemberId && requestedMemberId != Guid.Empty;
        int? effectiveDurationDays = durationDays;
        // A tier card has no selected-period price until the client explicitly
        // sends the duration. Never infer a family duration from catalog presets.

        if (effectiveDurationDays.HasValue)
        {
            var allowed = await _allowedDurations.EvaluateAsync(effectiveDurationDays.Value, includeFriday, ct);
            if (!allowed.IsAllowed)
                return Error.Validation(allowed.ErrorCode ?? "subscription_flow.duration_not_available");
        }

        var (gateError, deliveryRegionId) = await ValidateCustomerProfilePrerequisitesAsync(customerUserId, familyMemberId, ct);
        if (gateError is not null) return gateError;

        var catalog = await _sender.Send(new GetCustomerSubscriptionPricingCatalogQuery(), ct);
        if (catalog.IsFailure) return catalog.Error;
        var programBundles = catalog.Value.Where(x => x.ProgramId == programId && x.BundleId == targetBundleId).ToList();
        if (programBundles.Count == 0) return Error.Conflict("pricing_unavailable");
        var pricedTiersList = programBundles.Select(x => x.Tier).Distinct().ToList();
        var fridayAvailability = await ResolveFridayAvailabilityAsync(programId, targetBundleId, null, ct, pricedTiersList);
        if (isFamilyFlow && includeFriday && !fridayAvailability.IsAvailable)
        {
            return new Error("subscription_flow.friday_unavailable", null, ErrorType.Conflict,
                [new { error = "subscription_flow.friday_unavailable", fridayAvailability }]);
        }

        int? fridayDays = null;
        int? totalServiceDays = null;
        var pricedTiers = new List<TierPricingRow>();
        if (effectiveDurationDays.HasValue)
        {
            fridayDays = includeFriday ? _schedule.GetFridayAddonDays(effectiveDurationDays.Value) : 0;
            totalServiceDays = effectiveDurationDays.Value + fridayDays.Value;
            foreach (var item in programBundles.OrderBy(x => x.Tier))
            {
                var catalogPrice = item.Prices.SingleOrDefault(p => p.DurationDays == effectiveDurationDays.Value);
                if (catalogPrice is not null && !includeFriday)
                {
                    pricedTiers.Add(new TierPricingRow(item, catalogPrice.TotalPriceKwd, catalogPrice.DailyPriceKwd));
                    continue;
                }

                var preview = await _sender.Send(new PreviewCustomerPricingQuery(programId, targetBundleId, item.Tier, effectiveDurationDays.Value,
                    IncludeFriday: includeFriday, FridayDays: fridayDays.Value), ct);
                if (preview.IsFailure)
                {
                    if (isFamilyFlow) return preview.Error;
                    pricedTiers.Add(new TierPricingRow(item, null, null));
                    continue;
                }
                pricedTiers.Add(new TierPricingRow(item, preview.Value.FinalCustomerPrice,
                    preview.Value.FinalCustomerPrice / totalServiceDays.Value));
            }
            if (pricedTiers.Count == 0) return Error.Validation("subscription_flow.duration_not_available");
        }
        else
        {
            pricedTiers.AddRange(programBundles.GroupBy(x => x.Tier).OrderBy(x => x.Key)
                .Select(group => new TierPricingRow(group.First(), null, null)));
        }

        var classification = await _tiers.ResolveAsync(programId, targetBundleId, eligibleOnly: true, ct);
        var classifiedRestaurantIds = await FilterRestaurantIdsByDeliveryRegionAsync(
            classification.Assignments.Select(x => x.RestaurantId).Distinct().ToList(),
            deliveryRegionId!.Value,
            ct);
        var coveredSet = classifiedRestaurantIds.ToHashSet();
        var rawCoverageByTier = pricedTiers.Select(x => x.Catalog.Tier).Distinct().ToDictionary(tier => tier,
            tier => classification.Assignments.Count(x => coveredSet.Contains(x.RestaurantId)
                && RestaurantTierAccess.CanSelect(tier, x.Tier)));
        if (!isFamilyFlow)
        {
            var prepared = await _preparation.ResolveAsync(customerUserId, deliveryRegionId.Value, [targetBundleId], ct);
            coveredSet.IntersectWith(prepared.GetValueOrDefault(targetBundleId) ?? []);
            classifiedRestaurantIds = classifiedRestaurantIds.Where(coveredSet.Contains).ToList();
        }
        var logos = await LoadRestaurantLogoUrlsAsync(classifiedRestaurantIds, ct);
        var restaurantCards = classifiedRestaurantIds.ToDictionary(
            id => id,
            id => new CustomerBundleRestaurantDto(id, logos.GetValueOrDefault(id)));
        var restaurantsByTier = pricedTiers.Select(x => x.Catalog.Tier).Distinct().ToDictionary(
            tier => tier,
            tier => classification.Assignments
                .Where(x => coveredSet.Contains(x.RestaurantId) && RestaurantTierAccess.CanSelect(tier, x.Tier))
                .OrderBy(x => x.Tier)
                .Select(x => restaurantCards[x.RestaurantId])
                .ToList());
        var tierSchedules = await _db.RestaurantOperatingSchedules.AsNoTracking()
            .Where(x => classifiedRestaurantIds.Contains(x.RestaurantId))
            .ToListAsync(ct);
        var fridayWorkingIds = classifiedRestaurantIds.Where(id =>
            tierSchedules.Any(x => x.RestaurantId == id && x.DayOfWeek == DayOfWeek.Friday && x.IsWorkingDay)).ToHashSet();
        var fridayAvailabilityByTier = pricedTiers.ToDictionary(
            x => x.Catalog.Tier,
            x =>
            {
                var ids = classification.Assignments
                    .Where(a => coveredSet.Contains(a.RestaurantId) && RestaurantTierAccess.CanSelect(x.Catalog.Tier, a.Tier))
                    .Select(a => a.RestaurantId)
                    .Distinct()
                    .ToList();
                var working = ids.Count(id => fridayWorkingIds.Contains(id));
                return CreateFridayAvailability(working);
            });
        if (isFamilyFlow && includeFriday && fridayAvailabilityByTier.Values.All(x => !x.IsAvailable))
        {
            return new Error("subscription_flow.friday_unavailable", null, ErrorType.Conflict,
                [new { error = "subscription_flow.friday_unavailable", fridayAvailability = CreateFridayAvailability(0) }]);
        }

        var selectionQuery = _db.Subscriptions.AsNoTracking()
            .Where(x => x.ProgramId == programId && x.BundleId == targetBundleId && CountedSubscriptionStatuses.Contains(x.Status));
        if (effectiveDurationDays.HasValue)
            selectionQuery = selectionQuery.Where(x => x.DurationDays == effectiveDurationDays.Value);
        var selectionCounts = await selectionQuery
            .GroupBy(x => x.SelectedTier)
            .Select(g => new { Tier = g.Key, Count = g.Count() })
            .ToDictionaryAsync(x => x.Tier, x => x.Count, ct);

        var displayedTiers = pricedTiers
            .Where(x => !isFamilyFlow || (restaurantsByTier.GetValueOrDefault(x.Catalog.Tier)?.Count ?? 0) > 0)
            .Where(x => !isFamilyFlow || !includeFriday || fridayAvailabilityByTier[x.Catalog.Tier].IsAvailable)
            .ToList();
        if (displayedTiers.Count == 0)
            return Error.Conflict(SubscriptionCheckoutGateCodes.NoRestaurantsForAddress);

        // Period verdicts for the individual flow only: the family flow resolves addresses per
        // member, so the individual evaluator's report is never attached to its tier cards.
        Dictionary<RestaurantTier, IndividualRestaurantAvailabilityReport>? periodReports = null;
        if (startDate.HasValue && effectiveDurationDays.HasValue && !isFamilyFlow && _availability is not null)
        {
            periodReports = new Dictionary<RestaurantTier, IndividualRestaurantAvailabilityReport>();
            if (!_schedule.TryResolveCheckoutStart(startDate, includeFriday, out var firstDeliveryDate, out var startError))
                return Error.Validation(startError ?? "checkout.start_date_invalid");
            foreach (var periodTier in displayedTiers.Select(x => x.Catalog.Tier))
            {
                periodReports[periodTier] = await _availability.EvaluateDetailedAsync(
                    customerUserId, programId, targetBundleId, periodTier,
                    effectiveDurationDays!.Value, includeFriday, firstDeliveryDate, ct);
            }
        }

        var availabilityByTier = displayedTiers.ToDictionary(x => x.Catalog.Tier, x =>
        {
            if (isFamilyFlow) return null;
            var verdict = BuildLegacyTierAvailability(periodReports?.GetValueOrDefault(x.Catalog.Tier),
                rawCoverageByTier[x.Catalog.Tier], !effectiveDurationDays.HasValue || x.TotalPrice.HasValue,
                startDate.HasValue && effectiveDurationDays.HasValue,
                includeFriday && !fridayAvailabilityByTier[x.Catalog.Tier].IsAvailable);
            return rawCoverageByTier[x.Catalog.Tier] > 0 && restaurantsByTier[x.Catalog.Tier].Count == 0
                ? verdict with { CanSelect = false, Status = SubscriptionCatalogAvailabilityStatus.NoRestaurantsForSelection,
                    MessageCode = "subscription_flow.no_prepared_restaurants" }
                : verdict;
        });
        var recommendedTier = displayedTiers
            .Where(x => availabilityByTier[x.Catalog.Tier]?.CanSelect != false)
            .OrderByDescending(x => selectionCounts.GetValueOrDefault(x.Catalog.Tier))
            .ThenBy(x => x.Catalog.Tier)
            .Select(x => (RestaurantTier?)x.Catalog.Tier).FirstOrDefault();

        decimal? basePrice = displayedTiers
            .Where(x => availabilityByTier[x.Catalog.Tier]?.CanSelect != false)
            .Select(x => x.TotalPrice).Min();
        var labels = await LoadTierLabelsAsync(ct);
        var theme = await LoadTierThemeAsync(ct);

        IReadOnlyList<CustomerTierOptionDto> tierDtos = displayedTiers.Select(x =>
        {
            var restaurants = restaurantsByTier.GetValueOrDefault(x.Catalog.Tier) ?? [];
            var (descriptionAr, descriptionEn) = SubscriptionFlowLocalization.TierMarketing(x.Catalog.Tier);
            var availability = availabilityByTier[x.Catalog.Tier];
            if (availability?.CanSelect == false) restaurants = [];
            var totalPrice = availability?.CanSelect == false ? null : x.TotalPrice;
            var dailyPrice = availability?.CanSelect == false ? null : x.DailyPrice;
            return new CustomerTierOptionDto(
                x.Catalog.Tier, x.Catalog.TierNameAr, x.Catalog.TierNameEn, TierLogoUrl(labels, x.Catalog.Tier),
                descriptionAr, descriptionEn,
                restaurants.Count, restaurants, effectiveDurationDays, totalPrice,
                dailyPrice, totalPrice.HasValue && basePrice.HasValue ? totalPrice.Value - basePrice.Value : null,
                totalPrice.HasValue ? x.Catalog.CurrencyCode : null,
                selectionCounts.GetValueOrDefault(x.Catalog.Tier), x.Catalog.Tier == recommendedTier,
                includeFriday, fridayDays, totalServiceDays, fridayAvailabilityByTier[x.Catalog.Tier],
                theme, availability,
                x.Catalog.Prices
                    .OrderBy(p => p.DurationDays)
                    .Select(p => new DurationPriceOptionDto(p.DurationDays, x.Catalog.TierNameAr,
                        x.Catalog.TierNameEn, p.TotalPriceKwd, p.DailyPriceKwd, x.Catalog.CurrencyCode))
                    .ToList());
        }).ToList();

        return Result.Success(tierDtos);
    }

    /// <summary>Maps the single evaluator's report to the legacy tier card's availability hint,
    /// mirroring the catalog tiers route: unavailable periods stay visible but not navigable,
    /// and a missing sale price with the duration already chosen is its own disabled state.</summary>
    private static SubscriptionCatalogOptionAvailabilityDto BuildLegacyTierAvailability(
        IndividualRestaurantAvailabilityReport? report, int coveredCount, bool priced,
        bool periodRequested, bool fridayUnavailable)
    {
        var scope = periodRequested ? SubscriptionCatalogAvailabilityService.CatalogScopes.Period
            : SubscriptionCatalogAvailabilityService.CatalogScopes.Coverage;
        if (coveredCount == 0)
            return new SubscriptionCatalogOptionAvailabilityDto(scope,
                SubscriptionCatalogAvailabilityStatus.NoRestaurantsForAddress, false, 0,
                periodRequested ? 0 : null, SubscriptionCheckoutGateCodes.NoRestaurantsForAddress);
        if (!priced)
            return new SubscriptionCatalogOptionAvailabilityDto(scope,
                SubscriptionCatalogAvailabilityStatus.PricingUnavailable, false, coveredCount,
                periodRequested ? report?.Availability.EligibleRestaurantCount ?? 0 : null,
                SubscriptionCatalogMessageCodes.PricingUnavailable);
        if (!periodRequested)
            return new SubscriptionCatalogOptionAvailabilityDto(scope,
                fridayUnavailable ? SubscriptionCatalogAvailabilityStatus.NoRestaurantsForSelection
                    : SubscriptionCatalogAvailabilityStatus.SelectionRequired,
                !fridayUnavailable, coveredCount, null,
                fridayUnavailable ? "subscription_flow.friday_unavailable" : null);
        if (report is null)
            return new SubscriptionCatalogOptionAvailabilityDto(
                scope,
                SubscriptionCatalogAvailabilityStatus.NoRestaurantsForSelection, false, coveredCount, 0,
                SubscriptionCheckoutGateCodes.NoRestaurantsForSelection);
        var status = report.Availability.Status;
        var canSelect = status == IndividualRestaurantAvailabilityStatus.Available;
        string? messageCode = status == IndividualRestaurantAvailabilityStatus.Available
            ? null
            : report.Availability.MessageCode ?? SubscriptionCheckoutGateCodes.NoRestaurantsForSelection;
        return new SubscriptionCatalogOptionAvailabilityDto(
            SubscriptionCatalogAvailabilityService.CatalogScopes.Period,
            status, canSelect, coveredCount, report.Availability.EligibleRestaurantCount, messageCode);
    }

    public async Task<Result<IReadOnlyList<CustomerRestaurantBrowseCardDto>>> GetTierRestaurantsAsync(
        Guid programId, Guid? bundleId, RestaurantTier tier, Guid customerUserId, Guid[]? restaurantIds = null, Guid? familyMemberId = null, CancellationToken ct = default,
        int? durationDays = null, bool includeFriday = false, DateOnly? startDate = null)
    {
        var ids = await CollectTierRestaurantIdsAsync(programId, bundleId, tier, customerUserId, familyMemberId, restaurantIds, ct, durationDays, includeFriday, startDate);
        if (ids.IsFailure) return ids.Error;
        var (targetBundleId, classification, orderedIds) = ids.Value;

        var cards = await BuildRestaurantBrowseCardsAsync(programId, targetBundleId, classification, orderedIds, ct);
        return Result.Success<IReadOnlyList<CustomerRestaurantBrowseCardDto>>(cards);
    }

    public async Task<Result<PagedResult<CustomerRestaurantBrowseCardDto>>> GetTierRestaurantsPagedAsync(
        Guid programId, Guid? bundleId, RestaurantTier tier, Guid customerUserId, Guid[]? restaurantIds, Guid? familyMemberId, PageRequest page, CancellationToken ct = default,
        int? durationDays = null, bool includeFriday = false, DateOnly? startDate = null)
    {
        var pageRequest = page ?? new PageRequest();
        var ids = await CollectTierRestaurantIdsAsync(programId, bundleId, tier, customerUserId, familyMemberId, restaurantIds, ct, durationDays, includeFriday, startDate);
        if (ids.IsFailure) return ids.Error;
        var (targetBundleId, classification, orderedIds) = ids.Value;

        // Ids are collected for the full eligible set (totalCount must reflect eligibility filters);
        // only the page's cards are enriched.
        if (TryGetOverflowError(pageRequest, orderedIds.Count, out var overflowError))
            return Error.Validation(overflowError!);
        var pageIds = orderedIds
            .Skip(pageRequest.Skip)
            .Take(pageRequest.NormalizedPageSize)
            .ToList();
        var cards = await BuildRestaurantBrowseCardsAsync(programId, targetBundleId, classification, pageIds, ct);
        return new PagedResult<CustomerRestaurantBrowseCardDto>(cards, orderedIds.Count, pageRequest.NormalizedPage, pageRequest.NormalizedPageSize);
    }

    private async Task<Result<(Guid TargetBundleId, RestaurantTierClassificationResult Classification, List<Guid> OrderedIds)>> CollectTierRestaurantIdsAsync(
        Guid programId, Guid? bundleId, RestaurantTier tier, Guid customerUserId, Guid? familyMemberId, Guid[]? restaurantIds, CancellationToken ct,
        int? durationDays, bool includeFriday, DateOnly? startDate)
    {
        var effectiveBundleId = await ResolveEffectiveBundleIdAsync(programId, bundleId, ct);
        if (!effectiveBundleId.HasValue || !await IsActiveBundleAsync(programId, effectiveBundleId.Value, ct))
            return Error.NotFound("subscription_flow.bundle_not_found");
        var targetBundleId = effectiveBundleId.Value;

        var (gateError, deliveryRegionId) = await ValidateCustomerProfilePrerequisitesAsync(customerUserId, familyMemberId, ct);
        if (gateError is not null) return gateError;

        var classification = await _tiers.ResolveAsync(programId, targetBundleId, eligibleOnly: true, ct);
        var tierRestaurantIds = classification.Assignments
            .Where(x => RestaurantTierAccess.CanSelect(tier, x.Tier))
            .OrderBy(x => x.Tier)
            .ThenBy(x => x.RestaurantId)
            .Select(x => x.RestaurantId)
            .Distinct()
            .ToList();
        tierRestaurantIds = await FilterRestaurantIdsByDeliveryRegionAsync(tierRestaurantIds, deliveryRegionId!.Value, ct);
        if (familyMemberId is null)
        {
            var prepared = await _preparation.ResolveAsync(customerUserId, deliveryRegionId.Value, [targetBundleId], ct);
            var preparedIds = prepared.GetValueOrDefault(targetBundleId) ?? [];
            tierRestaurantIds = tierRestaurantIds.Where(preparedIds.Contains).ToList();
        }
        // Duration may be selected before the date. Date-free browse remains preparation-only.
        // Friday must be explicitly supported before a date exists; dated exceptions are
        // resolved by the period evaluator once the customer chooses a start date.
        if (includeFriday && !startDate.HasValue)
        {
            var fridayRestaurantIds = await _db.RestaurantOperatingSchedules.AsNoTracking()
                .Where(x => tierRestaurantIds.Contains(x.RestaurantId)
                    && x.DayOfWeek == DayOfWeek.Friday && x.IsWorkingDay)
                .Select(x => x.RestaurantId).Distinct().ToListAsync(ct);
            var fridaySet = fridayRestaurantIds.ToHashSet();
            tierRestaurantIds = tierRestaurantIds.Where(fridaySet.Contains).ToList();
        }
        if (familyMemberId is null && durationDays.HasValue && !startDate.HasValue)
        {
            var durationVerdict = await _allowedDurations.EvaluateAsync(durationDays.Value, includeFriday, ct);
            if (!durationVerdict.IsAllowed)
                return Error.Validation(durationVerdict.ErrorCode ?? "subscription_flow.duration_not_available");
        }
        // Once a date is supplied, use the same period validation and evaluator as summary.
        // Apply eligibility before caller filtering and pagination.
        if (familyMemberId is null && startDate.HasValue)
        {
            var request = new SubscriptionSummaryRequest(programId, targetBundleId, tier, durationDays ?? 0,
                IncludeFriday: includeFriday, StartDate: startDate);
            var validation = await ValidateSummaryRequestAsync(request, ct);
            if (validation is not null) return Error.Validation(validation);
            if (!_schedule.TryResolveCheckoutStart(startDate, includeFriday, out var firstDeliveryDate, out var startError))
                return Error.Validation(startError ?? "checkout.start_date_invalid");
            if (_availability is null)
                return new Error("subscription_flow.availability_unavailable", null, ErrorType.Failure);
            var report = await _availability.EvaluateDetailedAsync(customerUserId, programId, targetBundleId,
                tier, durationDays!.Value, includeFriday, firstDeliveryDate, ct);
            var eligibleIds = report.EligibleRestaurants.Where(x => x.IsEligible).Select(x => x.RestaurantId).ToHashSet();
            tierRestaurantIds = tierRestaurantIds.Where(eligibleIds.Contains).ToList();
        }
        var allowed = tierRestaurantIds.ToHashSet();
        var orderedIds = restaurantIds is { Length: > 0 }
            ? restaurantIds.Where(id => id != Guid.Empty && allowed.Contains(id)).Distinct().ToList()
            : tierRestaurantIds;

        return (targetBundleId, classification, orderedIds);
    }

    public async Task<Result<CustomerRestaurantBrowseCardDto>> GetRestaurantBrowseAsync(
        Guid programId, Guid bundleId, Guid restaurantId, Guid customerUserId, CancellationToken ct = default)
    {
        if (!await IsActiveBundleAsync(programId, bundleId, ct))
            return Error.NotFound("subscription_flow.bundle_not_found");

        var browseError = await ValidateRestaurantBrowseAsync(bundleId, restaurantId, customerUserId, ct);
        if (browseError is not null) return browseError;

        var classification = await _tiers.ResolveAsync(programId, bundleId, eligibleOnly: true, ct);
        if (!classification.Assignments.Any(x => x.RestaurantId == restaurantId))
            return Error.NotFound("subscription_flow.restaurant_not_found");

        var card = (await BuildRestaurantBrowseCardsAsync(programId, bundleId, classification, [restaurantId], ct))
            .FirstOrDefault();
        return card is null
            ? Error.NotFound("subscription_flow.restaurant_not_found")
            : Result.Success(card);
    }

    public async Task<Result<CustomerRestaurantBrowseDetailsDto>> GetRestaurantBrowseDetailsAsync(
        Guid programId, Guid bundleId, Guid restaurantId, Guid customerUserId, CancellationToken ct = default)
    {
        if (!await IsActiveBundleAsync(programId, bundleId, ct))
            return Error.NotFound("subscription_flow.bundle_not_found");

        var browseError = await ValidateRestaurantBrowseAsync(bundleId, restaurantId, customerUserId, ct);
        if (browseError is not null) return browseError;

        var classification = await _tiers.ResolveAsync(programId, bundleId, eligibleOnly: true, ct);
        if (!classification.Assignments.Any(x => x.RestaurantId == restaurantId))
            return Error.NotFound("subscription_flow.restaurant_not_found");

        var details = await BuildRestaurantBrowseDetailsAsync(programId, bundleId, restaurantId, customerUserId, ct);
        return details is null
            ? Error.NotFound("subscription_flow.restaurant_not_found")
            : Result.Success(details);
    }

    public async Task<Result<CustomerRestaurantBrowseMealDetailsDto>> GetMealBrowseDetailsAsync(
        Guid programId, Guid bundleId, Guid restaurantId, Guid mealId, Guid customerUserId, CancellationToken ct = default)
    {
        if (!await IsActiveBundleAsync(programId, bundleId, ct))
            return Error.NotFound("subscription_flow.bundle_not_found");

        var browseError = await ValidateRestaurantBrowseAsync(bundleId, restaurantId, customerUserId, ct);
        if (browseError is not null) return browseError;

        var classification = await _tiers.ResolveAsync(programId, bundleId, eligibleOnly: true, ct);
        if (!classification.Assignments.Any(x => x.RestaurantId == restaurantId))
            return Error.NotFound("subscription_flow.restaurant_not_found");

        var details = await BuildRestaurantBrowseMealDetailsAsync(programId, bundleId, restaurantId, mealId, customerUserId, ct);
        return details is null
            ? Error.NotFound("subscription_flow.meal_not_found")
            : Result.Success(details);
    }

    public async Task<Result<CustomerRestaurantBrowseMealDetailsDto>> GetMealBrowseDetailsAsync(
        Guid bundleId, Guid mealId, Guid customerUserId, CancellationToken ct = default)
    {
        var programId = await _db.Bundles.AsNoTracking()
            .Where(x => x.Id == bundleId && x.IsActive && x.Status == ProgramStatus.Active
                && x.Program.IsActive && x.Program.Status == ProgramStatus.Active)
            .Select(x => (Guid?)x.ProgramId)
            .FirstOrDefaultAsync(ct);
        if (!programId.HasValue)
            return Error.NotFound("subscription_flow.bundle_not_found");

        var restaurantId = await _db.Meals.AsNoTracking()
            .Where(x => x.Id == mealId)
            .Select(x => (Guid?)x.RestaurantId)
            .FirstOrDefaultAsync(ct);
        if (!restaurantId.HasValue)
            return Error.NotFound("subscription_flow.meal_not_found");

        return await GetMealBrowseDetailsAsync(programId.Value, bundleId, restaurantId.Value, mealId, customerUserId, ct);
    }

    private async Task<Error?> ValidateRestaurantBrowseAsync(Guid bundleId, Guid restaurantId, Guid customerUserId, CancellationToken ct)
    {
        var prerequisites = await _prerequisitesGate.EvaluateAsync(customerUserId, ct);
        if (!prerequisites.IsMet) return prerequisites.GateError;
        var prepared = await _preparation.ResolveAsync(customerUserId, prerequisites.ActiveAddress!.RegionId, [bundleId], ct);
        return prepared.GetValueOrDefault(bundleId)?.Contains(restaurantId) == true
            ? null : Error.NotFound("subscription_flow.restaurant_not_found");
    }

    public async Task<Result<SubscriptionConfigurationDto>> GetConfigurationAsync(
        Guid programId, Guid bundleId, Guid customerUserId, CancellationToken ct = default)
    {
        var bundle = await _db.Bundles.AsNoTracking().FirstOrDefaultAsync(x => x.Id == bundleId && x.ProgramId == programId && x.IsActive && x.Status == ProgramStatus.Active, ct);
        if (bundle is null || !IsBundleSlotConfigurationValid(bundle))
            return Error.NotFound("subscription_flow.bundle_not_found");

        var catalog = await _sender.Send(new GetCustomerSubscriptionPricingCatalogQuery(), ct);
        if (catalog.IsFailure) return catalog.Error;

        var classification = await _tiers.ResolveAsync(programId, bundleId, eligibleOnly: true, ct);
        var tierCounts = classification.Assignments.GroupBy(x => x.Tier)
            .ToDictionary(x => x.Key, x => x.Select(v => v.RestaurantId).Distinct().Count());
        var options = catalog.Value.Where(x => x.ProgramId == programId && x.BundleId == bundleId)
            .Where(x => tierCounts.GetValueOrDefault(x.Tier) > 0)
            .GroupBy(x => x.Tier).OrderBy(x => x.Key).Select(group => new TierConfigurationDto(group.Key, group.First().TierNameAr, group.First().TierNameEn,
                tierCounts.GetValueOrDefault(group.Key), group.SelectMany(x => x.Prices).DistinctBy(x => x.DurationDays).OrderBy(x => x.DurationDays)
                    .Select(x => new DurationPriceOptionDto(x.DurationDays, SubscriptionFlowLocalization.DurationNameAr(x.DurationDays), SubscriptionFlowLocalization.DurationNameEn(x.DurationDays), x.TotalPriceKwd, x.DailyPriceKwd, "KWD")).ToList())).ToList();

        return new SubscriptionConfigurationDto(
            programId, bundle.Id, bundle.NameAr, bundle.NameEn, bundle.Description, bundle.MealsPerDay,
            ToSlotRequirements(bundle.BreakfastCount, bundle.LunchCount, bundle.DinnerCount, bundle.SnackCount),
            options,
            await LoadTierThemeAsync(ct));
    }

    public async Task<Result<IReadOnlyList<CustomerRestaurantCardDto>>> GetRestaurantsAsync(
        SubscriptionFlowContext request, Guid customerUserId, CancellationToken ct = default)
    {
        var context = await ResolveBrowseContextAsync(request, customerUserId, ct);
        if (context.IsFailure) return context.Error;
        request = context.Value;
        var validation = await ValidateContextAsync(request, ct);
        if (validation is not null) return Error.Validation(validation);

        var dates = _schedule.GetServiceDates(request.StartDate, request.DurationDays, request.IncludeFriday);
        var fridayDays = _schedule.CountFridays(dates);
        var pricing = await _sender.Send(new PreviewCustomerPricingQuery(request.ProgramId, request.BundleId, request.Tier, request.DurationDays,
            IncludeFriday: request.IncludeFriday, FridayDays: fridayDays), ct);
        if (pricing.IsFailure) return pricing.Error;

        var rows = await _eligibility.EvaluateAsync(ToEligibility(request, customerUserId), ct);
        var eligible = rows.Where(x => x.IsEligible).ToList();
        var resolved = await _menus.ResolveManyAsync(eligible.Select(x => x.RestaurantId).ToList(), request.ProgramId, dates, ct);
        var mealIdsByRestaurant = resolved.GroupBy(x => x.Key.RestaurantId).ToDictionary(x => x.Key, x => x.SelectMany(v => v.Value.Assignments).Select(a => a.MealId).Distinct().ToList());
        var allMealIds = mealIdsByRestaurant.Values.SelectMany(x => x).Distinct().ToList();
        var images = await _db.MealImages.AsNoTracking().Where(x => allMealIds.Contains(x.MealId)).OrderByDescending(x => x.IsPrimary).ThenBy(x => x.DisplayOrder).ToListAsync(ct);
        var regions = await _db.RestaurantServiceRegions.AsNoTracking().Where(x => eligible.Select(e => e.RestaurantId).Contains(x.RestaurantId) && x.RegionId == request.RegionId && x.IsActive).ToListAsync(ct);
        var labels = await LoadTierLabelsAsync(ct);
        var tierLogoUrl = TierLogoUrl(labels, request.Tier);

        IReadOnlyList<CustomerRestaurantCardDto> result = eligible.Select(x =>
        {
            var ids = mealIdsByRestaurant.GetValueOrDefault(x.RestaurantId) ?? [];
            var window = regions.FirstOrDefault(r => r.RestaurantId == x.RestaurantId);
            return new CustomerRestaurantCardDto(
                x.RestaurantId, x.NameAr, x.NameEn, images.FirstOrDefault(i => ids.Contains(i.MealId))?.ImageUrl,
                request.Tier, tierLogoUrl, pricing.Value.FinalCustomerPrice, "KWD", ids.Count, window?.DeliveryWindowStart, window?.DeliveryWindowEnd,
                x.MenuPlanVersionsByDate, request.IncludeFriday, fridayDays, dates.Count);
        }).ToList();

        return Result.Success(result);
    }

    /// <summary>
    /// Contextual availability for the current summary selections: one full-period evaluation,
    /// the availability verdict, and the eligible browse cards. TotalCount is the eligible list
    /// size before pagination; a transport failure stays an error and never looks like count 0.
    /// </summary>
    public async Task<Result<CustomerRestaurantAvailabilityResponseDto>> GetRestaurantAvailabilityAsync(
        SubscriptionSummaryRequest request, Guid customerUserId, CancellationToken ct = default)
    {
        var validation = await ValidateSummaryRequestAsync(request, ct);
        if (validation is not null) return Error.Validation(validation);

        if (_availability is null)
            return new Error("subscription_flow.availability_unavailable", "Availability evaluation is not configured.", ErrorType.Failure);

        DateOnly? firstDeliveryDate = null;
        if (request.StartDate.HasValue)
        {
            if (!_schedule.TryResolveCheckoutStart(request.StartDate, request.IncludeFriday, out var resolvedStartDate, out var startError))
                return Error.Validation(startError ?? "checkout.start_date_invalid");
            firstDeliveryDate = resolvedStartDate;
        }

        var report = await _availability.EvaluateDetailedAsync(
            customerUserId, request.ProgramId, request.BundleId, request.Tier, request.DurationDays,
            request.IncludeFriday, firstDeliveryDate, ct);

        var eligible = report.EligibleRestaurants
            .OrderBy(x => x.NameEn).ThenBy(x => x.RestaurantId)
            .ToList();
        var totalCount = eligible.Count;
        if (totalCount == 0)
            return new CustomerRestaurantAvailabilityResponseDto(report.Availability, 0, null, []);

        var dates = _schedule.GetServiceDates(report.Availability.EffectiveStartDate!.Value, request.DurationDays, request.IncludeFriday);
        var fridayDays = _schedule.CountFridays(dates);
        var price = await _sender.Send(new PreviewCustomerPricingQuery(request.ProgramId, request.BundleId, request.Tier, request.DurationDays,
            IncludeFriday: request.IncludeFriday, FridayDays: fridayDays), ct);
        if (price.IsFailure) return price.Error;

        var eligibleIds = eligible.Select(x => x.RestaurantId).ToList();
        var resolved = await _menus.ResolveManyAsync(eligibleIds, request.ProgramId, dates, ct);
        var mealIdsByRestaurant = resolved.GroupBy(x => x.Key.RestaurantId)
            .ToDictionary(x => x.Key, x => x.SelectMany(v => v.Value.Assignments).Select(a => a.MealId).Distinct().ToList());
        var allMealIds = mealIdsByRestaurant.Values.SelectMany(x => x).Distinct().ToList();
        var images = await _db.MealImages.AsNoTracking()
            .Where(x => allMealIds.Contains(x.MealId))
            .OrderByDescending(x => x.IsPrimary).ThenBy(x => x.DisplayOrder)
            .ToListAsync(ct);
        var regions = await _db.RestaurantServiceRegions.AsNoTracking()
            .Where(x => eligibleIds.Contains(x.RestaurantId) && x.RegionId == report.RegionId && x.IsActive)
            .ToListAsync(ct);
        var labels = await LoadTierLabelsAsync(ct);
        var tierLogoUrl = TierLogoUrl(labels, request.Tier);

        var cards = eligible.Select(x =>
        {
            var ids = mealIdsByRestaurant.GetValueOrDefault(x.RestaurantId) ?? [];
            var window = regions.FirstOrDefault(r => r.RestaurantId == x.RestaurantId);
            return new CustomerRestaurantCardDto(
                x.RestaurantId, x.NameAr, x.NameEn, images.FirstOrDefault(i => ids.Contains(i.MealId))?.ImageUrl,
                request.Tier, tierLogoUrl, price.Value.FinalCustomerPrice, "KWD", ids.Count,
                window?.DeliveryWindowStart, window?.DeliveryWindowEnd,
                x.MenuPlanVersionsByDate, request.IncludeFriday, fridayDays, dates.Count);
        }).ToList();

        CustomerPaginationMetaDto? pagination = null;
        IReadOnlyList<CustomerRestaurantCardDto> pageCards = cards;
        if (request.RestaurantsPage.HasValue || request.RestaurantsPageSize.HasValue)
        {
            var pageRequest = new PageRequest { Page = request.RestaurantsPage ?? 1, PageSize = request.RestaurantsPageSize ?? 20 };
            var totalPages = pageRequest.NormalizedPageSize == 0 ? 0 : (int)Math.Ceiling(totalCount / (double)pageRequest.NormalizedPageSize);
            pagination = new CustomerPaginationMetaDto(
                totalCount,
                pageRequest.NormalizedPage,
                pageRequest.NormalizedPageSize,
                totalPages,
                pageRequest.NormalizedPage < totalPages,
                pageRequest.NormalizedPage > 1);
            pageCards = cards.Skip(pageRequest.Skip).Take(pageRequest.NormalizedPageSize).ToList();
        }

        return new CustomerRestaurantAvailabilityResponseDto(report.Availability, totalCount, pagination, pageCards);
    }

    public async Task<Result<CustomerRestaurantDetailsDto>> GetRestaurantDetailsAsync(        Guid restaurantId, SubscriptionFlowContext request, Guid customerUserId, CancellationToken ct = default)
    {
        var (details, error) = await ResolveRestaurantDetailsAsync(restaurantId, request, customerUserId, ct);
        if (error is not null) return error;
        return Result.Success(details!);
    }

    public async Task<Result<CustomerMealDetailsDto>> GetMealDetailsAsync(
        Guid restaurantId, Guid mealId, SubscriptionFlowContext request, Guid customerUserId, CancellationToken ct = default)
    {
        var (details, error) = await ResolveRestaurantDetailsAsync(restaurantId, request, customerUserId, ct);
        if (error is not null) return error;

        var meal = details!.MealGroups.SelectMany(x => x.Meals).FirstOrDefault(x => x.MealId == mealId);
        if (meal is null) return Error.NotFound("subscription_flow.meal_not_in_resolved_menu");

        return Result.Success(meal);
    }

    public async Task<Result<SubscriptionCheckoutReadinessDto>> GetReadinessAsync(
        SubscriptionSummaryRequest request, Guid customerUserId, CancellationToken ct = default)
    {
        var readiness = await _readiness.EvaluateAsync(customerUserId, request.ProgramId, request.BundleId,
            request.Tier, request.DurationDays, request.IncludeFriday, ct, request.StartDate);
        if (readiness.IsFailure) return readiness.Error;
        return Result.Success(readiness.Value);
    }

    public async Task<Result<SubscriptionOrderSummaryDto>> GetOrderSummaryAsync(
        SubscriptionSummaryRequest request, Guid customerUserId, CancellationToken ct = default)
    {
        var validation = await ValidateSummaryRequestAsync(request, ct);
        if (validation is not null) return Error.Validation(validation);

        if (!_schedule.TryResolveCheckoutStart(request.StartDate, request.IncludeFriday, out var startDate, out var startError))
            return Error.Validation(startError ?? "checkout.start_date_invalid");

        var readiness = await _readiness.EvaluateAsync(customerUserId, request.ProgramId, request.BundleId,
            request.Tier, request.DurationDays, request.IncludeFriday, ct, request.StartDate);
        if (readiness.IsFailure) return readiness.Error;

        if (!readiness.Value.CanOpenSummary)
        {
            return new Error(SubscriptionCheckoutGateCodes.ProfileIncomplete, null, ErrorType.Conflict,
                [new { error = SubscriptionCheckoutGateCodes.ProfileIncomplete, readiness = readiness.Value }]);
        }

        var chosenStartDate = request.StartDate!.Value;
        var classification = await _tiers.ResolveAsync(request.ProgramId, request.BundleId, eligibleOnly: true, ct);
        if (!classification.Assignments.Any(x => x.Tier == request.Tier))
            return Error.Validation("subscription_flow.tier_unavailable");

        var program = await _db.Programs.AsNoTracking().SingleAsync(x => x.Id == request.ProgramId, ct);
        var bundle = await _db.Bundles.AsNoTracking().SingleAsync(x => x.Id == request.BundleId, ct);
        var dates = _schedule.GetServiceDates(startDate, request.DurationDays, request.IncludeFriday);
        var fridayDays = _schedule.CountFridays(dates);
        var price = await _sender.Send(new PreviewCustomerPricingQuery(request.ProgramId, request.BundleId, request.Tier, request.DurationDays,
            IncludeFriday: request.IncludeFriday, FridayDays: fridayDays), ct);
        if (price.IsFailure) return price.Error;

        // Coupon evaluation is read-only: no usage increment, no redemption row, no hidden hold.
        // Checkout re-validates and re-prices; this display is never a guarantee of the later price.
        SubscriptionSummaryCouponDto? coupon = null;
        var priceValue = price.Value;
        var promoCode = request.PromoCode?.Trim();
        if (!string.IsNullOrWhiteSpace(promoCode))
        {
            var evaluation = await _coupons.EvaluateAsync(
                promoCode, customerUserId, request.ProgramId, request.BundleId, request.DurationDays,
                priceValue.FinalCustomerPrice, ct);
            if (evaluation.IsValid)
            {
                var discounted = await _sender.Send(new PreviewCustomerPricingQuery(request.ProgramId, request.BundleId, request.Tier, request.DurationDays,
                    IncludeFriday: request.IncludeFriday, FridayDays: fridayDays,
                    CustomerId: customerUserId, PromoCode: promoCode), ct);
                if (discounted.IsFailure) return discounted.Error;
                priceValue = discounted.Value;
                // The pricing engine places the offer discount in the campaign slot; coupon.discountAmount
                // repeats that applied amount so the client never sums the two fields.
                coupon = new SubscriptionSummaryCouponDto(
                    evaluation.Code!, evaluation.Status, evaluation.OfferId, priceValue.CampaignDiscountApplied,
                    evaluation.MessageAr, evaluation.MessageEn);
            }
            else
            {
                // A rejected coupon is not an error: the summary shows the undiscounted price with the reason.
                coupon = new SubscriptionSummaryCouponDto(
                    evaluation.Code!, evaluation.Status, evaluation.OfferId, 0m,
                    evaluation.MessageAr, evaluation.MessageEn);
            }
        }

        var slots = await _slots.ResolveAsync(request.ProgramId, request.BundleId, ct);
        var summarySlots = ToSummarySlots(slots);
        var restaurantIds = readiness.Value.Restaurants.Select(x => x.RestaurantId).ToList();

        // Totals (count, repetition, meal variety, pricing, CanCheckout) always reflect the full
        // eligible set; pagination only limits the displayed restaurant cards.
        var allRestaurants = readiness.Value.Restaurants;
        var restaurantCount = allRestaurants.Count;
        IReadOnlyList<SubscriptionSummaryRestaurantDto> displayedRestaurants = allRestaurants;
        CustomerPaginationMetaDto? restaurantsPagination = null;
        if (request.RestaurantsPage.HasValue || request.RestaurantsPageSize.HasValue)
        {
            var restaurantsPage = new PageRequest { Page = request.RestaurantsPage ?? 1, PageSize = request.RestaurantsPageSize ?? 20 };
            if (TryGetOverflowError(restaurantsPage, restaurantCount, out var overflowError))
                return Error.Validation(overflowError!);
            var totalPages = (int)Math.Ceiling(restaurantCount / (double)restaurantsPage.NormalizedPageSize);
            restaurantsPagination = new CustomerPaginationMetaDto(
                restaurantCount,
                restaurantsPage.NormalizedPage,
                restaurantsPage.NormalizedPageSize,
                totalPages,
                restaurantsPage.NormalizedPage < totalPages,
                restaurantsPage.NormalizedPage > 1);
            displayedRestaurants = allRestaurants
                .Skip(restaurantsPage.Skip)
                .Take(restaurantsPage.NormalizedPageSize)
                .ToList();
        }

        var logos = await LoadRestaurantLogoUrlsAsync(displayedRestaurants.Select(x => x.RestaurantId).ToList(), ct);
        var restaurants = displayedRestaurants
            .Select(x => x with { LogoUrl = logos.GetValueOrDefault(x.RestaurantId) ?? x.LogoUrl })
            .ToList();
        // A zero-eligible set must not fabricate a positive repetition limit: show 0 instead of
        // calculating a limit against a placeholder count of 1.
        var repetition = restaurantCount > 0
            ? _diversification.CalculateLimit(Guid.Empty, request.DurationDays, restaurantCount)
            : null;
        var restaurantRepetition = repetition is null
            ? new RestaurantRepetitionSummaryDto(0, 0, request.DurationDays)
            : new RestaurantRepetitionSummaryDto(
                repetition.LimitValue,
                restaurantCount,
                request.DurationDays);
        var mealVariety = await CountTierBundleMealsAsync(restaurantIds, request.ProgramId, request.BundleId, ct);
        var labels = await LoadTierLabelsAsync(ct);
        var (tierNameAr, tierNameEn) = TierDisplayName(request.Tier, labels);

        return new SubscriptionOrderSummaryDto(
            program.Id, program.NameAr, program.NameEn, CatalogImageStorage.ToContentUrl(program.ImageStorageKey),
            bundle.Id, bundle.NameAr, bundle.NameEn, CatalogImageStorage.ToContentUrl(bundle.ImageStorageKey),
            restaurantCount, request.Tier, tierNameAr, tierNameEn, request.DurationDays, startDate, dates.Last(), dates,
            summarySlots, priceValue.UnroundedCorePrice, priceValue.CampaignDiscountApplied, priceValue.PromoCodeDiscountApplied,
            priceValue.WalletCreditApplied, priceValue.GatewayCostRecoveryAmount, priceValue.FinalCustomerPrice, priceValue.CurrencyCode,
            true, "POST /api/v1/customer/subscriptions/checkout", request.IncludeFriday, fridayDays, dates.Count,
            BuildIncludes(summarySlots, restaurantCount, dates.Count, mealVariety),
            readiness.Value.Address, restaurants, readiness.Value.CanCheckout, readiness.Value.CheckoutBlockReason, chosenStartDate,
            restaurantRepetition,
            restaurantsPagination,
            coupon,
            readiness.Value.Availability);
    }

    public async Task<Result<IReadOnlyList<CustomerPaymentMethodDto>>> GetPaymentMethodsAsync(decimal amount, CancellationToken ct = default, bool allowSandboxCompletion = true)
    {
        if (amount <= 0) return Error.Validation("subscription_flow.invalid_amount");

        var config = await _db.PayzahGatewayConfigs.AsNoTracking().FirstOrDefaultAsync(x => x.GatewayName == "Payzah", ct);
        if (config is null)
        {
            return Error.Conflict("payment.gateway_not_configured");
        }

        if (allowSandboxCompletion && await SandboxCheckoutPolicy.ReadAsync(_db, ct) is not null)
            return Result.Success<IReadOnlyList<CustomerPaymentMethodDto>>([
                new(SandboxCheckoutPolicy.PaymentMethodId, "SANDBOX_WITHOUT_PAYMENT", "إكمال بدون دفع — تجربة",
                    "Complete without payment — sandbox", "SANDBOX_HOSTED", 0m, amount, config.DefaultCurrency, true, null)
            ]);

        IReadOnlyList<PaymentGatewayMethod> methods;
        try
        {
            methods = await _payments.GetPaymentMethodsAsync(amount, ct);
        }
        catch
        {
            return new Error("payment.gateway_unavailable", "Payzah did not return payment methods.", ErrorType.Failure);
        }

        var result = methods
            .Select(x => new CustomerPaymentMethodDto(x.PaymentMethodId, x.Code, x.NameAr, x.NameEn, "PAYZAH_HOSTED", x.ServiceCharge, x.TotalAmount, x.CurrencyCode, true, null, x.ImageUrl))
            .ToList();

        result.Add(new CustomerPaymentMethodDto(null, "MEALMATE_WALLET", "محفظة MealMate", "MealMate Wallet", "WALLET", 0m, amount, config.DefaultCurrency, false, "wallet_not_implemented", null));
        return Result.Success<IReadOnlyList<CustomerPaymentMethodDto>>(result);
    }

    private async Task<(CustomerRestaurantDetailsDto? Value, Error? Error)> ResolveRestaurantDetailsAsync(
        Guid restaurantId, SubscriptionFlowContext request, Guid customerUserId, CancellationToken ct)
    {
        var context = await ResolveBrowseContextAsync(request, customerUserId, ct);
        if (context.IsFailure) return (null, context.Error);
        request = context.Value;
        var validation = await ValidateContextAsync(request, ct);
        if (validation is not null) return (null, Error.Validation(validation));

        var eligibleRows = await _eligibility.EvaluateAsync(ToEligibility(request, customerUserId, restaurantId), ct);
        var eligible = eligibleRows.SingleOrDefault(x => x.IsEligible);
        if (eligible is null) return (null, Error.Validation("restaurant.not_eligible"));

        var dates = _schedule.GetServiceDates(request.StartDate, request.DurationDays, request.IncludeFriday);
        var fridayDays = _schedule.CountFridays(dates);
        var resolved = await _menus.ResolveManyAsync([restaurantId], request.ProgramId, dates, ct);
        var assignments = resolved.Values.SelectMany(x => x.Assignments).DistinctBy(x => x.MealId).OrderBy(x => x.Slot).ThenBy(x => x.SortOrder).ToList();
        var cards = await BuildMealCardsAsync(assignments, restaurantId, customerUserId, ct);
        var window = await _db.RestaurantServiceRegions.AsNoTracking().FirstOrDefaultAsync(x => x.RestaurantId == restaurantId && x.RegionId == request.RegionId && x.IsActive, ct);
        var pricing = await _sender.Send(new PreviewCustomerPricingQuery(request.ProgramId, request.BundleId, request.Tier, request.DurationDays,
            IncludeFriday: request.IncludeFriday, FridayDays: fridayDays), ct);
        if (pricing.IsFailure) return (null, pricing.Error);

        var groups = cards.GroupBy(x => x.Slot, StringComparer.OrdinalIgnoreCase).OrderBy(x => SlotOrder(x.Key))
            .Select(x =>
            {
                var (slotNameAr, slotNameEn) = SubscriptionFlowLocalization.SlotName(x.Key);
                return new CustomerMealGroupDto(x.Key, slotNameAr, slotNameEn, x.OrderBy(m => m.SortOrder).ToList());
            }).ToList();
        var labels = await LoadTierLabelsAsync(ct);

        return (new CustomerRestaurantDetailsDto(restaurantId, eligible.NameAr, eligible.NameEn, cards.FirstOrDefault()?.ImageUrl, request.Tier,
            TierLogoUrl(labels, request.Tier), pricing.Value.FinalCustomerPrice, pricing.Value.CurrencyCode, window?.DeliveryWindowStart, window?.DeliveryWindowEnd,
            eligible.MenuPlanVersionsByDate, groups, request.IncludeFriday, fridayDays, dates.Count), null);
    }

    private async Task<List<CustomerMealDetailsDto>> BuildMealCardsAsync(
        IReadOnlyList<ResolvedMenuAssignment> assignments, Guid restaurantId, Guid customerUserId, CancellationToken ct)
    {
        var mealIds = assignments.Select(x => x.MealId).Distinct().ToList();
        var meals = await _db.Meals.AsNoTracking()
            .Where(x => mealIds.Contains(x.Id) && x.RestaurantId == restaurantId && x.ApprovalStatus == VerificationStatus.Approved && x.IsAvailableForNewOrders)
            .ToListAsync(ct);
        var images = await _db.MealImages.AsNoTracking()
            .Where(x => mealIds.Contains(x.MealId))
            .OrderByDescending(x => x.IsPrimary).ThenBy(x => x.DisplayOrder)
            .ToListAsync(ct);
        var links = await _db.MealIngredients.AsNoTracking()
            .Where(x => mealIds.Contains(x.MealId))
            .Join(_db.Ingredients.AsNoTracking(), x => x.IngredientId, x => x.Id, (link, ingredient) => new { link.MealId, link.PortionGrams, link.SortOrder, Ingredient = ingredient })
            .ToListAsync(ct);
        var mealPreferences = await _db.MealFoodPreferences.AsNoTracking().Where(x => mealIds.Contains(x.MealId)).ToListAsync(ct);
        var ingredientIds = links.Select(x => x.Ingredient.Id).Distinct().ToList();
        var ingredientPrefs = (await _db.IngredientFoodPreferences.AsNoTracking().Where(x => ingredientIds.Contains(x.IngredientId)).ToListAsync(ct))
            .Select(x => (x.IngredientId, x.PreferenceCode)).ToList();
        var prefs = await MealPreferenceFilter.LoadAsync(_db, customerUserId, ct);
        var result = new List<CustomerMealDetailsDto>();
        foreach (var assignment in assignments)
        {
            var meal = meals.FirstOrDefault(x => x.Id == assignment.MealId);
            if (meal is null) continue;
            var ingredients = links.Where(x => x.MealId == meal.Id).OrderBy(x => x.SortOrder).ToList();
            var named = ingredients.Select(x => new MealPreferenceFilter.NamedIngredient(x.Ingredient.Id, x.Ingredient.NameAr, x.Ingredient.NameEn, FoodCodeNormalizer.Split(x.Ingredient.AllergenCodes))).ToList();
            var tags = MealPreferenceFilter.DeriveDietaryTags(meal.NutritionTagCodes, mealPreferences.Where(x => x.MealId == meal.Id).Select(x => x.PreferenceCode), named, ingredientPrefs);
            if (MealPreferenceFilter.BlockReason(FoodCodeNormalizer.Split(meal.PresentAllergenCodes), FoodCodeNormalizer.Split(meal.MayContainAllergenCodes), named, tags, prefs) is not null)
                continue;
            result.Add(new CustomerMealDetailsDto(
                meal.Id, meal.NameAr, meal.NameEn, meal.DescriptionAr, meal.DescriptionEn, assignment.Slot, assignment.SortOrder,
                images.FirstOrDefault(x => x.MealId == meal.Id)?.ImageUrl, meal.Calories, meal.ProteinG, meal.CarbsG, meal.NetCarbsG, meal.FatG, meal.FiberG,
                meal.ServingWeightG, FoodCodeNormalizer.Split(meal.PresentAllergenCodes), FoodCodeNormalizer.Split(meal.MayContainAllergenCodes), tags,
                ingredients.Where(x => !x.Ingredient.IsCore).Select(x => new CustomerIngredientDetailsDto(
                    x.Ingredient.Id, x.Ingredient.NameAr, x.Ingredient.NameEn, x.PortionGrams,
                    x.Ingredient.IsOriginVisibleToCustomers ? x.Ingredient.OriginCountryCode : null,
                    x.Ingredient.IsOriginVisibleToCustomers ? x.Ingredient.SupplierName : null, x.Ingredient.IsLocal, x.Ingredient.IsHalalCertified,
                    FoodCodeNormalizer.Split(x.Ingredient.AllergenCodes), x.Ingredient.IsCore,
                    ToPublicRestaurantImageUrl(x.Ingredient.MealIconUrl))).ToList(), true, null));
        }
        return result;
    }

    private async Task<CustomerFridayAvailabilityDto> ResolveFridayAvailabilityAsync(
        Guid programId, Guid bundleId, RestaurantTier? tier, CancellationToken ct, IReadOnlyCollection<RestaurantTier>? pricedTiers = null)
    {
        var classification = await _tiers.ResolveAsync(programId, bundleId, eligibleOnly: true, ct);
        var classified = classification.Assignments.AsEnumerable();
        if (tier.HasValue)
            classified = classified.Where(x => x.Tier == tier.Value);
        if (pricedTiers is { Count: > 0 })
            classified = classified.Where(x => pricedTiers.Contains(x.Tier));
        var classifiedIds = classified.Select(x => x.RestaurantId).ToList();
        var schedules = await _db.RestaurantOperatingSchedules.AsNoTracking()
            .Where(x => classifiedIds.Contains(x.RestaurantId))
            .ToListAsync(ct);
        var fridayCount = classifiedIds.Count(id =>
            schedules.Any(x => x.RestaurantId == id && x.DayOfWeek == DayOfWeek.Friday && x.IsWorkingDay));
        return CreateFridayAvailability(fridayCount);
    }

    private static CustomerFridayAvailabilityDto CreateFridayAvailability(int restaurantCount)
    {
        var available = restaurantCount > 0;
        var (msgAr, msgEn) = SubscriptionFlowLocalization.FridayAvailability(available);
        return new CustomerFridayAvailabilityDto(
            available,
            restaurantCount,
            available ? "FRIDAY_AVAILABLE" : "FRIDAY_UNAVAILABLE",
            msgAr,
            msgEn,
            available ? "INFO" : "DISABLED");
    }

    private async Task<(Error? Error, Guid? DeliveryRegionId)> ValidateCustomerProfilePrerequisitesAsync(
        Guid customerUserId, Guid? familyMemberId, CancellationToken ct)
    {
        if (customerUserId == Guid.Empty)
            return (Error.Unauthorized("auth.unauthorized"), null);

        if (familyMemberId is Guid memberId && memberId != Guid.Empty)
            return await ValidateFamilyMemberClassificationPrerequisitesAsync(customerUserId, memberId, ct);

        var prerequisites = await _prerequisitesGate.EvaluateAsync(customerUserId, ct);
        if (!prerequisites.IsMet)
            return (prerequisites.GateError, null);

        return (null, prerequisites.ActiveAddress?.RegionId);
    }

    private async Task<(Error? Error, Guid? DeliveryRegionId)> ValidateFamilyMemberClassificationPrerequisitesAsync(
        Guid managerUserId, Guid familyMemberId, CancellationToken ct)
    {
        var group = await _db.FamilyGroups.AsNoTracking()
            .Where(x => x.ManagerCustomerUserId == managerUserId
                && (x.Status == FamilyGroupStatus.Draft
                    || x.Status == FamilyGroupStatus.CheckoutPending
                    || x.Status == FamilyGroupStatus.Active
                    || x.Status == FamilyGroupStatus.Frozen))
            .Select(x => new { x.Id })
            .FirstOrDefaultAsync(ct);
        if (group is null)
            return (Error.NotFound("family.not_found"), null);

        var member = await _db.FamilyMembers.AsNoTracking()
            .FirstOrDefaultAsync(x => x.Id == familyMemberId && x.FamilyGroupId == group.Id, ct);
        if (member is null)
            return (Error.NotFound("family.not_found"), null);

        var linkedUserId = await _db.FamilyMemberAccesses.AsNoTracking()
            .Where(a => a.FamilyMemberId == member.Id && a.RemovedAtUtc == null)
            .Select(a => a.LinkedCustomerUserId)
            .FirstOrDefaultAsync(ct);
        Guid? targetUserId = linkedUserId ?? (member.Role == FamilyMemberRole.Manager ? managerUserId : null);

        if (member.DeliveryAddressId is Guid assignedId && targetUserId is Guid assignedUserId)
        {
            var assigned = await LoadCustomerDeliveryAddressDtoAsync(assignedId, assignedUserId, ct);
            if (assigned is not null)
                return (null, assigned.RegionId);
        }

        if (targetUserId is Guid userId)
        {
            var owned = await ResolveCustomerDeliveryAddressAsync(userId, ct);
            if (owned is not null)
                return (null, owned.RegionId);
        }

        return (new Error("family.address_required", null, ErrorType.Conflict,
            [new
            {
                error = "family.address_required",
                missingStep = SubscriptionCheckoutGateCodes.Address,
                actionEndpoint = $"POST /api/v1/customer/family-subscription-flow/members/{familyMemberId}/addresses"
            }]), null);
    }

    private async Task<Guid?> ResolveCustomerDeliveryRegionIdAsync(Guid userId, CancellationToken ct)
    {
        var address = await ResolveCustomerDeliveryAddressAsync(userId, ct);
        return address?.RegionId;
    }

    private async Task<CustomerSubscriptionDeliveryAddressDto?> ResolveCustomerDeliveryAddressAsync(Guid userId, CancellationToken ct)
    {
        if (userId == Guid.Empty) return null;

        var profile = await _db.CustomerProfiles.AsNoTracking()
            .Where(x => x.UserId == userId)
            .Select(x => new { x.ActiveDeliveryAddressId, x.ActiveDeliveryRegionId })
            .FirstOrDefaultAsync(ct);

        if (profile?.ActiveDeliveryAddressId is Guid activeAddressId)
        {
            var active = await LoadCustomerDeliveryAddressDtoAsync(activeAddressId, userId, ct);
            if (active is not null)
                return active;
        }

        if (profile?.ActiveDeliveryRegionId is Guid activeRegionId && activeRegionId != Guid.Empty)
        {
            var byRegion = await _db.CustomerAddresses.AsNoTracking()
                .Where(x => x.UserId == userId && x.RegionId == activeRegionId)
                .OrderByDescending(x => x.IsDefault)
                .ThenByDescending(x => x.CreatedAtUtc)
                .FirstOrDefaultAsync(ct);
            if (byRegion is not null)
                return MapCustomerDeliveryAddressDto(byRegion);
        }

        var fallback = await _db.CustomerAddresses.AsNoTracking()
            .Where(x => x.UserId == userId && x.RegionId != null && x.RegionId != Guid.Empty)
            .OrderByDescending(x => x.IsDefault)
            .ThenByDescending(x => x.CreatedAtUtc)
            .FirstOrDefaultAsync(ct);
        return fallback is null ? null : MapCustomerDeliveryAddressDto(fallback);
    }

    private async Task<CustomerSubscriptionDeliveryAddressDto?> LoadCustomerDeliveryAddressDtoAsync(Guid addressId, Guid userId, CancellationToken ct)
    {
        var address = await _db.CustomerAddresses.AsNoTracking()
            .FirstOrDefaultAsync(x => x.Id == addressId && x.UserId == userId, ct);
        if (address is null || address.RegionId is null || address.RegionId == Guid.Empty)
            return null;
        return MapCustomerDeliveryAddressDto(address);
    }

    private static CustomerSubscriptionDeliveryAddressDto MapCustomerDeliveryAddressDto(CustomerAddress address)
        => new(
            address.Id,
            address.RegionId!.Value,
            address.Label,
            address.AddressLine,
            address.Governorate,
            address.Area,
            address.Block,
            address.Street,
            address.Avenue,
            address.Latitude,
            address.Longitude,
            address.IsDefault);

    private async Task<List<Guid>> FilterRestaurantIdsByDeliveryRegionAsync(IReadOnlyList<Guid> restaurantIds, Guid regionId, CancellationToken ct)
    {
        if (restaurantIds.Count == 0 || regionId == Guid.Empty)
            return [];

        var covered = await _db.RestaurantServiceRegions.AsNoTracking()
            .Where(x => restaurantIds.Contains(x.RestaurantId) && x.RegionId == regionId && x.IsActive)
            .Select(x => x.RestaurantId)
            .Distinct()
            .ToListAsync(ct);
        var coveredSet = covered.ToHashSet();
        return restaurantIds.Where(coveredSet.Contains).ToList();
    }

    private Task<bool> IsActiveBundleAsync(Guid programId, Guid bundleId, CancellationToken ct)
        => _db.Bundles.AsNoTracking().AnyAsync(x => x.Id == bundleId && x.ProgramId == programId
            && x.IsActive && x.Status == ProgramStatus.Active && x.Program.IsActive && x.Program.Status == ProgramStatus.Active, ct);

    private static RestaurantEligibilityRequest ToEligibility(SubscriptionFlowContext r, Guid? userId, Guid? restaurantId = null)
        => new(r.ProgramId, r.BundleId, r.Tier, r.RegionId, r.StartDate, r.DurationDays, r.IncludeFriday, userId, restaurantId);

    private async Task<Result<SubscriptionFlowContext>> ResolveBrowseContextAsync(
        SubscriptionFlowContext request, Guid customerUserId, CancellationToken ct)
    {
        var prerequisites = await _prerequisitesGate.EvaluateAsync(customerUserId, ct);
        if (!prerequisites.IsMet) return prerequisites.GateError!;
        if (!_schedule.TryResolveCheckoutStart(request.StartDate, request.IncludeFriday, out var firstDeliveryDate, out var startError))
            return Error.Validation(startError ?? "checkout.start_date_invalid");
        return request with { RegionId = prerequisites.ActiveAddress!.RegionId, StartDate = firstDeliveryDate };
    }

    private async Task<string?> ValidateContextAsync(SubscriptionFlowContext r, CancellationToken ct)
    {
        if (r.ProgramId == Guid.Empty || r.BundleId == Guid.Empty || r.RegionId == Guid.Empty
            || r.StartDate < DateOnly.FromDateTime(DateTime.UtcNow))
            return "subscription_flow.invalid_context";
        var allowed = await _allowedDurations.EvaluateAsync(r.DurationDays, r.IncludeFriday, ct);
        return allowed.IsAllowed ? null : allowed.ErrorCode ?? "subscription_flow.duration_not_available";
    }

    private async Task<string?> ValidateSummaryRequestAsync(SubscriptionSummaryRequest r, CancellationToken ct)
    {
        if (r.ProgramId == Guid.Empty || r.BundleId == Guid.Empty)
            return "subscription_flow.invalid_context";
        if (r.StartDate is null || r.StartDate == default)
            return "checkout.start_date_required";
        var allowed = await _allowedDurations.EvaluateAsync(r.DurationDays, r.IncludeFriday, ct);
        return allowed.IsAllowed ? null : allowed.ErrorCode ?? "subscription_flow.duration_not_available";
    }

    private static bool IsBundleSlotConfigurationValid(MealMate.Domain.ProgramsSubscriptions.Bundle x)
        => x.MealsPerDay > 0 && x.MealsPerDay == x.BreakfastCount + x.LunchCount + x.DinnerCount + x.SnackCount;

    private static List<SlotRequirementDto> ToSlotRequirements(int breakfast, int lunch, int dinner, int snack)
        => new[] { new SlotRequirementDto("breakfast", breakfast), new SlotRequirementDto("lunch", lunch), new SlotRequirementDto("dinner", dinner), new SlotRequirementDto("snack", snack) }.Where(x => x.Count > 0).ToList();

    private static List<CustomerBundleSlotDto> ToDisplaySlots(int breakfast, int lunch, int dinner, int snack)
        => ToSlotRequirements(breakfast, lunch, dinner, snack)
            .Select(x =>
            {
                var (nameAr, nameEn) = SubscriptionFlowLocalization.SlotName(x.Slot);
                return new CustomerBundleSlotDto(x.Slot, nameAr, nameEn, x.Count);
            })
            .ToList();

    private static IReadOnlyList<CustomerSummarySlotDto> ToSummarySlots(IReadOnlyList<SlotRequirementDto> slots)
        => slots.Where(x => x.Count > 0)
            .Select(x =>
            {
                var (nameAr, nameEn) = SubscriptionFlowLocalization.SlotName(x.Slot);
                return new CustomerSummarySlotDto(x.Slot, nameAr, nameEn, x.Count);
            })
            .ToList();

    private async Task<int> CountTierBundleMealsAsync(IReadOnlyList<Guid> restaurantIds, Guid programId, Guid bundleId, CancellationToken ct)
    {
        if (restaurantIds.Count == 0) return 0;
        var approvedMeals = await _db.Meals.AsNoTracking()
            .Where(x => restaurantIds.Contains(x.RestaurantId)
                && x.ApprovalStatus == VerificationStatus.Approved
                && x.IsAvailableForNewOrders)
            .Select(x => new { x.Id, x.ProgramId, x.BundleId })
            .ToListAsync(ct);
        if (approvedMeals.Count == 0) return 0;
        var mealIds = approvedMeals.Select(x => x.Id).ToList();
        var assignedToProgram = (await _db.MealProgramAssignments.AsNoTracking()
            .Where(x => mealIds.Contains(x.MealId) && x.ProgramId == programId)
            .Select(x => x.MealId)
            .ToListAsync(ct)).ToHashSet();
        return approvedMeals.Count(x => x.ProgramId == programId || x.BundleId == bundleId || assignedToProgram.Contains(x.Id));
    }

    private static CustomerSummaryIncludesDto BuildIncludes(
        IReadOnlyList<CustomerSummarySlotDto> slots, int restaurantCount, int serviceDays, int mealVariety)
    {
        var items = new List<CustomerSummaryIncludeDto>();
        var mealsPerDay = slots.Sum(x => x.Count);
        if (mealsPerDay > 0)
        {
            var namesAr = string.Join(" - ", slots.Select(SlotLabelAr));
            var namesEn = string.Join(" · ", slots.Select(SlotLabelEn));
            var (mealsAr, mealsEn) = SubscriptionFlowLocalization.MealsPerDayInclude(mealsPerDay);
            items.Add(new("DAILY_MEALS", $"{mealsAr} ({namesAr})", $"{mealsEn} ({namesEn})"));
        }
        var (restAr, restEn) = SubscriptionFlowLocalization.RestaurantsInTierInclude(restaurantCount);
        items.Add(new("RESTAURANTS", restAr, restEn));

        var (srvAr, srvEn) = SubscriptionFlowLocalization.ServiceDaysInclude(serviceDays);
        items.Add(new("SERVICE_DAYS", srvAr, srvEn));

        if (mealVariety > 0)
        {
            var (mealAr, mealEn) = SubscriptionFlowLocalization.ApprovedMealsInclude(mealVariety);
            items.Add(new("MEAL_VARIETY", mealAr, mealEn));
        }

        var (titleAr, titleEn) = SubscriptionFlowLocalization.IncludesTitle();
        return new(titleAr, titleEn, items);
    }

    private static string SlotLabelAr(CustomerSummarySlotDto slot) => slot.Count > 1 ? $"{slot.NameAr} ×{slot.Count}" : slot.NameAr;
    private static string SlotLabelEn(CustomerSummarySlotDto slot) => slot.Count > 1 ? $"{slot.NameEn} ×{slot.Count}" : slot.NameEn;

    private static string? ToPublicRestaurantImageUrl(string? value) => CatalogImageStorage.ToContentUrl(value);

    private async Task<Dictionary<Guid, string?>> LoadRestaurantLogoUrlsAsync(IReadOnlyCollection<Guid> restaurantIds, CancellationToken ct)
    {
        var ids = restaurantIds.Where(x => x != Guid.Empty).Distinct().ToList();
        var result = ids.ToDictionary(x => x, _ => (string?)null);
        if (ids.Count == 0) return result;

        var documents = await _db.Documents.AsNoTracking()
            .Where(x => ids.Contains(x.OwnerId)
                && x.OwnerType == DocumentOwnerType.Restaurant
                && x.DocumentType == DocumentType.ProfileImage)
            .Select(x => new { x.OwnerId, x.StoragePath })
            .ToListAsync(ct);
        var registrations = await _db.RestaurantRegistrations.AsNoTracking()
            .Where(x => x.ResultingRestaurantId != null && ids.Contains(x.ResultingRestaurantId.Value))
            .Select(x => new { x.Id, RestaurantId = x.ResultingRestaurantId!.Value })
            .ToListAsync(ct);
        var branchOwnerIds = ids.Concat(registrations.Select(x => x.Id)).Distinct().ToList();
        var branchPhotos = await _db.RestaurantBranches.AsNoTracking()
            .Where(x => branchOwnerIds.Contains(x.RestaurantRegistrationId) && x.IsActive)
            .OrderByDescending(x => x.IsPrimary)
            .ThenBy(x => x.NameAr)
            .Select(x => new { x.RestaurantRegistrationId, x.PhotoStorageKey })
            .ToListAsync(ct);

        foreach (var id in ids)
        {
            var logo = documents.FirstOrDefault(x => x.OwnerId == id)?.StoragePath;
            if (string.IsNullOrWhiteSpace(logo))
            {
                var registrationId = registrations.FirstOrDefault(x => x.RestaurantId == id)?.Id;
                logo = branchPhotos.FirstOrDefault(x => x.RestaurantRegistrationId == id)?.PhotoStorageKey
                    ?? (registrationId is null
                        ? null
                        : branchPhotos.FirstOrDefault(x => x.RestaurantRegistrationId == registrationId)?.PhotoStorageKey);
            }

            result[id] = ToPublicRestaurantImageUrl(logo);
        }

        return result;
    }

    private static string NormalizeIngredientCategory(string? category)
    {
        var value = (category ?? "other").Trim().ToLowerInvariant();
        return value switch
        {
            "oil" or "oils" => "oils",
            "fish" or "seafood" => "seafood",
            "chicken" or "poultry" => "poultry",
            "meat" or "beef" or "lamb" => "meat",
            "dairy" or "eggs" or "cheese" => "dairy",
            "produce" or "vegetables" or "fruits" => "produce",
            "grain" or "grains" => "grains",
            _ => "other",
        };
    }

    private static int IngredientCategoryOrder(string category) => category switch
    {
        "oils" => 1,
        "seafood" => 2,
        "poultry" => 3,
        "meat" => 4,
        "dairy" => 5,
        "produce" => 6,
        "grains" => 7,
        _ => 99,
    };

    private static (string Ar, string En) TierDisplayName(RestaurantTier tier, TierLabelsSettings labels) => tier switch
    {
        RestaurantTier.Basic => (labels.BasicAr, labels.BasicEn),
        RestaurantTier.Platinum => (labels.PlatinumAr, labels.PlatinumEn),
        _ => (labels.EliteAr, labels.EliteEn),
    };

    private async Task<TierLabelsSettings> LoadTierLabelsAsync(CancellationToken ct)
    {
        var settingsRow = await _db.SystemSettings.AsNoTracking()
            .FirstOrDefaultAsync(x => x.Key == SubscriptionPricingSettingsDefaults.Key && x.IsActive, ct);
        return SubscriptionPricingSettingsReader.Deserialize(settingsRow?.ValueJson).TierLabels;
    }

    /// <summary>
    /// The administrator-picked mobile design for the classification-selection screen, or null when
    /// nothing has been saved. Sourced from the saved pricing settings document; the name is derived
    /// from the closed design catalog, and an unknown stored value falls back to no theme.
    /// </summary>
    private async Task<CustomerTierThemeDto?> LoadTierThemeAsync(CancellationToken ct)
    {
        var settingsRow = await _db.SystemSettings.AsNoTracking()
            .FirstOrDefaultAsync(x => x.Key == SubscriptionPricingSettingsDefaults.Key && x.IsActive, ct);
        var theme = SubscriptionPricingSettingsReader.Deserialize(settingsRow?.ValueJson).Theme;
        if (theme is null || !SubscriptionPricingSettingsReader.IsKnownDesign(theme.DesignId))
            return null;
        return new CustomerTierThemeDto(theme.DesignId, ((TierThemeDesign)theme.DesignId).ToString());
    }

    private static string? TierLogoUrl(TierLabelsSettings labels, RestaurantTier tier)
        => CatalogImageStorage.ToContentUrl(tier switch
        {
            RestaurantTier.Basic => labels.BasicLogoStorageKey,
            RestaurantTier.Platinum => labels.PlatinumLogoStorageKey,
            _ => labels.EliteLogoStorageKey,
        });

    private async Task<CustomerRestaurantBrowseDetailsDto?> BuildRestaurantBrowseDetailsAsync(
        Guid programId, Guid bundleId, Guid restaurantId, Guid customerUserId, CancellationToken ct)
    {
        var restaurant = await _db.RestaurantProfiles.AsNoTracking()
            .Where(x => x.Id == restaurantId && x.VerificationStatus == VerificationStatus.Approved && x.IsOperationallyActive)
            .Select(x => new { x.Id, x.TradeNameAr, x.TradeNameEn })
            .FirstOrDefaultAsync(ct);
        if (restaurant is null) return null;

        var logos = await LoadRestaurantLogoUrlsAsync([restaurantId], ct);
        var logoKey = logos.GetValueOrDefault(restaurantId);

        var approvedMeals = await _db.Meals.AsNoTracking()
            .Where(x => x.RestaurantId == restaurantId
                && x.ApprovalStatus == VerificationStatus.Approved
                && x.IsAvailableForNewOrders)
            .Select(x => new { x.Id, x.ProgramId, x.BundleId, x.NameAr, x.NameEn, x.Slot, x.Calories, x.ProteinG, x.CarbsG, x.FatG, x.PresentAllergenCodes, x.MayContainAllergenCodes, x.NutritionTagCodes })
            .ToListAsync(ct);
        var mealIds = approvedMeals.Select(x => x.Id).ToList();
        var assignedToProgram = mealIds.Count == 0
            ? new HashSet<Guid>()
            : (await _db.MealProgramAssignments.AsNoTracking()
                .Where(x => mealIds.Contains(x.MealId) && x.ProgramId == programId)
                .Select(x => x.MealId)
                .ToListAsync(ct)).ToHashSet();
        var bundleMeals = approvedMeals
            .Where(x => x.ProgramId == programId || x.BundleId == bundleId || assignedToProgram.Contains(x.Id))
            .Where(x => !string.IsNullOrWhiteSpace(x.Slot))
            .ToList();
        var bundleMealIds = bundleMeals.Select(x => x.Id).ToList();

        var images = bundleMealIds.Count == 0
            ? []
            : await _db.MealImages.AsNoTracking()
                .Where(x => bundleMealIds.Contains(x.MealId))
                .OrderByDescending(x => x.IsPrimary)
                .ThenBy(x => x.DisplayOrder)
                .Select(x => new { x.MealId, x.ImageUrl })
                .ToListAsync(ct);
        var imageByMeal = images
            .GroupBy(x => x.MealId)
            .ToDictionary(x => x.Key, x => ToPublicRestaurantImageUrl(x.First().ImageUrl));

        var ingredientLinks = bundleMealIds.Count == 0
            ? []
            : await _db.MealIngredients.AsNoTracking()
                .Where(x => bundleMealIds.Contains(x.MealId))
                .Join(_db.Ingredients.AsNoTracking(),
                    link => link.IngredientId,
                    ingredient => ingredient.Id,
                    (link, ingredient) => new
                    {
                        link.MealId,
                        link.SortOrder,
                        ingredient.Id,
                        ingredient.NameAr,
                        ingredient.NameEn,
                        ingredient.Category,
                        ingredient.IsActive,
                        ingredient.IsCore,
                        ingredient.MealIconUrl,
                        ingredient.AllergenCodes,
                    })
                .ToListAsync(ct);
        var ingredientsByMeal = ingredientLinks
            .Where(x => x.IsActive && !x.IsCore)
            .GroupBy(x => x.MealId)
            .ToDictionary(
                x => x.Key,
                x => x.OrderBy(i => i.SortOrder)
                    .Select(i => new CustomerRestaurantBrowseMealIngredientDto(
                        i.Id, i.NameAr, i.NameEn, NormalizeIngredientCategory(i.Category),
                        i.IsCore, ToPublicRestaurantImageUrl(i.MealIconUrl)))
                    .ToList());

        var linkedIngredientIds = ingredientLinks.Select(x => x.Id).Distinct().ToList();
        var mealPreferences = bundleMealIds.Count == 0
            ? []
            : await _db.MealFoodPreferences.AsNoTracking().Where(x => bundleMealIds.Contains(x.MealId)).ToListAsync(ct);
        var ingredientPreferenceRows = linkedIngredientIds.Count == 0
            ? []
            : await _db.IngredientFoodPreferences.AsNoTracking()
                .Where(x => linkedIngredientIds.Contains(x.IngredientId))
                .ToListAsync(ct);
        var ingredientPrefs = ingredientPreferenceRows.Select(x => (x.IngredientId, x.PreferenceCode)).ToList();
        var prefs = await MealPreferenceFilter.LoadAsync(_db, customerUserId, ct);
        bundleMeals = bundleMeals.Where(meal =>
        {
            var named = ingredientLinks.Where(x => x.MealId == meal.Id && x.IsActive)
                .Select(x => new MealPreferenceFilter.NamedIngredient(x.Id, x.NameAr, x.NameEn, FoodCodeNormalizer.Split(x.AllergenCodes)))
                .ToList();
            var tags = MealPreferenceFilter.DeriveDietaryTags(
                meal.NutritionTagCodes,
                mealPreferences.Where(x => x.MealId == meal.Id).Select(x => x.PreferenceCode),
                named,
                ingredientPrefs);
            return MealPreferenceFilter.BlockReason(
                FoodCodeNormalizer.Split(meal.PresentAllergenCodes),
                FoodCodeNormalizer.Split(meal.MayContainAllergenCodes),
                named,
                tags,
                prefs) is null;
        }).ToList();

        var mealGroups = bundleMeals
            .GroupBy(x => x.Slot!.Trim().ToLowerInvariant())
            .OrderBy(x => SlotOrder(x.Key))
            .Select(group =>
            {
                var (slotNameAr, slotNameEn) = SubscriptionFlowLocalization.SlotName(group.Key);
                return new CustomerRestaurantBrowseMealGroupDto(
                    group.Key,
                    slotNameAr,
                    slotNameEn,
                    group.OrderBy(x => x.NameAr)
                        .Select(meal => new CustomerRestaurantBrowseMealDto(
                            meal.Id,
                            meal.NameAr,
                            meal.NameEn,
                            imageByMeal.GetValueOrDefault(meal.Id),
                            meal.Calories,
                            meal.ProteinG,
                            meal.CarbsG,
                            meal.FatG,
                            ingredientsByMeal.GetValueOrDefault(meal.Id) ?? []))
                        .ToList());
            })
            .Where(group => group.Meals.Count > 0)
            .ToList();

        var originIngredients = await _db.Ingredients.AsNoTracking()
            .Where(x => x.RestaurantId == restaurantId
                && x.IsActive
                && x.IsCore
                && x.IsOriginVisibleToCustomers
                && x.OriginCountryCode != null
                && x.OriginCountryCode != "")
            .Select(x => new { x.Category, x.OriginCountryCode, x.IsLocal, x.RestaurantIconUrl })
            .ToListAsync(ct);

        var coreIngredients = await _db.Ingredients.AsNoTracking()
            .Where(x => x.RestaurantId == restaurantId && x.IsActive && x.IsCore)
            .OrderBy(x => x.NameEn)
            .Select(x => new
            {
                x.Id,
                x.NameAr,
                x.NameEn,
                x.RestaurantIconUrl,
                x.MealIconUrl,
                x.IsLocal,
                x.IsOriginVisibleToCustomers,
                x.OriginCountryCode,
            })
            .ToListAsync(ct);

        var countryCodes = originIngredients
            .Select(x => x.OriginCountryCode!.Trim().ToUpperInvariant())
            .Concat(coreIngredients
                .Where(x => x.IsOriginVisibleToCustomers && !string.IsNullOrWhiteSpace(x.OriginCountryCode))
                .Select(x => x.OriginCountryCode!.Trim().ToUpperInvariant()))
            .Distinct()
            .ToList();
        var countries = countryCodes.Count == 0
            ? []
            : await _db.Countries.AsNoTracking()
                .Select(x => new { x.Code, x.NameAr, x.NameEn, x.FlagEmoji })
                .ToListAsync(ct);
        var countryByCode = countries
            .Where(x => countryCodes.Contains(x.Code.Trim().ToUpperInvariant()))
            .GroupBy(x => x.Code.Trim().ToUpperInvariant())
            .ToDictionary(x => x.Key, x => x.First());
        var ingredientOrigins = originIngredients
            .GroupBy(x => NormalizeIngredientCategory(x.Category))
            .OrderBy(x => IngredientCategoryOrder(x.Key))
            .Select(group =>
            {
                var selected = group
                    .GroupBy(x => x.OriginCountryCode!.Trim().ToUpperInvariant())
                    .OrderByDescending(x => x.Count())
                    .ThenBy(x => x.Key)
                    .First();
                var isLocal = selected.Count(x => x.IsLocal) >= selected.Count(x => !x.IsLocal);
                countryByCode.TryGetValue(selected.Key, out var country);
                var (nameAr, nameEn) = SubscriptionFlowLocalization.IngredientCategory(group.Key);
                var (sourceAr, sourceEn) = SubscriptionFlowLocalization.IngredientSource(isLocal);
                var iconUrl = selected
                    .Select(x => ToPublicRestaurantImageUrl(x.RestaurantIconUrl))
                    .FirstOrDefault(x => !string.IsNullOrWhiteSpace(x));
                return new CustomerIngredientOriginDto(
                    group.Key, nameAr, nameEn, isLocal, sourceAr, sourceEn,
                    selected.Key, country?.NameAr, country?.NameEn, country?.FlagEmoji, iconUrl);
            })
            .ToList();

        var coreIngredientDtos = coreIngredients
            .Select(x =>
            {
                var showOrigin = x.IsOriginVisibleToCustomers && !string.IsNullOrWhiteSpace(x.OriginCountryCode);
                var code = showOrigin ? x.OriginCountryCode!.Trim().ToUpperInvariant() : null;
                countryByCode.TryGetValue(code ?? string.Empty, out var country);
                return new CustomerRestaurantBrowseCoreIngredientDto(
                    x.Id,
                    x.NameAr,
                    x.NameEn,
                    ToPublicRestaurantImageUrl(x.RestaurantIconUrl ?? x.MealIconUrl),
                    showOrigin && x.IsLocal,
                    code,
                    showOrigin ? country?.NameAr : null,
                    showOrigin ? country?.NameEn : null,
                    showOrigin ? country?.FlagEmoji : null);
            })
            .ToList();

        var coverImageUrl = mealGroups.SelectMany(x => x.Meals).Select(x => x.ImageUrl).FirstOrDefault(x => !string.IsNullOrWhiteSpace(x));
        return new CustomerRestaurantBrowseDetailsDto(
            restaurant.Id, restaurant.TradeNameAr, restaurant.TradeNameEn,
            ToPublicRestaurantImageUrl(logoKey), coverImageUrl,
            coreIngredientDtos, ingredientOrigins, mealGroups);
    }

    private async Task<CustomerRestaurantBrowseMealDetailsDto?> BuildRestaurantBrowseMealDetailsAsync(
        Guid programId, Guid bundleId, Guid restaurantId, Guid mealId, Guid customerUserId, CancellationToken ct)
    {
        var restaurant = await _db.RestaurantProfiles.AsNoTracking()
            .Where(x => x.Id == restaurantId && x.VerificationStatus == VerificationStatus.Approved && x.IsOperationallyActive)
            .Select(x => new { x.Id, x.TradeNameAr, x.TradeNameEn })
            .FirstOrDefaultAsync(ct);
        if (restaurant is null) return null;

        var meal = await _db.Meals.AsNoTracking()
            .Where(x => x.Id == mealId
                && x.RestaurantId == restaurantId
                && x.ApprovalStatus == VerificationStatus.Approved
                && x.IsAvailableForNewOrders)
            .Select(x => new
            {
                x.Id, x.ProgramId, x.BundleId, x.NameAr, x.NameEn, x.DescriptionAr, x.DescriptionEn, x.Slot,
                x.Calories, x.ProteinG, x.CarbsG, x.FatG, x.FiberG, x.ServingWeightG, x.PresentAllergenCodes, x.MayContainAllergenCodes, x.NutritionTagCodes,
            })
            .FirstOrDefaultAsync(ct);
        if (meal is null || string.IsNullOrWhiteSpace(meal.Slot)) return null;

        var assignedToProgram = await _db.MealProgramAssignments.AsNoTracking()
            .AnyAsync(x => x.MealId == meal.Id && x.ProgramId == programId, ct);
        if (meal.ProgramId != programId && meal.BundleId != bundleId && !assignedToProgram)
            return null;

        var logos = await LoadRestaurantLogoUrlsAsync([restaurantId], ct);
        var logoKey = logos.GetValueOrDefault(restaurantId);
        var imageUrl = await _db.MealImages.AsNoTracking()
            .Where(x => x.MealId == meal.Id)
            .OrderByDescending(x => x.IsPrimary)
            .ThenBy(x => x.DisplayOrder)
            .Select(x => x.ImageUrl)
            .FirstOrDefaultAsync(ct);
        var ingredients = await _db.MealIngredients.AsNoTracking()
            .Where(x => x.MealId == meal.Id)
            .Join(_db.Ingredients.AsNoTracking().Where(i => i.IsActive),
                link => link.IngredientId,
                ingredient => ingredient.Id,
                (link, ingredient) => new { link.SortOrder, link.PortionGrams, ingredient.Id, ingredient.NameAr, ingredient.NameEn, ingredient.Category, ingredient.IsCore, ingredient.MealIconUrl, ingredient.AllergenCodes })
            .OrderBy(x => x.SortOrder)
            .ToListAsync(ct);
        var mealPreferenceCodes = await _db.MealFoodPreferences.AsNoTracking().Where(x => x.MealId == meal.Id).Select(x => x.PreferenceCode).ToListAsync(ct);
        var detailIngredientIds = ingredients.Select(x => x.Id).ToList();
        var ingredientPrefs = detailIngredientIds.Count == 0
            ? []
            : (await _db.IngredientFoodPreferences.AsNoTracking().Where(x => detailIngredientIds.Contains(x.IngredientId)).ToListAsync(ct))
                .Select(x => (x.IngredientId, x.PreferenceCode)).ToList();
        var named = ingredients.Select(x => new MealPreferenceFilter.NamedIngredient(x.Id, x.NameAr, x.NameEn, FoodCodeNormalizer.Split(x.AllergenCodes))).ToList();
        var tags = MealPreferenceFilter.DeriveDietaryTags(meal.NutritionTagCodes, mealPreferenceCodes, named, ingredientPrefs);
        var prefs = await MealPreferenceFilter.LoadAsync(_db, customerUserId, ct);
        if (MealPreferenceFilter.BlockReason(FoodCodeNormalizer.Split(meal.PresentAllergenCodes), FoodCodeNormalizer.Split(meal.MayContainAllergenCodes), named, tags, prefs) is not null)
            return null;
        var allergenCodes = FoodCodeNormalizer.Split(meal.PresentAllergenCodes);
        var allergenDefs = allergenCodes.Count == 0
            ? []
            : await _db.AllergenDefinitions.AsNoTracking()
                .Where(x => x.IsActive)
                .Select(x => new { x.Code, x.NameAr, x.NameEn })
                .ToListAsync(ct);
        var allergenByCode = allergenDefs.ToDictionary(x => x.Code, StringComparer.OrdinalIgnoreCase);
        var allergens = allergenCodes.Select(code =>
        {
            allergenByCode.TryGetValue(code, out var definition);
            return new CustomerMealAllergenDto(code, definition?.NameAr ?? code, definition?.NameEn ?? code);
        }).ToList();

        var ratingScores = await _db.RestaurantRatings.AsNoTracking()
            .Where(x => x.RestaurantId == restaurantId)
            .Select(x => x.Score)
            .ToListAsync(ct);
        decimal? rating = ratingScores.Count == 0 ? null : Math.Round((decimal)ratingScores.Average(), 1);
        var (carbsPercent, proteinPercent, fatPercent) = MacroPercents(meal.CarbsG, meal.ProteinG, meal.FatG);
        var slot = meal.Slot.Trim().ToLowerInvariant();
        var (slotNameAr, slotNameEn) = SubscriptionFlowLocalization.SlotName(slot);

        var nutrition = new CustomerMealNutritionDto(meal.Calories, meal.ProteinG, meal.CarbsG, meal.FatG, meal.FiberG,
            carbsPercent, proteinPercent, fatPercent)
        {
            Per100g = meal.ServingWeightG is > 0
                ? new CustomerMealNutritionValuesDto(
                    Per100g(meal.Calories, meal.ServingWeightG.Value),
                    Per100g(meal.ProteinG, meal.ServingWeightG.Value),
                    Per100g(meal.CarbsG, meal.ServingWeightG.Value),
                    Per100g(meal.FatG, meal.ServingWeightG.Value),
                    Per100g(meal.FiberG, meal.ServingWeightG.Value),
                    carbsPercent, proteinPercent, fatPercent)
                : null,
        };
        var highlightDefinitions = await _db.FoodPreferenceDefinitions.AsNoTracking()
            .Where(x => x.IsActive)
            .OrderBy(x => x.SortOrder)
            .Select(x => new { x.Code, x.NameAr, x.NameEn })
            .ToListAsync(ct);
        var highlights = new List<CustomerMealHighlightDto>();
        foreach (var code in tags.Select(FoodCodeNormalizer.Normalize).Distinct(StringComparer.OrdinalIgnoreCase))
        {
            var highlight = code switch
            {
                "HIGH_PROTEIN" => new CustomerMealHighlightDto("high_protein", "عالي البروتين", "High Protein", "protein"),
                "DIET_FRIENDLY" => new CustomerMealHighlightDto("diet_friendly", "مناسب للدايت", "Diet Friendly", "diet"),
                "NATURAL" => new CustomerMealHighlightDto("natural", "مكونات طبيعية", "Natural Ingredients", "leaf"),
                _ => null,
            };
            if (highlight is null)
            {
                var definition = highlightDefinitions.FirstOrDefault(x => FoodCodeNormalizer.Normalize(x.Code) == code);
                if (definition is not null)
                    highlight = new CustomerMealHighlightDto(code.ToLowerInvariant(), definition.NameAr, definition.NameEn, "tag");
            }
            if (highlight is not null) highlights.Add(highlight);
        }

        return new CustomerRestaurantBrowseMealDetailsDto(
            meal.Id, meal.NameAr, meal.NameEn, meal.DescriptionAr, meal.DescriptionEn,
            ToPublicRestaurantImageUrl(imageUrl), slot, slotNameAr, slotNameEn,
            restaurant.Id, restaurant.TradeNameAr, restaurant.TradeNameEn, ToPublicRestaurantImageUrl(logoKey),
            rating, ratingScores.Count, null, null,
            ingredients.Where(x => !x.IsCore).Select(x => new CustomerRestaurantBrowseMealIngredientDto(
                x.Id, x.NameAr, x.NameEn, NormalizeIngredientCategory(x.Category),
                x.IsCore, ToPublicRestaurantImageUrl(x.MealIconUrl), x.PortionGrams > 0 ? x.PortionGrams : null)).ToList(),
            nutrition,
            allergens)
        {
            Tags = highlights,
        };
    }

    private static decimal? Per100g(decimal? value, decimal servingWeightG)
        => value.HasValue ? Math.Round(value.Value * 100m / servingWeightG, 2, MidpointRounding.AwayFromZero) : null;

    private static (int? Carbs, int? Protein, int? Fat) MacroPercents(decimal? carbsG, decimal? proteinG, decimal? fatG)
    {
        var carbsKcal = (carbsG ?? 0m) * 4m;
        var proteinKcal = (proteinG ?? 0m) * 4m;
        var fatKcal = (fatG ?? 0m) * 9m;
        var total = carbsKcal + proteinKcal + fatKcal;
        if (total <= 0m) return (null, null, null);
        var carbs = (int)Math.Round(100m * carbsKcal / total, MidpointRounding.AwayFromZero);
        var protein = (int)Math.Round(100m * proteinKcal / total, MidpointRounding.AwayFromZero);
        var fat = (int)Math.Round(100m * fatKcal / total, MidpointRounding.AwayFromZero);
        var drift = 100 - (carbs + protein + fat);
        if (drift != 0)
        {
            if (carbsKcal >= proteinKcal && carbsKcal >= fatKcal) carbs += drift;
            else if (proteinKcal >= fatKcal) protein += drift;
            else fat += drift;
        }
        return (carbs, protein, fat);
    }

    private async Task<List<CustomerRestaurantBrowseCardDto>> BuildRestaurantBrowseCardsAsync(
        Guid programId,
        Guid bundleId,
        RestaurantTierClassificationResult classification,
        IReadOnlyList<Guid> restaurantIds,
        CancellationToken ct)
    {
        if (restaurantIds.Count == 0) return [];

        var assignmentByRestaurant = classification.Assignments
            .GroupBy(x => x.RestaurantId)
            .ToDictionary(x => x.Key, x => x.First().Tier);
        var orderedIds = restaurantIds.Where(assignmentByRestaurant.ContainsKey).Distinct().ToList();
        if (orderedIds.Count == 0) return [];

        var restaurants = await _db.RestaurantProfiles.AsNoTracking()
            .Where(x => orderedIds.Contains(x.Id)
                && x.VerificationStatus == VerificationStatus.Approved
                && x.IsOperationallyActive)
            .Select(x => new { x.Id, x.TradeNameAr, x.TradeNameEn })
            .ToListAsync(ct);
        var restaurantById = restaurants.ToDictionary(x => x.Id);

        var logoByRestaurant = await LoadRestaurantLogoUrlsAsync(orderedIds, ct);

        var approvedMeals = await _db.Meals.AsNoTracking()
            .Where(x => orderedIds.Contains(x.RestaurantId)
                && x.ApprovalStatus == VerificationStatus.Approved
                && x.IsAvailableForNewOrders)
            .Select(x => new { x.Id, x.RestaurantId, x.ProgramId, x.BundleId })
            .ToListAsync(ct);
        var mealIds = approvedMeals.Select(x => x.Id).ToList();
        var assignedToProgram = mealIds.Count == 0
            ? new HashSet<Guid>()
            : (await _db.MealProgramAssignments.AsNoTracking()
                .Where(x => mealIds.Contains(x.MealId) && x.ProgramId == programId)
                .Select(x => x.MealId)
                .ToListAsync(ct)).ToHashSet();
        var mealsByRestaurant = approvedMeals.GroupBy(x => x.RestaurantId).ToDictionary(x => x.Key, x => x.ToList());

        var ratingRows = await _db.RestaurantRatings.AsNoTracking()
            .Where(x => orderedIds.Contains(x.RestaurantId))
            .Select(x => new { x.RestaurantId, x.Score })
            .ToListAsync(ct);
        var ratingByRestaurant = ratingRows
            .GroupBy(x => x.RestaurantId)
            .ToDictionary(x => x.Key, x => Math.Round((decimal)x.Average(r => r.Score), 1));

        var classificationsByRestaurant = await ResolveRestaurantClassificationsAsync(orderedIds, ct);
        var labels = await LoadTierLabelsAsync(ct);

        var cards = new List<CustomerRestaurantBrowseCardDto>(orderedIds.Count);
        foreach (var id in orderedIds)
        {
            if (!restaurantById.TryGetValue(id, out var restaurant)) continue;
            var currentTier = assignmentByRestaurant[id];
            var meals = mealsByRestaurant.GetValueOrDefault(id) ?? [];
            var bundleMealCount = meals.Count(x =>
                x.ProgramId == programId || x.BundleId == bundleId || assignedToProgram.Contains(x.Id));
            var (tierNameAr, tierNameEn) = TierDisplayName(currentTier, labels);
            cards.Add(new CustomerRestaurantBrowseCardDto(
                restaurant.Id, restaurant.TradeNameAr, restaurant.TradeNameEn,
                logoByRestaurant.GetValueOrDefault(id),
                ratingByRestaurant.TryGetValue(id, out var rating) ? rating : null,
                meals.Count, bundleMealCount, currentTier, tierNameAr, tierNameEn,
                TierLogoUrl(labels, currentTier),
                classificationsByRestaurant.GetValueOrDefault(id) ?? []));
        }

        return cards;
    }

    private async Task<Dictionary<Guid, IReadOnlyList<CustomerRestaurantClassificationDto>>> ResolveRestaurantClassificationsAsync(
        IReadOnlyList<Guid> restaurantIds, CancellationToken ct)
    {
        var result = restaurantIds.Distinct().ToDictionary(
            id => id,
            _ => (IReadOnlyList<CustomerRestaurantClassificationDto>)[]);
        if (restaurantIds.Count == 0) return result;

        var offers = await _db.RestaurantProgramBundleOffers.AsNoTracking()
            .Where(x => restaurantIds.Contains(x.RestaurantId) && x.IsActive)
            .Select(x => new { x.RestaurantId, x.ProgramId, x.BundleId })
            .ToListAsync(ct);
        if (offers.Count == 0) return result;

        var programIds = offers.Select(x => x.ProgramId).Distinct().ToList();
        var bundleIds = offers.Select(x => x.BundleId).Distinct().ToList();
        var activeProgramIds = (await _db.Programs.AsNoTracking()
            .Where(x => programIds.Contains(x.Id) && x.IsActive && x.Status == ProgramStatus.Active)
            .Select(x => x.Id)
            .ToListAsync(ct)).ToHashSet();
        var activeBundleIds = (await _db.Bundles.AsNoTracking()
            .Where(x => bundleIds.Contains(x.Id) && x.IsActive && x.Status == ProgramStatus.Active)
            .Select(x => x.Id)
            .ToListAsync(ct)).ToHashSet();

        var pairs = offers
            .Where(x => activeProgramIds.Contains(x.ProgramId) && activeBundleIds.Contains(x.BundleId))
            .Select(x => (x.ProgramId, x.BundleId))
            .Distinct()
            .ToList();
        var tiersByRestaurant = restaurantIds.Distinct().ToDictionary(id => id, _ => new HashSet<RestaurantTier>());
        foreach (var (offerProgramId, offerBundleId) in pairs)
        {
            var classification = await _tiers.ResolveAsync(offerProgramId, offerBundleId, eligibleOnly: true, ct);
            foreach (var assignment in classification.Assignments)
            {
                if (tiersByRestaurant.TryGetValue(assignment.RestaurantId, out var tiers))
                    tiers.Add(assignment.Tier);
            }
        }

        var labels = await LoadTierLabelsAsync(ct);
        foreach (var (restaurantId, tiers) in tiersByRestaurant)
        {
            result[restaurantId] = tiers.OrderBy(x => x)
                .Select(tier => new CustomerRestaurantClassificationDto(TierLogoUrl(labels, tier), tier))
                .ToList();
        }

        return result;
    }

    private static int SlotOrder(string slot) => slot.ToLowerInvariant() switch { "breakfast" => 1, "lunch" => 2, "dinner" => 3, "snack" => 4, _ => 99 };

    private static bool IsConfiguredMethod(PaymentGatewayMethod method, bool knet, bool card, bool apple)
    {
        var code = method.Code.Trim().ToUpperInvariant();
        var name = method.NameEn.Trim().ToUpperInvariant();
        if (code == "KN" || code == "KNET" || name.Contains("KNET")) return knet;
        if (code == "AP" || name.Contains("APPLE PAY") || name.Contains("APPLEPAY")) return apple;
        if (code == "VM" || name.Contains("VISA/MASTER") || name.Contains("VISA / MASTER") || name.Contains("VISA") || name.Contains("MASTER")) return card;
        return false;
    }

    private sealed record TierPricingRow(CustomerSubscriptionCatalogItemDto Catalog, decimal? TotalPrice, decimal? DailyPrice);
}
