using MealMate.Api.Common;
using MealMate.Application.Common;
using MealMate.Application.Common.Abstractions;
using MealMate.Application.Common.Models;
using MealMate.Application.Common.Storage;
using MealMate.Application.Features.CustomerCheckout;
using MealMate.Application.Features.ProgramsSubscriptions;
using MealMate.Application.Features.ProgramsSubscriptions.Pricing;
using MealMate.Domain.Enums;
using MealMate.Domain.IdentityAccess;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace MealMate.Api.Controllers.V1;

/// <summary>Mobile-first read endpoints for every screen before subscription checkout.</summary>
[Authorize]
[Route("api/v1/customer/subscription-flow")]
public sealed class CustomerSubscriptionFlowController : ApiControllerBase
{
    private static readonly SubscriptionStatus[] CountedSubscriptionStatuses =
    [
        SubscriptionStatus.Paid, SubscriptionStatus.Scheduled, SubscriptionStatus.Active,
        SubscriptionStatus.Frozen, SubscriptionStatus.Completed, SubscriptionStatus.Expired,
        SubscriptionStatus.CancellationPending, SubscriptionStatus.Upgraded,
    ];
    private readonly IAppDbContext _db;
    private readonly ICurrentUser _currentUser;
    private readonly IRestaurantEligibilityService _eligibility;
    private readonly IMenuPlanResolutionService _menus;
    private readonly ISubscriptionScheduleService _schedule;
    private readonly IBundleSlotRequirementService _slots;
    private readonly IMyFatoorahPaymentService _payments;
    private readonly IAllowedSubscriptionDurationService _allowedDurations;
    private readonly IRestaurantTierClassificationService _tiers;
    private readonly ISubscriptionCheckoutReadinessService _readiness;
    private readonly ICustomerIndividualSubscriptionGate _individualGate;
    private readonly IRestaurantDiversificationCalculator _diversification;

    public CustomerSubscriptionFlowController(IAppDbContext db, ICurrentUser currentUser, IRestaurantEligibilityService eligibility,
        IMenuPlanResolutionService menus, ISubscriptionScheduleService schedule, IBundleSlotRequirementService slots, IMyFatoorahPaymentService payments,
        IAllowedSubscriptionDurationService allowedDurations, IRestaurantTierClassificationService tiers, ISubscriptionCheckoutReadinessService readiness,
        ICustomerIndividualSubscriptionGate individualGate, IRestaurantDiversificationCalculator diversification)
        => (_db, _currentUser, _eligibility, _menus, _schedule, _slots, _payments, _allowedDurations, _tiers, _readiness, _individualGate, _diversification)
            = (db, currentUser, eligibility, menus, schedule, slots, payments, allowedDurations, tiers, readiness, individualGate, diversification);

    [HttpGet("types")]
    public async Task<IActionResult> Types(CancellationToken ct)
    {
        if (!IsCustomer()) return Forbid();
        var userId = _currentUser.UserId is Guid id ? id : Guid.Empty;
        var hasIndividual = userId != Guid.Empty && await _individualGate.FindBlockingAsync(userId, ct) is not null;
        var hasFamily = userId != Guid.Empty && await _individualGate.HasBlockingFamilyGroupAsync(userId, ct);
        var individualBlocked = hasIndividual || hasFamily;
        const string conversionOptions = "/api/v1/customer/subscriptions/current/conversion-options";
        return Ok(new[]
        {
            new SubscriptionTypeOptionDto(
                "INDIVIDUAL", "اشتراك فردي", "Individual subscription",
                "خطة غذائية لشخص واحد", "A meal plan for one person",
                1, !individualBlocked, false,
                individualBlocked ? CustomerIndividualSubscriptionGate.ExistingSubscriptionError : null),
            new SubscriptionTypeOptionDto(
                "FAMILY", "اشتراك عائلي", "Family subscription",
                hasIndividual
                    ? "حوّل اشتراكك الفردي لعائلة وابقَ مديرًا"
                    : hasFamily
                        ? "أكمل مسودة العائلة أو الاشتراك العائلي الحالي"
                        : "خصص برنامجاً وباقة لكل فرد وادفع مرة واحدة",
                hasIndividual
                    ? "Convert your individual plan into a family as manager"
                    : hasFamily
                        ? "Continue your family draft or active family subscription"
                        : "Configure a program and bundle per family member, then pay once",
                4, true, false,
                DisabledReason: null,
                RequiresConversion: hasIndividual,
                ConversionEndpoint: hasIndividual ? conversionOptions : null)
        });
    }

    [HttpGet("programs")]
    [ProducesResponseType(typeof(IReadOnlyList<CustomerProgramCardDto>), StatusCodes.Status200OK)]
    public async Task<IActionResult> Programs(CancellationToken ct)
    {
        if (!IsCustomer()) return Forbid();
        var programs = await _db.Programs.AsNoTracking()
            .Where(x => x.IsActive && x.Status == ProgramStatus.Active)
            .OrderBy(x => x.SortOrder).Select(x => new { x.Id, x.Code, x.ShortCode, x.NameAr, x.NameEn, x.DescriptionAr, x.DescriptionEn, x.KeywordsAr, x.KeywordsEn, x.ImageStorageKey }).ToListAsync(ct);
        var ids = programs.Select(x => x.Id).ToList();
        var bundleCounts = await (from bundle in _db.Bundles.AsNoTracking()
                                  join offer in _db.RestaurantProgramBundleOffers.AsNoTracking() on bundle.Id equals offer.BundleId
                                  join restaurant in _db.RestaurantProfiles.AsNoTracking() on offer.RestaurantId equals restaurant.Id
                                  where ids.Contains(bundle.ProgramId)
                                      && bundle.IsActive
                                      && bundle.Status == ProgramStatus.Active
                                      && offer.ProgramId == bundle.ProgramId
                                      && offer.IsActive
                                      && restaurant.VerificationStatus == VerificationStatus.Approved
                                      && restaurant.IsOperationallyActive
                                  group bundle by bundle.ProgramId into programBundles
                                  select new
                                  {
                                      ProgramId = programBundles.Key,
                                      Count = programBundles.Select(x => x.Id).Distinct().Count(),
                                  })
            .ToDictionaryAsync(x => x.ProgramId, x => x.Count, ct);
        var slots = await _db.ProgramMealSlotRequirements.AsNoTracking().Where(x => ids.Contains(x.ProgramId) && x.IsRequired).ToListAsync(ct);
        return Ok(programs.Where(x => bundleCounts.GetValueOrDefault(x.Id) > 0).Select(x => new CustomerProgramCardDto(
            x.Id, x.Code, x.ShortCode, x.NameAr, x.NameEn, x.DescriptionAr, x.DescriptionEn,
            x.KeywordsAr, x.KeywordsEn, CatalogImageStorage.ToContentUrl(x.ImageStorageKey), bundleCounts.GetValueOrDefault(x.Id),
            slots.Where(s => s.ProgramId == x.Id).Select(s => s.Slot).OrderBy(s => s).ToList())));
    }

