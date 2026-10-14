using FluentAssertions;
using MealMate.Application.Features.Offers.Services;
using MealMate.Domain.ProgramsSubscriptions;
using MealMate.Domain.ValueObjects;
using Xunit;

namespace MealMate.UnitTests.Offers;

public class DiscountCombinationPolicyTests
{
    private readonly DiscountCombinationPolicy _policy = new();

    [Fact]
    public void Evaluate_WhenCampaignDisallowsStacking_RefusesCouponWithoutChangingCampaignPrice()
    {
        var result = _policy.Evaluate(new DiscountCombinationRequest(
            BaseAmount: 100m,
            CampaignAmount: 30m,
            CouponAmount: 10m,
            ReferralAmount: 0m,
            CurrencyCode: "KWD",
            AllowStacking: false));

        result.IsAllowed.Should().BeFalse();
        result.ReasonCode.Should().Be(DiscountCombinationReasons.StackingNotAllowed);
        result.FinalAmount.Should().Be(70m);
        result.CurrencyCode.Should().Be("KWD");
    }

    [Fact]
    public void Evaluate_WhenStackingAllowed_SumsAllContributions()
    {
        var result = _policy.Evaluate(new DiscountCombinationRequest(
            BaseAmount: 100m,
            CampaignAmount: 20m,
            CouponAmount: 10m,
            ReferralAmount: 5m,
            CurrencyCode: "KWD",
            AllowStacking: true));

        result.IsAllowed.Should().BeTrue();
        result.FinalAmount.Should().Be(65m);
        result.ReasonCode.Should().Be(DiscountCombinationReasons.Allowed);
    }

    [Fact]
    public void Evaluate_NeverReturnsNegativeAmount()
    {
        var result = _policy.Evaluate(new DiscountCombinationRequest(
            BaseAmount: 50m,
            CampaignAmount: 30m,
            CouponAmount: 30m,
            ReferralAmount: 10m,
            CurrencyCode: "KWD",
            AllowStacking: true));

        result.IsAllowed.Should().BeTrue();
        result.FinalAmount.Should().Be(0m);
        result.ReasonCode.Should().Be(DiscountCombinationReasons.ClampedToZero);
    }

    [Fact]
    public void Evaluate_PreviewRunsTwice_PureFunctionNoState()
    {
        var request = new DiscountCombinationRequest(100m, 0m, 15m, 0m, "KWD", AllowStacking: true);

        var first = _policy.Evaluate(request);
        var second = _policy.Evaluate(request);

        first.FinalAmount.Should().Be(second.FinalAmount);
        first.FinalAmount.Should().Be(85m);
    }

    [Fact]
    public void Evaluate_NegativeContributionsAreTreatedAsZero()
    {
        var result = _policy.Evaluate(new DiscountCombinationRequest(
            BaseAmount: 100m,
            CampaignAmount: -5m,
            CouponAmount: -20m,
            ReferralAmount: 0m,
            CurrencyCode: "KWD",
            AllowStacking: true));

        result.IsAllowed.Should().BeTrue();
        result.FinalAmount.Should().Be(100m);
    }
}

public class LegacyPromoCodeAdapterTests
{
    private readonly LegacyPromoCodeAdapter _adapter = new();

    [Fact]
    public void PercentCode_ComputesFromServerBaseOnly()
    {
        var promo = new PromoCode
        {
            Code = "SAVE10",
            DiscountPercent = Percentage.FromFraction(0.10m)
        };

        var contribution = _adapter.ComputeContribution(promo, serverBaseAmount: 200m);

        contribution.Should().Be(20m);
    }

    [Fact]
    public void FixedAmountCode_IsCappedAtServerBase()
    {
        var promo = new PromoCode
        {
            Code = "FLAT50",
            DiscountAmount = new Money(50m, "KWD")
        };

        var contribution = _adapter.ComputeContribution(promo, serverBaseAmount: 30m);

        contribution.Should().Be(30m);
    }

    [Fact]
    public void CodeWithoutRule_ContributesNothing()
    {
        var promo = new PromoCode { Code = "NORULE" };

        var contribution = _adapter.ComputeContribution(promo, serverBaseAmount: 100m);

        contribution.Should().Be(0m);
    }

    [Fact]
    public void PercentAboveOne_IsCappedAtHundredPercent()
    {
        var promo = new PromoCode
        {
            Code = "WILD",
            DiscountPercent = Percentage.FromFraction(1.5m)
        };

        var contribution = _adapter.ComputeContribution(promo, serverBaseAmount: 100m);

        contribution.Should().Be(100m);
    }
}
