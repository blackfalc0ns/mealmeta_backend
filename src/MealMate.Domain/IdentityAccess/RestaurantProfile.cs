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

    public VerificationStatus VerificationStatus { get; set; } = VerificationStatus.Draft;

    public bool IsOperationallyActive { get; set; }

    public DateTime? ActivatedAtUtc { get; set; }

    public string? ContactPhone { get; set; }

    public string? ContactEmail { get; set; }
}
