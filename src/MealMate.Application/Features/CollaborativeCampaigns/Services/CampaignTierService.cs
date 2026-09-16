using System.Text.Json;
using MealMate.Domain.CollaborativeCampaigns;

namespace MealMate.Application.Features.CollaborativeCampaigns.Services;

public interface ICampaignTierService
{
    CampaignTierSnapshot GenerateTierSnapshot(
        Guid campaignId,
        IEnumerable<(Guid RestaurantId, decimal TierScore, int DedicatedDailyCapacity)> restaurantScores,
        int tierVersion = 1);
}

public sealed class CampaignTierService : ICampaignTierService
{
    public CampaignTierSnapshot GenerateTierSnapshot(
        Guid campaignId,
        IEnumerable<(Guid RestaurantId, decimal TierScore, int DedicatedDailyCapacity)> restaurantScores,
        int tierVersion = 1)
    {
        var sorted = restaurantScores
            .OrderByDescending(x => x.TierScore)
            .ThenByDescending(x => x.DedicatedDailyCapacity)
            .ThenBy(x => x.RestaurantId)
            .ToList();

        var rankItems = new List<CampaignRestaurantRankItem>(sorted.Count);
        for (var i = 0; i < sorted.Count; i++)
        {
            var item = sorted[i];
            rankItems.Add(new CampaignRestaurantRankItem(
                item.RestaurantId,
                item.TierScore,
                Rank: i + 1,
                item.DedicatedDailyCapacity));
        }

        return new CampaignTierSnapshot
        {
            CampaignId = campaignId,
            TierVersion = tierVersion,
            CapturedAtUtc = DateTime.UtcNow,
            RankingsJson = JsonSerializer.Serialize(rankItems)
        };
    }
}