    [HttpGet("programs/{programId:guid}/bundles")]
    [ProducesResponseType(typeof(CustomerProgramBundlesDto), StatusCodes.Status200OK)]
    public async Task<IActionResult> Bundles(Guid programId, CancellationToken ct)
    {
        if (!IsCustomer()) return Forbid();
        var program = await _db.Programs.AsNoTracking().FirstOrDefaultAsync(x => x.Id == programId && x.IsActive && x.Status == ProgramStatus.Active, ct);
        if (program is null) return NotFound(new { error = "subscription_flow.program_not_found" });
        var hasDeliveryAddress = await ResolveCustomerDeliveryRegionIdAsync(ct) is not null;
        var bundles = await _db.Bundles.AsNoTracking().Where(x => x.ProgramId == programId && x.IsActive && x.Status == ProgramStatus.Active)
            .OrderBy(x => x.SortOrder).ToListAsync(ct);
        var bundleIds = bundles.Select(x => x.Id).ToList();
        var selectionCounts = await _db.Subscriptions.AsNoTracking()
            .Where(x => x.ProgramId == programId
                && bundleIds.Contains(x.BundleId)
                && (x.Status == SubscriptionStatus.Paid
                    || x.Status == SubscriptionStatus.Scheduled
                    || x.Status == SubscriptionStatus.Active
                    || x.Status == SubscriptionStatus.Frozen
                    || x.Status == SubscriptionStatus.Completed
                    || x.Status == SubscriptionStatus.Expired
                    || x.Status == SubscriptionStatus.CancellationPending
                    || x.Status == SubscriptionStatus.Upgraded))
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
        var activeRestaurantIds = restaurantProfiles.Select(x => x.Id).ToList();
        var logos = await LoadRestaurantLogoUrlsAsync(activeRestaurantIds, ct);
        var restaurantCards = restaurantProfiles.ToDictionary(
            x => x.Id,
            x => new CustomerBundleRestaurantDto(x.Id, logos.GetValueOrDefault(x.Id)));

        return Ok(new CustomerProgramBundlesDto(
            hasDeliveryAddress,
            bundles.Where(IsBundleSlotConfigurationValid).Select(x =>
        {
            var restaurants = offers.Where(o => o.BundleId == x.Id)
                .Select(o => restaurantCards.GetValueOrDefault(o.RestaurantId))
                .Where(r => r is not null)
                .Select(r => r!)
                .ToList();
            return new CustomerBundleCardDto(x.Id, x.ProgramId, x.Code, x.NameAr, x.NameEn,
                x.Description ?? x.DescriptionEn ?? x.DescriptionAr, CatalogImageStorage.ToContentUrl(x.ImageStorageKey), x.MealsPerDay, x.BreakfastCount, x.LunchCount, x.DinnerCount, x.SnackCount,
                ToSlotRequirements(x.BreakfastCount, x.LunchCount, x.DinnerCount, x.SnackCount), x.SortOrder,
                x.DescriptionAr ?? x.NameAr, x.DescriptionEn ?? x.Description ?? x.NameEn,
                ToDisplaySlots(x.BreakfastCount, x.LunchCount, x.DinnerCount, x.SnackCount),
                restaurants.Count, restaurants.Take(8).ToList(), selectionCounts.GetValueOrDefault(x.Id),
                x.Id == mostSelectedBundleId,
                x.Id == mostSelectedBundleId ? new CustomerBundleBadgeDto("MOST_SELECTED", "الأكثر اختيارًا", "Most selected") : null);
        }).ToList()));
    }

    [HttpGet("programs/{programId:guid}/bundles/{bundleId:guid}/durations")]
    [ProducesResponseType(typeof(IReadOnlyList<CustomerDurationOptionDto>), StatusCodes.Status200OK)]
    public async Task<IActionResult> Durations(Guid programId, Guid bundleId, [FromQuery] bool includeFriday,
        CancellationToken ct)
    {
        if (!IsCustomer()) return Forbid();
        if (!await IsActiveBundleAsync(programId, bundleId, ct))
            return NotFound(new { error = "subscription_flow.bundle_not_found" });

        var settings = await _allowedDurations.GetSettingsAsync(ct);
        var activeOptions = settings.Durations.Where(x => x.IsActive).OrderBy(x => x.IsCustom).ThenBy(x => x.Days).ToList();
        if (activeOptions.Count == 0) return Conflict(new { error = "pricing_unavailable" });

        // Ensure pricing exists for the program/bundle before exposing duration cards.
        var catalog = await Sender.Send(new GetCustomerSubscriptionPricingCatalogQuery(), ct);
        if (catalog.IsFailure) return HandleResult(catalog);
        var pricedTiers = catalog.Value
            .Where(x => x.ProgramId == programId && x.BundleId == bundleId)
            .Select(x => x.Tier)
            .Distinct()
            .ToList();
        if (pricedTiers.Count == 0)
            return Conflict(new { error = "pricing_unavailable" });

        var fridayAvailability = await ResolveFridayAvailabilityAsync(programId, bundleId, null, ct, pricedTiers);
        var selectionCounts = await _db.Subscriptions.AsNoTracking()
            .Where(x => x.ProgramId == programId && x.BundleId == bundleId && CountedSubscriptionStatuses.Contains(x.Status))
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
            return Conflict(new { error = "subscription_flow.friday_unavailable", fridayAvailability });

        var popularDuration = selectionCounts.Count == 0
            ? (int?)null
            : presetRows.OrderByDescending(x => selectionCounts.GetValueOrDefault(x.DurationDays ?? -1))
                .ThenByDescending(x => x.DurationDays)
                .Where(x => selectionCounts.GetValueOrDefault(x.DurationDays ?? -1) > 0)
                .Select(x => x.DurationDays)
                .FirstOrDefault();

        var result = new List<CustomerDurationOptionDto>();
        foreach (var row in presetRows)
        {
            if (includeFriday && !row.SupportsFriday) continue;
            var option = row.Option;
            result.Add(new CustomerDurationOptionDto(
                row.DurationDays, option.NameAr, option.NameEn,
                selectionCounts.GetValueOrDefault(option.Days),
                row.DurationDays == popularDuration,
                row.DurationDays == popularDuration ? new CustomerBundleBadgeDto("MOST_POPULAR", "الأكثر اختيارًا", "Most popular") : null,
                row.SupportsFriday, includeFriday && row.SupportsFriday, row.FridayDays, row.TotalServiceDays,
                new CustomerFridayOptionDto("INCLUDE_FRIDAY", "إضافة يوم الجمعة", "Include Friday", false, row.SupportsFriday),
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
                    new CustomerFridayOptionDto("INCLUDE_FRIDAY", "إضافة يوم الجمعة", "Include Friday", false, supportsFriday),
                    supportsFriday ? fridayAvailability : CreateFridayAvailability(0),
                    true, custom.MinDays, custom.MaxDays));
            }
        }

