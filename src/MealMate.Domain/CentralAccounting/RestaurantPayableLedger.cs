using MealMate.Domain.Common;
using MealMate.Domain.ValueObjects;

namespace MealMate.Domain.CentralAccounting;

public sealed class RestaurantPayableLedger : BaseEntity
{
    public Guid RestaurantId { get; set; }

    public Guid? SubscriptionDayId { get; set; }

    public Money GrossAmount { get; set; } = default!;

    public Money CommissionAmount { get; set; } = default!;

    public Money NetAmount { get; set; } = default!;

    public Money? CampaignContribution { get; set; }

    public DateTime RecognizedAtUtc { get; set; }
}
