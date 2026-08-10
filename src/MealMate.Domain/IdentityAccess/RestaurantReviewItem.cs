using MealMate.Domain.Common;
using MealMate.Domain.Enums;

namespace MealMate.Domain.IdentityAccess;

public sealed class RestaurantReviewItem : AuditableEntity
{
    public Guid RestaurantId { get; set; }
    public Guid? RegistrationId { get; set; }
    public string Section { get; set; } = default!;
    public string SubjectType { get; set; } = default!;
    public Guid? SubjectId { get; set; }
    public string? FieldKey { get; set; }
    public string? VersionId { get; set; }
    public VerificationStatus Status { get; set; } = VerificationStatus.Draft;
    public string? Reason { get; set; }
    public Guid? ReviewedByUserId { get; set; }
    public DateTime? ReviewedAtUtc { get; set; }
    public DateTime? SubmittedAtUtc { get; set; }
}
