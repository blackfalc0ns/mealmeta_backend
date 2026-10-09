using MealMate.Application.Common.Abstractions;
using MealMate.Application.Common.Models;
using MealMate.Application.Common.Storage;
using MealMate.Application.Features.CustomerCheckout;
using MealMate.Application.Features.IdentityAccess.Customers;
using MealMate.Application.Features.ProgramsSubscriptions.Pricing;
using MealMate.Domain.AdminDashboard;
using MealMate.Domain.Enums;
using MealMate.Domain.ProgramsSubscriptions;
using MealMate.Domain.RestaurantOperations;
using MediatR;
using Microsoft.EntityFrameworkCore;

namespace MealMate.Application.Features.ProgramsSubscriptions.Flow;

/// <summary>Semantic availability states for the catalog routes. Coverage states describe the
/// active address against the option; period states describe full-period eligibility once the
/// date/duration/Friday context is complete. Codes are identifiers, never final product text.</summary>
public static class SubscriptionCatalogAvailabilityStatus
{
    public const string AddressRequired = "address_required";
    public const string SelectionRequired = "selection_required";
    public const string NoRestaurantsForAddress = "no_restaurants_for_address";
    public const string NoRestaurantsForSelection = "no_restaurants_for_selection";
    public const string PricingUnavailable = "pricing_unavailable";
    public const string Available = "available";
}

/// <summary>Semantic message codes for general address coverage (no program/bundle condition).</summary>
public static class SubscriptionCatalogMessageCodes
{
    public const string NoDeliveryToAddress = "subscription_flow.no_delivery_to_address";
    public const string PricingUnavailable = "subscription_flow.pricing_unavailable";
}

public interface ISubscriptionCatalogAvailabilityService
{
    /// <summary>Active programs with address-scoped coverage; the envelope carries the general
    /// address coverage (service regions + approved active restaurants, no program condition).</summary>
    Task<Result<SubscriptionCatalogProgramsEnvelopeDto>> GetProgramsAsync(Guid customerUserId, PageRequest? page, CancellationToken ct = default);

    /// <summary>Every active, slot-valid bundle of the program — including zero-coverage bundles —
    /// with its coverage status; legacy bare lists stay unchanged.</summary>
    Task<Result<SubscriptionCatalogBundlesEnvelopeDto>> GetBundlesAsync(Guid programId, Guid customerUserId, PageRequest? page, CancellationToken ct = default);

    /// <summary>Every sale-prepared tier of the bundle with coverage/pricing status; when the full
    /// period context (duration + start date) is supplied, each tier additionally carries the
    /// period-eligibility verdict from the single availability evaluator.</summary>
    Task<Result<SubscriptionCatalogTiersEnvelopeDto>> GetTiersAsync(
        Guid programId, Guid bundleId, Guid customerUserId,
        int? durationDays, bool includeFriday, DateOnly? startDate,
        PageRequest? page, CancellationToken ct = default);
}

/// <summary>
/// Catalog reads for the individual subscription flow that keep unavailable options visible with
/// a precise disabled reason, unlike the legacy lists that silently drop zero-coverage programs
/// and tiers. All counts are distinct and computed before pagination; a program's restaurant
/// count is the UNION of its bundles' offered restaurants, never the sum of per-bundle counts.
/// The address always comes from the authenticated customer's active delivery address — no
/// client-supplied regionId is accepted. CanSelect is a navigation hint, never a payment
/// authorization; only readiness.CanCheckout plus checkout re-validation gate payment.
/// </summary>
public sealed class SubscriptionCatalogAvailabilityService : ISubscriptionCatalogAvailabilityService
{
    private readonly IAppDbContext _db;
    private readonly IDateTimeProvider _clock;
    private readonly ICustomerDeliveryAddressService _deliveryAddresses;
    private readonly IRestaurantTierClassificationService _tiers;
    private readonly IAllowedSubscriptionDurationService _allowedDurations;
    private readonly ISubscriptionRestaurantAvailabilityService _availability;
    private readonly ISender _sender;

    public SubscriptionCatalogAvailabilityService(
        IAppDbContext db,
        IDateTimeProvider clock,
        ICustomerDeliveryAddressService deliveryAddresses,
        IRestaurantTierClassificationService tiers,
        IAllowedSubscriptionDurationService allowedDurations,
        ISubscriptionRestaurantAvailabilityService availability,
        ISender sender)
    {
        _db = db;
        _clock = clock;
        _deliveryAddresses = deliveryAddresses;
        _tiers = tiers;
        _allowedDurations = allowedDurations;
        _availability = availability;
        _sender = sender;
    }

