using FluentAssertions;
using MealMate.Application.Common.Abstractions;
using MealMate.Application.Features.CustomerCheckout;
using MealMate.Application.Features.Offers.Services;
using MealMate.Application.Features.ProgramsSubscriptions.Flow;
using MealMate.Domain.ProgramsSubscriptions;
using MealMate.Domain.ValueObjects;
using MealMate.Persistence;
using Microsoft.EntityFrameworkCore;
using NSubstitute;
using Xunit;

namespace MealMate.UnitTests.ProgramsSubscriptions;

public class ReferralChainCouponTests
{
    private (AppDbContext Db, SubscriptionFlowCouponService Service, DateTimeProviderStub Clock) Create()
    {
        var db = new AppDbContext(
            new DbContextOptionsBuilder<AppDbContext>().UseInMemoryDatabase(Guid.NewGuid().ToString()).Options,
            Substitute.For<IDomainEventDispatcher>());
        var clock = new DateTimeProviderStub();
        var service = new SubscriptionFlowCouponService(db, Substitute.For<IOfferEligibilityEngine>(), clock);
        return (db, service, clock);
    }

    private static ReferralCode SeedReferral(string code, Guid owner, decimal fraction, bool isActive = true,
        int usedCount = 0, int? maxUses = null)
        => new()
        {
            Code = code,
            OwnerUserId = owner,
            RewardPercent = Percentage.FromFraction(fraction),
            MaxUses = maxUses,
            UsedCount = usedCount,
            IsActive = isActive,
        };

    [Fact]
    public async Task ValidReferral_AppliesRoundTo3Reward()
    {
        var (db, service, _) = Create();
        var customer = Guid.NewGuid();
        db.ReferralCodes.Add(SeedReferral("REF-VALID", Guid.NewGuid(), 0.2m));
        await db.SaveChangesAsync();

        var evaluation = await service.EvaluateAsync("ref-valid", customer, Guid.NewGuid(), Guid.NewGuid(), 26, 31.375m);

        evaluation.IsValid.Should().BeTrue();
        evaluation.Status.Should().Be("valid");
        evaluation.DiscountAmount.Should().Be(Math.Round(31.375m * 0.2m, 3, MidpointRounding.AwayFromZero));
        evaluation.OfferId.Should().BeNull();
        evaluation.ReferralCodeId.Should().NotBeNull();
        evaluation.IsReferral.Should().BeTrue();
        evaluation.MessageAr.Should().Contain("الإحالة");
        evaluation.MessageEn.Should().Contain("Referral");
    }

    [Fact]
    public async Task UnknownCode_WithNoReferralMatch_FailsNotFound()
    {
        var (db, service, _) = Create();
        await db.SaveChangesAsync();

        var evaluation = await service.EvaluateAsync("nothing-here", Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid(), 26, 30m);

        evaluation.IsValid.Should().BeFalse();
        evaluation.Status.Should().Be("not_found");
    }

    [Fact]
    public async Task InactiveReferral_FailsInactive()
    {
        var (db, service, _) = Create();
        db.ReferralCodes.Add(SeedReferral("REF-OFF", Guid.NewGuid(), 0.2m, isActive: false));
        await db.SaveChangesAsync();

        var evaluation = await service.EvaluateAsync("ref-off", Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid(), 26, 30m);

        evaluation.Status.Should().Be("inactive");
        evaluation.MessageAr.Should().Contain("الإحالة");
    }

    [Fact]
    public async Task RewardPolicyUnset_FailsHonestly()
    {
        var (db, service, _) = Create();
        var code = SeedReferral("REF-NOPCT", Guid.NewGuid(), 0m);
        code.RewardPercent = null;
        db.ReferralCodes.Add(code);
        await db.SaveChangesAsync();

        var evaluation = await service.EvaluateAsync("ref-nopct", Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid(), 26, 30m);

        evaluation.Status.Should().Be("reward_policy_unset");
        evaluation.DiscountAmount.Should().Be(0m);
    }

    [Fact]
    public async Task SelfReferral_FailsForTheOwner()
    {
        var (db, service, _) = Create();
        var owner = Guid.NewGuid();
        db.ReferralCodes.Add(SeedReferral("REF-OWN", owner, 0.2m));
        await db.SaveChangesAsync();

        var evaluation = await service.EvaluateAsync("ref-own", owner, Guid.NewGuid(), Guid.NewGuid(), 26, 30m);

        evaluation.Status.Should().Be("self_referral");
    }

    [Fact]
    public async Task AlreadyAttributed_FailsForAnotherCustomersCode()
    {
        var (db, service, _) = Create();
        var customer = Guid.NewGuid();
        db.ReferralCodes.Add(SeedReferral("REF-TWICE", Guid.NewGuid(), 0.2m));
        db.ReferralAttributions.Add(new ReferralAttribution
        {
            Code = "REF-OTHER",
            ReferrerUserId = Guid.NewGuid(),
            ReferredCustomerUserId = customer,
            AttributedAtUtc = DateTime.UtcNow,
            RewardSubscriptionId = Guid.NewGuid(),
        });
        await db.SaveChangesAsync();

        var evaluation = await service.EvaluateAsync("ref-twice", customer, Guid.NewGuid(), Guid.NewGuid(), 26, 30m);

        evaluation.Status.Should().Be("already_attributed");
    }

