using MealMate.Domain.Common;

namespace MealMate.Domain.DriverDelivery;

public sealed class DeliveryConfirmationScan : AuditableEntity
{
    public Guid? OrderId { get; set; }

    public Guid? SubscriptionDayId { get; set; }

    public Guid DriverProfileId { get; set; }

    public string BarcodeValue { get; set; } = default!;

    public string? PhotoPath { get; set; }

    public DateTime ScannedAtUtc { get; set; }

    public decimal? Latitude { get; set; }

    public decimal? Longitude { get; set; }
}