    public async Task<Result<SubscriptionCatalogProgramsEnvelopeDto>> GetProgramsAsync(Guid customerUserId, PageRequest? page, CancellationToken ct = default)
    {
        var programs = await _db.Programs.AsNoTracking()
            .Where(x => x.IsActive && x.Status == ProgramStatus.Active)
            .OrderBy(x => x.SortOrder).ThenBy(x => x.Id)
            .Select(x => new { x.Id, x.Code, x.ShortCode, x.NameAr, x.NameEn, x.DescriptionAr, x.DescriptionEn, x.KeywordsAr, x.KeywordsEn, x.ImageStorageKey })
            .ToListAsync(ct);
        var programIds = programs.Select(x => x.Id).ToList();

        var bundles = await _db.Bundles.AsNoTracking()
            .Where(x => programIds.Contains(x.ProgramId) && x.IsActive && x.Status == ProgramStatus.Active)
            .Select(x => new { x.Id, x.ProgramId, x.BreakfastCount, x.LunchCount, x.DinnerCount, x.SnackCount })
            .ToListAsync(ct);
        var validBundleIdsByProgram = bundles
            .Where(x => IsBundleSlotConfigurationValid(x.BreakfastCount, x.LunchCount, x.DinnerCount, x.SnackCount))
            .GroupBy(x => x.ProgramId)
            .ToDictionary(g => g.Key, g => g.Select(x => x.Id).ToList());
        var validBundleIds = validBundleIdsByProgram.Values.SelectMany(x => x).ToList();

        // One offer query for all programs, then in-memory union per program (never per-card
        // queries, never summed per-bundle counts).
        var offers = await _db.RestaurantProgramBundleOffers.AsNoTracking()
            .Where(x => validBundleIds.Contains(x.BundleId) && x.IsActive)
            .Select(x => new { x.ProgramId, x.BundleId, x.RestaurantId })
            .ToListAsync(ct);
        var offeredRestaurantIds = offers.Select(x => x.RestaurantId).Distinct().ToList();

        var approvedActiveIds = await _db.RestaurantProfiles.AsNoTracking()
            .Where(x => offeredRestaurantIds.Contains(x.Id)
                && x.VerificationStatus == VerificationStatus.Approved
                && x.IsOperationallyActive)
            .Select(x => x.Id)
            .ToListAsync(ct);
        var approvedActiveSet = approvedActiveIds.ToHashSet();

        var slots = await _db.ProgramMealSlotRequirements.AsNoTracking()
            .Where(x => programIds.Contains(x.ProgramId) && x.IsRequired)
            .Select(x => new { x.ProgramId, x.Slot })
            .ToListAsync(ct);

        var activeAddress = await _deliveryAddresses.ResolveActiveDeliveryAddressAsync(customerUserId, ct);
        var coveredSet = await CoverByRegionAsync(offeredRestaurantIds, activeAddress?.RegionId, ct);

        var generalCoverage = activeAddress is null
            ? null
            : (int?)await CountGeneralAddressCoverageAsync(activeAddress.RegionId, ct);

        var cards = new List<SubscriptionCatalogOptionDto<CustomerProgramCardDto>>(programs.Count);
        foreach (var program in programs)
        {
            var programBundleIds = validBundleIdsByProgram.GetValueOrDefault(program.Id, []);
            var programOfferedIds = offers
                .Where(x => x.ProgramId == program.Id && approvedActiveSet.Contains(x.RestaurantId))
                .Select(x => x.RestaurantId)
                .Distinct()
                .ToList();
            var coveredCount = programOfferedIds.Count(coveredSet.Contains);

            var availability = ResolveCoverageAvailability(activeAddress is not null, coveredCount, programOfferedIds.Count);
            cards.Add(new SubscriptionCatalogOptionDto<CustomerProgramCardDto>(
                new CustomerProgramCardDto(
                    program.Id, program.Code, program.ShortCode, program.NameAr, program.NameEn,
                    program.DescriptionAr, program.DescriptionEn,
                    program.KeywordsAr, program.KeywordsEn,
                    CatalogImageStorage.ToContentUrl(program.ImageStorageKey),
                    programBundleIds.Count,
                    slots.Where(s => s.ProgramId == program.Id).Select(s => s.Slot).OrderBy(s => s).ToList()),
                availability));
        }

        var (visible, pagination) = Slice(cards, page);
        return new SubscriptionCatalogProgramsEnvelopeDto(
            activeAddress is not null,
            AddressCoverageAvailability(activeAddress, generalCoverage),
            pagination,
            visible);
    }

