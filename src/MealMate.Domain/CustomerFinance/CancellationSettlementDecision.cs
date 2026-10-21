using MealMate.Domain.Common;

namespace MealMate.Domain.CustomerFinance;

/// <summary>
/// F06.1 & F06.4:
/// Immutable settlement decision snapshot produced upon confirmation.
/// Consumed by background financial outbox / ledger processor without locking operational aggregates.
/// </summary>
public sealed class CancellationSettlementDecision : BaseEntity
{
    public Guid QuoteId { get; set; }
    public Guid SubscriptionId { get; set; }
    public Guid ConfirmedBy { get; set; }
    public DateTime ConfirmedAtUtc { get; set; } = DateTime.UtcNow;
    public string Reason { get; set; } = "CustomerRequested";
    public string FinalDaySetHash { get; set; } = "";
    public string OperationId { get; set; } = "";
    public string FinancialState { get; set; } = "PendingPosting";

    public decimal ListUnused { get; set; }
    public decimal NetUnused { get; set; }
    public decimal RepriceCalculated { get; set; }
    public decimal RepriceApplied { get; set; }
    public decimal RepriceUnapplied { get; set; }
    public decimal CancelFeeCalculated { get; set; }
    public decimal CancelFeeApplied { get; set; }
    public decimal OtherFees { get; set; }
    public decimal RefundDue { get; set; }
    public bool IsZeroRefund { get; set; }

    public decimal ExecutedRepriceShare { get; set; }
    public decimal LockedRepriceShare { get; set; }

    public string? SourceAllocationsJson { get; set; }
    public string? JournalRef { get; set; }
}
