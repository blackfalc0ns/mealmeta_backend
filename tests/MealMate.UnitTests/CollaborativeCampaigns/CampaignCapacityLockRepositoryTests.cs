using FluentAssertions;
using MealMate.Application.Common.Abstractions;
using MealMate.Domain.CollaborativeCampaigns;
using MealMate.Persistence;
using MealMate.Persistence.CollaborativeCampaigns;
using Microsoft.EntityFrameworkCore;
using NSubstitute;
using Xunit;

namespace MealMate.UnitTests.CollaborativeCampaigns;

public class CampaignCapacityLockRepositoryTests
{
    private readonly AppDbContext _context;
    private readonly CampaignCapacityLockRepository _repository;

    public CampaignCapacityLockRepositoryTests()
    {
        var options = new DbContextOptionsBuilder<AppDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString())
            .Options;

        var dispatcher = Substitute.For<IDomainEventDispatcher>();
        _context = new AppDbContext(options, dispatcher);
        _repository = new CampaignCapacityLockRepository(_context);
    }

    [Fact]
    public async Task LockCampaignAsync_WhenCampaignExists_ReturnsCampaign()
    {
        var campaign = new Campaign
        {
            NameAr = "حملة تجريبية",
            NameEn = "Test Campaign",
            StartsAtUtc = DateTime.UtcNow,
            EndsAtUtc = DateTime.UtcNow.AddDays(10),
            TotalDiscountRate = MealMate.Domain.ValueObjects.Percentage.FromFraction(0.15m),
            MealMateShareRate = MealMate.Domain.ValueObjects.Percentage.FromFraction(0.05m),
            RestaurantShareRate = MealMate.Domain.ValueObjects.Percentage.FromFraction(0.10m)
        };
        _context.Campaigns.Add(campaign);
        await _context.SaveChangesAsync();

        var result = await _repository.LockCampaignAsync(campaign.Id);

        result.Should().NotBeNull();
        result!.Id.Should().Be(campaign.Id);
    }

    [Fact]
    public async Task LockCampaignAsync_WhenCampaignDoesNotExist_ReturnsNull()
    {
        var result = await _repository.LockCampaignAsync(Guid.NewGuid());
        result.Should().BeNull();
    }

    [Fact]
    public async Task LockDailyCapacitiesAsync_WhenTargetsExist_ReturnsOrderedMatchingCapacities()
    {
        var campaignId = Guid.NewGuid();
        var restA = Guid.NewGuid();
        var restB = Guid.NewGuid();
        var d1 = new DateOnly(2026, 10, 5);
        var d2 = new DateOnly(2026, 10, 6);

        var cap1 = new CampaignRestaurantCapacity
        {
            CampaignId = campaignId,
            RestaurantId = restA,
            ServiceDate = d1,
            MaxBoxes = 10,
            ReservedBoxes = 2
        };
        var cap2 = new CampaignRestaurantCapacity
        {
            CampaignId = campaignId,
            RestaurantId = restB,
            ServiceDate = d2,
            MaxBoxes = 15,
            ReservedBoxes = 0
        };
        _context.CampaignRestaurantCapacities.AddRange(cap1, cap2);
        await _context.SaveChangesAsync();

        // Pass targets in reverse order
        var targets = new List<(DateOnly ServiceDate, Guid RestaurantId)>
        {
            (d2, restB),
            (d1, restA)
        };

        var result = await _repository.LockDailyCapacitiesAsync(campaignId, targets);

        result.Should().HaveCount(2);
        // Should be ordered by ServiceDate ascending
        result[0].ServiceDate.Should().Be(d1);
        result[1].ServiceDate.Should().Be(d2);
    }

    [Fact]
    public async Task LockDailyCapacitiesAsync_WhenTargetsEmpty_ReturnsEmptyList()
    {
        var result = await _repository.LockDailyCapacitiesAsync(Guid.NewGuid(), Array.Empty<(DateOnly, Guid)>());
        result.Should().BeEmpty();
    }

    [Fact]
    public async Task ExecuteInTransactionWithRetryAsync_ExecutesOperationAndReturnsResult()
    {
        var executed = false;
        var result = await _repository.ExecuteInTransactionWithRetryAsync(async ct =>
        {
            await Task.Yield();
            executed = true;
            return "committed";
        });

        executed.Should().BeTrue();
        result.Should().Be("committed");
    }
}