        if (result.Count == 0) return Conflict(new { error = "pricing_unavailable" });
        return Ok(result);
    }

    [HttpGet("programs/{programId:guid}/bundles/{bundleId:guid}/tiers")]
    [ProducesResponseType(typeof(IReadOnlyList<CustomerTierOptionDto>), StatusCodes.Status200OK)]
    public async Task<IActionResult> Tiers(Guid programId, Guid bundleId, [FromQuery] int? durationDays,
        [FromQuery] bool includeFriday, CancellationToken ct)
    {
        if (!IsCustomer()) return Forbid();
        if (!await IsActiveBundleAsync(programId, bundleId, ct))
            return NotFound(new { error = "subscription_flow.bundle_not_found" });
        if (durationDays.HasValue)
        {
            var allowed = await _allowedDurations.EvaluateAsync(durationDays.Value, includeFriday, ct);
            if (!allowed.IsAllowed)
                return BadRequest(new { error = allowed.ErrorCode ?? "subscription_flow.duration_not_available" });
        }

        var deliveryRegionId = await ResolveCustomerDeliveryRegionIdAsync(ct);
        if (deliveryRegionId is null)
            return Conflict(new { error = "checkout.address_required" });

        var catalog = await Sender.Send(new GetCustomerSubscriptionPricingCatalogQuery(), ct);
        if (catalog.IsFailure) return HandleResult(catalog);
        var programBundles = catalog.Value.Where(x => x.ProgramId == programId && x.BundleId == bundleId).ToList();
        if (programBundles.Count == 0) return Conflict(new { error = "pricing_unavailable" });
        var pricedTiersList = programBundles.Select(x => x.Tier).Distinct().ToList();
        var fridayAvailability = await ResolveFridayAvailabilityAsync(programId, bundleId, null, ct, pricedTiersList);
        if (includeFriday && !fridayAvailability.IsAvailable)
            return Conflict(new { error = "subscription_flow.friday_unavailable", fridayAvailability });

        int? fridayDays = null;
        int? totalServiceDays = null;
        var pricedTiers = new List<TierPricingRow>();
        if (durationDays.HasValue)
        {
            fridayDays = includeFriday ? _schedule.GetFridayAddonDays(durationDays.Value) : 0;
            totalServiceDays = durationDays.Value + fridayDays.Value;
            foreach (var item in programBundles.OrderBy(x => x.Tier))
            {
                var catalogPrice = item.Prices.SingleOrDefault(p => p.DurationDays == durationDays.Value);
                if (catalogPrice is not null && !includeFriday)
                {
                    pricedTiers.Add(new TierPricingRow(item, catalogPrice.TotalPriceKwd, catalogPrice.DailyPriceKwd));
                    continue;
                }

                var preview = await Sender.Send(new PreviewCustomerPricingQuery(programId, bundleId, item.Tier, durationDays.Value,
                    IncludeFriday: includeFriday, FridayDays: fridayDays.Value), ct);
                if (preview.IsFailure) return HandleResult(preview);
                pricedTiers.Add(new TierPricingRow(item, preview.Value.FinalCustomerPrice,
                    preview.Value.FinalCustomerPrice / totalServiceDays.Value));
            }
            if (pricedTiers.Count == 0) return BadRequest(new { error = "subscription_flow.duration_not_available" });
        }
        else
        {
            pricedTiers.AddRange(programBundles.GroupBy(x => x.Tier).OrderBy(x => x.Key)
                .Select(group => new TierPricingRow(group.First(), null, null)));
        }

        var classification = await _tiers.ResolveAsync(programId, bundleId, eligibleOnly: true, ct);
        var classifiedRestaurantIds = await FilterRestaurantIdsByDeliveryRegionAsync(
            classification.Assignments.Select(x => x.RestaurantId).Distinct().ToList(),
            deliveryRegionId.Value,
            ct);
        var coveredSet = classifiedRestaurantIds.ToHashSet();
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
        if (includeFriday && fridayAvailabilityByTier.Values.All(x => !x.IsAvailable))
            return Conflict(new { error = "subscription_flow.friday_unavailable", fridayAvailability = CreateFridayAvailability(0) });

        var selectionQuery = _db.Subscriptions.AsNoTracking()
            .Where(x => x.ProgramId == programId && x.BundleId == bundleId && CountedSubscriptionStatuses.Contains(x.Status));
        if (durationDays.HasValue)
            selectionQuery = selectionQuery.Where(x => x.DurationDays == durationDays.Value);
        var selectionCounts = await selectionQuery
            .GroupBy(x => x.SelectedTier)
            .Select(g => new { Tier = g.Key, Count = g.Count() })
            .ToDictionaryAsync(x => x.Tier, x => x.Count, ct);

        var displayedTiers = pricedTiers
            .Where(x => (restaurantsByTier.GetValueOrDefault(x.Catalog.Tier)?.Count ?? 0) > 0)
            .Where(x => !includeFriday || fridayAvailabilityByTier[x.Catalog.Tier].IsAvailable)
            .ToList();
        if (displayedTiers.Count == 0)
            return Conflict(new { error = SubscriptionCheckoutGateCodes.NoRestaurantsForAddress });

        var recommendedTier = selectionCounts.Count > 0
            ? displayedTiers.OrderByDescending(x => selectionCounts.GetValueOrDefault(x.Catalog.Tier)).ThenBy(x => x.Catalog.Tier).Select(x => x.Catalog.Tier).First()
            : displayedTiers.Select(x => x.Catalog.Tier).First();
        decimal? basePrice = displayedTiers.Any(x => x.TotalPrice.HasValue)
            ? displayedTiers.Where(x => x.TotalPrice.HasValue).Min(x => x.TotalPrice)
            : null;
        var labels = await LoadTierLabelsAsync(ct);

        return Ok(displayedTiers.Select(x =>
        {
            var restaurants = restaurantsByTier.GetValueOrDefault(x.Catalog.Tier) ?? [];
            var (descriptionAr, descriptionEn) = TierDescription(x.Catalog.Tier);
            return new CustomerTierOptionDto(
                x.Catalog.Tier, x.Catalog.TierNameAr, x.Catalog.TierNameEn, TierLogoUrl(labels, x.Catalog.Tier),
                descriptionAr, descriptionEn,
                restaurants.Count, restaurants, durationDays, x.TotalPrice,
                x.DailyPrice, x.TotalPrice.HasValue && basePrice.HasValue ? x.TotalPrice.Value - basePrice.Value : null,
                x.TotalPrice.HasValue ? x.Catalog.CurrencyCode : null,
                selectionCounts.GetValueOrDefault(x.Catalog.Tier), x.Catalog.Tier == recommendedTier,
                includeFriday, fridayDays, totalServiceDays, fridayAvailabilityByTier[x.Catalog.Tier]);
        }).ToList());
    }

    [HttpGet("programs/{programId:guid}/bundles/{bundleId:guid}/tiers/{tier}/restaurants")]
    [ProducesResponseType(typeof(IReadOnlyList<CustomerRestaurantBrowseCardDto>), StatusCodes.Status200OK)]
    public async Task<IActionResult> TierRestaurants(Guid programId, Guid bundleId, RestaurantTier tier,
        [FromQuery] Guid[]? restaurantIds, CancellationToken ct)
    {
        if (!IsCustomer()) return Forbid();
        if (!await IsActiveBundleAsync(programId, bundleId, ct))
            return NotFound(new { error = "subscription_flow.bundle_not_found" });

        var deliveryRegionId = await ResolveCustomerDeliveryRegionIdAsync(ct);
        if (deliveryRegionId is null)
            return Conflict(new { error = "checkout.address_required" });

        var classification = await _tiers.ResolveAsync(programId, bundleId, eligibleOnly: true, ct);
        var tierRestaurantIds = classification.Assignments
            .Where(x => RestaurantTierAccess.CanSelect(tier, x.Tier))
            .OrderBy(x => x.Tier)
            .Select(x => x.RestaurantId)
            .Distinct()
            .ToList();
        tierRestaurantIds = await FilterRestaurantIdsByDeliveryRegionAsync(
            tierRestaurantIds, deliveryRegionId.Value, ct);
        var allowed = tierRestaurantIds.ToHashSet();
        var orderedIds = restaurantIds is { Length: > 0 }
            ? restaurantIds.Where(id => id != Guid.Empty && allowed.Contains(id)).Distinct().ToList()
            : tierRestaurantIds;

        return Ok(await BuildRestaurantBrowseCardsAsync(programId, bundleId, classification, orderedIds, ct));
    }

    [HttpGet("programs/{programId:guid}/bundles/{bundleId:guid}/restaurants/{restaurantId:guid}")]
    [ProducesResponseType(typeof(CustomerRestaurantBrowseCardDto), StatusCodes.Status200OK)]
    public async Task<IActionResult> RestaurantBrowseCard(Guid programId, Guid bundleId, Guid restaurantId, CancellationToken ct)
    {
        if (!IsCustomer()) return Forbid();
        if (!await IsActiveBundleAsync(programId, bundleId, ct))
            return NotFound(new { error = "subscription_flow.bundle_not_found" });

        var classification = await _tiers.ResolveAsync(programId, bundleId, eligibleOnly: true, ct);
        if (!classification.Assignments.Any(x => x.RestaurantId == restaurantId))
            return NotFound(new { error = "subscription_flow.restaurant_not_found" });

        var card = (await BuildRestaurantBrowseCardsAsync(programId, bundleId, classification, [restaurantId], ct))
            .FirstOrDefault();
        return card is null
            ? NotFound(new { error = "subscription_flow.restaurant_not_found" })
            : Ok(card);
    }

    [HttpGet("programs/{programId:guid}/bundles/{bundleId:guid}/restaurants/{restaurantId:guid}/details")]
    [ProducesResponseType(typeof(CustomerRestaurantBrowseDetailsDto), StatusCodes.Status200OK)]
    public async Task<IActionResult> RestaurantBrowseDetails(Guid programId, Guid bundleId, Guid restaurantId, CancellationToken ct)
    {
        if (!IsCustomer()) return Forbid();
        if (!await IsActiveBundleAsync(programId, bundleId, ct))
            return NotFound(new { error = "subscription_flow.bundle_not_found" });

        var classification = await _tiers.ResolveAsync(programId, bundleId, eligibleOnly: true, ct);
        if (!classification.Assignments.Any(x => x.RestaurantId == restaurantId))
            return NotFound(new { error = "subscription_flow.restaurant_not_found" });

        var details = await BuildRestaurantBrowseDetailsAsync(programId, bundleId, restaurantId, ct);
        return details is null
            ? NotFound(new { error = "subscription_flow.restaurant_not_found" })
            : Ok(details);
    }

    [HttpGet("programs/{programId:guid}/bundles/{bundleId:guid}/restaurants/{restaurantId:guid}/meals/{mealId:guid}")]
    [ProducesResponseType(typeof(CustomerRestaurantBrowseMealDetailsDto), StatusCodes.Status200OK)]
    public async Task<IActionResult> RestaurantBrowseMealDetails(Guid programId, Guid bundleId, Guid restaurantId, Guid mealId, CancellationToken ct)
    {
        if (!IsCustomer()) return Forbid();
        if (!await IsActiveBundleAsync(programId, bundleId, ct))
            return NotFound(new { error = "subscription_flow.bundle_not_found" });

        var classification = await _tiers.ResolveAsync(programId, bundleId, eligibleOnly: true, ct);
        if (!classification.Assignments.Any(x => x.RestaurantId == restaurantId))
            return NotFound(new { error = "subscription_flow.restaurant_not_found" });

        var details = await BuildRestaurantBrowseMealDetailsAsync(programId, bundleId, restaurantId, mealId, ct);
        return details is null
            ? NotFound(new { error = "subscription_flow.meal_not_found" })
            : Ok(details);
    }

    [HttpGet("configuration")]
    public async Task<IActionResult> Configuration([FromQuery] Guid programId, [FromQuery] Guid bundleId, CancellationToken ct)
    {
        if (!IsCustomer()) return Forbid();
        var bundle = await _db.Bundles.AsNoTracking().FirstOrDefaultAsync(x => x.Id == bundleId && x.ProgramId == programId && x.IsActive && x.Status == ProgramStatus.Active, ct);
        if (bundle is null || !IsBundleSlotConfigurationValid(bundle)) return NotFound(new { error = "subscription_flow.bundle_not_found" });
        var catalog = await Sender.Send(new GetCustomerSubscriptionPricingCatalogQuery(), ct);
        if (catalog.IsFailure) return HandleResult(catalog);
        var classification = await _tiers.ResolveAsync(programId, bundleId, eligibleOnly: true, ct);
        var tierCounts = classification.Assignments.GroupBy(x => x.Tier)
            .ToDictionary(x => x.Key, x => x.Select(v => v.RestaurantId).Distinct().Count());
        var options = catalog.Value.Where(x => x.ProgramId == programId && x.BundleId == bundleId)
            .Where(x => tierCounts.GetValueOrDefault(x.Tier) > 0)
            .GroupBy(x => x.Tier).OrderBy(x => x.Key).Select(group => new TierConfigurationDto(group.Key, group.First().TierNameAr, group.First().TierNameEn,
                tierCounts.GetValueOrDefault(group.Key), group.SelectMany(x => x.Prices).DistinctBy(x => x.DurationDays).OrderBy(x => x.DurationDays)
                    .Select(x => new DurationPriceOptionDto(x.DurationDays, DurationNameAr(x.DurationDays), $"{x.DurationDays} days", x.TotalPriceKwd, x.DailyPriceKwd, "KWD")).ToList())).ToList();
        return Ok(new SubscriptionConfigurationDto(programId, bundle.Id, bundle.NameAr, bundle.NameEn, bundle.Description, bundle.MealsPerDay,
            ToSlotRequirements(bundle.BreakfastCount, bundle.LunchCount, bundle.DinnerCount, bundle.SnackCount), options));
    }

    [HttpGet("restaurants")]
    public async Task<IActionResult> Restaurants([FromQuery] SubscriptionFlowContext request, CancellationToken ct)
    {
        if (!IsCustomer()) return Forbid();
        var validation = await ValidateContextAsync(request, ct);
        if (validation is not null) return BadRequest(new { error = validation });
        var dates = _schedule.GetServiceDates(request.StartDate, request.DurationDays, request.IncludeFriday);
        var fridayDays = _schedule.CountFridays(dates);
        var pricing = await Sender.Send(new PreviewCustomerPricingQuery(request.ProgramId, request.BundleId, request.Tier, request.DurationDays,
            IncludeFriday: request.IncludeFriday, FridayDays: fridayDays), ct);
        if (pricing.IsFailure) return HandleResult(pricing);
        var rows = await _eligibility.EvaluateAsync(ToEligibility(request, _currentUser.UserId), ct);
        var eligible = rows.Where(x => x.IsEligible).ToList();
        var resolved = await _menus.ResolveManyAsync(eligible.Select(x => x.RestaurantId).ToList(), request.ProgramId, dates, ct);
        var mealIdsByRestaurant = resolved.GroupBy(x => x.Key.RestaurantId).ToDictionary(x => x.Key, x => x.SelectMany(v => v.Value.Assignments).Select(a => a.MealId).Distinct().ToList());
        var allMealIds = mealIdsByRestaurant.Values.SelectMany(x => x).Distinct().ToList();
        var images = await _db.MealImages.AsNoTracking().Where(x => allMealIds.Contains(x.MealId)).OrderByDescending(x => x.IsPrimary).ThenBy(x => x.DisplayOrder).ToListAsync(ct);
        var regions = await _db.RestaurantServiceRegions.AsNoTracking().Where(x => eligible.Select(e => e.RestaurantId).Contains(x.RestaurantId) && x.RegionId == request.RegionId && x.IsActive).ToListAsync(ct);
        var labels = await LoadTierLabelsAsync(ct);
        var tierLogoUrl = TierLogoUrl(labels, request.Tier);
        return Ok(eligible.Select(x =>
        {
            var ids = mealIdsByRestaurant.GetValueOrDefault(x.RestaurantId) ?? [];
            var window = regions.FirstOrDefault(r => r.RestaurantId == x.RestaurantId);
            return new CustomerRestaurantCardDto(x.RestaurantId, x.NameAr, x.NameEn, images.FirstOrDefault(i => ids.Contains(i.MealId))?.ImageUrl,
                request.Tier, tierLogoUrl, pricing.Value.FinalCustomerPrice, "KWD", ids.Count, window?.DeliveryWindowStart, window?.DeliveryWindowEnd,
                x.MenuPlanVersionsByDate, request.IncludeFriday, fridayDays, dates.Count);
        }));
    }

    [HttpGet("restaurants/{restaurantId:guid}")]
    public async Task<IActionResult> Restaurant(Guid restaurantId, [FromQuery] SubscriptionFlowContext request, CancellationToken ct)
    {
        if (!IsCustomer()) return Forbid();
        var details = await ResolveRestaurantDetailsAsync(restaurantId, request, ct);
        return details.ErrorResult ?? Ok(details.Value);
    }

    [HttpGet("restaurants/{restaurantId:guid}/meals/{mealId:guid}")]
    public async Task<IActionResult> Meal(Guid restaurantId, Guid mealId, [FromQuery] SubscriptionFlowContext request, CancellationToken ct)
    {
        if (!IsCustomer()) return Forbid();
        var details = await ResolveRestaurantDetailsAsync(restaurantId, request, ct);
        if (details.ErrorResult is not null) return details.ErrorResult;
        var meal = details.Value!.MealGroups.SelectMany(x => x.Meals).FirstOrDefault(x => x.MealId == mealId);
        if (meal is null) return NotFound(new { error = "subscription_flow.meal_not_in_resolved_menu" });
        return Ok(meal);
    }

    [HttpGet("readiness")]
    [ProducesResponseType(typeof(SubscriptionCheckoutReadinessDto), StatusCodes.Status200OK)]
    public async Task<IActionResult> Readiness([FromQuery] SubscriptionSummaryRequest request, CancellationToken ct)
    {
        if (!IsCustomer()) return Forbid();
        var readiness = await _readiness.EvaluateAsync(_currentUser.UserId!.Value, request.ProgramId, request.BundleId,
            request.Tier, request.DurationDays, request.IncludeFriday, ct);
        return readiness.IsFailure ? ReadinessError(readiness.Error) : Ok(readiness.Value);
    }

    [HttpGet("summary")]
    public async Task<IActionResult> Summary([FromQuery] SubscriptionSummaryRequest request, CancellationToken ct)
    {
        if (!IsCustomer()) return Forbid();
        var validation = await ValidateSummaryRequestAsync(request, ct);
        if (validation is not null) return BadRequest(new { error = validation });
        var readiness = await _readiness.EvaluateAsync(_currentUser.UserId!.Value, request.ProgramId, request.BundleId,
            request.Tier, request.DurationDays, request.IncludeFriday, ct);
        if (readiness.IsFailure) return ReadinessError(readiness.Error);
        if (!readiness.Value.CanOpenSummary)
            return Conflict(new { error = SubscriptionCheckoutGateCodes.ProfileIncomplete, readiness = readiness.Value });
        if (!_schedule.TryResolveCheckoutStart(request.StartDate, request.IncludeFriday, out var startDate, out var startError))
            return BadRequest(new { error = startError });
        var chosenStartDate = request.StartDate!.Value;
        var classification = await _tiers.ResolveAsync(request.ProgramId, request.BundleId, eligibleOnly: true, ct);
        if (!classification.Assignments.Any(x => x.Tier == request.Tier))
            return BadRequest(new { error = "subscription_flow.tier_unavailable" });

        var program = await _db.Programs.AsNoTracking().SingleAsync(x => x.Id == request.ProgramId, ct);
        var bundle = await _db.Bundles.AsNoTracking().SingleAsync(x => x.Id == request.BundleId, ct);
        var dates = _schedule.GetServiceDates(startDate, request.DurationDays, request.IncludeFriday);
        var fridayDays = _schedule.CountFridays(dates);
        var price = await Sender.Send(new PreviewCustomerPricingQuery(request.ProgramId, request.BundleId, request.Tier, request.DurationDays,
            IncludeFriday: request.IncludeFriday, FridayDays: fridayDays), ct);
        if (price.IsFailure) return HandleResult(price);
        var slots = await _slots.ResolveAsync(request.ProgramId, request.BundleId, ct);
        var summarySlots = ToSummarySlots(slots);
        var restaurantIds = readiness.Value.Restaurants.Select(x => x.RestaurantId).ToList();
        var logos = await LoadRestaurantLogoUrlsAsync(restaurantIds, ct);
        var restaurants = readiness.Value.Restaurants
            .Select(x => x with { LogoUrl = logos.GetValueOrDefault(x.RestaurantId) ?? x.LogoUrl })
            .ToList();
        var restaurantCount = restaurants.Count;
        var repetition = _diversification.CalculateLimit(
            Guid.Empty, request.DurationDays, Math.Max(restaurantCount, 1));
        var restaurantRepetition = new RestaurantRepetitionSummaryDto(
            repetition.LimitValue,
            restaurantCount,
            request.DurationDays);
        var mealVariety = await CountTierBundleMealsAsync(restaurantIds, request.ProgramId, request.BundleId, ct);
        var labels = await LoadTierLabelsAsync(ct);
        var (tierNameAr, tierNameEn) = TierDisplayName(request.Tier, labels);
        return Ok(new SubscriptionOrderSummaryDto(program.Id, program.NameAr, program.NameEn, CatalogImageStorage.ToContentUrl(program.ImageStorageKey),
            bundle.Id, bundle.NameAr, bundle.NameEn, CatalogImageStorage.ToContentUrl(bundle.ImageStorageKey),
            restaurantCount, request.Tier, tierNameAr, tierNameEn, request.DurationDays, startDate, dates.Last(), dates,
            summarySlots, price.Value.UnroundedCorePrice, price.Value.CampaignDiscountApplied, price.Value.PromoCodeDiscountApplied,
            price.Value.WalletCreditApplied, price.Value.GatewayCostRecoveryAmount, price.Value.FinalCustomerPrice, price.Value.CurrencyCode,
            true, "POST /api/v1/customer/subscriptions/checkout", request.IncludeFriday, fridayDays, dates.Count,
            BuildIncludes(summarySlots, restaurantCount, dates.Count, mealVariety),
            readiness.Value.Address, restaurants, readiness.Value.CanCheckout, readiness.Value.CheckoutBlockReason, chosenStartDate,
            restaurantRepetition));
    }

    [HttpGet("payment-methods")]
    public async Task<IActionResult> PaymentMethods([FromQuery] decimal amount, CancellationToken ct)
    {
        if (!IsCustomer()) return Forbid();
        if (amount <= 0) return BadRequest(new { error = "subscription_flow.invalid_amount" });
        var config = await _db.MyFatoorahGatewayConfigs.AsNoTracking().FirstOrDefaultAsync(x => x.GatewayName == "MyFatoorah", ct);
        if (config is null) return Conflict(new { error = "payment.gateway_not_configured" });
        IReadOnlyList<MyFatoorahPaymentMethod> methods;
        try { methods = await _payments.GetPaymentMethodsAsync(amount, ct); }
        catch { return Problem(statusCode: StatusCodes.Status502BadGateway, title: "Payment methods unavailable", detail: "MyFatoorah did not return payment methods."); }
        var result = methods.Where(x => IsConfiguredMethod(x, config.IsKnetEnabled, config.IsVisaMasterEnabled, config.IsApplePayEnabled))
            .Select(x => new CustomerPaymentMethodDto(x.PaymentMethodId, x.Code, x.NameAr, x.NameEn, "MYFATOORAH_HOSTED", x.ServiceCharge, x.TotalAmount, x.CurrencyCode, true, null)).ToList();
        result.Add(new CustomerPaymentMethodDto(null, "MEALMATE_WALLET", "محفظة MealMate", "MealMate Wallet", "WALLET", 0m, amount, config.DefaultCurrency, false, "wallet_not_implemented"));
        return Ok(result);
    }

    private async Task<ResolvedRestaurantDetails> ResolveRestaurantDetailsAsync(Guid restaurantId, SubscriptionFlowContext request, CancellationToken ct)
    {
        var validation = await ValidateContextAsync(request, ct);
        if (validation is not null) return new(null, BadRequest(new { error = validation }));
        var eligibleRows = await _eligibility.EvaluateAsync(ToEligibility(request, _currentUser.UserId, restaurantId), ct);
        var eligible = eligibleRows.SingleOrDefault(x => x.IsEligible);
        if (eligible is null) return new(null, BadRequest(new { error = "restaurant.not_eligible" }));
        var dates = _schedule.GetServiceDates(request.StartDate, request.DurationDays, request.IncludeFriday);
        var fridayDays = _schedule.CountFridays(dates);
        var resolved = await _menus.ResolveManyAsync([restaurantId], request.ProgramId, dates, ct);
        var assignments = resolved.Values.SelectMany(x => x.Assignments).DistinctBy(x => x.MealId).OrderBy(x => x.Slot).ThenBy(x => x.SortOrder).ToList();
        var cards = await BuildMealCardsAsync(assignments, restaurantId, ct);
        var window = await _db.RestaurantServiceRegions.AsNoTracking().FirstOrDefaultAsync(x => x.RestaurantId == restaurantId && x.RegionId == request.RegionId && x.IsActive, ct);
        var pricing = await Sender.Send(new PreviewCustomerPricingQuery(request.ProgramId, request.BundleId, request.Tier, request.DurationDays,
            IncludeFriday: request.IncludeFriday, FridayDays: fridayDays), ct);
        if (pricing.IsFailure) return new(null, HandleResult(pricing));
        var groups = cards.GroupBy(x => x.Slot, StringComparer.OrdinalIgnoreCase).OrderBy(x => SlotOrder(x.Key))
            .Select(x => new CustomerMealGroupDto(x.Key, SlotNameAr(x.Key), SlotNameEn(x.Key), x.OrderBy(m => m.SortOrder).ToList())).ToList();
        var labels = await LoadTierLabelsAsync(ct);
        return new(new CustomerRestaurantDetailsDto(restaurantId, eligible.NameAr, eligible.NameEn, cards.FirstOrDefault()?.ImageUrl, request.Tier,
            TierLogoUrl(labels, request.Tier), pricing.Value.FinalCustomerPrice, pricing.Value.CurrencyCode, window?.DeliveryWindowStart, window?.DeliveryWindowEnd,
            eligible.MenuPlanVersionsByDate, groups, request.IncludeFriday, fridayDays, dates.Count), null);
    }

    private async Task<List<CustomerMealDetailsDto>> BuildMealCardsAsync(IReadOnlyList<ResolvedMenuAssignment> assignments, Guid restaurantId, CancellationToken ct)
    {
        var mealIds = assignments.Select(x => x.MealId).Distinct().ToList();
        var meals = await _db.Meals.AsNoTracking().Where(x => mealIds.Contains(x.Id) && x.RestaurantId == restaurantId && x.ApprovalStatus == VerificationStatus.Approved && x.IsAvailableForNewOrders).ToListAsync(ct);
        var images = await _db.MealImages.AsNoTracking().Where(x => mealIds.Contains(x.MealId)).OrderByDescending(x => x.IsPrimary).ThenBy(x => x.DisplayOrder).ToListAsync(ct);
        var links = await _db.MealIngredients.AsNoTracking().Where(x => mealIds.Contains(x.MealId))
            .Join(_db.Ingredients.AsNoTracking(), x => x.IngredientId, x => x.Id, (link, ingredient) => new { link.MealId, link.PortionGrams, link.SortOrder, Ingredient = ingredient }).ToListAsync(ct);
        var mealPreferences = await _db.MealFoodPreferences.AsNoTracking().Where(x => mealIds.Contains(x.MealId)).ToListAsync(ct);
        var ingredientIds = links.Select(x => x.Ingredient.Id).Distinct().ToList();
        var ingredientPrefs = (await _db.IngredientFoodPreferences.AsNoTracking().Where(x => ingredientIds.Contains(x.IngredientId)).ToListAsync(ct))
            .Select(x => (x.IngredientId, x.PreferenceCode)).ToList();
        var prefs = await MealPreferenceFilter.LoadAsync(_db, _currentUser.UserId, ct);
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
            result.Add(new CustomerMealDetailsDto(meal.Id, meal.NameAr, meal.NameEn, meal.DescriptionAr, meal.DescriptionEn, assignment.Slot, assignment.SortOrder,
                images.FirstOrDefault(x => x.MealId == meal.Id)?.ImageUrl, meal.Calories, meal.ProteinG, meal.CarbsG, meal.NetCarbsG, meal.FatG, meal.FiberG,
                meal.ServingWeightG, FoodCodeNormalizer.Split(meal.PresentAllergenCodes), FoodCodeNormalizer.Split(meal.MayContainAllergenCodes), tags,
                ingredients.Where(x => !x.Ingredient.IsCore).Select(x => new CustomerIngredientDetailsDto(x.Ingredient.Id, x.Ingredient.NameAr, x.Ingredient.NameEn, x.PortionGrams,
                    x.Ingredient.IsOriginVisibleToCustomers ? x.Ingredient.OriginCountryCode : null,
                    x.Ingredient.IsOriginVisibleToCustomers ? x.Ingredient.SupplierName : null, x.Ingredient.IsLocal, x.Ingredient.IsHalalCertified,
                    FoodCodeNormalizer.Split(x.Ingredient.AllergenCodes), x.Ingredient.IsCore,
                    ToPublicRestaurantImageUrl(x.Ingredient.MealIconUrl))).ToList(), true, null));
        }
        return result;
    }

    private async Task<CustomerFridayAvailabilityDto> ResolveFridayAvailabilityAsync(Guid programId, Guid bundleId,
        RestaurantTier? tier, CancellationToken ct, IReadOnlyCollection<RestaurantTier>? pricedTiers = null)
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
        return new CustomerFridayAvailabilityDto(
            available,
            restaurantCount,
            available ? "FRIDAY_AVAILABLE" : "FRIDAY_UNAVAILABLE",
            available ? "يوم الجمعة متاح ضمن هذه الباقة" : "يوم الجمعة غير متاح لعدم وجود مطاعم تعمل الجمعة ضمن هذه الباقة",
            available ? "Friday is available for this bundle" : "Friday is unavailable because no restaurant in this bundle operates on Friday",
            available ? "INFO" : "DISABLED");
    }

    private async Task<Guid?> ResolveCustomerDeliveryRegionIdAsync(CancellationToken ct)
    {
        var address = await ResolveCustomerDeliveryAddressAsync(ct);
        return address?.RegionId;
    }

    private async Task<CustomerSubscriptionDeliveryAddressDto?> ResolveCustomerDeliveryAddressAsync(CancellationToken ct)
    {
        if (_currentUser.UserId is not Guid userId || userId == Guid.Empty)
            return null;

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

    private async Task<CustomerSubscriptionDeliveryAddressDto?> LoadCustomerDeliveryAddressDtoAsync(
        Guid addressId, Guid userId, CancellationToken ct)
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

    private async Task<List<Guid>> FilterRestaurantIdsByDeliveryRegionAsync(
        IReadOnlyList<Guid> restaurantIds,
        Guid regionId,
        CancellationToken ct)
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

    private bool IsCustomer() => _currentUser.UserType == UserType.Customer && _currentUser.UserId.HasValue;
    private IActionResult ReadinessError(Error error) => error.Type switch
    {
        ErrorType.NotFound => NotFound(new { error = error.Code }),
        ErrorType.Conflict => Conflict(new { error = error.Code }),
        _ => BadRequest(new { error = error.Code }),
    };
    private Task<bool> IsActiveBundleAsync(Guid programId, Guid bundleId, CancellationToken ct)
        => _db.Bundles.AsNoTracking().AnyAsync(x => x.Id == bundleId && x.ProgramId == programId
            && x.IsActive && x.Status == ProgramStatus.Active && x.Program.IsActive && x.Program.Status == ProgramStatus.Active, ct);
    private static RestaurantEligibilityRequest ToEligibility(SubscriptionFlowContext r, Guid? userId, Guid? restaurantId = null)
        => new(r.ProgramId, r.BundleId, r.Tier, r.RegionId, r.StartDate, r.DurationDays, r.IncludeFriday, userId, restaurantId);
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
            .Select(x => new CustomerBundleSlotDto(x.Slot, SlotNameAr(x.Slot), SlotNameEn(x.Slot), x.Count))
            .ToList();
    private static IReadOnlyList<CustomerSummarySlotDto> ToSummarySlots(IReadOnlyList<SlotRequirementDto> slots)
        => slots.Where(x => x.Count > 0)
            .Select(x => new CustomerSummarySlotDto(x.Slot, SlotNameAr(x.Slot), SlotNameEn(x.Slot), x.Count))
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
            items.Add(new("DAILY_MEALS",
                $"{MealsPerDayAr(mealsPerDay)} ({namesAr})",
                $"{MealsPerDayEn(mealsPerDay)} ({namesEn})"));
        }
        items.Add(new("RESTAURANTS", RestaurantsAr(restaurantCount), RestaurantsEn(restaurantCount)));
        items.Add(new("SERVICE_DAYS", ServiceDaysAr(serviceDays), ServiceDaysEn(serviceDays)));
        if (mealVariety > 0)
            items.Add(new("MEAL_VARIETY", MealVarietyAr(mealVariety), MealVarietyEn(mealVariety)));
        return new("يشمل اشتراكك", "Your subscription includes", items);
    }
    private static string SlotLabelAr(CustomerSummarySlotDto slot) => slot.Count > 1 ? $"{slot.NameAr} ×{slot.Count}" : slot.NameAr;
    private static string SlotLabelEn(CustomerSummarySlotDto slot) => slot.Count > 1 ? $"{slot.NameEn} ×{slot.Count}" : slot.NameEn;
    private static string MealsPerDayAr(int count) => count switch
    {
        1 => "وجبة واحدة يوميًا",
        2 => "وجبتان يوميًا",
        >= 3 and <= 10 => $"{count} وجبات يوميًا",
        _ => $"{count} وجبة يوميًا",
    };
    private static string MealsPerDayEn(int count) => count == 1 ? "1 meal daily" : $"{count} meals daily";
    private static string RestaurantsAr(int count) => count switch
    {
        1 => "مطعم واحد في هذا التصنيف",
        2 => "مطعمَان في هذا التصنيف",
        >= 3 and <= 10 => $"{count} مطاعم في هذا التصنيف",
        _ => $"{count} مطعمًا في هذا التصنيف",
    };
    private static string RestaurantsEn(int count) => count == 1
        ? "1 restaurant in this classification"
        : $"{count} restaurants in this classification";
    private static string ServiceDaysAr(int count) => count switch
    {
        1 => "يوم خدمة واحد",
        2 => "يومان خدمة",
        >= 3 and <= 10 => $"{count} أيام خدمة",
        _ => $"{count} يوم خدمة",
    };
    private static string ServiceDaysEn(int count) => count == 1 ? "1 service day" : $"{count} service days";
    private static string MealVarietyAr(int count) => count switch
    {
        1 => "وجبة معتمدة واحدة للاختيار",
        2 => "وجبتان معتمدتان للاختيار",
        >= 3 and <= 10 => $"{count} وجبات معتمدة للاختيار",
        _ => $"{count} وجبة معتمدة للاختيار",
    };
    private static string MealVarietyEn(int count) => count == 1
        ? "1 approved meal to choose from"
        : $"{count} approved meals to choose from";
    private static string? ToPublicRestaurantImageUrl(string? value) => CatalogImageStorage.ToContentUrl(value);

    private async Task<Dictionary<Guid, string?>> LoadRestaurantLogoUrlsAsync(
        IReadOnlyCollection<Guid> restaurantIds,
        CancellationToken ct)
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
    private static string DurationNameAr(int days) => days == 6 ? "6 أيام" : days == 12 ? "12 يومًا" : days == 26 ? "26 يومًا" : $"{days} يومًا";
    private static string SlotNameAr(string slot) => slot.ToLowerInvariant() switch { "breakfast" => "الإفطار", "lunch" => "الغداء", "dinner" => "العشاء", "snack" => "سناك", _ => slot };
    private static string SlotNameEn(string slot) => slot.ToLowerInvariant() switch { "breakfast" => "Breakfast", "lunch" => "Lunch", "dinner" => "Dinner", "snack" => "Snack", _ => slot };
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
    private static (string Ar, string En) IngredientCategoryName(string category) => category switch
    {
        "oils" => ("الزيوت", "Oils"),
        "seafood" => ("الأسماك", "Fish"),
        "poultry" => ("الدواجن", "Poultry"),
        "meat" => ("اللحوم", "Meat"),
        "dairy" => ("ألبان وبيض", "Dairy"),
        "produce" => ("خضار وفواكه", "Produce"),
        "grains" => ("حبوب", "Grains"),
        _ => ("أخرى", "Other"),
    };
    private static (string Ar, string En) OriginSource(bool isLocal)
        => isLocal ? ("طازجة محلية", "Local fresh") : ("مستوردة", "Imported");
    private static (string Ar, string En) TierDescription(RestaurantTier tier) => tier switch
    {
        RestaurantTier.Basic => ("مطاعم مختارة بجودة مميزة", "Selected restaurants with quality meals"),
        RestaurantTier.Platinum => ("خيارات أوسع من المطاعم المميزة", "A wider choice of premium restaurants"),
        _ => ("أعلى تصنيف للمطاعم وتجربة مميزة", "Top-tier restaurants and an elevated experience"),
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
    private static string? TierLogoUrl(TierLabelsSettings labels, RestaurantTier tier)
        => CatalogImageStorage.ToContentUrl(tier switch
        {
            RestaurantTier.Basic => labels.BasicLogoStorageKey,
            RestaurantTier.Platinum => labels.PlatinumLogoStorageKey,
            _ => labels.EliteLogoStorageKey,
        });
    private async Task<CustomerRestaurantBrowseDetailsDto?> BuildRestaurantBrowseDetailsAsync(
        Guid programId, Guid bundleId, Guid restaurantId, CancellationToken ct)
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
        var prefs = await MealPreferenceFilter.LoadAsync(_db, _currentUser.UserId, ct);
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
            .Select(group => new CustomerRestaurantBrowseMealGroupDto(
                group.Key,
                SlotNameAr(group.Key),
                SlotNameEn(group.Key),
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
                    .ToList()))
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
                var (nameAr, nameEn) = IngredientCategoryName(group.Key);
                var (sourceAr, sourceEn) = OriginSource(isLocal);
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
        Guid programId, Guid bundleId, Guid restaurantId, Guid mealId, CancellationToken ct)
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
                x.Calories, x.ProteinG, x.CarbsG, x.FatG, x.FiberG, x.PresentAllergenCodes, x.MayContainAllergenCodes, x.NutritionTagCodes,
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
                (link, ingredient) => new { link.SortOrder, ingredient.Id, ingredient.NameAr, ingredient.NameEn, ingredient.Category, ingredient.IsCore, ingredient.MealIconUrl, ingredient.AllergenCodes })
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
        var prefs = await MealPreferenceFilter.LoadAsync(_db, _currentUser.UserId, ct);
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

        return new CustomerRestaurantBrowseMealDetailsDto(
            meal.Id, meal.NameAr, meal.NameEn, meal.DescriptionAr, meal.DescriptionEn,
            ToPublicRestaurantImageUrl(imageUrl), slot, SlotNameAr(slot), SlotNameEn(slot),
            restaurant.Id, restaurant.TradeNameAr, restaurant.TradeNameEn, ToPublicRestaurantImageUrl(logoKey),
            rating, ratingScores.Count, null, null,
            ingredients.Where(x => !x.IsCore).Select(x => new CustomerRestaurantBrowseMealIngredientDto(
                x.Id, x.NameAr, x.NameEn, NormalizeIngredientCategory(x.Category),
                x.IsCore, ToPublicRestaurantImageUrl(x.MealIconUrl))).ToList(),
            new CustomerMealNutritionDto(meal.Calories, meal.ProteinG, meal.CarbsG, meal.FatG, meal.FiberG,
                carbsPercent, proteinPercent, fatPercent),
            allergens);
    }

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
    private static bool IsConfiguredMethod(MyFatoorahPaymentMethod method, bool knet, bool card, bool apple)
    {
        var code = method.Code.Trim().ToUpperInvariant();
        var name = method.NameEn.Trim().ToUpperInvariant();
        if (code == "KN" || name.Contains("KNET")) return knet;
        if (code == "AP" || name.Contains("APPLE PAY")) return apple;
        if (code == "VM" || name.Contains("VISA/MASTER") || name.Contains("VISA / MASTER")) return card;
        return false;
    }
    private sealed record ResolvedRestaurantDetails(CustomerRestaurantDetailsDto? Value, IActionResult? ErrorResult);
    private sealed record DurationPricingRow(int DurationDays, int FridayDays, int TotalServiceDays);
    private sealed record TierPricingRow(CustomerSubscriptionCatalogItemDto Catalog, decimal? TotalPrice, decimal? DailyPrice);
}

