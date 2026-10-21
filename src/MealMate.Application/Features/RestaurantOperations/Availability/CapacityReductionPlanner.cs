namespace MealMate.Application.Features.RestaurantOperations.Availability;

public sealed record DateOccupancy(DateOnly Date, int CommittedUnits);

public sealed record ReductionSafetyPlan(
    bool CanApplyImmediately,
    DateOnly FirstSafeDate,
    IReadOnlyList<DateOccupancy> OverCapacityDates,
    string? Message);

/// <summary>
/// Evaluates restaurant capacity reduction requests against forward commitments
/// to identify the earliest safe date on which no existing customer rights are compromised.
/// </summary>
public static class CapacityReductionPlanner
{
    public static ReductionSafetyPlan EvaluateReduction(
        int requestedLimit,
        DateOnly requestedEffectiveDate,
        IReadOnlyList<DateOccupancy> upcomingOccupancies)
    {
        if (requestedLimit <= 0)
            throw new ArgumentOutOfRangeException(nameof(requestedLimit), "Requested limit must be positive.");

        var overCapacityDates = upcomingOccupancies
            .Where(o => o.Date >= requestedEffectiveDate && o.CommittedUnits > requestedLimit)
            .OrderBy(o => o.Date)
            .ToList();

        if (overCapacityDates.Count == 0)
        {
            return new ReductionSafetyPlan(
                CanApplyImmediately: true,
                FirstSafeDate: requestedEffectiveDate,
                OverCapacityDates: Array.Empty<DateOccupancy>(),
                Message: "Capacity reduction is safe to apply on the requested effective date.");
        }

        var lastOverCapacityDate = overCapacityDates.Max(o => o.Date);
        var firstSafeDate = lastOverCapacityDate.AddDays(1);

        return new ReductionSafetyPlan(
            CanApplyImmediately: false,
            FirstSafeDate: firstSafeDate,
            OverCapacityDates: overCapacityDates,
            Message: $"Cannot apply on {requestedEffectiveDate:yyyy-MM-dd}: {overCapacityDates.Count} date(s) exceed {requestedLimit} boxes. Earliest safe date is {firstSafeDate:yyyy-MM-dd}.");
    }
}
