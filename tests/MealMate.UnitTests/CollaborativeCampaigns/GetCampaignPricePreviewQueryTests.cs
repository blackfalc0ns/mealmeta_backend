using FluentAssertions;
using MealMate.Application.Common.Abstractions;
using MealMate.Application.Features.CollaborativeCampaigns.Queries;
using MealMate.Application.Features.CollaborativeCampaigns.Services;
using MealMate.Domain.CollaborativeCampaigns;
using MealMate.Domain.Enums;
using MealMate.Domain.ValueObjects;
using MealMate.Persistence;
using Microsoft.EntityFrameworkCore;
using NSubstitute;
using Xunit;

namespace MealMate.UnitTests.CollaborativeCampaigns;

public class GetCampaignPricePreviewQueryTests
{
    private readonly AppDbContext _context;
    private readonly IDateTimeProvider _clock = Substitute.For<IDateTimeProvider>();
    private readonly ICampaignEligibilityEngine _eligibilityEngine = new CampaignEligibilityEngine();
    private readonly GetCampaignPricePreviewQueryHandler _handler;
    private readonly DateTime _now = new(2026, 10, 5, 12, 0, 0, DateTimeKind.Utc);

    public GetCampaignPricePreviewQueryTests()
    {
        var options = new DbContextOptionsBuilder<AppDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString())
            .Options;

        var dispatcher = Substitute.For<IDomainEventDispatcher>();
        _context = new AppDbContext(options, dispatcher);
        _clock.UtcNow.Returns(_now);

        _handler = new GetCampaignPricePreviewQueryHandler(_context, _clock, _eligibilityEngine);
    }

    [Fact]
    public async Task Handle_WhenBundleDurationExists_ReturnsCalculatedPreviewDto()
    {
        var campaign = new Campaign
        {
            NameAr = "حملة الصيف",
            NameEn = "Summer Campaign",
            Status = CampaignStatus.Active,
            StartsAtUtc = _now.AddDays(-2),
            EndsAtUtc = _now.AddDays(10),
            TotalDiscountRate = Percentage.FromFraction(0.20m),
            MealMateShareRate = Percentage.FromFraction(0.10m),
            RestaurantShareRate = Percentage.FromFraction(0.10m)
        };
        _context.Campaigns.Add(campaign);

        var bundleId = Guid.NewGuid();
        var durationPrice = CampaignBundleDurationPrice.Create(
            bundleId,
            26,
            new Money(100.000m, "KWD"),
            "System",
            1).Value;
        _context.CampaignBundleDurationPrices.Add(durationPrice);
        await _context.SaveChangesAsync();

        var query = new GetCampaignPricePreviewQuery(campaign.Id, bundleId, DurationDays: 26);

        var result = await _handler.Handle(query, CancellationToken.None);

        result.IsSuccess.Should().BeTrue();
        var dto = result.Value;
        dto.ReferenceCustomerPrice.Should().Be(100.000m);
        dto.TotalDiscountPct.Should().Be(0.20m);
        dto.TotalDiscountAmount.Should().Be(20.000m);
        dto.RestaurantContributionPct.Should().Be(0.10m);
        dto.RestaurantContributionAmount.Should().Be(10.000m);
        dto.PlatformContributionPct.Should().Be(0.10m);
        dto.PlatformContributionAmount.Should().Be(10.000m);
        dto.FinalCustomerPrice.Should().Be(80.000m);
        dto.PricePerDay.Should().Be(Math.Round(80.000m / 26, 3, MidpointRounding.AwayFromZero));
        dto.IsCustomerEligible.Should().BeTrue();
    }

    [Fact]
    public async Task Handle_WhenCampaignNotFound_ReturnsFailure()
    {
        var query = new GetCampaignPricePreviewQuery(Guid.NewGuid(), Guid.NewGuid(), 26);

        var result = await _handler.Handle(query, CancellationToken.None);

        result.IsFailure.Should().BeTrue();
        result.Error.Code.Should().Be("Campaign.NotFound");
    }

    [Fact]
    public async Task Handle_WhenDurationPricingNotFound_ReturnsFailure()
    {
        var campaign = new Campaign
        {
            NameAr = "حملة",
            NameEn = "Campaign",
            Status = CampaignStatus.Active,
            StartsAtUtc = _now.AddDays(-1),
            EndsAtUtc = _now.AddDays(5),
            TotalDiscountRate = Percentage.FromFraction(0.20m),
            MealMateShareRate = Percentage.FromFraction(0.10m),
            RestaurantShareRate = Percentage.FromFraction(0.10m)
        };
        _context.Campaigns.Add(campaign);
        await _context.SaveChangesAsync();

        var query = new GetCampaignPricePreviewQuery(campaign.Id, Guid.NewGuid(), 14);

        var result = await _handler.Handle(query, CancellationToken.None);

        result.IsFailure.Should().BeTrue();
        result.Error.Code.Should().Be("Campaign.PricingNotFound");
    }

    [Fact]
    public async Task Handle_WhenCustomerIneligible_IncludesEligibilityFailureReasons()
    {
        var campaign = new Campaign
        {
            NameAr = "حملة موقوفة",
            NameEn = "Stopped Campaign",
            Status = CampaignStatus.Stopped, // Not active!
            StartsAtUtc = _now.AddDays(-5),
            EndsAtUtc = _now.AddDays(5),
            TotalDiscountRate = Percentage.FromFraction(0.20m),
            MealMateShareRate = Percentage.FromFraction(0.10m),
            RestaurantShareRate = Percentage.FromFraction(0.10m)
        };
        _context.Campaigns.Add(campaign);

        var bundleId = Guid.NewGuid();
        var durationPrice = CampaignBundleDurationPrice.Create(
            bundleId,
            20,
            new Money(60.000m, "KWD"),
            "System",
            1).Value;
        _context.CampaignBundleDurationPrices.Add(durationPrice);
        await _context.SaveChangesAsync();

        var query = new GetCampaignPricePreviewQuery(campaign.Id, bundleId, 20, CustomerId: Guid.NewGuid());

        var result = await _handler.Handle(query, CancellationToken.None);

        result.IsSuccess.Should().BeTrue();
        result.Value.IsCustomerEligible.Should().BeFalse();
        result.Value.IneligibilityReasons.Should().NotBeEmpty();
    }
}