public sealed record SubscriptionTypeOptionDto(
    string Code,
    string NameAr,
    string NameEn,
    string DescriptionAr,
    string DescriptionEn,
    int MaxMembers,
    bool IsAvailable,
    bool MembersConfiguredAfterPayment,
    string? DisabledReason = null,
    bool RequiresConversion = false,
    string? ConversionEndpoint = null);
public sealed record CustomerProgramCardDto(Guid ProgramId, string Code, string? ShortCode, string NameAr, string NameEn,
    string? DescriptionAr, string? DescriptionEn, IReadOnlyList<string> KeywordsAr, IReadOnlyList<string> KeywordsEn,
    string? ImageUrl, int BundleCount, IReadOnlyList<string> SupportedSlots);
public sealed record CustomerSubscriptionDeliveryAddressDto(
    Guid AddressId,
    Guid RegionId,
    string Label,
    string AddressLine,
    string? Governorate,
    string? Area,
    string? Block,
    string? Street,
    string? Avenue,
    decimal? Latitude,
    decimal? Longitude,
    bool IsDefault);
public sealed record CustomerProgramBundlesDto(
    bool HasDeliveryAddress,
    IReadOnlyList<CustomerBundleCardDto> Bundles);
public sealed record CustomerBundleCardDto(Guid BundleId, Guid ProgramId, string Code, string NameAr, string NameEn, string? Description, string? ImageUrl,
    int MealsPerDay, int BreakfastCount, int LunchCount, int DinnerCount, int SnackCount, IReadOnlyList<SlotRequirementDto> SlotRequirements, int SortOrder,
    string? DescriptionAr, string? DescriptionEn, IReadOnlyList<CustomerBundleSlotDto> Slots, int RestaurantCount,
    IReadOnlyList<CustomerBundleRestaurantDto> Restaurants, int SelectionCount, bool IsMostSelected, CustomerBundleBadgeDto? Badge);
