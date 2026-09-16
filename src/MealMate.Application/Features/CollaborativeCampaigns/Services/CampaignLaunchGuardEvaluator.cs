using MealMate.Domain.CollaborativeCampaigns;
using MealMate.Domain.Enums;

namespace MealMate.Application.Features.CollaborativeCampaigns.Services;

public sealed record LaunchGuardEvaluationContext(
    Campaign Campaign,
    CampaignProfitabilitySnapshot? ProfitabilitySnapshot,
    CampaignTierSnapshot? TierSnapshot,
    SnapshotFreshnessResult FreshnessResult,
    int AcceptedRestaurantCount,
    int TotalDedicatedDailyCapacity);

public sealed record LaunchGuardEvaluationResult(
    bool CanLaunch,
    IReadOnlyList<string> BlockingReasons,
    IReadOnlyList<string> Warnings);

public interface ICampaignLaunchGuardEvaluator
{
    LaunchGuardEvaluationResult Evaluate(LaunchGuardEvaluationContext context);
}

public sealed class CampaignLaunchGuardEvaluator : ICampaignLaunchGuardEvaluator
{
    public LaunchGuardEvaluationResult Evaluate(LaunchGuardEvaluationContext context)
    {
        var blocking = new List<string>();
        var warnings = new List<string>();
        var campaign = context.Campaign;

        // 1. Status Check
        if (campaign.Status is not (CampaignStatus.UnderReview or CampaignStatus.Scheduled))
        {
            blocking.Add($"Campaign must be in 'UnderReview' or 'Scheduled' state to be evaluated for launch, current state is '{campaign.Status}'.");
        }

        // 2. Dates Check
        if (campaign.StartsAtUtc >= campaign.EndsAtUtc)
        {
            blocking.Add("Customer sales start date must precede sales end date.");
        }

        if (campaign.EnrollmentClosesAtUtc.HasValue && campaign.EnrollmentClosesAtUtc.Value >= campaign.StartsAtUtc)
        {
            blocking.Add("Restaurant joining close date must precede customer sales start date.");
        }

        // 3. Restaurant & Capacity Coverage Check
        if (context.AcceptedRestaurantCount <= 0)
        {
            blocking.Add("Campaign has no accepted restaurant offers.");
        }

        if (context.TotalDedicatedDailyCapacity <= 0)
        {
            blocking.Add("Total dedicated daily capacity must be greater than zero.");
        }

        // 4. Snapshots & Freshness
        if (context.ProfitabilitySnapshot is null)
        {
            blocking.Add("Profitability snapshot is missing.");
        }

        if (context.TierSnapshot is null)
        {
            blocking.Add("Tier ranking snapshot is missing.");
        }

        if (!context.FreshnessResult.IsFresh)
        {
            foreach (var staleReason in context.FreshnessResult.StaleReasons)
            {
                blocking.Add($"Snapshot stale: {staleReason}");
            }
        }

        // 5. Profitability & Margin Checks
        if (context.ProfitabilitySnapshot is not null)
        {
            var p = context.ProfitabilitySnapshot;
            if (!p.IsViable)
            {
                blocking.Add($"Campaign expected margin is negative ({p.OverallExpectedMarginPct:P2}); negative margin campaigns are strictly prohibited from launching.");
            }
            else if (p.RequiresOverride)
            {
                var hasValidOverride = !string.IsNullOrWhiteSpace(p.OverrideReason) && p.OverrideApprovedByUserId.HasValue;
                if (!hasValidOverride)
                {
                    blocking.Add($"Campaign margin ({p.OverallExpectedMarginPct:P2}) is below threshold ({p.MinimumExpectedMarginPct:P2}) and lacks an authorized financial override.");
                }
                else
                {
                    warnings.Add($"Campaign is launching under an authorized profitability override: {p.OverrideReason} (Approved by {p.OverrideApprovedByUserId}).");
                }
            }
        }

        // 6. Non-blocking Warnings
        if (campaign.MaxSubscribers is null)
        {
            warnings.Add("Campaign has no subscriber cap configured.");
        }

        return new LaunchGuardEvaluationResult(blocking.Count == 0, blocking, warnings);
    }
}
