using MealMate.Domain.Common;
using MealMate.Domain.Enums;
using MealMate.Domain.ValueObjects;

namespace MealMate.Domain.ProgramsSubscriptions;

public sealed class Subscription : AuditableEntity
{
    public Guid CustomerUserId { get; set; }

    public Guid ProgramId { get; set; }

    public Program Program { get; set; } = default!;

    public Guid BundleId { get; set; }

    public Bundle Bundle { get; set; } = default!;

    public RestaurantTier SelectedTier { get; set; }

    public int DurationDays { get; set; }

    public bool IncludeFriday { get; set; }

    public SubscriptionStatus Status { get; set; } = SubscriptionStatus.Draft;

    /// <summary>Subscription start and first delivery box date. Not the payment day.</summary>
    public DateOnly StartDate { get; set; }

    /// <summary>
    /// The chosen start day of the checkout offer this StartDate was derived from, stored once
    /// at creation. Null for legacy subscriptions and never back-computed from StartDate.
    /// </summary>
    public DateOnly? CheckoutAnchorDate { get; set; }

    public DateOnly EndDate { get; set; }

    public Guid? CountryId { get; set; }

    public Guid? RegionId { get; set; }

    public string? DeliveryAddress { get; set; }

    public Guid? PricingRuleVersionId { get; set; }

    public PricingRuleVersion? PricingRuleVersion { get; set; }

    public Guid? CampaignId { get; set; }

    public Guid? PromoCodeId { get; set; }

    public PromoCode? PromoCode { get; set; }

    public Guid? InfluencerCodeId { get; set; }

    public Guid? PackageContentVersionId { get; set; }

    public PackageContentVersion? PackageContentVersion { get; set; }

    public Money PaidAmount { get; set; } = default!;

    public string? CorrelationId { get; set; }

    public DateTime? LastOperationAtUtc { get; set; }
    public string? PaymentId { get; set; }
    public string? PaymentInvoiceId { get; set; }
    public DateTime? PaymentVerifiedAtUtc { get; set; }
    public int FulfillmentAttemptCount { get; set; }
    public DateTime? LastFulfillmentAttemptUtc { get; set; }
    public string? LastFulfillmentErrorCode { get; set; }
    public DateTime? FulfillmentCompletedAtUtc { get; set; }

    // Customer consent is stored separately from provider billing enrollment. Consent alone never authorizes a charge.
    public DateTime? AutoRenewConsentAtUtc { get; set; }
    public DateTime? AutoRenewConsentWithdrawnAtUtc { get; set; }
    public string? AutoRenewConsentVersion { get; set; }
    public decimal? AutoRenewConsentAmount { get; set; }
    public string? AutoRenewConsentCurrency { get; set; }
    public Guid? AutoRenewConsentActorUserId { get; set; }
    public Guid? AutoRenewConsentWithdrawnByUserId { get; set; }

    public byte[] RowVersion { get; set; } = Array.Empty<byte>();

    public Guid? FamilyGroupId { get; set; }

    public Guid? FamilyMemberId { get; set; }

    public ICollection<SubscriptionSnapshot> Snapshots { get; set; } = new List<SubscriptionSnapshot>();

    public ICollection<SubscriptionOperation> Operations { get; set; } = new List<SubscriptionOperation>();

    public ICollection<DiscountsAndReferralLog> DiscountsAndReferralLogs { get; set; } = new List<DiscountsAndReferralLog>();

    public bool CanTransitionTo(SubscriptionStatus target)
    {
        if (Status == target) return true;

        return Status switch
        {
            SubscriptionStatus.Draft => target is SubscriptionStatus.PendingPayment,
            SubscriptionStatus.PendingPayment => target is SubscriptionStatus.Paid
                                                        or SubscriptionStatus.PaymentFailed
                                                        or SubscriptionStatus.Draft,
            SubscriptionStatus.Paid => target is SubscriptionStatus.Active
                                              or SubscriptionStatus.Scheduled
                                              or SubscriptionStatus.SuspendedForReview,
            SubscriptionStatus.Scheduled => target is SubscriptionStatus.Active
                                                   or SubscriptionStatus.Frozen
                                                   or SubscriptionStatus.Cancelled
                                                   or SubscriptionStatus.Completed,
            SubscriptionStatus.Active => target is SubscriptionStatus.Frozen
                                                or SubscriptionStatus.Cancelled
                                                or SubscriptionStatus.Completed
                                                or SubscriptionStatus.Upgraded
                                                or SubscriptionStatus.Expired,
            SubscriptionStatus.Frozen => target is SubscriptionStatus.Active
                                                or SubscriptionStatus.Scheduled
                                                or SubscriptionStatus.Cancelled,
            SubscriptionStatus.SuspendedForReview => target is SubscriptionStatus.Active
                                                            or SubscriptionStatus.Scheduled
                                                            or SubscriptionStatus.Cancelled,
            SubscriptionStatus.PaymentFailed => target is SubscriptionStatus.PendingPayment
                                                       or SubscriptionStatus.Draft
                                                       or SubscriptionStatus.Cancelled,
            SubscriptionStatus.Upgraded => target is SubscriptionStatus.Active
                                                  or SubscriptionStatus.Scheduled
                                                  or SubscriptionStatus.Cancelled,
            SubscriptionStatus.Completed => false,
            SubscriptionStatus.Cancelled => false,
            SubscriptionStatus.Expired => false,
            _ => false
        };
    }

    public Result TransitionTo(SubscriptionStatus target)
    {
        if (!CanTransitionTo(target))
        {
            return Result.Failure($"INVALID_SUBSCRIPTION_TRANSITION: Cannot transition subscription from {Status} to {target}.");
        }

        Status = target;
        return Result.Success();
    }
}