public sealed record CustomerBundleSlotDto(string Code, string NameAr, string NameEn, int Quantity);
public sealed record CustomerBundleRestaurantDto(Guid RestaurantId, string? LogoUrl);
public sealed record CustomerBundleBadgeDto(string Code, string NameAr, string NameEn);
public sealed record CustomerDurationOptionDto(int? DurationDays, string NameAr, string NameEn, int SelectionCount, bool IsPopular,
    CustomerBundleBadgeDto? Badge, bool SupportsFriday, bool IncludeFriday, int? FridayDays, int? TotalServiceDays,
    CustomerFridayOptionDto FridayOption, CustomerFridayAvailabilityDto FridayAvailability,
    bool IsCustom = false, int? MinDays = null, int? MaxDays = null);
public sealed record CustomerFridayOptionDto(string Code, string NameAr, string NameEn, bool DefaultSelected, bool IsEnabled);
public sealed record CustomerFridayAvailabilityDto(bool IsAvailable, int RestaurantCount, string Code, string MessageAr,
    string MessageEn, string DisplayType);
public sealed record CustomerTierOptionDto(RestaurantTier Tier, string NameAr, string NameEn, string? LogoUrl, string DescriptionAr, string DescriptionEn,
    int RestaurantCount, IReadOnlyList<CustomerBundleRestaurantDto> Restaurants, int? DurationDays, decimal? TotalPrice, decimal? DailyPrice,
    decimal? PriceDifference, string? CurrencyCode, int SelectionCount, bool IsRecommended, bool IncludeFriday, int? FridayDays,
    int? TotalServiceDays, CustomerFridayAvailabilityDto FridayAvailability);
