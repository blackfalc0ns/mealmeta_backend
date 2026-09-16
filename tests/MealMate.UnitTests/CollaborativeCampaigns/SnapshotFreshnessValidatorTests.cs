using FluentAssertions;
using MealMate.Application.Features.CollaborativeCampaigns.Services;
using Xunit;

namespace MealMate.UnitTests.CollaborativeCampaigns;

public class SnapshotFreshnessValidatorTests
{
    private readonly SnapshotFreshnessValidator _validator = new();

    [Fact]
    public void Validate_WhenAllVersionsMatch_ReturnsFresh()
    {
        var current = new CampaignVersionVector(1, 1, 1, 1, 1);
        var snapshot = new CampaignVersionVector(1, 1, 1, 1, 1);

        var result = _validator.Validate(current, snapshot);

        result.IsFresh.Should().BeTrue();
        result.StaleReasons.Should().BeEmpty();
    }

    [Fact]
    public void Validate_WhenCampaignVersionModified_ReturnsStaleWithReason()
    {
        var current = new CampaignVersionVector(2, 1, 1, 1, 1); // Campaign discount split updated
        var snapshot = new CampaignVersionVector(1, 1, 1, 1, 1);

        var result = _validator.Validate(current, snapshot);

        result.IsFresh.Should().BeFalse();
        result.StaleReasons.Should().ContainSingle(r => r.Contains("CampaignVersion mismatch"));
    }

    [Fact]
    public void Validate_WhenOffersVersionModified_ReturnsStaleWithReason()
    {
        var current = new CampaignVersionVector(1, 3, 1, 1, 1); // Restaurant offer withdrawn
        var snapshot = new CampaignVersionVector(1, 2, 1, 1, 1);

        var result = _validator.Validate(current, snapshot);

        result.IsFresh.Should().BeFalse();
        result.StaleReasons.Should().ContainSingle(r => r.Contains("OffersVersion mismatch"));
    }

    [Fact]
    public void Validate_WhenPricingOrCapacityModified_ReturnsStaleWithMultipleReasons()
    {
        var current = new CampaignVersionVector(1, 1, 2, 2, 1);
        var snapshot = new CampaignVersionVector(1, 1, 1, 1, 1);

        var result = _validator.Validate(current, snapshot);

        result.IsFresh.Should().BeFalse();
        result.StaleReasons.Should().HaveCount(2);
        result.StaleReasons.Should().Contain(r => r.Contains("PricingVersion mismatch"));
        result.StaleReasons.Should().Contain(r => r.Contains("CapacityVersion mismatch"));
    }
}
