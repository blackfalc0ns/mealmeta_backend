using MealMate.Domain.Common;
using MealMate.Domain.Enums;

namespace MealMate.Domain.IdentityAccess;

public sealed class DriverProfile : AuditableEntity
{
    public Guid UserId { get; set; }

    public Guid? RestaurantId { get; set; }

    public string FullNameAr { get; set; } = default!;

    public string FullNameEn { get; set; } = default!;

    public string? NationalId { get; set; }

    public string? LicenseNumber { get; set; }

    public string? VehiclePlate { get; set; }

    public VerificationStatus VerificationStatus { get; set; } = VerificationStatus.Draft;

    public bool IsAvailable { get; set; }

    public DateTime? ActivatedAtUtc { get; set; }
}
