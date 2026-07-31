using MealMate.Domain.Common;
using MealMate.Domain.Enums;

namespace MealMate.Domain.IdentityAccess;

/// <summary>
/// Per-field review for a restaurant registration.
/// Tracks the admin review status of each canonical field.
/// </summary>
public sealed class RestaurantRegistrationFieldReview : AuditableEntity
{
    /// <summary>Foreign key to the parent registration.</summary>
    public Guid RegistrationId { get; set; }

    /// <summary>Parent registration navigation property.</summary>
    public RestaurantRegistration Registration { get; set; } = default!;

    /// <summary>
    /// Canonical field key, e.g. "legalNameAr", "legalNameEn", "tradeNameAr",
    /// "tradeNameEn", "contactPhone", "contactEmail", "commercialRegistrationNumber",
    /// "ownerName", "address", "countryId", "regionId".
    /// </summary>
    public string FieldKey { get; set; } = default!;

    /// <summary>Current verification status for this field.</summary>
    public VerificationStatus Status { get; set; } = VerificationStatus.Submitted;

    /// <summary>Optional reason provided by the reviewer when rejecting.</summary>
    public string? Reason { get; set; }

    /// <summary>Admin user who performed the review.</summary>
    public Guid? ReviewedByUserId { get; set; }

    /// <summary>Timestamp when the review was performed.</summary>
    public DateTime? ReviewedAtUtc { get; set; }
}
