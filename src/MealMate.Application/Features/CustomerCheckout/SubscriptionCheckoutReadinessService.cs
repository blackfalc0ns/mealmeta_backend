using MealMate.Application.Common.Abstractions;
using MealMate.Application.Common.Models;
using MealMate.Application.Features.IdentityAccess.Customers;
using MealMate.Application.Features.ProgramsSubscriptions;
using MealMate.Application.Features.ProgramsSubscriptions.Pricing;
using MealMate.Domain.Enums;
using Microsoft.EntityFrameworkCore;

namespace MealMate.Application.Features.CustomerCheckout;

public sealed record SubscriptionReadinessProgramDto(Guid ProgramId, string NameAr, string NameEn);
/// <summary>
/// Address shown on readiness/summary. Governorate/Area/Block are the stored snapshots from
/// address creation (free text, may be Arabic or English); the NameAr/NameEn fields are resolved
/// live from the region catalog by RegionId/BlockRegionId so the client can localize.
/// </summary>
public sealed record SubscriptionSummaryAddressDto(
    Guid Id,
    string Label,
    string AddressLine,
    string? Governorate,
    string? Area,
    string? Block,
    Guid RegionId,
    string? GovernorateNameAr = null,
    string? GovernorateNameEn = null,
    string? AreaNameAr = null,
    string? AreaNameEn = null,
    string? BlockNameAr = null,
    string? BlockNameEn = null);
public sealed record SubscriptionSummaryRestaurantDto(
    Guid RestaurantId, string NameAr, string NameEn, string? LogoUrl, RestaurantTier Tier);
public sealed record SubscriptionProfileGateItemDto(
    string Code, bool IsComplete, string TitleAr, string TitleEn, string ActionScreen, string Endpoint);
public sealed record SubscriptionCheckoutReadinessDto(
    bool IsComplete,
    bool CanOpenSummary,
    bool CanCheckout,
    string NextAction,
    string WarningAr,
    string WarningEn,
    string? CheckoutBlockReason,
    SubscriptionReadinessProgramDto Program,
    SubscriptionSummaryAddressDto? Address,
    int DeliveringRestaurantCount,
    IReadOnlyList<SubscriptionProfileGateItemDto> Items,
    IReadOnlyList<SubscriptionSummaryRestaurantDto> Restaurants,
    Guid? BlockingSubscriptionId = null,
    string? BlockingSubscriptionStatus = null);

public interface ISubscriptionCheckoutReadinessService
{
    Task<Result<SubscriptionCheckoutReadinessDto>> EvaluateAsync(
        Guid customerUserId,
        Guid programId,
        Guid bundleId,
        RestaurantTier tier,
        int durationDays,
        bool includeFriday,
        CancellationToken cancellationToken = default);
}

public static class SubscriptionCheckoutGateCodes
{
    public const string Address = "ADDRESS";
    public const string Allergies = "ALLERGIES";
    public const string DietaryPreferences = "DIETARY_PREFERENCES";
    public const string Summary = "SUMMARY";
    public const string ProfileIncomplete = "subscription_flow.profile_incomplete";
    public const string NoRestaurantsForAddress = "subscription_flow.no_restaurants_for_address";
    public const string ExistingSubscription = "checkout.existing_subscription";

    public static string? ProfileCheckoutBlockCode(SubscriptionCheckoutReadinessDto readiness)
    {
        var address = readiness.Items.FirstOrDefault(x => x.Code == Address);
        if (address is { IsComplete: false }) return "checkout.address_required";
        var allergies = readiness.Items.FirstOrDefault(x => x.Code == Allergies);
        if (allergies is { IsComplete: false }) return "checkout.allergies_required";
        var dietary = readiness.Items.FirstOrDefault(x => x.Code == DietaryPreferences);
        if (dietary is { IsComplete: false }) return "checkout.dietary_preferences_required";
        return null;
    }
}

public sealed class SubscriptionCheckoutReadinessService : ISubscriptionCheckoutReadinessService
{
    private const string WarningAr = "لازم تضيف عنوان توصيل وتأكد الحساسية والتفضيلات الغذائية قبل عرض ملخص الاشتراك.";
    private const string WarningEn = "You must add a delivery address and confirm allergies and dietary preferences before viewing the subscription summary.";

    private readonly IAppDbContext _db;
    private readonly IAllowedSubscriptionDurationService _allowedDurations;
    private readonly IRestaurantTierClassificationService _tiers;
    private readonly ICustomerDeliveryAddressService _deliveryAddresses;
    private readonly ICustomerIndividualSubscriptionGate? _individualGate;

