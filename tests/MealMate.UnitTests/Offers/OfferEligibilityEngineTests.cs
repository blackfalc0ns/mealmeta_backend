using FluentAssertions;
using MealMate.Application.Features.Offers.Services;
using MealMate.Domain.Offers;
using MealMate.Domain.Offers.Enums;
using MealMate.Domain.ValueObjects;
using Xunit;

namespace MealMate.UnitTests.Offers;

public class OfferEligibilityEngineTests
{
    private readonly OfferEligibilityEngine _engine = new();

    [Fact]
    public void Evaluate_ReturnsIneligible_WhenOfferNotActive()
    {
        var offer = new Offer
        {
            Status = OfferStatus.Draft,
            StartsAtUtc = DateTime.UtcNow.AddDays(-1),
            EndsAtUtc = DateTime.UtcNow.AddDays(5)
        };

        var context = new OfferEligibilityContext(Guid.NewGuid(), 0, 0, 50m, 20, "Hawalli", null, null, null);
        var result = _engine.Evaluate(offer, context, DateTime.UtcNow);

        result.IsEligible.Should().BeFalse();
        result.IneligibilityReasons.Should().Contain(r => r.Contains("not active"));
    }

    [Fact]
    public void Evaluate_EnforcesCustomerSegmentFilter()
    {
        var offer = new Offer
        {
            Status = OfferStatus.Active,
            StartsAtUtc = DateTime.UtcNow.AddDays(-1),
            EndsAtUtc = DateTime.UtcNow.AddDays(5),
            CustomerSegment = CustomerSegmentFilter.NewCustomersOnly,
            DiscountType = DiscountType.Percentage,
            DiscountRate = Percentage.FromFraction(0.10m)
        };

        var existingCustomerCtx = new OfferEligibilityContext(Guid.NewGuid(), 2, 0, 50m, 20, "Hawalli", null, null, null);
        var result = _engine.Evaluate(offer, existingCustomerCtx, DateTime.UtcNow);

        result.IsEligible.Should().BeFalse();
        result.IneligibilityReasons.Should().Contain(r => r.Contains("new customers only"));
    }

    [Fact]
    public void Evaluate_CalculatesCorrectDiscount_ForEligibleCustomer()
    {
        var offer = new Offer
        {
            Status = OfferStatus.Active,
            StartsAtUtc = DateTime.UtcNow.AddDays(-1),
            EndsAtUtc = DateTime.UtcNow.AddDays(5),
            CustomerSegment = CustomerSegmentFilter.All,
            DiscountType = DiscountType.Percentage,
            DiscountRate = Percentage.FromFraction(0.15m),
            PerCustomerLimit = 1
        };

        var eligibleCtx = new OfferEligibilityContext(Guid.NewGuid(), 1, 0, 100m, 20, "Hawalli", null, null, null);
        var result = _engine.Evaluate(offer, eligibleCtx, DateTime.UtcNow);

        result.IsEligible.Should().BeTrue();
        result.CalculatedDiscountAmount.Should().Be(15.000m);
    }
}
