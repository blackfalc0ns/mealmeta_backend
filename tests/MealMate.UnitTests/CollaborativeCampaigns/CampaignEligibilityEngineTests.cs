using FluentAssertions;
using MealMate.Application.Features.CollaborativeCampaigns.Services;
using MealMate.Domain.CollaborativeCampaigns;
using MealMate.Domain.Enums;
using MealMate.Domain.ValueObjects;
using Xunit;

namespace MealMate.UnitTests.CollaborativeCampaigns;

public class CampaignEligibilityEngineTests
{
    private readonly CampaignEligibilityEngine _engine = new();
    private readonly DateTime _now = new(2026, 10, 5, 12, 0, 0, DateTimeKind.Utc);

    private Campaign CreateActiveCampaign()
    {
        return new Campaign
        {
            NameAr = "حملة الأبطال",
            NameEn = "Champions Campaign",
            Status = CampaignStatus.Active,
            StartsAtUtc = _now.AddDays(-2),
            EndsAtUtc = _now.AddDays(10),
            TotalDiscountRate = Percentage.FromFraction(0.20m),
            MealMateShareRate = Percentage.FromFraction(0.10m),
            RestaurantShareRate = Percentage.FromFraction(0.10m)
        };
    }

    [Fact]
    public void Evaluate_WhenCustomerMeetsAllCriteria_ReturnsEligible()
    {
        var campaign = CreateActiveCampaign();
        var profile = new CustomerEligibilityProfile(
            CustomerId: Guid.NewGuid(),
            PriorSubscriptionCount: 0,
            ActiveCampaignSubscriptionCount: 0,
            DeliveryGovernorate: "Capital",
            RequestedDurationDays: 26);

        var criteria = new CampaignEligibilityCriteria();

        var result = _engine.Evaluate(campaign, profile, criteria, _now);

        result.IsEligible.Should().BeTrue();
        result.IneligibilityReasons.Should().BeEmpty();
    }

    [Fact]
    public void Evaluate_WhenCampaignNotActive_ReturnsIneligibleWithStatusReason()
    {
        var campaign = CreateActiveCampaign();
        campaign.Status = CampaignStatus.Stopped;

        var profile = new CustomerEligibilityProfile(Guid.NewGuid(), 0, 0, "Capital", 26);
        var criteria = new CampaignEligibilityCriteria();

        var result = _engine.Evaluate(campaign, profile, criteria, _now);

        result.IsEligible.Should().BeFalse();
        result.IneligibilityReasons.Should().Contain(r => r.Contains("Stopped"));
    }

    [Fact]
    public void Evaluate_WhenOutsideSalesDates_ReturnsIneligibleWithDateReason()
    {
        var campaign = CreateActiveCampaign();
        campaign.EndsAtUtc = _now.AddDays(-1); // Ended yesterday

        var profile = new CustomerEligibilityProfile(Guid.NewGuid(), 0, 0, "Capital", 26);
        var criteria = new CampaignEligibilityCriteria();

        var result = _engine.Evaluate(campaign, profile, criteria, _now);

        result.IsEligible.Should().BeFalse();
        result.IneligibilityReasons.Should().Contain(r => r.Contains("sales window"));
    }

    [Fact]
    public void Evaluate_WhenNewCustomersOnlyAndCustomerHasPriorSubscriptions_ReturnsIneligible()
    {
        var campaign = CreateActiveCampaign();
        var profile = new CustomerEligibilityProfile(Guid.NewGuid(), PriorSubscriptionCount: 2, 0, "Capital", 26);
        var criteria = new CampaignEligibilityCriteria(CustomerSegment: CustomerSegmentFilter.NewCustomersOnly);

        var result = _engine.Evaluate(campaign, profile, criteria, _now);

        result.IsEligible.Should().BeFalse();
        result.IneligibilityReasons.Should().Contain(r => r.Contains("new customers only"));
    }

    [Fact]
    public void Evaluate_WhenExistingCustomersOnlyAndCustomerHasNoPriorSubscriptions_ReturnsIneligible()
    {
        var campaign = CreateActiveCampaign();
        var profile = new CustomerEligibilityProfile(Guid.NewGuid(), PriorSubscriptionCount: 0, 0, "Capital", 26);
        var criteria = new CampaignEligibilityCriteria(CustomerSegment: CustomerSegmentFilter.ExistingCustomersOnly);

        var result = _engine.Evaluate(campaign, profile, criteria, _now);

        result.IsEligible.Should().BeFalse();
        result.IneligibilityReasons.Should().Contain(r => r.Contains("existing subscribers only"));
    }

    [Fact]
    public void Evaluate_WhenCustomerExceedsPerCustomerLimit_ReturnsIneligible()
    {
        var campaign = CreateActiveCampaign();
        var profile = new CustomerEligibilityProfile(Guid.NewGuid(), 1, ActiveCampaignSubscriptionCount: 1, "Capital", 26);
        var criteria = new CampaignEligibilityCriteria(PerCustomerLimit: 1);

        var result = _engine.Evaluate(campaign, profile, criteria, _now);

        result.IsEligible.Should().BeFalse();
        result.IneligibilityReasons.Should().Contain(r => r.Contains("subscription limit"));
    }

    [Fact]
    public void Evaluate_WhenDeliveryGovernorateNotAllowed_ReturnsIneligible()
    {
        var campaign = CreateActiveCampaign();
        var profile = new CustomerEligibilityProfile(Guid.NewGuid(), 0, 0, DeliveryGovernorate: "Jahra", 26);
        var criteria = new CampaignEligibilityCriteria(AllowedGovernorates: new[] { "Capital", "Hawalli" });

        var result = _engine.Evaluate(campaign, profile, criteria, _now);

        result.IsEligible.Should().BeFalse();
        result.IneligibilityReasons.Should().Contain(r => r.Contains("outside the campaign service area"));
    }

    [Fact]
    public void Evaluate_WhenRequestedDurationOutsideRange_ReturnsIneligible()
    {
        var campaign = CreateActiveCampaign();
        var profile = new CustomerEligibilityProfile(Guid.NewGuid(), 0, 0, "Capital", RequestedDurationDays: 10);
        var criteria = new CampaignEligibilityCriteria(MinDurationDays: 14, MaxDurationDays: 30);

        var result = _engine.Evaluate(campaign, profile, criteria, _now);

        result.IsEligible.Should().BeFalse();
        result.IneligibilityReasons.Should().Contain(r => r.Contains("duration"));
    }
}