    public async Task<Result<SubscriptionCatalogBundlesEnvelopeDto>> GetBundlesAsync(Guid programId, Guid customerUserId, PageRequest? page, CancellationToken ct = default)
    {
        var program = await _db.Programs.AsNoTracking()
            .FirstOrDefaultAsync(x => x.Id == programId && x.IsActive && x.Status == ProgramStatus.Active, ct);
        if (program is null) return Error.NotFound("subscription_flow.program_not_found");

        var bundles = await _db.Bundles.AsNoTracking()
            .Where(x => x.ProgramId == programId && x.IsActive && x.Status == ProgramStatus.Active)
            .OrderBy(x => x.SortOrder).ThenBy(x => x.Id)
            .ToListAsync(ct);
        var validBundles = bundles.Where(x => IsBundleSlotConfigurationValid(x.BreakfastCount, x.LunchCount, x.DinnerCount, x.SnackCount)).ToList();
        var bundleIds = validBundles.Select(x => x.Id).ToList();

        var offers = bundleIds.Count == 0
            ? []
            : await _db.RestaurantProgramBundleOffers.AsNoTracking()
                .Where(x => x.ProgramId == programId && bundleIds.Contains(x.BundleId) && x.IsActive)
                .Select(x => new { x.BundleId, x.RestaurantId })
                .ToListAsync(ct);
        var offeredRestaurantIds = offers.Select(x => x.RestaurantId).Distinct().ToList();

        var approvedActiveIds = await _db.RestaurantProfiles.AsNoTracking()
            .Where(x => offeredRestaurantIds.Contains(x.Id)
                && x.VerificationStatus == VerificationStatus.Approved
                && x.IsOperationallyActive)
            .Select(x => new { x.Id, x.TradeNameAr })
            .ToListAsync(ct);
        var approvedActiveSet = approvedActiveIds.Select(x => x.Id).ToHashSet();

        var activeAddress = await _deliveryAddresses.ResolveActiveDeliveryAddressAsync(customerUserId, ct);
        var coveredSet = await CoverByRegionAsync(offeredRestaurantIds, activeAddress?.RegionId, ct);
        var logos = await LoadRestaurantLogoUrlsAsync(approvedActiveSet, ct);

        var pageRequest = page ?? new PageRequest();
        var cards = new List<SubscriptionCatalogOptionDto<CustomerBundleCardDto>>(validBundles.Count);
        foreach (var bundle in validBundles)
        {
            var bundleCoveredIds = offers
                .Where(x => x.BundleId == bundle.Id && approvedActiveSet.Contains(x.RestaurantId) && coveredSet.Contains(x.RestaurantId))
                .Select(x => x.RestaurantId)
                .Distinct()
                .ToList();
            var bundleOfferedIds = offers
                .Where(x => x.BundleId == bundle.Id && approvedActiveSet.Contains(x.RestaurantId))
                .Select(x => x.RestaurantId)
                .Distinct()
                .ToList();
            var availability = ResolveCoverageAvailability(activeAddress is not null, bundleCoveredIds.Count, bundleOfferedIds.Count);

            var restaurants = (activeAddress is null ? bundleOfferedIds : bundleCoveredIds)
                .OrderBy(id => approvedActiveIds.Single(p => p.Id == id).TradeNameAr)
                .Select(id => new CustomerBundleRestaurantDto(id, logos.GetValueOrDefault(id)))
                .ToList();

            cards.Add(new SubscriptionCatalogOptionDto<CustomerBundleCardDto>(
                new CustomerBundleCardDto(
                    bundle.Id, bundle.ProgramId, bundle.Code, bundle.NameAr, bundle.NameEn,
                    bundle.Description ?? bundle.DescriptionEn ?? bundle.DescriptionAr,
                    CatalogImageStorage.ToContentUrl(bundle.ImageStorageKey),
                    bundle.MealsPerDay, bundle.BreakfastCount, bundle.LunchCount, bundle.DinnerCount, bundle.SnackCount,
                    ToSlotRequirements(bundle.BreakfastCount, bundle.LunchCount, bundle.DinnerCount, bundle.SnackCount),
                    bundle.SortOrder,
                    bundle.DescriptionAr ?? bundle.NameAr, bundle.DescriptionEn ?? bundle.Description ?? bundle.NameEn,
                    ToDisplaySlots(bundle.BreakfastCount, bundle.LunchCount, bundle.DinnerCount, bundle.SnackCount),
                    bundleCoveredIds.Count,
                    restaurants.Take(8).ToList(),
                    SelectionCount: 0,
                    IsMostSelected: false,
                    Badge: null),
                availability));
        }

        var (visible, pagination) = Slice(cards, page);
        return new SubscriptionCatalogBundlesEnvelopeDto(
            programId,
            activeAddress is not null,
            pagination,
            visible);
    }

