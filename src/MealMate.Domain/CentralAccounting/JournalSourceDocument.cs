using MealMate.Domain.Common;

namespace MealMate.Domain.CentralAccounting;

/// <summary>Approved human-entered source document; never a directly posted journal.</summary>
public sealed class JournalSourceDocument : BaseEntity
{
    public string DocumentNumber { get; set; } = default!;
    public JournalSourceDocumentType Type { get; set; }
    public JournalSourceDocumentStatus Status { get; set; } = JournalSourceDocumentStatus.Draft;
    public Guid LegalEntityId { get; set; }
    public DateOnly BusinessDate { get; set; }
    public string CurrencyCode { get; set; } = "KWD";
    public string Reason { get; set; } = default!;
    public string EvidenceReference { get; set; } = default!;
    public string ContentJson { get; set; } = default!;
    public string ContentFingerprint { get; set; } = default!;
    public string? SubmittedFingerprint { get; set; }
    public Guid CreatedByUserId { get; set; }
    public DateTime CreatedAtUtc { get; set; }
    public DateTime? SubmittedAtUtc { get; set; }
    public Guid? SubmittedByUserId { get; set; }
    public DateTime? ApprovedAtUtc { get; set; }
    public Guid? ApprovedByUserId { get; set; }
    public DateTime? RejectedAtUtc { get; set; }
    public Guid? RejectedByUserId { get; set; }
    public string? RejectionReason { get; set; }
    public Guid? OriginalJournalEntryId { get; set; }
    public long RowVersion { get; set; }
    public Guid? PostedJournalEntryId { get; set; }
}
