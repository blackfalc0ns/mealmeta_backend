using System;
using MealMate.Domain.Common;

namespace MealMate.Domain.RestaurantOperations;

public sealed class RestaurantEmergencyOverride : AuditableEntity
{
    public Guid RestaurantId { get; set; }
    public DateOnly StartDate { get; set; }
    public DateOnly EndDate { get; set; }
    public int? ReducedCapacity { get; set; }
    public bool IsClosed { get; set; }
    public string Reason { get; set; } = string.Empty;
    public Guid TriggeredByUserId { get; set; }
    public bool IsResolved { get; set; }
    public DateTime? ResolvedAtUtc { get; set; }
    public Guid? ResolvedByUserId { get; set; }
    public string? ResolutionReason { get; set; }
}