    [Fact]
    public async Task ResumeEvaluationOfOwnHold_IsValid()
    {
        var (db, service, _) = Create();
        var customer = Guid.NewGuid();
        var ownCheckoutSubscriptionId = Guid.NewGuid();
        db.ReferralCodes.Add(SeedReferral("REF-RESUME", Guid.NewGuid(), 0.2m));
        db.ReferralAttributions.Add(new ReferralAttribution
        {
            Code = "REF-RESUME",
            ReferrerUserId = Guid.NewGuid(),
            ReferredCustomerUserId = customer,
            AttributedAtUtc = DateTime.UtcNow,
            RewardSubscriptionId = ownCheckoutSubscriptionId,
        });
        await db.SaveChangesAsync();

        var evaluation = await service.EvaluateAsync("ref-resume", customer, Guid.NewGuid(), Guid.NewGuid(), 26, 30m,
            ct: default, excludeSubscriptionId: ownCheckoutSubscriptionId);

        evaluation.IsValid.Should().BeTrue();
    }

    [Fact]
    public async Task MaxUsesRespected_OwnHoldDoesNotCount()
    {
        var (db, service, _) = Create();
        var customer = Guid.NewGuid();
        var ownCheckoutSubscriptionId = Guid.NewGuid();
        db.ReferralCodes.Add(SeedReferral("REF-CAP", Guid.NewGuid(), 0.2m, usedCount: 1, maxUses: 1));
        db.ReferralAttributions.Add(new ReferralAttribution
        {
            Code = "REF-CAP",
            ReferrerUserId = Guid.NewGuid(),
            ReferredCustomerUserId = customer,
            AttributedAtUtc = DateTime.UtcNow,
            RewardSubscriptionId = ownCheckoutSubscriptionId,
        });
        await db.SaveChangesAsync();

        var resume = await service.EvaluateAsync("ref-cap", customer, Guid.NewGuid(), Guid.NewGuid(), 26, 30m,
            excludeSubscriptionId: ownCheckoutSubscriptionId);
        resume.IsValid.Should().BeTrue();

        var stranger = await service.EvaluateAsync("ref-cap", Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid(), 26, 30m);
        stranger.Status.Should().Be("usage_exhausted");
    }

    [Fact]
    public async Task ReferralHold_CreatesAttribution_PinSnapshot_IncrementsUsage()
    {
        var (db, service, _) = Create();
        var owner = Guid.NewGuid();
        var customer = Guid.NewGuid();
        var subscriptionId = Guid.NewGuid();
        db.ReferralCodes.Add(SeedReferral("REF-HOLD", owner, 0.1m));
        await db.SaveChangesAsync();

        var evaluation = await service.EvaluateAsync("ref-hold", customer, Guid.NewGuid(), Guid.NewGuid(), 26, 30m);
        evaluation.IsValid.Should().BeTrue();
        await service.CreateCheckoutHoldAsync(evaluation, subscriptionId, customer, evaluation.DiscountAmount);

        var attribution = await db.ReferralAttributions.IgnoreQueryFilters()
            .SingleAsync(x => x.ReferredCustomerUserId == customer);
        attribution.RewardSubscriptionId.Should().Be(subscriptionId);
        attribution.ReferrerUserId.Should().Be(owner);
        attribution.RewardPercentSnapshot.Should().Be(0.1m);
        var code = await db.ReferralCodes.SingleAsync(x => x.Code == "REF-HOLD");
        code.UsedCount.Should().Be(1);
    }

    [Fact]
    public async Task RepeatedHoldForSameCheckout_DoesNotDoubleCount()
    {
        var (db, service, _) = Create();
        var customer = Guid.NewGuid();
        var subscriptionId = Guid.NewGuid();
        db.ReferralCodes.Add(SeedReferral("REF-IDEM", Guid.NewGuid(), 0.1m));
        await db.SaveChangesAsync();

        var evaluation = await service.EvaluateAsync("ref-idem", customer, Guid.NewGuid(), Guid.NewGuid(), 26, 30m);
        await service.CreateCheckoutHoldAsync(evaluation, subscriptionId, customer, evaluation.DiscountAmount);
        await service.CreateCheckoutHoldAsync(evaluation, subscriptionId, customer, evaluation.DiscountAmount);

        (await db.ReferralCodes.SingleAsync(x => x.Code == "REF-IDEM")).UsedCount.Should().Be(1);
        db.ReferralAttributions.IgnoreQueryFilters().Count(x => x.ReferredCustomerUserId == customer).Should().Be(1);
    }

    [Fact]
    public async Task Release_RemovesAttribution_RestoresUsage()
    {
        var (db, service, _) = Create();
        var customer = Guid.NewGuid();
        var subscriptionId = Guid.NewGuid();
        db.ReferralCodes.Add(SeedReferral("REF-REL", Guid.NewGuid(), 0.1m));
        await db.SaveChangesAsync();

        var evaluation = await service.EvaluateAsync("ref-rel", customer, Guid.NewGuid(), Guid.NewGuid(), 26, 30m);
        await service.CreateCheckoutHoldAsync(evaluation, subscriptionId, customer, evaluation.DiscountAmount);
        await service.ReleaseCheckoutRedemptionAsync(subscriptionId);

        db.ReferralAttributions
            .Count(x => x.ReferredCustomerUserId == customer).Should().Be(0);
        (await db.ReferralCodes.SingleAsync(x => x.Code == "REF-REL")).UsedCount.Should().Be(0);
    }

    [Fact]
    public async Task ZeroOrNegativeDiscount_IsRejectedIneligible()
    {
        var (db, service, _) = Create();
        db.ReferralCodes.Add(SeedReferral("REF-ZERO", Guid.NewGuid(), 0m));
        await db.SaveChangesAsync();

        var evaluation = await service.EvaluateAsync("ref-zero", Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid(), 26, 30m);

        evaluation.IsValid.Should().BeFalse();
        evaluation.Status.Should().Be("ineligible");
    }

    private sealed class DateTimeProviderStub : IDateTimeProvider
    {
        public DateTime UtcNow => DateTime.UtcNow;
        public DateOnly TodayUtc => DateOnly.FromDateTime(DateTime.UtcNow);
    }
}