    public async Task<Result<SubscriptionCatalogTiersEnvelopeDto>> GetTiersAsync(
        Guid programId, Guid bundleId, Guid customerUserId,
        int? durationDays, bool includeFriday, DateOnly? startDate,
        PageRequest? page, CancellationToken ct = default)
    {
        var programActive = await _db.Programs.AsNoTracking()
            .AnyAsync(x => x.Id == programId && x.IsActive && x.Status == ProgramStatus.Active, ct);
        if (!programActive) return Error.NotFound("subscription_flow.program_not_found");
        // Slot-validity is evaluated in memory: the helper is not translatable to SQL.
        var bundleRow = await _db.Bundles.AsNoTracking()
            .Where(x => x.Id == bundleId && x.ProgramId == programId && x.IsActive && x.Status == ProgramStatus.Active)
            .Select(x => new { x.BreakfastCount, x.LunchCount, x.DinnerCount, x.SnackCount })
            .FirstOrDefaultAsync(ct);
        if (bundleRow is null
            || !IsBundleSlotConfigurationValid(bundleRow.BreakfastCount, bundleRow.LunchCount, bundleRow.DinnerCount, bundleRow.SnackCount))
            return Error.NotFound("subscription_flow.bundle_not_found");

        if (durationDays.HasValue)
        {
            var allowed = await _allowedDurations.EvaluateAsync(durationDays.Value, includeFriday, ct);
            if (!allowed.IsAllowed)
                return Error.Validation(allowed.ErrorCode ?? "subscription_flow.duration_not_available");
        }

        var activeAddress = await _deliveryAddresses.ResolveActiveDeliveryAddressAsync(customerUserId, ct);

        var classification = await _tiers.ResolveAsync(programId, bundleId, eligibleOnly: true, ct);
        var classificationByRestaurant = classification.Assignments
            .GroupBy(x => x.RestaurantId)
            .ToDictionary(g => g.Key, g => g.First().Tier);
        var classificationRestaurantIds = classificationByRestaurant.Keys.ToList();

        var coveredSet = await CoverByRegionAsync(classificationRestaurantIds, activeAddress?.RegionId, ct);

        var fridayWorkingIds = await LoadFridayWorkingIdsAsync(classificationRestaurantIds, ct);

        var catalogResult = await _sender.Send(new GetCustomerSubscriptionPricingCatalogQuery(), ct);
        if (catalogResult.IsFailure) return catalogResult.Error;
        var catalogTiers = catalogResult.Value
            .Where(x => x.ProgramId == programId && x.BundleId == bundleId)
            .GroupBy(x => x.Tier)
            .OrderBy(x => x.Key)
            .Select(g => g.First())
            .ToList();

        // Distinct, pre-pagination tier coverage from the current tier-access rule (a selected
        // tier may use lower-tier restaurants); literal tier matching is a separate product call.
        var tierCoverage = new Dictionary<RestaurantTier, (int Covered, int Offered, int FridayWorking)>();
        foreach (var catalogTier in catalogTiers.Select(x => x.Tier))
        {
            var allowedIds = classificationByRestaurant
                .Where(x => RestaurantTierAccess.CanSelect(catalogTier, x.Value))
                .Select(x => x.Key)
                .ToList();
            var covered = activeAddress is null ? allowedIds.Count : allowedIds.Count(coveredSet.Contains);
            tierCoverage[catalogTier] = (covered, allowedIds.Count, allowedIds.Count(fridayWorkingIds.Contains));
        }

        var periodContextComplete = durationDays.HasValue && startDate.HasValue;
        Dictionary<RestaurantTier, IndividualRestaurantAvailabilityReport>? periodReports = null;
        if (periodContextComplete)
        {
            // One evaluator run per displayed tier; the chosen start date is passed through as
            // itself — never re-branded as the first delivery date.
            periodReports = new Dictionary<RestaurantTier, IndividualRestaurantAvailabilityReport>();
            foreach (var catalogTier in catalogTiers.Select(x => x.Tier))
            {
                periodReports[catalogTier] = await _availability.EvaluateDetailedAsync(
                    customerUserId, programId, bundleId, catalogTier,
                    durationDays!.Value, includeFriday, startDate!.Value, ct);
            }
        }

        int? fridayDays = null;
        int? totalServiceDays = null;
        var pricesByTier = new Dictionary<RestaurantTier, (decimal? Total, decimal? Daily, bool Valid)>();
        if (durationDays.HasValue)
        {
            fridayDays = includeFriday ? ScheduleFridayAddonDays(durationDays.Value) : 0;
            totalServiceDays = durationDays.Value + fridayDays.Value;
            foreach (var item in catalogTiers)
            {
                var catalogPrice = item.Prices.SingleOrDefault(p => p.DurationDays == durationDays.Value);
                if (catalogPrice is not null && !includeFriday)
                {
                    pricesByTier[item.Tier] = (catalogPrice.TotalPriceKwd, catalogPrice.DailyPriceKwd, true);
                    continue;
                }

                var preview = await _sender.Send(new PreviewCustomerPricingQuery(
                    programId, bundleId, item.Tier, durationDays.Value,
                    IncludeFriday: includeFriday, FridayDays: fridayDays.Value), ct);
                pricesByTier[item.Tier] = preview.IsSuccess
                    ? (preview.Value.FinalCustomerPrice, preview.Value.FinalCustomerPrice / totalServiceDays.Value, true)
                    : (null, null, false);
            }
        }

        var pageRequest = page;
        var labels = await LoadTierLabelsAsync(ct);
        var theme = await LoadTierThemeAsync(ct);

        var cards = new List<SubscriptionCatalogOptionDto<CustomerTierOptionDto>>(catalogTiers.Count);
        foreach (var item in catalogTiers)
        {
            var (coveredCount, offeredCount, fridayWorking) = tierCoverage.GetValueOrDefault(item.Tier);
            var price = pricesByTier.GetValueOrDefault(item.Tier);

            string scope;
            string status;
            bool canSelect;
            int reportedCovered;
            int? eligibleCount;
            string? messageCode;

            if (activeAddress is null)
            {
                scope = CatalogScopes.Coverage;
                status = SubscriptionCatalogAvailabilityStatus.AddressRequired;
                canSelect = true;
                reportedCovered = offeredCount;
                eligibleCount = null;
                messageCode = "checkout.address_required";
            }
            else if (coveredCount == 0)
            {
                // A tier with no allowed restaurants for the address stays visible, disabled,
                // even before any period evaluation.
                scope = periodContextComplete ? CatalogScopes.Period : CatalogScopes.Coverage;
                status = SubscriptionCatalogAvailabilityStatus.NoRestaurantsForAddress;
                canSelect = false;
                reportedCovered = 0;
                eligibleCount = periodContextComplete ? 0 : null;
                messageCode = SubscriptionCheckoutGateCodes.NoRestaurantsForAddress;
            }
            else if (periodReports is not null)
            {
                var report = periodReports[item.Tier];
                scope = CatalogScopes.Period;
                status = report.Availability.Status;
                eligibleCount = report.Availability.EligibleRestaurantCount;
                // A tier that cannot complete a safe, fully-priced period is not navigable.
                canSelect = status == SubscriptionCatalogAvailabilityStatus.Available && price.Valid;
                reportedCovered = coveredCount;
                messageCode = status == SubscriptionCatalogAvailabilityStatus.Available
                    ? null
                    : report.Availability.MessageCode ?? SubscriptionCheckoutGateCodes.NoRestaurantsForSelection;
            }
            else
            {
                scope = CatalogScopes.Coverage;
                status = SubscriptionCatalogAvailabilityStatus.SelectionRequired;
                canSelect = true;
                reportedCovered = coveredCount;
                eligibleCount = null;
                messageCode = null;
            }

            // A missing sale price with the duration already chosen is its own disabled state;
            // it never fabricates a zero price.
            if (activeAddress is not null && durationDays.HasValue && !price.Valid)
            {
                status = SubscriptionCatalogAvailabilityStatus.PricingUnavailable;
                canSelect = false;
                messageCode = SubscriptionCatalogMessageCodes.PricingUnavailable;
            }

            var (tierDescriptionAr, tierDescriptionEn) = SubscriptionFlowLocalization.TierMarketing(item.Tier);
            cards.Add(new SubscriptionCatalogOptionDto<CustomerTierOptionDto>(
                new CustomerTierOptionDto(
                    item.Tier, item.TierNameAr, item.TierNameEn, TierLogoUrl(labels, item.Tier),
                    tierDescriptionAr, tierDescriptionEn,
                    reportedCovered, [], durationDays,
                    price.Valid ? price.Total : null,
                    price.Valid ? price.Daily : null,
                    null,
                    price.Valid ? item.CurrencyCode : null,
                    SelectionCount: 0,
                    IsRecommended: false,
                    includeFriday, fridayDays, totalServiceDays,
                    CreateFridayAvailability(fridayWorking),
                    theme),
                new SubscriptionCatalogOptionAvailabilityDto(
                    scope, status, canSelect, reportedCovered, eligibleCount, messageCode)));
        }

        var (visible, pagination) = Slice(cards, pageRequest);
        return new SubscriptionCatalogTiersEnvelopeDto(
            programId,
            bundleId,
            activeAddress is not null,
            periodContextComplete,
            pagination,
            visible);
    }

