namespace MealMate.Application.Features.RestaurantOperations.Availability;

public sealed record CommitmentDemand(
    Guid CustomerUserId,
    Guid? FamilyMemberId,
    Guid RegionId,
    IReadOnlyList<string> DietaryRestrictions,
    DateOnly ServiceDate);

public sealed record RestaurantCapacityDto(
    Guid RestaurantId,
    int DailyLimit,
    Guid RegionId,
    IReadOnlyList<string> SupportedDietaryRestrictions,
    int CurrentOccupancy)
{
    public int RemainingCapacity => Math.Max(0, DailyLimit - CurrentOccupancy);
}

public sealed record CapacityGuaranteeResult(
    bool IsGuaranteed,
    string? FailureReason,
    int AvailablePlatformCapacity,
    int RenewalReserveUnits);

public interface IPlatformCapacityGuaranteeService
{
    CapacityGuaranteeResult EvaluatePlatformCeiling(
        int platformCeiling,
        decimal renewalReservePercent,
        int currentOccupancy,
        int protectedRenewalUnits,
        int requestedUnits);

    bool CanSatisfyDemand(
        IReadOnlyList<CommitmentDemand> demands,
        IReadOnlyList<RestaurantCapacityDto> availableRestaurants,
        out string? failureReason);
}

public sealed class PlatformCapacityGuaranteeService : IPlatformCapacityGuaranteeService
{
    public CapacityGuaranteeResult EvaluatePlatformCeiling(
        int platformCeiling,
        decimal renewalReservePercent,
        int currentOccupancy,
        int protectedRenewalUnits,
        int requestedUnits)
    {
        if (platformCeiling <= 0)
        {
            return new CapacityGuaranteeResult(
                IsGuaranteed: false,
                FailureReason: "Platform capacity ceiling is not configured or zero.",
                AvailablePlatformCapacity: 0,
                RenewalReserveUnits: 0);
        }

        // Calculate flexible renewal reserve: max of percentage minimum and active protected renewal commitments
        var percentReserve = renewalReservePercent > 0
            ? (int)Math.Ceiling(platformCeiling * (renewalReservePercent / 100.0m))
            : 0;

        var effectiveRenewalReserve = Math.Max(percentReserve, protectedRenewalUnits);
        var totalCommitted = currentOccupancy + effectiveRenewalReserve;
        var availableForNewSale = Math.Max(0, platformCeiling - totalCommitted);

        if (requestedUnits > availableForNewSale)
        {
            return new CapacityGuaranteeResult(
                IsGuaranteed: false,
                FailureReason: $"Requested {requestedUnits} unit(s) exceed available platform capacity ({availableForNewSale}) after reserving {effectiveRenewalReserve} unit(s) for renewal protection.",
                AvailablePlatformCapacity: availableForNewSale,
                RenewalReserveUnits: effectiveRenewalReserve);
        }

        return new CapacityGuaranteeResult(
            IsGuaranteed: true,
            FailureReason: null,
            AvailablePlatformCapacity: availableForNewSale,
            RenewalReserveUnits: effectiveRenewalReserve);
    }

    public bool CanSatisfyDemand(
        IReadOnlyList<CommitmentDemand> demands,
        IReadOnlyList<RestaurantCapacityDto> availableRestaurants,
        out string? failureReason)
    {
        failureReason = null;

        if (demands.Count == 0)
            return true;

        // Group demands by date and region
        var demandsByDateAndRegion = demands
            .GroupBy(d => (d.ServiceDate, d.RegionId));

        foreach (var group in demandsByDateAndRegion)
        {
            var (serviceDate, regionId) = group.Key;
            var regionDemands = group.ToList();

            // Find all restaurants operating in this region with capacity
            var regionRestaurants = availableRestaurants
                .Where(r => r.RegionId == regionId && r.RemainingCapacity > 0)
                .Select(r => new MutableRestaurantCapacity(r.RestaurantId, r.RemainingCapacity, r.SupportedDietaryRestrictions))
                .ToList();

            if (regionRestaurants.Count == 0)
            {
                failureReason = $"No active restaurants with available capacity found for region {regionId} on {serviceDate:yyyy-MM-dd}.";
                return false;
            }

            var totalRegionCapacity = regionRestaurants.Sum(r => r.RemainingCapacity);
            if (totalRegionCapacity < regionDemands.Count)
            {
                failureReason = $"Insufficient aggregate capacity in region {regionId} on {serviceDate:yyyy-MM-dd}: required {regionDemands.Count}, available {totalRegionCapacity}.";
                return false;
            }

            // Verify each demand has a matching restaurant satisfying all dietary constraints
            // Order demands with most dietary restrictions first (constrained-first matching)
            var sortedDemands = regionDemands
                .OrderByDescending(d => d.DietaryRestrictions.Count)
                .ToList();

            foreach (var demand in sortedDemands)
            {
                var candidate = regionRestaurants
                    .Where(r => r.RemainingCapacity > 0 &&
                                demand.DietaryRestrictions.All(req => r.SupportedDietaryRestrictions.Contains(req, StringComparer.OrdinalIgnoreCase)))
                    .OrderBy(r => r.RemainingCapacity) // tie-break to pack tighter
                    .FirstOrDefault();

                if (candidate is null)
                {
                    var restrictionStr = demand.DietaryRestrictions.Count > 0
                        ? string.Join(", ", demand.DietaryRestrictions)
                        : "standard";
                    failureReason = $"No restaurant in region {regionId} with available capacity can satisfy dietary restrictions [{restrictionStr}] on {serviceDate:yyyy-MM-dd}.";
                    return false;
                }

                candidate.RemainingCapacity--;
            }
        }

        return true;
    }

    private sealed class MutableRestaurantCapacity
    {
        public Guid RestaurantId { get; }
        public int RemainingCapacity { get; set; }
        public IReadOnlyList<string> SupportedDietaryRestrictions { get; }

        public MutableRestaurantCapacity(Guid restaurantId, int remainingCapacity, IReadOnlyList<string> supportedDietaryRestrictions)
        {
            RestaurantId = restaurantId;
            RemainingCapacity = remainingCapacity;
            SupportedDietaryRestrictions = supportedDietaryRestrictions;
        }
    }
}
