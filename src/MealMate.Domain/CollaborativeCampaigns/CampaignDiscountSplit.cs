using MealMate.Domain.Common;
using MealMate.Domain.Enums;
using MealMate.Domain.ValueObjects;

namespace MealMate.Domain.CollaborativeCampaigns;

public sealed class CampaignDiscountSplit : AuditableEntity
{
    public Guid CampaignId { get; set; }

    public Campaign Campaign { get; set; } = default!;

    public CampaignFundingMode FundingMode { get; set; }

    public Percentage MealMateShareRate { get; set; } = default!;

    public Percentage RestaurantShareRate { get; set; } = default!;

    public string? Notes { get; set; }
}
