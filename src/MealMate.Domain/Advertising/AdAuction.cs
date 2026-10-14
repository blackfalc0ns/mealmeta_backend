using MealMate.Domain.Common;

namespace MealMate.Domain.Advertising;

/// <summary>A purchasable advertising surface: a placement slot with dimensions and market scope.</summary>
public sealed class AdSpace : AuditableEntity
{
    /// <summary>Machine-readable slot key the client renders (e.g. offers.hero). Never localized.</summary>
    public string PlacementKey { get; set; } = default!;

    public string NameAr { get; set; } = default!;

    public string NameEn { get; set; } = default!;

    public int WidthPx { get; set; }

    public int HeightPx { get; set; }

    /// <summary>Market/governorate scope identifiers; empty list means all markets.</summary>
    public string MarketIdsJson { get; set; } = "[]";

    public bool IsActive { get; set; } = true;
}

/// <summary>Auction states. Bidding period is separate from the display period.</summary>
public enum AdAuctionStatus
{
    Open = 1,
    Closed = 2,
    AwaitingPayment = 3,
    Awarded = 4,
    Cancelled = 5,
    NoFill = 6
}

/// <summary>Placement (creative) lifecycle, independent of the auction lifecycle.</summary>
public enum AdPlacementStatus
{
    AwaitingCreative = 1,
    UnderReview = 2,
    NeedsChanges = 3,
    Scheduled = 4,
    Active = 5,
    Stopped = 6,
    Ended = 7
}

/// <summary>An auction selling one ad space for one display window.</summary>
public sealed class AdAuction : AuditableEntity
{
    public Guid AdSpaceId { get; set; }

    public AdSpace AdSpace { get; set; } = default!;

    public AdAuctionStatus Status { get; set; } = AdAuctionStatus.Open;

    /// <summary>Bidding window [BidsOpenAtUtc, BidsCloseAtUtc) — server time.</summary>
    public DateTime BidsOpenAtUtc { get; set; }

    public DateTime BidsCloseAtUtc { get; set; }

    /// <summary>The display window the winner purchases (separate from bidding window).</summary>
    public DateTime DisplayStartsAtUtc { get; set; }

    public DateTime DisplayEndsAtUtc { get; set; }

    public Guid PolicyId { get; set; }

    public int PolicyVersion { get; set; }

    /// <summary>Winner, recorded exactly once in the close transaction.</summary>
    public Guid? WinningRestaurantId { get; set; }

    public Guid? WinningBidId { get; set; }

    public decimal? WinningAmount { get; set; }

    public string? CurrencyCode { get; set; }

    public DateTime? ClosedAtUtc { get; set; }

    public ICollection<AdBid> Bids { get; set; } = new List<AdBid>();
}

/// <summary>A restaurant's bid in an auction. Idempotent per (auction, restaurant, idempotency key).</summary>
public sealed class AdBid : AuditableEntity
{
    public Guid AdAuctionId { get; set; }

    public AdAuction AdAuction { get; set; } = default!;

    public Guid RestaurantId { get; set; }

    public decimal Amount { get; set; }

    public string CurrencyCode { get; set; } = default!;

    public string IdempotencyKey { get; set; } = default!;

    public bool IsWinning { get; set; }

    public DateTime AcceptedAtUtc { get; set; }
}

public sealed class AdAuctionNextBidderOffer : BaseEntity
{
    public Guid AdAuctionId { get; set; }
    public Guid AdBidId { get; set; }
    public Guid RestaurantId { get; set; }
    public decimal Amount { get; set; }
    public string CurrencyCode { get; set; } = default!;
    public string Status { get; set; } = "Pending";
    public DateTime ExpiresAtUtc { get; set; }
    public Guid? RespondedByUserId { get; set; }
    public DateTime? RespondedAtUtc { get; set; }
    public string? ResponseReason { get; set; }
}

/// <summary>The paid, reviewed creative placed by an auction winner.</summary>
public sealed class AdPlacement : AuditableEntity
{
    public Guid AdAuctionId { get; set; }

    public AdAuction AdAuction { get; set; } = default!;

    public Guid RestaurantId { get; set; }

    public Guid AdSpaceId { get; set; }

    public AdPlacementStatus Status { get; set; } = AdPlacementStatus.AwaitingCreative;

    /// <summary>Storage key of the uploaded creative (via the existing IFileStorage pipeline).</summary>
    public string? CreativeStorageKey { get; set; }

    public string? TargetType { get; set; }

    public Guid? TargetId { get; set; }

    public DateTime? DisplayStartsAtUtc { get; set; }

    public DateTime? DisplayEndsAtUtc { get; set; }

    public Guid? ReviewedByUserId { get; set; }

    public DateTime? ReviewedAtUtc { get; set; }

    public string? ReviewNotes { get; set; }

    public DateTime? StoppedAtUtc { get; set; }

    public string? StopReason { get; set; }

    public Guid? StoppedByUserId { get; set; }

