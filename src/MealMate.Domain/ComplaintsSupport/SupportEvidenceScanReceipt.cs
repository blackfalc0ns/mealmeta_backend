using MealMate.Domain.Common;

namespace MealMate.Domain.ComplaintsSupport;

public sealed class SupportEvidenceScanReceipt : BaseEntity
{
    public Guid UploadFileRecordId { get; set; }
    public string Sha256 { get; set; } = default!;
    public long SizeBytes { get; set; }
    public string ContentType { get; set; } = default!;
    public string Verdict { get; set; } = default!;
    public string Provider { get; set; } = default!;
    public string FailureCode { get; set; } = default!;
    public DateTime ScannedAtUtc { get; set; }
    public int ValidationVersion { get; set; }
}