public sealed record DurationPriceOptionDto(int DurationDays, string NameAr, string NameEn, decimal TotalPrice, decimal DailyPrice, string CurrencyCode);
public sealed record TierConfigurationDto(RestaurantTier Tier, string NameAr, string NameEn, int RestaurantCount, IReadOnlyList<DurationPriceOptionDto> Durations);
public sealed record SubscriptionConfigurationDto(Guid ProgramId, Guid BundleId, string BundleNameAr, string BundleNameEn, string? Description, int MealsPerDay, IReadOnlyList<SlotRequirementDto> SlotRequirements, IReadOnlyList<TierConfigurationDto> Tiers);
public sealed record SubscriptionSummaryRequest(Guid ProgramId, Guid BundleId, RestaurantTier Tier, int DurationDays, DateOnly? StartDate = null, bool IncludeFriday = false);
public sealed record SubscriptionFlowContext(Guid ProgramId, Guid BundleId, RestaurantTier Tier, int DurationDays, DateOnly StartDate, Guid RegionId, bool IncludeFriday = false);
public sealed record CustomerRestaurantBrowseCardDto(
    Guid RestaurantId, string NameAr, string NameEn, string? LogoUrl, decimal? Rating, int MealCount, int BundleMealCount,
    RestaurantTier Tier, string TierNameAr, string TierNameEn, string? TierLogoUrl, IReadOnlyList<CustomerRestaurantClassificationDto> Classifications);
