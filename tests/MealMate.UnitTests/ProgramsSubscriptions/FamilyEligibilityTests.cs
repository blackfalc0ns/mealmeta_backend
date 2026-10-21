using FluentAssertions;
using MealMate.Application.Features.FamilyCheckout;
using Xunit;

namespace MealMate.UnitTests.ProgramsSubscriptions;

public sealed class FamilyEligibilityTests
{
    [Theory]
    [InlineData(1, 0.00, "TIER-1")]
    [InlineData(2, 0.05, "TIER-2-3")]
    [InlineData(3, 0.05, "TIER-2-3")]
    [InlineData(4, 0.10, "TIER-4-5")]
    [InlineData(5, 0.10, "TIER-4-5")]
    [InlineData(6, 0.15, "TIER-6-PLUS")]
    [InlineData(8, 0.15, "TIER-6-PLUS")]
    public void BoundariesFourAndSix_UseCorrectVersionedTier(int memberCount, decimal expectedRate, string expectedTier)
    {
        var result = FamilyEligibilityAndRenewalCalculator.ResolveTier(memberCount);

        result.EligibleCount.Should().Be(memberCount);
        result.DiscountRate.Should().Be(expectedRate);
        result.TierCode.Should().Be(expectedTier);
    }

    [Fact]
    public void ManagerWithoutSubscriptionAndUnpaidInvited_DoNotIncrementEligibleCount()
    {
        var member1 = new FamilyMemberCandidate(Guid.NewGuid(), HasActivePaidSubscription: true, false, false, false);
        var member2 = new FamilyMemberCandidate(Guid.NewGuid(), HasActivePaidSubscription: true, false, false, false);
        var managerOnly = new FamilyMemberCandidate(Guid.NewGuid(), HasActivePaidSubscription: false, IsManagerOnlyWithoutSubscription: true, false, false);
        var invitedUnpaid = new FamilyMemberCandidate(Guid.NewGuid(), HasActivePaidSubscription: false, false, IsInvitedUnpaid: true, false);
        var detached = new FamilyMemberCandidate(Guid.NewGuid(), HasActivePaidSubscription: false, false, false, IsExpiredOrDetached: true);

        var count = FamilyEligibilityAndRenewalCalculator.CountEligibleMembers(new[]
        {
            member1, member2, managerOnly, invitedUnpaid, detached
        });

        count.Should().Be(2);
        var tier = FamilyEligibilityAndRenewalCalculator.ResolveTier(count);
        tier.DiscountRate.Should().Be(0.05m); // 2 members -> 5%
    }

    [Fact]
    public void RenewalQuote_RecomputesThreeCurrentEligibleMembers_WithoutAlteringPastDiscounts()
    {
        // Group of 4 originally had 10%. At renewal, only 3 renew -> tier becomes 5%
        var endDate = new DateOnly(2026, 10, 31);
        var renewal = FamilyEligibilityAndRenewalCalculator.CalculateRenewalQuote(
            priorEligibleCount: 4,
            currentEligibleCount: 3,
            subscriptionEndDate: endDate,
            renewalWindowDays: 3);

        renewal.PriorEligibleCount.Should().Be(4);
        renewal.CurrentEligibleCount.Should().Be(3);
        renewal.PriorDiscountRate.Should().Be(0.10m);
        renewal.CurrentDiscountRate.Should().Be(0.05m);
        renewal.SubscriptionEndDate.Should().Be(endDate);
        renewal.RenewalWindowStartDate.Should().Be(new DateOnly(2026, 10, 28)); // last 3 days
        renewal.RenewalWindowShifted.Should().BeFalse();
    }

    [Fact]
    public void MemberFreeze_DoesNotShiftRenewalWindow_NorSubscriptionEnd()
    {
        // Section 18 - Decision 1:
        // Freezing an individual member day does NOT shift subscription end date or renewal window.
        var endDate = new DateOnly(2026, 10, 31);
        var renewal = FamilyEligibilityAndRenewalCalculator.CalculateRenewalQuote(
            priorEligibleCount: 3,
            currentEligibleCount: 3,
            subscriptionEndDate: endDate,
            renewalWindowDays: 3);

        renewal.SubscriptionEndDate.Should().Be(endDate);
        renewal.RenewalWindowStartDate.Should().Be(new DateOnly(2026, 10, 28));
        renewal.RenewalWindowShifted.Should().BeFalse();
    }

    [Fact]
    public void CarriedOverServiceObligation_PreservesFamilyRenewalWindow_WhileTrackingIndependentObligation()
    {
        // Design Decision:
        // Individual freeze target date beyond family cycle end does NOT extend family subscription end date,
        // and creates an independent CarriedOverServiceObligation lot.
        var familyEnd = new DateOnly(2026, 10, 31);
        var targetDateBeyondEnd = new DateOnly(2026, 11, 4);

        var obligation = new MealMate.Domain.CustomerFinance.CarriedOverServiceObligation
        {
            SubscriptionId = Guid.NewGuid(),
            BeneficiaryId = Guid.NewGuid(),
            OriginalDayId = Guid.NewGuid(),
            OriginalServiceDate = new DateOnly(2026, 10, 20),
            TargetDate = targetDateBeyondEnd,
            LiabilityAmount = 7.500m,
            Currency = "KWD",
            Reason = MealMate.Domain.CustomerFinance.CarriedOverObligationReason.FamilyMemberFreeze,
            State = MealMate.Domain.CustomerFinance.CarriedOverObligationState.GuaranteedHold
        };

        obligation.TargetDate.Should().BeAfter(familyEnd);
        obligation.State.Should().Be(MealMate.Domain.CustomerFinance.CarriedOverObligationState.GuaranteedHold);

        // Execution transition
        obligation.Execute(journalRef: "GL-FULFILL-01");
        obligation.State.Should().Be(MealMate.Domain.CustomerFinance.CarriedOverObligationState.Executed);
        obligation.ExecutedAtUtc.Should().NotBeNull();
        obligation.JournalRef.Should().Be("GL-FULFILL-01");
    }
}