    internal static class CatalogScopes
    {
        public const string Coverage = "coverage";
        public const string Period = "period";
        public const string CoverageScope = "coverage";
    }

    internal static SubscriptionCatalogOptionAvailabilityDto ResolveCoverageAvailability(
        bool hasAddress, int coveredCount, int offeredCount)
        => !hasAddress
            ? new(CatalogScopes.Coverage, SubscriptionCatalogAvailabilityStatus.AddressRequired, true, offeredCount, null, "checkout.address_required")
        : coveredCount == 0
            ? new(CatalogScopes.Coverage, SubscriptionCatalogAvailabilityStatus.NoRestaurantsForAddress, false, 0, null, SubscriptionCheckoutGateCodes.NoRestaurantsForAddress)
            : new(CatalogScopes.Coverage, SubscriptionCatalogAvailabilityStatus.SelectionRequired, true, coveredCount, null, null);

    private async Task<HashSet<Guid>> CoverByRegionAsync(IReadOnlyCollection<Guid> restaurantIds, Guid? regionId, CancellationToken ct)
    {
        if (regionId is null || regionId == Guid.Empty || restaurantIds.Count == 0) return new HashSet<Guid>();
        var covered = await _db.RestaurantServiceRegions.AsNoTracking()
            .Where(x => restaurantIds.Contains(x.RestaurantId) && x.RegionId == regionId && x.IsActive)
            .Select(x => x.RestaurantId)
            .Distinct()
            .ToListAsync(ct);
        return covered.ToHashSet();
    }

