using MealMate.Domain.Common;
using MealMate.Domain.Enums;

namespace MealMate.Domain.Foundation;

public sealed class Document : AuditableEntity
{
    public DocumentOwnerType OwnerType { get; set; }

    public Guid OwnerId { get; set; }

    public DocumentType DocumentType { get; set; }

    public string FileName { get; set; } = default!;

    public string ContentType { get; set; } = default!;

    public string StoragePath { get; set; } = default!;

    public long SizeBytes { get; set; }

    public string? Checksum { get; set; }

    public DateTime? ExpiresAtUtc { get; set; }

    public ICollection<DocumentVerification> Verifications { get; set; } = new List<DocumentVerification>();
}
