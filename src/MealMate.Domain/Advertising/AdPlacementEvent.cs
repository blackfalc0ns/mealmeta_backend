using MealMate.Domain.Advertising;

using MealMate.Domain.Common;

namespace MealMate.Domain.Advertising;

/// <summary>Idempotent analytics event for a placement; the same event id is counted once.</summary>
public sealed class AdPlacementEvent : BaseEntity
{
    public Guid PlacementId { get; set; }

    public string EventId { get; set; } = default!;

    public string EventType { get; set; } = default!; // impression | click

    public DateTime OccurredAtUtc { get; set; }
}
