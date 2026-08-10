using MealMate.Domain.Common;
using MealMate.Domain.Enums;

namespace MealMate.Domain.IdentityAccess;

public sealed class RestaurantRegistration : AuditableEntity
{
    public Guid? SubmittedByUserId { get; set; }

    public string? LegalNameAr { get; set; }

    public string? LegalNameEn { get; set; }

    public string? TradeNameAr { get; set; }

    public string? TradeNameEn { get; set; }

    public string? ContactPhone { get; set; }

    public string? ContactEmail { get; set; }

    public string? CommercialRegistrationNumber { get; set; }

    public DateOnly? CommercialRegistrationExpiry { get; set; }

    public string? CommercialLicenseNumber { get; set; }

    public DateOnly? CommercialLicenseExpiry { get; set; }

    public string? FoodLicenseNumber { get; set; }

    public DateOnly? FoodLicenseExpiry { get; set; }

    public string? LegalEntityType { get; set; }

    public string? CompanyCivilId { get; set; }

    public string? Website { get; set; }

    public string? InstagramHandle { get; set; }

    public string? RestaurantCode { get; set; }

    /// <summary>Maps from authorized_signatory_name in the onboarding template.</summary>
    public string? OwnerName { get; set; }

    public string? AuthorizedSignatoryCivilId { get; set; }

    public string? AuthorizedSignatoryTitle { get; set; }

    public string? PrimaryContactName { get; set; }

    public string? PrimaryContactTitle { get; set; }

    public string? PrimaryContactPhone { get; set; }

    public string? PrimaryContactEmail { get; set; }

    public string? BankName { get; set; }

    public string? BankBeneficiaryName { get; set; }

    public string? Iban { get; set; }

    public string? BankAccountNumber { get; set; }

    public string? SwiftCode { get; set; }

    public string? SettlementCurrency { get; set; } = "KWD";

    public string? Address { get; set; }

    public Guid? CountryId { get; set; }

    public Guid? RegionId { get; set; }

    public string? ServiceRegionIdsJson { get; set; }

    public VerificationStatus Status { get; set; } = VerificationStatus.Draft;

    public string? RejectionReason { get; set; }

    public string? ChangeRequestNotes { get; set; }

    public Guid? ReviewedByUserId { get; set; }

    public DateTime? ReviewedAtUtc { get; set; }

    public Guid? ResultingRestaurantId { get; set; }

    public bool? DeliveryByRestaurantConfirmed { get; set; }

    public bool? DataResponsibilityConfirmed { get; set; }

    public DateOnly? SubmissionDate { get; set; }

    public string? RestaurantNotes { get; set; }

    /// <summary>Per-field reviews maintained by admins.</summary>
    public ICollection<RestaurantRegistrationFieldReview> FieldReviews { get; set; }
        = new List<RestaurantRegistrationFieldReview>();
}