public sealed record CustomerRestaurantClassificationDto(string? LogoUrl, RestaurantTier Tier);
public sealed record CustomerRestaurantBrowseDetailsDto(
    Guid RestaurantId, string NameAr, string NameEn, string? LogoUrl, string? CoverImageUrl,
    IReadOnlyList<CustomerRestaurantBrowseCoreIngredientDto> CoreIngredients,
    IReadOnlyList<CustomerIngredientOriginDto> IngredientOrigins,
    IReadOnlyList<CustomerRestaurantBrowseMealGroupDto> MealGroups);
public sealed record CustomerRestaurantBrowseCoreIngredientDto(
    Guid IngredientId, string NameAr, string NameEn, string? IconUrl,
    bool IsLocal = false,
    string? OriginCountryCode = null,
    string? OriginCountryNameAr = null,
    string? OriginCountryNameEn = null,
    string? FlagEmoji = null);
public sealed record CustomerIngredientOriginDto(
    string Category, string NameAr, string NameEn, bool IsLocal, string SourceAr, string SourceEn,
    string? OriginCountryCode, string? OriginCountryNameAr, string? OriginCountryNameEn, string? FlagEmoji,
    string? IconUrl = null);
public sealed record CustomerRestaurantBrowseMealGroupDto(
    string Slot, string NameAr, string NameEn, IReadOnlyList<CustomerRestaurantBrowseMealDto> Meals);
