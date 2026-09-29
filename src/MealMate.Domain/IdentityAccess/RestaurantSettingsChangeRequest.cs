using MealMate.Domain.Common;
using MealMate.Domain.Enums;

namespace MealMate.Domain.IdentityAccess;

/// <summary>
/// Pending restaurant change proposed after initial approval.
/// Payload is category-specific JSON matching the proposal shape in Application layer.
/// </summary>
public sealed class RestaurantSettingsChangeRequest : AuditableEntity
{
    public Guid RestaurantId { get; set; }

    public Guid RegistrationId { get; set; }

    public RestaurantChangeCategory Category { get; set; } = RestaurantChangeCategory.Profile;

    public VerificationStatus Status { get; set; } = VerificationStatus.Draft;

    /// <summary>The aggregate version of the restaurant when proposal was created/submitted.</summary>
    public long BaseVersion { get; set; } = 1;

    /// <summary>Revision number of the payload for this request (starts at 1, incremented on correction).</summary>
    public int PayloadVersion { get; set; } = 1;

    /// <summary>Serialized draft fields for the category.</summary>
    public string PayloadJson { get; set; } = default!;

    /// <summary>JSON array of field names changed compared to live snapshot.</summary>
    public string? ChangedFieldsJson { get; set; }

    /// <summary>Append-only audit events of review decisions.</summary>
    public string? DecisionEventsJson { get; set; }

    public string? AdminNotes { get; set; }

    public DateTime? SubmittedAtUtc { get; set; }

    public DateTime? ReviewedAtUtc { get; set; }

    public Guid? ReviewedByUserId { get; set; }
}
