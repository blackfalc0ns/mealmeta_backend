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

    public string? VehicleType { get; set; }

    public string? VehicleModel { get; set; }

    public DateTime? NationalIdExpiryUtc { get; set; }

    public DateTime? LicenseExpiryUtc { get; set; }

    public DateTime? VehicleRegistrationExpiryUtc { get; set; }

    public DateTime? ContractExpiryUtc { get; set; }

    public Guid? CountryId { get; set; }

    public Guid? RegionId { get; set; }

    public DateTime? DateOfBirthUtc { get; set; }

    public string? Nationality { get; set; }

    public int? VehicleYear { get; set; }

    public string? VehicleColor { get; set; }

    public bool? IsVehicleOwned { get; set; }

    public string? ProfileImageStorageKey { get; set; }

    public string? VehiclePhotoStorageKey { get; set; }

    public VerificationStatus Status { get; set; } = VerificationStatus.Draft;

    public string? RejectionReason { get; set; }

    public string? ChangeRequestNotes { get; set; }

    public Guid? ReviewedByUserId { get; set; }

    public DateTime? ReviewedAtUtc { get; set; }

    public string RegistrationSource { get; set; } = "MobileApp";

    public VerificationStatus RestaurantApprovalStatus { get; set; } = VerificationStatus.Submitted;

    public Guid? RestaurantReviewedByUserId { get; set; }

    public DateTime? RestaurantReviewedAtUtc { get; set; }

    public string? RestaurantReviewNotes { get; set; }

    public VerificationStatus AdminApprovalStatus { get; set; } = VerificationStatus.Submitted;

    public string? AdminReviewNotes { get; set; }

    public Guid? ResultingDriverProfileId { get; set; }
}
