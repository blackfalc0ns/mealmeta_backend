namespace MealMate.Application.Features.CollaborativeCampaigns.Services;

public sealed record RestaurantDailyAvailability(
    Guid RestaurantId,
    DateOnly ServiceDate,
    int AvailableCapacity,
    int TierRank = 1);

public sealed record CampaignReservationAllocationItem(
    DateOnly ServiceDate,
    Guid RestaurantId,
    int Units = 1);

public sealed record CampaignAllocationResult(
    bool IsSuccess,
    IReadOnlyList<CampaignReservationAllocationItem> Allocations,
    string? FailureReason = null)
{
    public static CampaignAllocationResult Success(IReadOnlyList<CampaignReservationAllocationItem> allocations) =>
        new(true, allocations);

    public static CampaignAllocationResult Failure(string reason) =>
        new(false, Array.Empty<CampaignReservationAllocationItem>(), reason);
}

public interface ICampaignFulfillmentAllocator
{
    CampaignAllocationResult Allocate(
        IReadOnlyList<DateOnly> serviceDates,
        IReadOnlyList<RestaurantDailyAvailability> availabilities);
}

public sealed class CampaignFulfillmentAllocator : ICampaignFulfillmentAllocator
{
    public CampaignAllocationResult Allocate(
        IReadOnlyList<DateOnly> serviceDates,
        IReadOnlyList<RestaurantDailyAvailability> availabilities)
    {
        if (serviceDates.Count == 0)
        {
            return CampaignAllocationResult.Success(Array.Empty<CampaignReservationAllocationItem>());
        }

        // Index availabilities by (ServiceDate, RestaurantId)
        var availabilityMap = availabilities
            .GroupBy(a => a.ServiceDate)
            .ToDictionary(
                g => g.Key,
                g => g.ToList());

        var allocatedCounts = new Dictionary<Guid, int>();
        var allocations = new List<CampaignReservationAllocationItem>(serviceDates.Count);

        // Sort dates to ensure deterministic chronological processing
        var orderedDates = serviceDates.OrderBy(d => d).ToList();

        foreach (var date in orderedDates)
        {
            if (!availabilityMap.TryGetValue(date, out var candidatesForDate))
            {
                return CampaignAllocationResult.Failure($"Fulfillment capacity unavailable on date {date:yyyy-MM-dd}.");
            }

            var viableCandidates = candidatesForDate
                .Where(c => c.AvailableCapacity > 0)
                .ToList();

            if (viableCandidates.Count == 0)
            {
                return CampaignAllocationResult.Failure($"Fulfillment capacity unavailable on date {date:yyyy-MM-dd}.");
            }

            // Diversification heuristic:
            // 1. Minimum allocated days so far
            // 2. Best TierRank (ascending, 1 is best)
            // 3. Deterministic Guid string tie-break
            var chosen = viableCandidates
                .OrderBy(c => allocatedCounts.GetValueOrDefault(c.RestaurantId, 0))
                .ThenBy(c => c.TierRank)
                .ThenBy(c => c.RestaurantId.ToString())
                .First();

            allocations.Add(new CampaignReservationAllocationItem(date, chosen.RestaurantId, Units: 1));

            // Decrement remaining capacity in our local tracking copy
            var index = candidatesForDate.IndexOf(chosen);
            candidatesForDate[index] = chosen with { AvailableCapacity = chosen.AvailableCapacity - 1 };

            allocatedCounts[chosen.RestaurantId] = allocatedCounts.GetValueOrDefault(chosen.RestaurantId, 0) + 1;
        }

        return CampaignAllocationResult.Success(allocations);
    }
}
