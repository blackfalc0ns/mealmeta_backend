using MealMate.Domain.Common;
using MealMate.Domain.Enums;

namespace MealMate.Domain.IdentityAccess;

/// <summary>
/// Pending restaurant profile change proposed after initial approval.
/// Payload is JSON matching <see cref="RestaurantSettingsDraftPayload"/> shape in Application layer.
/// </summary>
public sealed class RestaurantSettingsChangeRequest : AuditableEntity
{
    public Guid RestaurantId { get; set; }

    public Guid RegistrationId { get; set; }

    public VerificationStatus Status { get; set; } = VerificationStatus.Draft;

    /// <summary>Serialized draft fields (registration snapshot).</summary>
    public string PayloadJson { get; set; } = default!;

    public string? AdminNotes { get; set; }

    public DateTime? SubmittedAtUtc { get; set; }

    public DateTime? ReviewedAtUtc { get; set; }

    public Guid? ReviewedByUserId { get; set; }
}
