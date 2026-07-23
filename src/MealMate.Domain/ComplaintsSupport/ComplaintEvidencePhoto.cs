using MealMate.Domain.Common;

namespace MealMate.Domain.ComplaintsSupport;

public sealed class ComplaintEvidencePhoto : AuditableEntity
{
    public Guid ComplaintId { get; set; }

    public Complaint Complaint { get; set; } = default!;

    public string StoragePath { get; set; } = default!;

    public string ContentType { get; set; } = default!;

    public DateTime UploadedAtUtc { get; set; }
}
