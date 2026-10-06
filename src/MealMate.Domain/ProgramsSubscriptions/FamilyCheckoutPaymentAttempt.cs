using MealMate.Domain.Common;

namespace MealMate.Domain.ProgramsSubscriptions;

/// <summary>
/// One gateway payment attempt for a family checkout batch. A payment link
/// alone is not proof of success: only attempts with a verified gateway result
/// may activate a batch. The unique gateway payment index prevents the same
/// payment from being attached to two batches.
/// </summary>
public sealed class FamilyCheckoutPaymentAttempt : BaseEntity
{
    public Guid BatchId { get; set; }
    public FamilyCheckoutBatch Batch { get; set; } = default!;

    /// <summary>Retry scope key inside the batch (e.g. a sequence or request id).</summary>
    public string AttemptKey { get; set; } = default!;

    public string? GatewayInvoiceId { get; set; }
    public string? GatewayPaymentId { get; set; }

    /// <summary>R05: full gateway link so a same-key retry can reuse it verbatim.</summary>
    public string? GatewayPaymentUrl { get; set; }

    public DateTime RequestedAtUtc { get; set; }
    public DateTime? VerifiedAtUtc { get; set; }
    public string? ResultCode { get; set; }

    /// <summary>R05: optimistic token protecting single-writer link attachment.</summary>
    public long Version { get; set; } = 1;

    public DateTime CreatedAtUtc { get; set; }
}
