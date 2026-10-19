using FluentAssertions;
using MealMate.Application.Features.Offers.Services;
using MealMate.Domain.Offers;
using MealMate.Domain.Offers.Enums;
using MealMate.Domain.Enums;
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

    [Fact]
    public void Evaluate_RequiresExactPackageTargetTupleAndAllowedDuration()
    {
        var offer = new Offer
        {
            Status = OfferStatus.Active,
            StartsAtUtc = DateTime.UtcNow.AddDays(-1),
            EndsAtUtc = DateTime.UtcNow.AddDays(5),
            DiscountType = DiscountType.Percentage,
            DiscountRate = Percentage.FromFraction(0.20m),
            PerCustomerLimit = 1,
            PackageTargets =
            [
                new OfferPackageTarget
                {
                    SubscriptionType = "Individual",
                    ProgramId = Guid.Parse("aaaaaaaa-aaaa-aaaa-aaaa-aaaaaaaaaaaa"),
                    BundleId = Guid.Parse("bbbbbbbb-bbbb-bbbb-bbbb-bbbbbbbbbbbb"),
                    Tier = RestaurantTier.Platinum,
                    AllowedDurationDays = [30, 60]
                }
            ]
        };

        var eligible = new OfferEligibilityContext(
            Guid.NewGuid(), 0, 0, 100m, 60, ProgramId: Guid.Parse("aaaaaaaa-aaaa-aaaa-aaaa-aaaaaaaaaaaa"),
            BundleId: Guid.Parse("bbbbbbbb-bbbb-bbbb-bbbb-bbbbbbbbbbbb"), Tier: RestaurantTier.Platinum,
            SubscriptionType: "Individual");
        _engine.Evaluate(offer, eligible, DateTime.UtcNow).IsEligible.Should().BeTrue();

        var wrongTier = eligible with { Tier = RestaurantTier.Elite };
        var wrongDuration = eligible with { DurationDays = 90 };
        _engine.Evaluate(offer, wrongTier, DateTime.UtcNow).IneligibilityReasons
            .Should().Contain(x => x.Contains("package target"));
        _engine.Evaluate(offer, wrongDuration, DateTime.UtcNow).IneligibilityReasons
            .Should().Contain(x => x.Contains("package target"));
    }

    [Fact]
    public void Evaluate_UsesLegacyEligibilityWhenOfferHasNoPackageTargets()
    {
        var programId = Guid.NewGuid();
        var offer = new Offer
        {
            Status = OfferStatus.Active,
            StartsAtUtc = DateTime.UtcNow.AddDays(-1),
            EndsAtUtc = DateTime.UtcNow.AddDays(5),
            DiscountType = DiscountType.Percentage,
            DiscountRate = Percentage.FromFraction(0.10m),
            EligiblePrograms = [new OfferProgram { ProgramId = programId }]
        };

        var result = _engine.Evaluate(offer,
            new OfferEligibilityContext(Guid.NewGuid(), 0, 0, 100m, 30, ProgramId: programId), DateTime.UtcNow);

        result.IsEligible.Should().BeTrue();
    }

    [Fact]
    public void Evaluate_UsesExactTargetsAsScopeAndAllowsItsOwnPromoCode()
    {
        var programId = Guid.NewGuid();
        var bundleId = Guid.NewGuid();
        var offer = new Offer
        {
            Status = OfferStatus.Active,
            StartsAtUtc = DateTime.UtcNow.AddDays(-1),
            EndsAtUtc = DateTime.UtcNow.AddDays(5),
            PromoCode = "SAVE20",
            DiscountType = DiscountType.Percentage,
            DiscountRate = Percentage.FromFraction(0.20m),
            AllowPromoCodeStacking = true,
            PackageTargets = [new OfferPackageTarget
            {
                SubscriptionType = "Individual", ProgramId = programId, BundleId = bundleId,
                Tier = RestaurantTier.Basic, AllowedDurationDays = [30]
            }],
            EligiblePrograms = [new OfferProgram { ProgramId = Guid.NewGuid() }],
            EligibleBundles = [new OfferBundle { BundleId = Guid.NewGuid() }]
        };

        var result = _engine.Evaluate(offer, new OfferEligibilityContext(
            Guid.NewGuid(), 0, 0, 100m, 30, ProgramId: programId, BundleId: bundleId,
            Tier: RestaurantTier.Basic, SubscriptionType: "Individual", HasPromoCode: true,
            IsOfferPromoCode: true), DateTime.UtcNow);

        result.IsEligible.Should().BeTrue();
    }

    [Fact]
    public void Evaluate_AllowsTargetedOfferInDiscovery_WithoutSelectedPackage()
    {
        var programId = Guid.NewGuid();
        var bundleId = Guid.NewGuid();
        var offer = new Offer
        {
            Status = OfferStatus.Active,
            StartsAtUtc = DateTime.UtcNow.AddDays(-1),
            EndsAtUtc = DateTime.UtcNow.AddDays(5),
            PackageTargets = [new OfferPackageTarget
            {
                SubscriptionType = "Individual",
                ProgramId = programId,
                BundleId = bundleId,
                Tier = RestaurantTier.Platinum,
                AllowedDurationDays = [20, 26]
            }]
        };

        // In Discovery mode without package selection, the offer should be discoverable
        var discoveryContext = new OfferEligibilityContext(
            Guid.NewGuid(), 0, 0,
            Purpose: OfferEvaluationPurpose.Discovery);

        var result = _engine.Evaluate(offer, discoveryContext, DateTime.UtcNow);

        result.IsEligible.Should().BeTrue();
        result.IsScopeUnresolved.Should().BeTrue();
    }

    [Fact]
    public void Evaluate_RejectsTargetedOfferAtCheckout_WhenMissingOrWrongSubscriptionType()
    {
        var programId = Guid.NewGuid();
        var bundleId = Guid.NewGuid();
        var offer = new Offer
        {
            Status = OfferStatus.Active,
            StartsAtUtc = DateTime.UtcNow.AddDays(-1),
            EndsAtUtc = DateTime.UtcNow.AddDays(5),
            PackageTargets = [new OfferPackageTarget
            {
                SubscriptionType = "Individual",
                ProgramId = programId,
                BundleId = bundleId,
                Tier = RestaurantTier.Platinum,
                AllowedDurationDays = [26]
            }]
        };

        // Checkout mode without package selection: must fail
        var missingPackageContext = new OfferEligibilityContext(
            Guid.NewGuid(), 0, 0,
            Purpose: OfferEvaluationPurpose.Checkout);
        var missingResult = _engine.Evaluate(offer, missingPackageContext, DateTime.UtcNow);
        missingResult.IsEligible.Should().BeFalse();
        missingResult.IneligibilityReasons.Should().Contain(r => r.Contains("package target"));

        // Checkout mode with wrong subscription type (Family vs Individual)
        var wrongTypeContext = new OfferEligibilityContext(
            Guid.NewGuid(), 0, 0, CartAmount: 100m, DurationDays: 26,
            ProgramId: programId, BundleId: bundleId, Tier: RestaurantTier.Platinum,
            SubscriptionType: "Family", Purpose: OfferEvaluationPurpose.Checkout);
        var wrongTypeResult = _engine.Evaluate(offer, wrongTypeContext, DateTime.UtcNow);
        wrongTypeResult.IsEligible.Should().BeFalse();
        wrongTypeResult.IneligibilityReasons.Should().Contain(r => r.Contains("package target"));
    }
}
