using MealMate.Domain.Common;
using MealMate.Domain.ValueObjects;

namespace MealMate.Domain.DriverDelivery;

public sealed class DriverPerformanceStat : AuditableEntity
{
    public Guid DriverProfileId { get; set; }

    public DateOnly PeriodStart { get; set; }

    public DateOnly PeriodEnd { get; set; }

    public int DeliveredCount { get; set; }

    public int FailedCount { get; set; }

    public decimal? AvgRating { get; set; }

    public Percentage? OnTimeRate { get; set; }
}
