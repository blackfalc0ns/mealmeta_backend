using MealMate.Application.Common.Abstractions;
using Microsoft.EntityFrameworkCore;

namespace MealMate.Application.Features.RestaurantOperations.Availability;

/// <summary>
/// The single capacity-accounting contract: one unit per live commitment identity
/// (SubscriptionId, ServiceDate) at the restaurant that identity currently sits at.
/// Booking eligibility, capacity transfer, admin reassignment, and availability
/// display must all read through this reader so no path keeps a private formula
/// (reconciliation gaps G07/G41). Liveness rules mirror the confirmed unit rule:
/// one unit per member/subscription/service-date, regardless of meals in the bundle.
/// </summary>
public interface IRestaurantCapacityReader
{
    Task<IReadOnlyDictionary<(Guid RestaurantId, DateOnly ServiceDate), RestaurantCapacityUnits>> ReadAsync(
        IReadOnlyCollection<(Guid RestaurantId, DateOnly ServiceDate)> keys,
        CancellationToken cancellationToken = default);
}

/// <summary>
/// Capacity numbers for one restaurant/service-date. <see cref="LimitBoxes"/> is null
/// when no Base/DateOverride row configures a limit — callers must treat unknown as
/// unsellable, never as zero or as an available default.
/// </summary>
public sealed record RestaurantCapacityUnits(
    Guid RestaurantId,
    DateOnly ServiceDate,
    int? LimitBoxes,
    int TotalCommittedUnits,
    int DayUnits,
    int HoldUnits,
    int DayOnlyUnits,
    int HoldOnlyUnits,
    int LivePendingUnits,
    int PaymentConfirmedUnits,
    int ConfirmedHoldUnits)
{
    /// <summary>True when no Base/DateOverride row configures a limit for this key.</summary>
    public bool LimitUnknown => LimitBoxes is null;
}
