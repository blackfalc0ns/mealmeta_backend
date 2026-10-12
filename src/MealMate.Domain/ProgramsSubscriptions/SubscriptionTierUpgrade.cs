using MealMate.Domain.Common;
using MealMate.Domain.Enums;
using MealMate.Domain.ValueObjects;

namespace MealMate.Domain.ProgramsSubscriptions;

public sealed class SubscriptionTierUpgrade : AuditableEntity
{
    public Guid SubscriptionId { get; set; }

    public Subscription Subscription { get; set; } = default!;

    public RestaurantTier FromTier { get; set; }

    public RestaurantTier ToTier { get; set; }

    public Money PriceDifference { get; set; } = default!;

    public string? PaymentId { get; set; }

    public int QuotedRemainingDays { get; set; }

    public Money QuotedTotalDue { get; set; } = default!;

    public string? QuoteJson { get; set; }

    public VerificationStatus Status { get; set; } = VerificationStatus.Submitted;

    public DateTime? EffectiveAtUtc { get; set; }

    public Guid? PayerCustomerUserId { get; set; }
    public decimal? VerifiedPaidAmount { get; set; }
    public string? VerifiedCurrencyCode { get; set; }
    public string? VerifiedInvoiceId { get; set; }
    public DateTime? VerifiedPaidAtUtc { get; set; }
    public DateOnly? EffectiveServiceDate { get; set; }
    public string? EffectiveServiceDatesJson { get; set; }
    public string? RequestIdempotencyKey { get; set; }
    public string? RequestFingerprint { get; set; }
    public string? PaymentCreationState { get; set; }
    public string? PaymentUrl { get; set; }
}
