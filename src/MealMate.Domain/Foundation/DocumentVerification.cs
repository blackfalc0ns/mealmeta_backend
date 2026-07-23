using MealMate.Domain.Common;
using MealMate.Domain.Enums;

namespace MealMate.Domain.Foundation;

public sealed class DocumentVerification : AuditableEntity
{
    public Guid DocumentId { get; set; }

    public Document Document { get; set; } = default!;

    public VerificationStatus Status { get; set; } = VerificationStatus.Submitted;

    public Guid? ReviewedByUserId { get; set; }

    public DateTime? ReviewedAtUtc { get; set; }

    public string? Reason { get; set; }

    public string? Notes { get; set; }
}
