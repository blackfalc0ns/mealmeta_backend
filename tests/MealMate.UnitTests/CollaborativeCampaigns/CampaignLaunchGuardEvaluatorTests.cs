using FluentAssertions;
using MealMate.Application.Features.CollaborativeCampaigns.Services;
using MealMate.Domain.CollaborativeCampaigns;
using MealMate.Domain.Enums;
using MealMate.Domain.ValueObjects;
using Xunit;

namespace MealMate.UnitTests.CollaborativeCampaigns;

public class CampaignLaunchGuardEvaluatorTests
{
    private readonly CampaignLaunchGuardEvaluator _evaluator = new();

    private Campaign CreateValidCampaign(CampaignStatus status = CampaignStatus.Scheduled)
    {
        return new Campaign
        {
            NameAr = "حملة الصيف",
            NameEn = "Summer Campaign",
            Status = status,
            EnrollmentOpensAtUtc = DateTime.UtcNow.AddDays(-10),
            EnrollmentClosesAtUtc = DateTime.UtcNow.AddDays(-2),
            StartsAtUtc = DateTime.UtcNow.AddDays(1),
            EndsAtUtc = DateTime.UtcNow.AddDays(30),
            TotalDiscountRate = Percentage.FromFraction(0.20m),
            MealMateShareRate = Percentage.FromFraction(0.10m),
            RestaurantShareRate = Percentage.FromFraction(0.10m),
            MaxSubscribers = 100
        };
    }

    [Fact]
    public void Evaluate_WhenAllConditionsMet_ReturnsCanLaunchTrue()
    {
        var campaign = CreateValidCampaign();
        var profitability = new CampaignProfitabilitySnapshot
        {
            CampaignId = campaign.Id,
            IsViable = true,
            RequiresOverride = false,
            OverallExpectedMarginPct = 0.15m,
            MinimumExpectedMarginPct = 0.10m
        };
        var tier = new CampaignTierSnapshot
        {
            CampaignId = campaign.Id,
            RankingsJson = "[]"
        };
        var freshness = new SnapshotFreshnessResult(true, Array.Empty<string>());

        var context = new LaunchGuardEvaluationContext(
            campaign,
            profitability,
            tier,
            freshness,
            AcceptedRestaurantCount: 3,
            TotalDedicatedDailyCapacity: 50);

        var result = _evaluator.Evaluate(context);

        result.CanLaunch.Should().BeTrue();
        result.BlockingReasons.Should().BeEmpty();
    }

    [Fact]
    public void Evaluate_WhenCampaignStatusNotReviewOrScheduled_ReturnsBlocked()
    {
        var campaign = CreateValidCampaign(CampaignStatus.Draft);
        var profitability = new CampaignProfitabilitySnapshot { IsViable = true, RequiresOverride = false };
        var tier = new CampaignTierSnapshot { RankingsJson = "[]" };
        var freshness = new SnapshotFreshnessResult(true, Array.Empty<string>());

        var context = new LaunchGuardEvaluationContext(campaign, profitability, tier, freshness, 2, 20);

        var result = _evaluator.Evaluate(context);

        result.CanLaunch.Should().BeFalse();
        result.BlockingReasons.Should().Contain(r => r.Contains("Draft"));
    }

    [Fact]
    public void Evaluate_WhenDatesAreInvalid_ReturnsBlockedWithDateErrors()
    {
        var campaign = CreateValidCampaign();
        campaign.StartsAtUtc = DateTime.UtcNow.AddDays(10);
        campaign.EndsAtUtc = DateTime.UtcNow.AddDays(5); // Ends before starts
        campaign.EnrollmentClosesAtUtc = DateTime.UtcNow.AddDays(12); // Closes after sales start

        var profitability = new CampaignProfitabilitySnapshot { IsViable = true, RequiresOverride = false };
        var tier = new CampaignTierSnapshot { RankingsJson = "[]" };
        var freshness = new SnapshotFreshnessResult(true, Array.Empty<string>());

        var context = new LaunchGuardEvaluationContext(campaign, profitability, tier, freshness, 2, 20);

        var result = _evaluator.Evaluate(context);

        result.CanLaunch.Should().BeFalse();
        result.BlockingReasons.Should().Contain(r => r.Contains("start date must precede"));
        result.BlockingReasons.Should().Contain(r => r.Contains("joining close date must precede"));
    }

