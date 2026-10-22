using MealMate.Domain.Common;
using MealMate.Domain.Enums;

namespace MealMate.Domain.IdentityAccess;

public sealed class RestaurantProfile : AuditableEntity
{
    public Guid UserId { get; set; }

    public string LegalNameAr { get; set; } = default!;

    public string LegalNameEn { get; set; } = default!;

    public string TradeNameAr { get; set; } = default!;

    public string TradeNameEn { get; set; } = default!;

    public string? CommercialLicenseNumber { get; set; }

    public Guid? CountryId { get; set; }

    public Guid? PrimaryRegionId { get; set; }

    public decimal? Latitude { get; set; }

    public decimal? Longitude { get; set; }

    public VerificationStatus VerificationStatus { get; set; } = VerificationStatus.Draft;

    public bool IsOperationallyActive { get; set; }

    public bool IsArchived { get; set; }

    public DateTime? ClosedAtUtc { get; set; }

    public DateTime? ActivatedAtUtc { get; set; }

    public string? ContactPhone { get; set; }

    public string? ContactEmail { get; set; }

    /// <summary>Settlement commission percent deducted from restaurant box price.</summary>
    public decimal? CommissionPercent { get; set; }

    /// <summary>Approved restaurant-wide delivery window, shared by every service region.</summary>
    public TimeOnly? DeliveryWindowStart { get; set; }

    public TimeOnly? DeliveryWindowEnd { get; set; }

    public int? PreparationLeadTimeMinutes { get; set; }

    public bool AutoConfirmOrders { get; set; }

    /// <summary>Restaurant-wide busy preference; date-level busy remains on operations APIs.</summary>
    public bool ManualBusyOverride { get; set; }

    /// <summary>The aggregate approved version incremented on each approved proposal.</summary>
    public long ApprovedVersion { get; set; } = 1;
}
