using MealMate.Application.Common.Abstractions;
using MealMate.Application.Features.IdentityAccess.Customers;
using MealMate.Application.Features.ProgramsSubscriptions;
using MealMate.Application.Features.ProgramsSubscriptions.Pricing;
using MealMate.Domain.Enums;
using Microsoft.EntityFrameworkCore;

namespace MealMate.Application.Features.CustomerCheckout;

public static class IndividualRestaurantAvailabilityStatus
{
    public const string AddressRequired = "address_required";
    public const string SelectionRequired = "selection_required";
    public const string NoRestaurantsForAddress = "no_restaurants_for_address";
    public const string NoRestaurantsForSelection = "no_restaurants_for_selection";
    public const string Available = "available";
}

/// <summary>
/// Single availability verdict for the individual subscription period. Coverage (the address has
/// delivering restaurants) is reported separately from full-period eligibility (menu, capacity,
/// safe meals and pricing hold for every service day), so the client can explain "no restaurant
/// delivers here" differently from "restaurants deliver here but not for this whole period".
/// </summary>
public sealed record IndividualRestaurantAvailabilityDto(
    string Status,
    int CoveredRestaurantCount,
    int EligibleRestaurantCount,
    DateTime CheckedAtUtc,
    DateOnly? EffectiveStartDate,
    string? MessageCode);

/// <summary>Availability verdict plus the full-period eligible rows and their tier classification,
/// so readiness and the contextual browse route render from one evaluation.</summary>
public sealed record IndividualRestaurantAvailabilityReport(
    IndividualRestaurantAvailabilityDto Availability,
    IReadOnlyList<RestaurantEligibilityDto> EligibleRestaurants,
    IReadOnlyDictionary<Guid, RestaurantTier> TierByRestaurant,
    Guid RegionId);

public interface ISubscriptionRestaurantAvailabilityService
{
    Task<IndividualRestaurantAvailabilityReport> EvaluateDetailedAsync(
        Guid customerUserId,
        Guid programId,
        Guid bundleId,
        RestaurantTier tier,
        int durationDays,
        bool includeFriday,
        DateOnly? startDate,
        CancellationToken ct = default);
}

public sealed class SubscriptionRestaurantAvailabilityService : ISubscriptionRestaurantAvailabilityService
{
    private readonly IAppDbContext _db;
    private readonly IDateTimeProvider _clock;
    private readonly ICustomerDeliveryAddressService _deliveryAddresses;
    private readonly IRestaurantEligibilityService _eligibility;
    private readonly IRestaurantTierClassificationService _tiers;

    public SubscriptionRestaurantAvailabilityService(
        IAppDbContext db,
        IDateTimeProvider clock,
        ICustomerDeliveryAddressService deliveryAddresses,
        IRestaurantEligibilityService eligibility,
        IRestaurantTierClassificationService tiers)
        => (_db, _clock, _deliveryAddresses, _eligibility, _tiers)
            = (db, clock, deliveryAddresses, eligibility, tiers);

    public async Task<IndividualRestaurantAvailabilityReport> EvaluateDetailedAsync(
        Guid customerUserId,
        Guid programId,
        Guid bundleId,
        RestaurantTier tier,
        int durationDays,
        bool includeFriday,
        DateOnly? startDate,
        CancellationToken ct = default)
    {
        var checkedAtUtc = _clock.UtcNow;

        if (customerUserId == Guid.Empty || programId == Guid.Empty || bundleId == Guid.Empty || durationDays < 1)
            return Incomplete(IndividualRestaurantAvailabilityStatus.SelectionRequired, "subscription_flow.invalid_context", checkedAtUtc);

        var activeAddress = await _deliveryAddresses.ResolveActiveDeliveryAddressAsync(customerUserId, ct);
        if (activeAddress is null)
            return Incomplete(IndividualRestaurantAvailabilityStatus.AddressRequired, "checkout.address_required", checkedAtUtc);

        // Coverage = an approved, operationally active restaurant offers this program/bundle and
        // covers the customer's region. Pricing, tier, menu, capacity and safe meals are
        // full-period eligibility questions handled by the evaluator below, never coverage.
        var offeringIds = await _db.RestaurantProgramBundleOffers.AsNoTracking()
            .Where(x => x.ProgramId == programId && x.BundleId == bundleId && x.IsActive)
            .Select(x => x.RestaurantId)
            .Distinct()
            .ToListAsync(ct);
        var regionCoveredIds = offeringIds.Count == 0
            ? []
            : await _db.RestaurantServiceRegions.AsNoTracking()
                .Where(x => offeringIds.Contains(x.RestaurantId)
                    && x.RegionId == activeAddress.RegionId
                    && x.IsActive)
                .Select(x => x.RestaurantId)
                .Distinct()
                .ToListAsync(ct);
        var coveredIds = regionCoveredIds.Count == 0
            ? []
            : await _db.RestaurantProfiles.AsNoTracking()
                .Where(x => regionCoveredIds.Contains(x.Id)
                    && x.VerificationStatus == VerificationStatus.Approved
                    && x.IsOperationallyActive)
                .Select(x => x.Id)
                .Distinct()
                .ToListAsync(ct);
        var coveredCount = coveredIds.Count;

        // Tier lookup stays classification-based for display; classification has no vote in coverage.
        var classification = await _tiers.ResolveAsync(programId, bundleId, eligibleOnly: true, ct);
        var tierByRestaurant = classification.Assignments
            .GroupBy(x => x.RestaurantId)
            .ToDictionary(x => x.Key, x => x.First().Tier);

        if (startDate is null || startDate == default)
            return new IndividualRestaurantAvailabilityReport(
                new IndividualRestaurantAvailabilityDto(
                    IndividualRestaurantAvailabilityStatus.SelectionRequired, coveredCount, 0, checkedAtUtc, null,
                    "checkout.start_date_required"),
                [], tierByRestaurant, activeAddress.RegionId);

        var rows = await _eligibility.EvaluateAsync(
            new RestaurantEligibilityRequest(programId, bundleId, tier, activeAddress.RegionId, startDate.Value, durationDays, includeFriday, customerUserId), ct);
        var eligible = rows.Where(x => x.IsEligible)
            .OrderBy(x => x.NameEn).ThenBy(x => x.RestaurantId)
            .ToList();

        var (status, messageCode) = eligible.Count > 0
            ? (IndividualRestaurantAvailabilityStatus.Available, null)
            : coveredCount > 0
                ? (IndividualRestaurantAvailabilityStatus.NoRestaurantsForSelection, SubscriptionCheckoutGateCodes.NoRestaurantsForSelection)
                : (IndividualRestaurantAvailabilityStatus.NoRestaurantsForAddress, SubscriptionCheckoutGateCodes.NoRestaurantsForAddress);

        return new IndividualRestaurantAvailabilityReport(
            new IndividualRestaurantAvailabilityDto(status, coveredCount, eligible.Count, checkedAtUtc, startDate, messageCode),
            eligible,
            tierByRestaurant,
            activeAddress.RegionId);
    }

    private static IndividualRestaurantAvailabilityReport Incomplete(string status, string messageCode, DateTime checkedAtUtc)
        => new(new IndividualRestaurantAvailabilityDto(status, 0, 0, checkedAtUtc, null, messageCode), [],
            new Dictionary<Guid, RestaurantTier>(), Guid.Empty);
}
