using MealMate.Domain.Common;
using MealMate.Domain.Enums;

namespace MealMate.Domain.DriverDelivery;

public sealed class TripStop : AuditableEntity
{
    public Guid TripId { get; set; }

    public Trip Trip { get; set; } = default!;

    public Guid? DailyOrderId { get; set; }

    public Guid? SubscriptionDayId { get; set; }

    public string OrderCode { get; set; } = default!;

    public string CustomerMaskedId { get; set; } = default!;

    public string? CustomerName { get; set; }

    public string? CustomerPhone { get; set; }

    public string ZoneNameAr { get; set; } = default!;

    public string ZoneNameEn { get; set; } = default!;

    public string MealSummaryAr { get; set; } = default!;

    public string MealSummaryEn { get; set; } = default!;

    public int BoxCount { get; set; } = 1;

    public string SlotLabel { get; set; } = default!;

    public int SequenceNumber { get; set; }

    public TripStopStatus Status { get; set; } = TripStopStatus.Pending;

    public DateTime? DeliveredAtUtc { get; set; }

    public string? FailureReason { get; set; }
}