    public SubscriptionCheckoutReadinessService(
        IAppDbContext db,
        IAllowedSubscriptionDurationService allowedDurations,
        IRestaurantTierClassificationService tiers,
        ICustomerDeliveryAddressService deliveryAddresses,
        ICustomerIndividualSubscriptionGate? individualGate = null)
        => (_db, _allowedDurations, _tiers, _deliveryAddresses, _individualGate)
            = (db, allowedDurations, tiers, deliveryAddresses, individualGate);

    public async Task<Result<SubscriptionCheckoutReadinessDto>> EvaluateAsync(
        Guid customerUserId,
        Guid programId,
        Guid bundleId,
        RestaurantTier tier,
        int durationDays,
        bool includeFriday,
        CancellationToken ct = default)
    {
        if (customerUserId == Guid.Empty || programId == Guid.Empty || bundleId == Guid.Empty)
            return Error.Validation("subscription_flow.invalid_context", "Subscription context is incomplete.");

        var allowed = await _allowedDurations.EvaluateAsync(durationDays, includeFriday, ct);
        if (!allowed.IsAllowed)
            return Error.Validation(allowed.ErrorCode ?? "subscription_flow.duration_not_available", allowed.ErrorMessage);

        var program = await _db.Programs.AsNoTracking()
            .Where(x => x.Id == programId && x.IsActive && x.Status == ProgramStatus.Active)
            .Select(x => new { x.Id, x.NameAr, x.NameEn })
            .FirstOrDefaultAsync(ct);
        if (program is null)
            return Error.NotFound("subscription_flow.program_not_found", "Program was not found.");

        var bundleExists = await _db.Bundles.AsNoTracking().AnyAsync(x =>
            x.Id == bundleId && x.ProgramId == programId && x.IsActive && x.Status == ProgramStatus.Active, ct);
        if (!bundleExists)
            return Error.NotFound("subscription_flow.bundle_not_found", "Bundle was not found.");

        var profile = await _db.CustomerProfiles.AsNoTracking()
            .Where(x => x.UserId == customerUserId)
            .Select(x => new { x.AllergiesCompletedAtUtc, x.DietaryPreferencesCompletedAtUtc })
            .FirstOrDefaultAsync(ct);
        if (profile is null)
            return Error.NotFound("auth.user_not_found", "Customer profile not found.");

        var activeAddress = await _deliveryAddresses.ResolveActiveDeliveryAddressAsync(customerUserId, ct);
        var savedAddress = activeAddress is null
            ? null
            : await BuildSummaryAddressAsync(activeAddress, ct);

        var hasAddress = savedAddress is not null;
        var hasAllergies = profile.AllergiesCompletedAtUtc.HasValue;
        var hasDietary = profile.DietaryPreferencesCompletedAtUtc.HasValue;
        var isComplete = hasAddress && hasAllergies && hasDietary;

        var restaurants = Array.Empty<SubscriptionSummaryRestaurantDto>();
        if (savedAddress is not null)
        {
            var classification = await _tiers.ResolveAsync(programId, bundleId, eligibleOnly: true, ct);
            var candidateIds = classification.Assignments
                .Where(x => RestaurantTierAccess.CanSelect(tier, x.Tier))
                .Select(x => x.RestaurantId)
                .Distinct()
                .ToList();
            var coveredIds = candidateIds.Count == 0
                ? []
                : await _db.RestaurantServiceRegions.AsNoTracking()
                    .Where(x => candidateIds.Contains(x.RestaurantId)
                        && x.RegionId == savedAddress.RegionId
                        && x.IsActive)
                    .Select(x => x.RestaurantId)
                    .Distinct()
                    .ToListAsync(ct);
            var tierByRestaurant = classification.Assignments
                .GroupBy(x => x.RestaurantId)
                .ToDictionary(x => x.Key, x => x.First().Tier);
            var profiles = await _db.RestaurantProfiles.AsNoTracking()
                .Where(x => coveredIds.Contains(x.Id))
                .Select(x => new { x.Id, x.TradeNameAr, x.TradeNameEn })
                .ToListAsync(ct);
            restaurants = profiles
                .Select(x => new SubscriptionSummaryRestaurantDto(
                    x.Id, x.TradeNameAr, x.TradeNameEn, null, tierByRestaurant.GetValueOrDefault(x.Id, tier)))
                .ToArray();
        }

        var items = new SubscriptionProfileGateItemDto[]
        {
            new(SubscriptionCheckoutGateCodes.Address, hasAddress, "عنوان التوصيل", "Delivery address",
                SubscriptionCheckoutGateCodes.Address, "GET /api/v1/customers/me/addresses"),
            new(SubscriptionCheckoutGateCodes.Allergies, hasAllergies, "الحساسية", "Allergies",
                SubscriptionCheckoutGateCodes.Allergies, "GET /api/v1/customers/me/food-preferences/allergies"),
            new(SubscriptionCheckoutGateCodes.DietaryPreferences, hasDietary, "التفضيلات الغذائية", "Dietary preferences",
                SubscriptionCheckoutGateCodes.DietaryPreferences, "GET /api/v1/customers/me/food-preferences/dietary"),
        };

        var nextAction = !hasAddress
            ? SubscriptionCheckoutGateCodes.Address
            : !hasDietary
                ? SubscriptionCheckoutGateCodes.DietaryPreferences
                : !hasAllergies
                    ? SubscriptionCheckoutGateCodes.Allergies
                    : SubscriptionCheckoutGateCodes.Summary;

        var canCheckout = isComplete && restaurants.Length > 0;
        var canOpenSummary = isComplete;
        var checkoutBlock = !hasAddress
            ? "checkout.address_required"
            : !hasAllergies
                ? "checkout.allergies_required"
                : !hasDietary
                    ? "checkout.dietary_preferences_required"
                    : canCheckout ? null : SubscriptionCheckoutGateCodes.NoRestaurantsForAddress;

        Guid? blockingSubscriptionId = null;
        string? blockingSubscriptionStatus = null;

        if (_individualGate is not null)
        {
            var blocking = await _individualGate.FindBlockingAsync(customerUserId, ct);
            if (blocking is not null)
            {
                canCheckout = false;
                canOpenSummary = false;
                checkoutBlock = CustomerIndividualSubscriptionGate.ExistingSubscriptionError;
                blockingSubscriptionId = blocking.SubscriptionId;
                blockingSubscriptionStatus = blocking.Status.ToString();
            }
            else if (await _individualGate.HasBlockingFamilyGroupAsync(customerUserId, ct))
            {
                canCheckout = false;
                canOpenSummary = false;
                checkoutBlock = CustomerIndividualSubscriptionGate.ExistingSubscriptionError;
            }
        }

        return new SubscriptionCheckoutReadinessDto(
            isComplete,
            canOpenSummary,
            canCheckout,
            nextAction,
            WarningAr,
            WarningEn,
            checkoutBlock,
            new SubscriptionReadinessProgramDto(program.Id, program.NameAr, program.NameEn),
            savedAddress,
            restaurants.Length,
            items,
            restaurants,
            blockingSubscriptionId,
            blockingSubscriptionStatus);
    }

