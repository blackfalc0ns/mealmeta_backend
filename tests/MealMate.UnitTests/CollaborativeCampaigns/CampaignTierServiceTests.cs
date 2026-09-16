using FluentAssertions;
using System.Text.Json;
using MealMate.Application.Features.CollaborativeCampaigns.Services;
using MealMate.Domain.CollaborativeCampaigns;
using Xunit;

namespace MealMate.UnitTests.CollaborativeCampaigns;

public class CampaignTierServiceTests
{
    private readonly CampaignTierService _tierService = new();

    [Fact]
    public void GenerateTierSnapshot_RanksCorrectlyByTierScoreDescending()
    {
        var campaignId = Guid.NewGuid();
        var r1 = Guid.NewGuid();
        var r2 = Guid.NewGuid();
        var r3 = Guid.NewGuid();

        var input = new List<(Guid RestaurantId, decimal TierScore, int DedicatedDailyCapacity)>
        {
            (r1, 85.5m, 30),
            (r2, 95.0m, 20),
            (r3, 70.0m, 50),
        };

        var snapshot = _tierService.GenerateTierSnapshot(campaignId, input, tierVersion: 2);

        snapshot.Should().NotBeNull();
        snapshot.CampaignId.Should().Be(campaignId);
        snapshot.TierVersion.Should().Be(2);

        var rankedItems = JsonSerializer.Deserialize<List<CampaignRestaurantRankItem>>(snapshot.RankingsJson);
        rankedItems.Should().NotBeNull();
        rankedItems!.Should().HaveCount(3);

        rankedItems[0].RestaurantId.Should().Be(r2);
        rankedItems[0].Rank.Should().Be(1);

        rankedItems[1].RestaurantId.Should().Be(r1);
        rankedItems[1].Rank.Should().Be(2);

        rankedItems[2].RestaurantId.Should().Be(r3);
        rankedItems[2].Rank.Should().Be(3);
    }

    [Fact]
    public void GenerateTierSnapshot_WhenScoresTied_RanksByCapacityDescending()
    {
        var campaignId = Guid.NewGuid();
        var r1 = Guid.NewGuid();
        var r2 = Guid.NewGuid();

        var input = new List<(Guid RestaurantId, decimal TierScore, int DedicatedDailyCapacity)>
        {
            (r1, 90.0m, 30),
            (r2, 90.0m, 60), // Higher capacity should rank first on tie
        };

        var snapshot = _tierService.GenerateTierSnapshot(campaignId, input);

        var rankedItems = JsonSerializer.Deserialize<List<CampaignRestaurantRankItem>>(snapshot.RankingsJson);
        rankedItems![0].RestaurantId.Should().Be(r2);
        rankedItems[0].Rank.Should().Be(1);

        rankedItems[1].RestaurantId.Should().Be(r1);
        rankedItems[1].Rank.Should().Be(2);
    }

    [Fact]
    public void GenerateTierSnapshot_WhenScoresAndCapacityTied_RanksByRestaurantIdAscending()
    {
        var campaignId = Guid.NewGuid();
        var idA = Guid.Parse("11111111-1111-1111-1111-111111111111");
        var idB = Guid.Parse("22222222-2222-2222-2222-222222222222");

        var input = new List<(Guid RestaurantId, decimal TierScore, int DedicatedDailyCapacity)>
        {
            (idB, 90.0m, 50),
            (idA, 90.0m, 50),
        };

        var snapshot = _tierService.GenerateTierSnapshot(campaignId, input);

        var rankedItems = JsonSerializer.Deserialize<List<CampaignRestaurantRankItem>>(snapshot.RankingsJson);
        rankedItems![0].RestaurantId.Should().Be(idA);
        rankedItems[0].Rank.Should().Be(1);

        rankedItems[1].RestaurantId.Should().Be(idB);
        rankedItems[1].Rank.Should().Be(2);
    }

    [Fact]
    public void GenerateTierSnapshot_EmptyInput_ProducesEmptyRankings()
    {
        var campaignId = Guid.NewGuid();
        var snapshot = _tierService.GenerateTierSnapshot(campaignId, Enumerable.Empty<(Guid, decimal, int)>());

        snapshot.Should().NotBeNull();
        var rankedItems = JsonSerializer.Deserialize<List<CampaignRestaurantRankItem>>(snapshot.RankingsJson);
        rankedItems.Should().BeEmpty();
    }
}
