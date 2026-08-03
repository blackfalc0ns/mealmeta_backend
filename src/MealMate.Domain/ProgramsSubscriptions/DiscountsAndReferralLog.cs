using MealMate.Domain.Common;
using MealMate.Domain.Enums;
using MealMate.Domain.ValueObjects;

namespace MealMate.Domain.ProgramsSubscriptions;

public sealed class DiscountsAndReferralLog : BaseEntity
{
    public Guid SubscriptionId { get; set; }

    public Subscription Subscription { get; set; } = default!;

    public string DiscountType { get; set; } = default!; // PromoCode | ReferralReward | CampaignDiscount

    public string? Code { get; set; }

    public Money Amount { get; set; } = default!;

    public CampaignFundingMode FundingMode { get; set; }

    public DateTime AppliedAtUtc { get; set; } = DateTime.UtcNow;

    public string? MetadataJson { get; set; }
}
