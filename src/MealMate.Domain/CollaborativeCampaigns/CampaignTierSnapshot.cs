using MealMate.Domain.Common;

namespace MealMate.Domain.CollaborativeCampaigns;

public sealed record CampaignRestaurantRankItem(
    Guid RestaurantId,
    decimal TierScore,
    int Rank,
    int DedicatedDailyCapacity);

public sealed class CampaignTierSnapshot : AuditableEntity
{
    public Guid CampaignId { get; set; }

    public Campaign Campaign { get; set; } = default!;

    public int TierVersion { get; set; } = 1;

    public DateTime CapturedAtUtc { get; set; } = DateTime.UtcNow;

    public string RankingsJson { get; set; } = default!;
}
