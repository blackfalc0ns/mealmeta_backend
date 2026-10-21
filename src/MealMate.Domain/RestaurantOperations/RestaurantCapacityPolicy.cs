using MealMate.Domain.Common;

namespace MealMate.Domain.RestaurantOperations;

/// <summary>
/// Defines the single fixed daily box capacity policy for a restaurant across all operational days.
/// Restaurants cannot set arbitrary limits per date; increases apply immediately, while decreases
/// require an approved CapacityReductionRequest.
/// </summary>
public sealed class RestaurantCapacityPolicy : AuditableEntity
{
    public Guid RestaurantId { get; set; }

    /// <summary>
    /// Fixed maximum number of boxes the restaurant commits to prepare on every working day.
    /// </summary>
    public int DailyBoxLimit { get; set; }

    public long Version { get; set; } = 1;

    public DateOnly EffectiveFromDate { get; set; }

    public bool IsActive { get; set; } = true;

    public string? Reason { get; set; }

    public static Result<RestaurantCapacityPolicy> Create(
        Guid restaurantId,
        int dailyBoxLimit,
        DateOnly effectiveFromDate,
        long version = 1,
        string? reason = null)
    {
        if (restaurantId == Guid.Empty)
            return Result<RestaurantCapacityPolicy>.Failure("Restaurant ID is required.");

        if (dailyBoxLimit <= 0)
            return Result<RestaurantCapacityPolicy>.Failure("Daily box limit must be a positive integer.");

        return Result<RestaurantCapacityPolicy>.Success(new RestaurantCapacityPolicy
        {
            RestaurantId = restaurantId,
            DailyBoxLimit = dailyBoxLimit,
            EffectiveFromDate = effectiveFromDate,
            Version = version,
            IsActive = true,
            Reason = reason
        });
    }
}