    [Fact]
    public void Evaluate_WhenNoAcceptedRestaurantsOrCapacityZero_ReturnsBlocked()
    {
        var campaign = CreateValidCampaign();
        var profitability = new CampaignProfitabilitySnapshot { IsViable = true, RequiresOverride = false };
        var tier = new CampaignTierSnapshot { RankingsJson = "[]" };
        var freshness = new SnapshotFreshnessResult(true, Array.Empty<string>());

        var context = new LaunchGuardEvaluationContext(campaign, profitability, tier, freshness, 0, 0);

        var result = _evaluator.Evaluate(context);

        result.CanLaunch.Should().BeFalse();
        result.BlockingReasons.Should().Contain(r => r.Contains("no accepted restaurant offers"));
        result.BlockingReasons.Should().Contain(r => r.Contains("capacity must be greater than zero"));
    }

    [Fact]
    public void Evaluate_WhenSnapshotsMissingOrStale_ReturnsBlocked()
    {
        var campaign = CreateValidCampaign();
        var freshness = new SnapshotFreshnessResult(false, new[] { "PricingVersion mismatch", "CapacityVersion mismatch" });

        var context = new LaunchGuardEvaluationContext(campaign, null, null, freshness, 2, 20);

        var result = _evaluator.Evaluate(context);

        result.CanLaunch.Should().BeFalse();
        result.BlockingReasons.Should().Contain(r => r.Contains("Profitability snapshot is missing"));
        result.BlockingReasons.Should().Contain(r => r.Contains("Tier ranking snapshot is missing"));
        result.BlockingReasons.Should().Contain(r => r.Contains("PricingVersion mismatch"));
        result.BlockingReasons.Should().Contain(r => r.Contains("CapacityVersion mismatch"));
    }

    [Fact]
    public void Evaluate_WhenProfitabilityNotViable_ReturnsBlocked()
    {
        var campaign = CreateValidCampaign();
        var profitability = new CampaignProfitabilitySnapshot
        {
            IsViable = false,
            OverallExpectedMarginPct = -0.05m
        };
        var tier = new CampaignTierSnapshot { RankingsJson = "[]" };
        var freshness = new SnapshotFreshnessResult(true, Array.Empty<string>());

        var context = new LaunchGuardEvaluationContext(campaign, profitability, tier, freshness, 2, 20);

        var result = _evaluator.Evaluate(context);

        result.CanLaunch.Should().BeFalse();
        result.BlockingReasons.Should().Contain(r => r.Contains("strictly prohibited"));
    }

    [Fact]
    public void Evaluate_WhenProfitabilityRequiresOverrideWithoutOverride_ReturnsBlocked()
    {
        var campaign = CreateValidCampaign();
        var profitability = new CampaignProfitabilitySnapshot
        {
            IsViable = true,
            RequiresOverride = true,
            OverallExpectedMarginPct = 0.05m,
            MinimumExpectedMarginPct = 0.10m,
            OverrideReason = null,
            OverrideApprovedByUserId = null
        };
        var tier = new CampaignTierSnapshot { RankingsJson = "[]" };
        var freshness = new SnapshotFreshnessResult(true, Array.Empty<string>());

        var context = new LaunchGuardEvaluationContext(campaign, profitability, tier, freshness, 2, 20);

        var result = _evaluator.Evaluate(context);

        result.CanLaunch.Should().BeFalse();
        result.BlockingReasons.Should().Contain(r => r.Contains("lacks an authorized financial override"));
    }

    [Fact]
    public void Evaluate_WhenProfitabilityRequiresOverrideWithValidOverride_AllowsLaunchWithWarning()
    {
        var campaign = CreateValidCampaign();
        var profitability = new CampaignProfitabilitySnapshot
        {
            IsViable = true,
            RequiresOverride = true,
            OverallExpectedMarginPct = 0.05m,
            MinimumExpectedMarginPct = 0.10m,
            OverrideReason = "Strategic Q4 market penetration",
            OverrideApprovedByUserId = Guid.NewGuid()
        };
        var tier = new CampaignTierSnapshot { RankingsJson = "[]" };
        var freshness = new SnapshotFreshnessResult(true, Array.Empty<string>());

        var context = new LaunchGuardEvaluationContext(campaign, profitability, tier, freshness, 2, 20);

        var result = _evaluator.Evaluate(context);

        result.CanLaunch.Should().BeTrue();
        result.BlockingReasons.Should().BeEmpty();
        result.Warnings.Should().Contain(w => w.Contains("authorized profitability override"));
    }
}