    private async Task<HashSet<Guid>> LoadFridayWorkingIdsAsync(IReadOnlyCollection<Guid> restaurantIds, CancellationToken ct)
    {
        if (restaurantIds.Count == 0) return new HashSet<Guid>();
        var schedules = await _db.RestaurantOperatingSchedules.AsNoTracking()
            .Where(x => restaurantIds.Contains(x.RestaurantId) && x.DayOfWeek == DayOfWeek.Friday && x.IsWorkingDay)
            .Select(x => x.RestaurantId)
            .Distinct()
            .ToListAsync(ct);
        return schedules.ToHashSet();
    }

    /// <summary>General address coverage: approved, operationally active restaurants with an
    /// active service region on the address region — deliberately without any program/bundle
    /// condition, so the client can distinguish "nothing delivers here" from "nothing offers
    /// this selection".</summary>
    private async Task<int> CountGeneralAddressCoverageAsync(Guid regionId, CancellationToken ct)
        => await _db.RestaurantServiceRegions.AsNoTracking()
            .Where(x => x.RegionId == regionId && x.IsActive
                && _db.RestaurantProfiles.Any(r => r.Id == x.RestaurantId
                    && r.VerificationStatus == VerificationStatus.Approved
                    && r.IsOperationallyActive))
            .Select(x => x.RestaurantId)
            .Distinct()
            .CountAsync(ct);

