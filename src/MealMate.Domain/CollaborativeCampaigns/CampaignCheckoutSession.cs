using MealMate.Domain.CollaborativeCampaigns.Enums;
using MealMate.Domain.Common;

namespace MealMate.Domain.CollaborativeCampaigns;

public sealed class CampaignCheckoutSession : AuditableEntity
{
    public CampaignCheckoutSession() { }

    public CampaignCheckoutSession(Guid id)
    {
        Id = id;
    }

    public Guid CampaignId { get; set; }

    public Campaign Campaign { get; set; } = default!;

    public Guid BundleId { get; set; }

    public Guid CustomerId { get; set; }

    public CampaignCheckoutSessionStatus Status { get; set; } = CampaignCheckoutSessionStatus.Pending;

    public DateTime ExpiresAtUtc { get; set; }

    public DateTime? ProcessingLeaseExpiresAtUtc { get; set; }

    public Guid? PriceQuoteSnapshotId { get; set; }

    /// <summary>Server-computed amount the customer must pay; webhook callbacks are verified against this.</summary>
    public decimal? FinalAmount { get; set; }

    public string? CurrencyCode { get; set; }

    /// <summary>Approved pricing policy version this quote was computed under.</summary>
    public Guid? PricingPolicyId { get; set; }

    public int? PricingPolicyVersion { get; set; }

    /// <summary>Campaign snapshot version captured when the quote was issued.</summary>
    public int? CampaignSnapshotVersion { get; set; }

    public ICollection<CampaignReservationAllocation> Allocations { get; set; } = new List<CampaignReservationAllocation>();

    public ICollection<CampaignPaymentTransaction> PaymentTransactions { get; set; } = new List<CampaignPaymentTransaction>();

    public Result MarkPaymentPending()
    {
        if (Status != CampaignCheckoutSessionStatus.Pending)
        {
            return Result.Failure($"Cannot transition to PaymentPending from status {Status}.");
        }

        Status = CampaignCheckoutSessionStatus.PaymentPending;
        return Result.Success();
    }

    public Result MarkPaid()
    {
        if (Status != CampaignCheckoutSessionStatus.PaymentPending &&
            Status != CampaignCheckoutSessionStatus.Pending &&
            Status != CampaignCheckoutSessionStatus.Expired)
        {
            return Result.Failure($"Cannot transition to Paid from status {Status}.");
        }

        Status = CampaignCheckoutSessionStatus.Paid;
        return Result.Success();
    }

    public Result MarkCommitting()
    {
        if (Status != CampaignCheckoutSessionStatus.Paid)
        {
            return Result.Failure($"Cannot transition to Committing from status {Status}.");
        }

        Status = CampaignCheckoutSessionStatus.Committing;
        return Result.Success();
    }

    public Result MarkCompleted()
    {
        if (Status != CampaignCheckoutSessionStatus.Committing && Status != CampaignCheckoutSessionStatus.Paid)
        {
            return Result.Failure($"Cannot transition to Completed from status {Status}.");
        }

        Status = CampaignCheckoutSessionStatus.Completed;
        return Result.Success();
    }

    public Result MarkPaymentFailed()
    {
        if (Status != CampaignCheckoutSessionStatus.PaymentPending && Status != CampaignCheckoutSessionStatus.Pending)
        {
            return Result.Failure($"Cannot transition to PaymentFailed from status {Status}.");
        }

        Status = CampaignCheckoutSessionStatus.PaymentFailed;
        return Result.Success();
    }

    public Result MarkExpired(DateTime utcNow)
    {
        if (Status != CampaignCheckoutSessionStatus.Pending && Status != CampaignCheckoutSessionStatus.PaymentPending)
        {
            return Result.Failure($"Cannot expire session in status {Status}.");
        }

        if (ProcessingLeaseExpiresAtUtc.HasValue && ProcessingLeaseExpiresAtUtc.Value > utcNow)
        {
            return Result.Failure("Cannot expire session while a processing lease is active.");
        }

        Status = CampaignCheckoutSessionStatus.Expired;
        return Result.Success();
    }

    public Result MarkRefundPending()
    {
        if (Status != CampaignCheckoutSessionStatus.Paid && Status != CampaignCheckoutSessionStatus.Expired)
        {
            return Result.Failure($"Cannot transition to RefundPending from status {Status}.");
        }

        Status = CampaignCheckoutSessionStatus.RefundPending;
        return Result.Success();
    }

    public Result MarkRefunded()
    {
        if (Status != CampaignCheckoutSessionStatus.RefundPending)
        {
            return Result.Failure($"Cannot transition to Refunded from status {Status}.");
        }

        Status = CampaignCheckoutSessionStatus.Refunded;
        return Result.Success();
    }

    public Result AcquireProcessingLease(TimeSpan leaseDuration, DateTime utcNow)
    {
        if (ProcessingLeaseExpiresAtUtc.HasValue && ProcessingLeaseExpiresAtUtc.Value > utcNow)
        {
            return Result.Failure("Active processing lease already exists.");
        }

        ProcessingLeaseExpiresAtUtc = utcNow.Add(leaseDuration);
        return Result.Success();
    }

    public void ReleaseProcessingLease()
    {
        ProcessingLeaseExpiresAtUtc = null;
    }
}
