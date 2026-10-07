using MealMate.Domain.Common;

namespace MealMate.Domain.CustomerFinance;

public enum RefundOperationState { Queued, Submitting, Pending, Confirmed, Rejected, NeedsReview }

public sealed class RefundOperation : BaseEntity
{
    public string OperationReference { get; set; } = "";
    public string PaymentProvider { get; set; } = "Payzah";
    public string PaymentId { get; set; } = "";
    public string InvoiceId { get; set; } = "";
    public decimal Amount { get; set; }
    public string Currency { get; set; } = "KWD";
    public string Reason { get; set; } = "";
    public RefundOperationState State { get; set; } = RefundOperationState.Queued;
    public string? ProviderRefundId { get; set; }
    public string? ProviderRefundReference { get; set; }
    public string? Detail { get; set; }
    public DateTime CreatedAtUtc { get; set; }
    public DateTime? SubmittedAtUtc { get; set; }
    public DateTime? ReconciledAtUtc { get; set; }
    public DateTime? ConfirmedAtUtc { get; set; }
    public DateTime? LocalAppliedAtUtc { get; set; }
    public long Version { get; set; } = 1;
}