    private static SubscriptionCatalogOptionAvailabilityDto AddressCoverageAvailability(
        CustomerActiveDeliveryAddress? activeAddress, int? generalCoverage)
        => activeAddress is null
            ? new(CatalogScopes.CoverageScope, SubscriptionCatalogAvailabilityStatus.AddressRequired, true, 0, null, "checkout.address_required")
        : generalCoverage is 0
            ? new(CatalogScopes.CoverageScope, SubscriptionCatalogAvailabilityStatus.NoRestaurantsForAddress, false, 0, null, SubscriptionCatalogMessageCodes.NoDeliveryToAddress)
            : new(CatalogScopes.CoverageScope, SubscriptionCatalogAvailabilityStatus.Available, true, generalCoverage.Value, null, null);

    private static (IReadOnlyList<SubscriptionCatalogOptionDto<T>> Visible, CustomerPaginationMetaDto? Pagination) Slice<T>(
        IReadOnlyList<SubscriptionCatalogOptionDto<T>> cards, PageRequest? pageRequest)
    {
        if (pageRequest is null)
            return (cards, null);
        var total = cards.Count;
        var totalPages = (int)Math.Ceiling(total / (double)pageRequest.NormalizedPageSize);
        var pagination = new CustomerPaginationMetaDto(
            total,
            pageRequest.NormalizedPage,
            pageRequest.NormalizedPageSize,
            totalPages,
            pageRequest.NormalizedPage < totalPages,
            pageRequest.NormalizedPage > 1);
        var visible = cards.Skip(pageRequest.Skip).Take(pageRequest.NormalizedPageSize).ToList();
        return (visible, pagination);
    }

    private static int ScheduleFridayAddonDays(int durationDays)
        => durationDays <= 0 ? 0 : Math.Max(1, durationDays / 6);

    private static bool IsBundleSlotConfigurationValid(int breakfast, int lunch, int dinner, int snack)
    {
        var total = breakfast + lunch + dinner + snack;
        return total > 0 && breakfast >= 0 && lunch >= 0 && dinner >= 0 && snack >= 0;
    }

    private static List<SlotRequirementDto> ToSlotRequirements(int breakfast, int lunch, int dinner, int snack)
        => new[]
        {
            new SlotRequirementDto("breakfast", breakfast),
            new SlotRequirementDto("lunch", lunch),
            new SlotRequirementDto("dinner", dinner),
            new SlotRequirementDto("snack", snack)
        }.Where(x => x.Count > 0).ToList();

    private static List<CustomerBundleSlotDto> ToDisplaySlots(int breakfast, int lunch, int dinner, int snack)
        => new[]
        {
            (Code: "breakfast", Ar: "الفطور", En: "Breakfast", breakfast),
            (Code: "lunch", Ar: "الغداء", En: "Lunch", lunch),
            (Code: "dinner", Ar: "العشاء", En: "Dinner", dinner),
            (Code: "snack", Ar: "سناك", En: "Snack", snack)
        }.Where(x => x.Item4 > 0)
            .Select(x => new CustomerBundleSlotDto(x.Code, x.Ar, x.En, x.Item4))
            .ToList();

    private static CustomerFridayAvailabilityDto CreateFridayAvailability(int restaurantCount)
        => new(
            restaurantCount > 0,
            restaurantCount,
            "friday",
            restaurantCount > 0 ? "مطاعم تعمل يوم الجمعة" : "لا توجد مطاعم تعمل يوم الجمعة",
            restaurantCount > 0 ? "Restaurants working on Friday" : "No restaurants work on Friday",
            restaurantCount > 0 ? "available" : "disabled");

    private async Task<Dictionary<Guid, string?>> LoadRestaurantLogoUrlsAsync(IReadOnlySet<Guid> restaurantIds, CancellationToken ct)
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

            result[id] = CatalogImageStorage.ToContentUrl(logo);
        }

        return result;
    }

    private async Task<TierLabelsSettings> LoadTierLabelsAsync(CancellationToken ct)
    {
        var settingsRow = await _db.SystemSettings.AsNoTracking()
            .FirstOrDefaultAsync(x => x.Key == SubscriptionPricingSettingsDefaults.Key && x.IsActive, ct);
        return SubscriptionPricingSettingsReader.Deserialize(settingsRow?.ValueJson).TierLabels;
    }

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
}