    /// <summary>
    /// Governorate/Area/Block localized names come from the live region catalog (the same source
    /// the client's address picker uses), not from the stored snapshot text, so both languages
    /// stay correct even after a region is renamed.
    /// </summary>
    private async Task<SubscriptionSummaryAddressDto> BuildSummaryAddressAsync(
        CustomerActiveDeliveryAddress address, CancellationToken ct)
    {
        string? governorateAr = null, governorateEn = null;
        string? areaAr = null, areaEn = null;
        string? blockAr = null, blockEn = null;

        var area = await _db.Regions.AsNoTracking()
            .FirstOrDefaultAsync(x => x.Id == address.RegionId && x.IsActive, ct);
        if (area is not null)
        {
            areaAr = area.NameAr;
            areaEn = area.NameEn;
            if (area.ParentRegionId is Guid governorateId)
            {
                var governorate = await _db.Regions.AsNoTracking()
                    .FirstOrDefaultAsync(x => x.Id == governorateId && x.IsActive && x.Type == RegionType.Governorate, ct);
                if (governorate is not null)
                {
                    governorateAr = governorate.NameAr;
                    governorateEn = governorate.NameEn;
                }
            }
        }

        if (address.BlockRegionId is Guid blockRegionId)
        {
            var block = await _db.Regions.AsNoTracking()
                .FirstOrDefaultAsync(x => x.Id == blockRegionId && x.IsActive && x.Type == RegionType.Block, ct);
            if (block is not null)
            {
                blockAr = block.NameAr;
                blockEn = block.NameEn;
            }
        }

        return new SubscriptionSummaryAddressDto(
            address.Id,
            address.Label,
            address.AddressLine,
            address.Governorate,
            address.Area,
            address.Block,
            address.RegionId,
            governorateAr, governorateEn,
            areaAr, areaEn,
            blockAr, blockEn);
    }
}
