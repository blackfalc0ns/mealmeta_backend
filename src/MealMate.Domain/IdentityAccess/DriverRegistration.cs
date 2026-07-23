using MealMate.Domain.Common;
using MealMate.Domain.Enums;

namespace MealMate.Domain.IdentityAccess;

public sealed class DriverRegistration : AuditableEntity
{
    public Guid? SubmittedByUserId { get; set; }

    public Guid? RestaurantId { get; set; }

    public string FullNameAr { get; set; } = default!;

    public string FullNameEn { get; set; } = default!;

    public string Phone { get; set; } = default!;

    public string? Email { get; set; }

    public string? NationalId { get; set; }

    public string? LicenseNumber { get; set; }

    public string? VehiclePlate { get; set; }

    public VerificationStatus Status { get; set; } = VerificationStatus.Submitted;

    public string? RejectionReason { get; set; }

    public string? ChangeRequestNotes { get; set; }

    public Guid? ReviewedByUserId { get; set; }

    public DateTime? ReviewedAtUtc { get; set; }

    public Guid? ResultingDriverProfileId { get; set; }
}
