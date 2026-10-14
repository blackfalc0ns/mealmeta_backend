using MealMate.Domain.Common;
using MealMate.Domain.ValueObjects;

namespace MealMate.Domain.ProgramsSubscriptions;

public enum PromoCodeRedemptionStatus
{
    Held = 1,
    Committed = 2,
    Released = 3,
}

/// <summary>Durable checkout use of an admin promo code, including its resolved rule and discount.</summary>
public sealed class PromoCodeRedemption : BaseEntity
{
    public Guid PromoCodeId { get; set; }
    public PromoCode PromoCode { get; set; } = default!;
    public Guid CustomerId { get; set; }
    public Guid SubscriptionId { get; set; }
    public PromoCodeRedemptionStatus Status { get; private set; } = PromoCodeRedemptionStatus.Held;
    public Money DiscountAppliedAmount { get; set; } = default!;
    public Money PlatformFundedAmount { get; set; } = default!;
    public Money RestaurantFundedAmount { get; set; } = default!;
    public decimal? DiscountPercentSnapshot { get; set; }
    public decimal? DiscountAmountSnapshot { get; set; }
    public string TargetingSnapshotJson { get; set; } = "{}";
    public DateTime HeldAtUtc { get; set; }
    public DateTime? CommittedAtUtc { get; private set; }
    public DateTime? ReleasedAtUtc { get; private set; }
    public string? ReleaseReason { get; private set; }

    public bool Commit(DateTime atUtc)
    {
        if (Status == PromoCodeRedemptionStatus.Committed) return false;
        if (Status != PromoCodeRedemptionStatus.Held) return false;
        Status = PromoCodeRedemptionStatus.Committed;
        CommittedAtUtc = atUtc;
        return true;
    }

    public bool Release(DateTime atUtc, string reason)
    {
        if (Status == PromoCodeRedemptionStatus.Released) return false;
        Status = PromoCodeRedemptionStatus.Released;
        ReleasedAtUtc = atUtc;
        ReleaseReason = string.IsNullOrWhiteSpace(reason) ? "checkout_released" : reason.Trim();
        return true;
    }

    public bool Rehold(DateTime atUtc, Money applied)
    {
        if (Status != PromoCodeRedemptionStatus.Released) return false;
        Status = PromoCodeRedemptionStatus.Held;
        HeldAtUtc = atUtc;
        ReleasedAtUtc = null;
        ReleaseReason = null;
        DiscountAppliedAmount = applied;
        PlatformFundedAmount = applied;
        return true;
    }
}
