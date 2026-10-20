using FluentAssertions;
using MealMate.Application.Common.Abstractions;
using MealMate.Application.Features.CollaborativeCampaigns.Queries;
using MealMate.Application.Features.CollaborativeCampaigns.Services;
using MealMate.Application.Features.IdentityAccess.Customers;
using MealMate.Domain.CollaborativeCampaigns;
using MealMate.Domain.Enums;
using MealMate.Domain.ProgramsSubscriptions;
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

        var evaluation = new CustomerCampaignEvaluationService(
            _context,
            _clock,
            new CampaignEligibilityEngine(),
            new CustomerDeliveryAddressService(_context, _clock));

        _handler = new GetCampaignPricePreviewQueryHandler(evaluation);
    }

    private async Task<(Campaign Campaign, Guid CampaignBundleId)> SeedCampaignAsync(
        CampaignStatus status = CampaignStatus.Active,
        decimal headline = 0.20m,
        decimal restaurantShare = 0.10m,
        decimal platformShare = 0.10m)
    {
        var campaign = new Campaign
        {
            NameAr = "حملة الصيف",
            NameEn = "Summer Campaign",
            Status = status,
            StartsAtUtc = _now.AddDays(-2),
            EndsAtUtc = _now.AddDays(10),
            TotalDiscountRate = Percentage.FromFraction(headline),
            MealMateShareRate = Percentage.FromFraction(platformShare),
            RestaurantShareRate = Percentage.FromFraction(restaurantShare)
        };
        _context.Campaigns.Add(campaign);

        var program = new Program
        {
            Code = "P-" + Guid.NewGuid().ToString("N")[..8],
            NameAr = "برنامج",
            NameEn = "Program",
            Status = ProgramStatus.Active,
            IsActive = true
        };
        _context.Programs.Add(program);

        var bundle = new Bundle
        {
            ProgramId = program.Id,
            Code = "B-" + Guid.NewGuid().ToString("N")[..8],
            NameAr = "باقة",
            NameEn = "Bundle",
            MealsPerDay = 3,
            Status = ProgramStatus.Active,
            IsActive = true
        };
        _context.Bundles.Add(bundle);

        var campaignBundle = new CampaignBundle
        {
            CampaignId = campaign.Id,
            BundleId = bundle.Id
        };
        _context.CampaignBundles.Add(campaignBundle);
        await _context.SaveChangesAsync();

        return (campaign, campaignBundle.Id);
    }

    private CampaignBundleDurationPrice SeedDurationPrice(Guid campaignBundleId, int days, decimal amount) =>
        CampaignBundleDurationPrice.Create(campaignBundleId, days, new Money(amount, "KWD"), "System", 1).Value;

    [Fact]
    public async Task Handle_WhenBundleDurationExists_ReturnsCalculatedPreviewDto()
    {
        var (campaign, campaignBundleId) = await SeedCampaignAsync();
        _context.CampaignBundleDurationPrices.Add(SeedDurationPrice(campaignBundleId, 26, 100.000m));
        await _context.SaveChangesAsync();

        var query = new GetCampaignPricePreviewQuery(campaign.Id, campaignBundleId, DurationDays: 26);

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
    public async Task Handle_156ReferenceWith30Percent_Yields109_200_UnderCurrentPolicy()
    {
        var (campaign, campaignBundleId) = await SeedCampaignAsync(headline: 0.30m, restaurantShare: 0.15m, platformShare: 0.15m);
        _context.CampaignBundleDurationPrices.Add(SeedDurationPrice(campaignBundleId, 26, 156.000m));
        await _context.SaveChangesAsync();

        var query = new GetCampaignPricePreviewQuery(campaign.Id, campaignBundleId, DurationDays: 26);

        var result = await _handler.Handle(query, CancellationToken.None);

        result.IsSuccess.Should().BeTrue();
        result.Value.TotalDiscountAmount.Should().Be(46.800m);
        result.Value.FinalCustomerPrice.Should().Be(109.200m);
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
    public async Task Handle_WhenBundleBelongsToAnotherCampaign_ReturnsFailure()
    {
        var (campaignA, _) = await SeedCampaignAsync();
        var (_, otherCampaignBundleId) = await SeedCampaignAsync();
        _context.CampaignBundleDurationPrices.Add(SeedDurationPrice(otherCampaignBundleId, 26, 100.000m));
        await _context.SaveChangesAsync();

        var query = new GetCampaignPricePreviewQuery(campaignA.Id, otherCampaignBundleId, 26);

        var result = await _handler.Handle(query, CancellationToken.None);

        result.IsFailure.Should().BeTrue();
        result.Error.Code.Should().Be("Campaign.BundleNotInCampaign");
    }

    [Fact]
    public async Task Handle_WhenDurationPricingNotFound_ReturnsFailure()
    {
        var (campaign, campaignBundleId) = await SeedCampaignAsync();
        _context.CampaignBundleDurationPrices.Add(SeedDurationPrice(campaignBundleId, 26, 100.000m));
        await _context.SaveChangesAsync();

        var query = new GetCampaignPricePreviewQuery(campaign.Id, campaignBundleId, 14);

        var result = await _handler.Handle(query, CancellationToken.None);

        result.IsFailure.Should().BeTrue();
        result.Error.Code.Should().Be("Campaign.PricingNotFound");
    }

    [Fact]
    public async Task Handle_WhenCustomerIneligible_IncludesEligibilityFailureReasons()
    {
        var (campaign, campaignBundleId) = await SeedCampaignAsync(status: CampaignStatus.Stopped);
        _context.CampaignBundleDurationPrices.Add(SeedDurationPrice(campaignBundleId, 20, 60.000m));
        await _context.SaveChangesAsync();

        var query = new GetCampaignPricePreviewQuery(campaign.Id, campaignBundleId, 20, CustomerId: Guid.NewGuid());

        var result = await _handler.Handle(query, CancellationToken.None);

        result.IsSuccess.Should().BeTrue();
        result.Value.IsCustomerEligible.Should().BeFalse();
        result.Value.IneligibilityReasons.Should().NotBeEmpty();
    }
}
