namespace MealMate.Application.Features.FamilyCheckout;

public sealed record FamilyMemberCandidate(
    Guid MemberId,
    bool HasActivePaidSubscription,
    bool IsManagerOnlyWithoutSubscription,
    bool IsInvitedUnpaid,
    bool IsExpiredOrDetached);

public sealed record FamilyTierResolutionResult(
    int EligibleCount,
    decimal DiscountRate,
    string TierCode);

public sealed record FamilyRenewalCalculationResult(
    int PriorEligibleCount,
    int CurrentEligibleCount,
    decimal PriorDiscountRate,
    decimal CurrentDiscountRate,
    DateOnly SubscriptionEndDate,
    DateOnly RenewalWindowStartDate,
    bool RenewalWindowShifted);

/// <summary>
/// F06.6 & Section 6 of Financial Plan:
/// Pure calculation engine for family discount tier resolution and renewal quoting.
/// Rules:
/// - Eligible count counts only distinct active paid members (not manager without subscription, unpaid, or detached).
/// - Current source tiers: 1=0%, 2-3=5%, 4-5=10%, 6+=15%.
/// - Boundary 4 enters 4-5 tier, boundary 6 enters 6+ tier.
/// - When 4 members become 3 at renewal, the new quote recomputes using tier 3 (5%) without clawing back past discounts from remaining members.
/// - Freezing an individual member does NOT extend the family subscription cycle or shift renewal dates.
/// </summary>
public static class FamilyEligibilityAndRenewalCalculator
{
    public static int CountEligibleMembers(IEnumerable<FamilyMemberCandidate> candidates)
    {
        ArgumentNullException.ThrowIfNull(candidates);

        return candidates
            .Where(c => c.HasActivePaidSubscription &&
                        !c.IsManagerOnlyWithoutSubscription &&
                        !c.IsInvitedUnpaid &&
                        !c.IsExpiredOrDetached)
            .Select(c => c.MemberId)
            .Distinct()
            .Count();
    }

    public static FamilyTierResolutionResult ResolveTier(int eligibleCount)
    {
        if (eligibleCount <= 1)
        {
            return new FamilyTierResolutionResult(eligibleCount, 0.00m, "TIER-1");
        }

        if (eligibleCount <= 3) // 2-3
        {
            return new FamilyTierResolutionResult(eligibleCount, 0.05m, "TIER-2-3");
        }

        if (eligibleCount <= 5) // 4-5
        {
            return new FamilyTierResolutionResult(eligibleCount, 0.10m, "TIER-4-5");
        }

        // 6+
        return new FamilyTierResolutionResult(eligibleCount, 0.15m, "TIER-6-PLUS");
    }

    public static FamilyRenewalCalculationResult CalculateRenewalQuote(
        int priorEligibleCount,
        int currentEligibleCount,
        DateOnly subscriptionEndDate,
        int renewalWindowDays = 3)
    {
        var priorTier = ResolveTier(priorEligibleCount);
        var currentTier = ResolveTier(currentEligibleCount);

        var renewalWindowStartDate = subscriptionEndDate.AddDays(-renewalWindowDays);

        return new FamilyRenewalCalculationResult(
            priorEligibleCount,
            currentEligibleCount,
            priorTier.DiscountRate,
            currentTier.DiscountRate,
            subscriptionEndDate,
            renewalWindowStartDate,
            RenewalWindowShifted: false);
    }
}