public sealed record CustomerRestaurantBrowseMealDto(
    Guid MealId, string NameAr, string NameEn, string? ImageUrl,
    decimal? Calories, decimal? ProteinG, decimal? CarbsG, decimal? FatG,
    IReadOnlyList<CustomerRestaurantBrowseMealIngredientDto> Ingredients);
public sealed record CustomerRestaurantBrowseMealIngredientDto(
    Guid IngredientId, string NameAr, string NameEn, string Category, bool IsCore = false, string? IconUrl = null);
public sealed record CustomerRestaurantBrowseMealDetailsDto(
    Guid MealId, string NameAr, string NameEn, string? DescriptionAr, string? DescriptionEn, string? ImageUrl,
    string Slot, string SlotNameAr, string SlotNameEn,
    Guid RestaurantId, string RestaurantNameAr, string RestaurantNameEn, string? RestaurantLogoUrl,
    decimal? RestaurantRating, int RestaurantRatingCount,
    TimeOnly? DeliveryWindowStart, TimeOnly? DeliveryWindowEnd,
    IReadOnlyList<CustomerRestaurantBrowseMealIngredientDto> Ingredients,
    CustomerMealNutritionDto Nutrition,
    IReadOnlyList<CustomerMealAllergenDto> Allergens);
public sealed record CustomerMealNutritionDto(
    decimal? Calories, decimal? ProteinG, decimal? CarbsG, decimal? FatG, decimal? FiberG,
    int? CarbsPercent, int? ProteinPercent, int? FatPercent);
public sealed record CustomerMealAllergenDto(string Code, string NameAr, string NameEn);
public sealed record CustomerRestaurantCardDto(Guid RestaurantId, string NameAr, string NameEn, string? ImageUrl, RestaurantTier Tier, string? TierLogoUrl, decimal FinalPrice, string CurrencyCode, int MealCount, TimeOnly? DeliveryWindowStart, TimeOnly? DeliveryWindowEnd, IReadOnlyList<EligibleMenuDateDto> MenuPlanVersionsByDate, bool IncludeFriday, int FridayDays, int TotalServiceDays);
public sealed record CustomerMealGroupDto(string Slot, string NameAr, string NameEn, IReadOnlyList<CustomerMealDetailsDto> Meals);
public sealed record CustomerRestaurantDetailsDto(Guid RestaurantId, string NameAr, string NameEn, string? CoverImageUrl, RestaurantTier Tier, string? TierLogoUrl, decimal FinalPrice, string CurrencyCode, TimeOnly? DeliveryWindowStart, TimeOnly? DeliveryWindowEnd, IReadOnlyList<EligibleMenuDateDto> MenuPlanVersionsByDate, IReadOnlyList<CustomerMealGroupDto> MealGroups, bool IncludeFriday, int FridayDays, int TotalServiceDays);
public sealed record CustomerIngredientDetailsDto(Guid IngredientId, string NameAr, string NameEn, decimal PortionGrams, string? OriginCountryCode, string? SupplierName, bool IsLocal, bool IsHalalCertified, IReadOnlyList<string> AllergenCodes, bool IsCore = false, string? IconUrl = null);
public sealed record CustomerMealDetailsDto(Guid MealId, string NameAr, string NameEn, string? DescriptionAr, string? DescriptionEn, string Slot, int SortOrder, string? ImageUrl, decimal? Calories, decimal? ProteinG, decimal? CarbsG, decimal? NetCarbsG, decimal? FatG, decimal? FiberG, decimal? ServingWeightG, IReadOnlyList<string> PresentAllergenCodes, IReadOnlyList<string> MayContainAllergenCodes, IReadOnlyList<string> DietaryPreferenceCodes, IReadOnlyList<CustomerIngredientDetailsDto> Ingredients, bool IsSelectable, string? BlockReason);
public sealed record CustomerSummarySlotDto(string Slot, string NameAr, string NameEn, int Count);
public sealed record CustomerSummaryIncludeDto(string Code, string TextAr, string TextEn);
public sealed record CustomerSummaryIncludesDto(string TitleAr, string TitleEn, IReadOnlyList<CustomerSummaryIncludeDto> Items);
/// <summary>
/// chosenStartDate = يوم اختيار/الدفع من الموبايل.
/// startDate = أول يوم توصيل (= chosenStartDate + 3 أيام، مع تخطي الجمعة إن لزم).
/// restaurantRepetition.limit = أقصى عدد أيام يمكن تكرار نفس المطعم خلال مدة الاشتراك.
/// </summary>
public sealed record RestaurantRepetitionSummaryDto(int Limit, int RestaurantCount, int DurationDays);
public sealed record SubscriptionOrderSummaryDto(Guid ProgramId, string ProgramNameAr, string ProgramNameEn, string? ProgramImageUrl, Guid BundleId, string BundleNameAr, string BundleNameEn, string? ImageUrl, int RestaurantCount, RestaurantTier Tier, string TierNameAr, string TierNameEn, int DurationDays, DateOnly StartDate, DateOnly EndDate, IReadOnlyList<DateOnly> ServiceDates, IReadOnlyList<CustomerSummarySlotDto> SlotRequirements, decimal BasePrice, decimal CampaignDiscount, decimal PromoDiscount, decimal WalletCredit, decimal GatewayFee, decimal FinalPrice, string CurrencyCode, bool TermsAcceptanceRequired, string CheckoutEndpoint, bool IncludeFriday, int FridayDays, int TotalServiceDays, CustomerSummaryIncludesDto Includes, SubscriptionSummaryAddressDto? Address = null, IReadOnlyList<SubscriptionSummaryRestaurantDto>? Restaurants = null, bool CanCheckout = true, string? CheckoutBlockReason = null, DateOnly? ChosenStartDate = null, RestaurantRepetitionSummaryDto? RestaurantRepetition = null);
public sealed record CustomerPaymentMethodDto(int? PaymentMethodId, string Code, string NameAr, string NameEn, string IntegrationType, decimal ServiceCharge, decimal TotalAmount, string CurrencyCode, bool IsAvailable, string? UnavailableReason);
