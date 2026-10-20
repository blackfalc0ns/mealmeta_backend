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

    /// <summary>
    /// The saved customer quote this session was derived from. The quote row is the immutable
    /// price/terms snapshot (reference, discount, final, currency, program, bundle, duration,
    /// service dates, policy and campaign versions); this session additionally pins the amount
    /// and versions it will charge. Null for the legacy client-dated flow.
    /// </summary>
    public Guid? CustomerQuoteId { get; set; }

    /// <summary>Server-computed amount the customer must pay; webhook callbacks are verified against this.</summary>
    public decimal? FinalAmount { get; set; }

    public string? CurrencyCode { get; set; }

    /// <summary>Approved pricing policy version this quote was computed under.</summary>
    public Guid? PricingPolicyId { get; set; }

    public int? PricingPolicyVersion { get; set; }

    /// <summary>Campaign snapshot version captured when the quote was issued.</summary>
    public int? CampaignSnapshotVersion { get; set; }

    /// <summary>Durable provider attempt and initiation journal; never populated from mobile payment claims.</summary>
    public string? PaymentId { get; set; }
    public string? PaymentUrl { get; set; }
    public string? PaymentIdempotencyKey { get; set; }
    public string? PaymentRequestHash { get; set; }
    public string? PaymentCallbackUrl { get; set; }
    public string? PaymentErrorUrl { get; set; }
    public DateTime? PaymentInitializationStartedAtUtc { get; set; }
    public string? PaymentInitializationErrorCode { get; set; }
    public bool PaymentWasCancelled { get; set; }
    /// <summary>Prevents a failed/expired reservation from releasing another session's capacity on replay.</summary>
    public DateTime? ReservationsReleasedAtUtc { get; set; }

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
