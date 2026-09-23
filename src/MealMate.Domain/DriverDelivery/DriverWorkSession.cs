using MealMate.Domain.Common;

namespace MealMate.Domain.DriverDelivery;

public sealed class DriverWorkSession : AuditableEntity
{
    public Guid DriverProfileId { get; set; }

    public Guid? RestaurantId { get; set; }

    public DateTime StartedAtUtc { get; set; }

    public DateTime? EndedAtUtc { get; set; }

    public double? StartLatitude { get; set; }

    public double? StartLongitude { get; set; }

    public double? EndLatitude { get; set; }

    public double? EndLongitude { get; set; }

    public double TotalDistanceKm { get; set; }

    public int TotalAssignedOrders { get; set; }

    public int CompletedOrdersCount { get; set; }

    public int InDeliveryOrdersCount { get; set; }

    public int FailedOrdersCount { get; set; }

    public int DailyTargetOrders { get; set; } = 8;

    public double? AverageDeliveryMinutes { get; set; }

    public double? OnTimeRatePercentage { get; set; }

    public string Status { get; set; } = "Active"; // Active, Ended, Cancelled

    public string? ShiftLabel { get; set; }

    public string? Notes { get; set; }
}
