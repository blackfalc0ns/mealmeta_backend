using MealMate.Domain.Common;
using MealMate.Domain.Enums;

namespace MealMate.Domain.ProgramsSubscriptions;

/// <summary>
/// One family checkout batch: a frozen financial quote (package, participants,
/// prices, addresses) paid with a single gateway payment. The legacy payment
/// fields on <see cref="FamilyGroup"/> become derived compatibility fields once
/// batches exist; the batch is the financial authority.
/// </summary>
public sealed class FamilyCheckoutBatch : BaseEntity
{
    public Guid FamilyGroupId { get; set; }
    public FamilyGroup FamilyGroup { get; set; } = default!;

    /// <summary>Authenticated customer who reserved and pays for this batch.</summary>
    public Guid PayerCustomerUserId { get; set; }

    /// <summary>Client-supplied retry key; unique per family group.</summary>
    public string IdempotencyKey { get; set; } = default!;

    public FamilyCheckoutBatchStatus Status { get; set; } = FamilyCheckoutBatchStatus.Draft;

    /// <summary>Distinguishes an initial purchase from a separately payable family renewal.</summary>
    public string Purpose { get; set; } = "Initial";

    /// <summary>Renewal intent represented by this batch, when Purpose is Renewal.</summary>
    public Guid? RenewalRequestId { get; set; }

    /// <summary>Optimistic concurrency token for quote versioning.</summary>
    public long Version { get; set; } = 1;

    // Frozen package snapshot (financial version captured at reservation time).
    public Guid? PackageId { get; set; }
    public string? PackageNameAr { get; set; }
    public string? PackageNameEn { get; set; }
    public int? PackageMinMembers { get; set; }
    public int? PackageMaxMembers { get; set; }
    public decimal FamilyDiscountPercent { get; set; }

    public string CurrencyCode { get; set; } = "KWD";
    public decimal Subtotal { get; set; }
    public decimal FamilyDiscountAmount { get; set; }
    public decimal FinalPrice { get; set; }

    public Guid? PaymentAnchorSubscriptionId { get; set; }
    public string? GatewayInvoiceId { get; set; }
    public string? GatewayPaymentId { get; set; }
    public DateTime? ExpiresAtUtc { get; set; }
    public DateTime? PaidAtUtc { get; set; }

    public DateTime CreatedAtUtc { get; set; }

    public ICollection<FamilyCheckoutBatchLine> Lines { get; set; } = new List<FamilyCheckoutBatchLine>();
    public ICollection<FamilyCheckoutPaymentAttempt> PaymentAttempts { get; set; } = new List<FamilyCheckoutPaymentAttempt>();
}
