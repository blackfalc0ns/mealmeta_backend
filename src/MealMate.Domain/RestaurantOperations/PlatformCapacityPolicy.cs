using MealMate.Domain.Common;

namespace MealMate.Domain.RestaurantOperations;

/// <summary>
/// Defines the overall platform sales capacity ceiling and renewal reserve parameters.
/// Controlled exclusively by administrators.
/// </summary>
public sealed class PlatformCapacityPolicy : AuditableEntity
{
    /// <summary>
    /// The maximum number of service units the platform is permitted to sell for any single operational day.
    /// </summary>
    public int PlatformDailyBoxCeiling { get; set; }

    /// <summary>
    /// The minimum percentage of capacity reserved to guarantee renewal priority for existing subscribers (default 15%).
    /// </summary>
    public decimal RenewalReserveMinimumPercent { get; set; } = 15.0m;

    public long Version { get; set; } = 1;

    public DateOnly EffectiveFromDate { get; set; }

    public bool IsActive { get; set; } = true;

    public string? Reason { get; set; }

    public int CalculateRenewalReserveUnits()
    {
        if (RenewalReserveMinimumPercent <= 0)
            return 0;

        return (int)Math.Ceiling(PlatformDailyBoxCeiling * (RenewalReserveMinimumPercent / 100.0m));
    }

    public static Result<PlatformCapacityPolicy> Create(
        int platformDailyBoxCeiling,
        decimal renewalReserveMinimumPercent = 15.0m,
        DateOnly? effectiveFromDate = null,
        long version = 1,
        string? reason = null)
    {
        if (platformDailyBoxCeiling <= 0)
            return Result<PlatformCapacityPolicy>.Failure("Platform daily box ceiling must be a positive integer.");

        if (renewalReserveMinimumPercent < 0 || renewalReserveMinimumPercent > 100)
            return Result<PlatformCapacityPolicy>.Failure("Renewal reserve percent must be between 0 and 100.");

        return Result<PlatformCapacityPolicy>.Success(new PlatformCapacityPolicy
        {
            PlatformDailyBoxCeiling = platformDailyBoxCeiling,
            RenewalReserveMinimumPercent = renewalReserveMinimumPercent,
            EffectiveFromDate = effectiveFromDate ?? DateOnly.FromDateTime(DateTime.UtcNow),
            Version = version,
            IsActive = true,
            Reason = reason
        });
    }
}