    public Result SubmitCreative(string storageKey, string targetType, Guid? targetId, DateTime displayStart, DateTime displayEnd)
    {
        if (Status != AdPlacementStatus.AwaitingCreative && Status != AdPlacementStatus.NeedsChanges)
        {
            return Result.Failure($"Cannot submit creative while placement is '{Status}'.");
        }

        if (string.IsNullOrWhiteSpace(storageKey))
        {
            return Result.Failure("Creative storage key is required.");
        }

        if (displayStart >= displayEnd)
        {
            return Result.Failure("Display start must precede display end.");
        }

        CreativeStorageKey = storageKey;
        TargetType = targetType;
        TargetId = targetId;
        DisplayStartsAtUtc = displayStart;
        DisplayEndsAtUtc = displayEnd;
        Status = AdPlacementStatus.UnderReview;
        return Result.Success();
    }

    public Result ApproveCreative(Guid reviewerUserId)
    {
        if (Status != AdPlacementStatus.UnderReview)
        {
            return Result.Failure($"Cannot approve creative while placement is '{Status}'.");
        }

        // Display activates only with confirmed payment, approved creative and a valid display date.
        var paid = AdAuction.Status == AdAuctionStatus.Awarded;
        if (!paid)
        {
            return Result.Failure("Auction is not in Awarded (paid) state; placement cannot be approved.");
        }

        if (!DisplayStartsAtUtc.HasValue || !DisplayEndsAtUtc.HasValue || DisplayEndsAtUtc.Value <= DisplayStartsAtUtc.Value)
        {
            return Result.Failure("Placement needs a valid display window before approval.");
        }

        Status = AdPlacementStatus.Scheduled;
        ReviewedByUserId = reviewerUserId;
        ReviewedAtUtc = DateTime.UtcNow;
        return Result.Success();
    }

    public Result RejectCreative(Guid reviewerUserId, string notes)
    {
        if (Status != AdPlacementStatus.UnderReview)
        {
            return Result.Failure($"Cannot reject creative while placement is '{Status}'.");
        }

        Status = AdPlacementStatus.NeedsChanges;
        ReviewedByUserId = reviewerUserId;
        ReviewedAtUtc = DateTime.UtcNow;
        ReviewNotes = notes;
        return Result.Success();
    }

    public Result Activate(DateTime utcNow)
    {
        if (Status != AdPlacementStatus.Scheduled)
        {
            return Result.Failure($"Cannot activate placement in status '{Status}'.");
        }

        if (AdAuction.Status != AdAuctionStatus.Awarded)
        {
            return Result.Failure("Placement cannot serve without a paid (Awarded) auction.");
        }

        if (DisplayStartsAtUtc.HasValue && DisplayStartsAtUtc.Value > utcNow)
        {
            return Result.Failure("Placement display window has not started.");
        }

        Status = AdPlacementStatus.Active;
        return Result.Success();
    }

    public Result Stop(Guid actorUserId, string reason, DateTime utcNow)
    {
        if (Status is not (AdPlacementStatus.Scheduled or AdPlacementStatus.Active))
        {
            return Result.Failure($"Cannot stop placement in status '{Status}'.");
        }

        if (string.IsNullOrWhiteSpace(reason))
        {
            return Result.Failure("A reason is required to stop a paid placement.");
        }

        Status = AdPlacementStatus.Stopped;
        StoppedAtUtc = utcNow;
        StopReason = reason;
        StoppedByUserId = actorUserId;
        return Result.Success();
    }
}

/// <summary>Payment attempt bound to an auction win; idempotent by provider transaction id.</summary>
public sealed class AdPaymentTransaction : BaseEntity
{
    public Guid AdAuctionId { get; set; }

    public Guid RestaurantId { get; set; }

    public string PaymentProvider { get; set; } = default!;

    public string ProviderTransactionId { get; set; } = default!;

    public decimal Amount { get; set; }

    public string CurrencyCode { get; set; } = default!;

    public string Status { get; set; } = default!; // Succeeded | Failed | PendingRefund | Refunded

    public DateTime InitiatedAtUtc { get; set; }

    public DateTime? CompletedAtUtc { get; set; }
}

/// <summary>Immutable financial ledger line for advertising money movements.</summary>
public sealed class AdFinancialLedgerEntry : BaseEntity
{
    public Guid AdAuctionId { get; set; }

    public Guid RestaurantId { get; set; }

    public string Source { get; set; } = default!; // AdPayment | AdStopSettlement

    public string Action { get; set; } = default!; // Charge | Refund

    public decimal Amount { get; set; }

    public string CurrencyCode { get; set; } = default!;

    public string? ProviderTransactionId { get; set; }

    public string? ExternalRefundIdentifier { get; set; }

    public string? ProviderRefundId { get; set; }

    /// <summary>Pending, Approved or Rejected review state for a refund request.</summary>
    public string? RefundReviewStatus { get; set; }

    public Guid? RefundReviewedByUserId { get; set; }

    public DateTime? RefundReviewedAtUtc { get; set; }

    public string? RefundReviewReason { get; set; }

    public decimal? RefundApprovedAmount { get; set; }

    public Guid? RefundApprovedPolicyId { get; set; }

    public int? RefundApprovedPolicyVersion { get; set; }

    // Compatibility name used by the review workflow; the persisted column keeps
    // the original ApprovedPolicy naming.
    [System.ComponentModel.DataAnnotations.Schema.NotMapped]
    public int? RefundApprovalPolicyVersion
    {
        get => RefundApprovedPolicyVersion;
        set => RefundApprovedPolicyVersion = value;
    }

    public DateTime OccurredAtUtc { get; set; }
}
