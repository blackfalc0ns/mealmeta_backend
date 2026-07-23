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

    public VerificationStatus Status { get; set; } = VerificationStatus.Submitted;

    public DateTime? EffectiveAtUtc { get; set; }
}
