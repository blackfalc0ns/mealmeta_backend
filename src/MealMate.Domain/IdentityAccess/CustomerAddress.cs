using MealMate.Domain.Common;

namespace MealMate.Domain.IdentityAccess;

/// <summary>A customer-managed saved delivery address.</summary>
public sealed class CustomerAddress : AuditableEntity
{
    public Guid UserId { get; set; }

    public string Label { get; set; } = default!;

    public string AddressLine { get; set; } = default!;

    public Guid? RegionId { get; set; }

    public decimal? Latitude { get; set; }

    public decimal? Longitude { get; set; }

    public string? Notes { get; set; }

    public bool IsDefault { get; set; }

    public bool NotifyOnDelivery { get; set; } = true;
}
