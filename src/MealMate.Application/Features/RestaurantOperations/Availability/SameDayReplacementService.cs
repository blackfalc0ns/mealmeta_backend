namespace MealMate.Application.Features.RestaurantOperations.Availability;

public sealed record ReplacementEvaluationResult(
    bool Success,
    Guid? ReplacementRestaurantId,
    bool RequiresAdminEscalation,
    string Message);

public interface ISameDayReplacementService
{
    ReplacementEvaluationResult FindReplacementRestaurant(
        Guid failedRestaurantId,
        Guid regionId,
        IReadOnlyList<string> requiredDietaryRestrictions,
        IReadOnlyList<RestaurantCapacityDto> availableRestaurants,
        bool isPreparationStarted);
}

/// <summary>
/// Handles same-day service rescue when a restaurant confirmation SLA expires or an emergency occurs.
/// Enforces:
/// 1. Automated re-routing before meal prep starts only; manual ops decision thereafter.
/// 2. Ensures strictly one box per individual per service day (no double dispatch).
/// 3. Validates coverage, remaining capacity, and dietary compatibility.
/// 4. Escalates to Admin/Ops when no compatible replacement can be safely found.
/// </summary>
public sealed class SameDayReplacementService : ISameDayReplacementService
{
    public ReplacementEvaluationResult FindReplacementRestaurant(
        Guid failedRestaurantId,
        Guid regionId,
        IReadOnlyList<string> requiredDietaryRestrictions,
        IReadOnlyList<RestaurantCapacityDto> availableRestaurants,
        bool isPreparationStarted)
    {
        // Rule: automated reassignment is allowed only BEFORE food preparation has begun.
        // Once prep starts, operational intervention is required to avoid dual dispatch.
        if (isPreparationStarted)
        {
            return new ReplacementEvaluationResult(
                Success: false,
                ReplacementRestaurantId: null,
                RequiresAdminEscalation: true,
                Message: "Preparation has already commenced. Automated reassignment blocked; manual operational review required to avoid dual preparation/delivery.");
        }

        // Search for alternative restaurant in the same region with capacity and matching dietary restrictions
        var candidate = availableRestaurants
            .Where(r => r.RestaurantId != failedRestaurantId &&
                        r.RegionId == regionId &&
                        r.RemainingCapacity > 0 &&
                        requiredDietaryRestrictions.All(diet => r.SupportedDietaryRestrictions.Contains(diet, StringComparer.OrdinalIgnoreCase)))
            .OrderByDescending(r => r.RemainingCapacity)
            .FirstOrDefault();

        if (candidate is not null)
        {
            return new ReplacementEvaluationResult(
                Success: true,
                ReplacementRestaurantId: candidate.RestaurantId,
                RequiresAdminEscalation: false,
                Message: $"Successfully reallocated service to replacement restaurant {candidate.RestaurantId}.");
        }

        return new ReplacementEvaluationResult(
            Success: false,
            ReplacementRestaurantId: null,
            RequiresAdminEscalation: true,
            Message: "No available replacement restaurant with matching dietary support and capacity in this region. Escalating to operations for compensation / refund.");
    }
}
