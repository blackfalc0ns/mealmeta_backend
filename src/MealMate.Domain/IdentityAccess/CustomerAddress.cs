using MealMate.Domain.Common;

namespace MealMate.Domain.IdentityAccess;

/// <summary>A customer-managed saved delivery address.</summary>
public sealed class CustomerAddress : AuditableEntity
{
    public Guid UserId { get; set; }

    public string Label { get; set; } = default!;

    public string AddressLine { get; set; } = default!;

    public Guid? RegionId { get; set; }

    /// <summary>Optional catalog block (piece) under RegionId, retained separately from legacy free text.</summary>
    public Guid? BlockRegionId { get; set; }

    public string? Governorate { get; set; }

    public string? Area { get; set; }

    public string? Block { get; set; }

    public string? Street { get; set; }

    /// <summary>Optional Kuwait avenue (جادة), separate from building number.</summary>
    public string? Avenue { get; set; }

    public string? Building { get; set; }

    public string? Unit { get; set; }

    public string? Floor { get; set; }

    public decimal? Latitude { get; set; }

    public decimal? Longitude { get; set; }

    public string? Notes { get; set; }

    public bool IsDefault { get; set; }

    public bool NotifyOnDelivery { get; set; } = true;
}
