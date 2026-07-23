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

    public SubscriptionStatus Status { get; set; } = SubscriptionStatus.Draft;

    public DateOnly StartDate { get; set; }

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

    public Money PaidAmount { get; set; } = default!;

    public string? CorrelationId { get; set; }

    public ICollection<SubscriptionSnapshot> Snapshots { get; set; } = new List<SubscriptionSnapshot>();
}
