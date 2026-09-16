using MealMate.Domain.CollaborativeCampaigns;
using MealMate.Domain.Enums;

namespace MealMate.Application.Features.CollaborativeCampaigns.Services;

public enum CustomerSegmentFilter
{
    All = 0,
    NewCustomersOnly = 1,
    ExistingCustomersOnly = 2
}

public sealed record CustomerEligibilityProfile(
    Guid CustomerId,
    int PriorSubscriptionCount,
    int ActiveCampaignSubscriptionCount,
    string? DeliveryGovernorate = null,
    int? RequestedDurationDays = null);

public sealed record CampaignEligibilityCriteria(
    CustomerSegmentFilter CustomerSegment = CustomerSegmentFilter.All,
    int PerCustomerLimit = 1,
    IReadOnlyCollection<string>? AllowedGovernorates = null,
    int? MinDurationDays = null,
    int? MaxDurationDays = null);

public sealed record CampaignEligibilityResult(
    bool IsEligible,
    IReadOnlyList<string> IneligibilityReasons);

public interface ICampaignEligibilityEngine
{
    CampaignEligibilityResult Evaluate(
        Campaign campaign,
        CustomerEligibilityProfile customer,
        CampaignEligibilityCriteria criteria,
        DateTime evaluationTimeUtc);
}

public sealed class CampaignEligibilityEngine : ICampaignEligibilityEngine
{
    public CampaignEligibilityResult Evaluate(
        Campaign campaign,
        CustomerEligibilityProfile customer,
        CampaignEligibilityCriteria criteria,
        DateTime evaluationTimeUtc)
    {
        var reasons = new List<string>();

        // 1. Campaign Status Check
        if (campaign.Status != CampaignStatus.Active)
        {
            reasons.Add($"Campaign is not active (current status is '{campaign.Status}').");
        }

        // 2. Sales Window Check
        if (evaluationTimeUtc < campaign.StartsAtUtc || evaluationTimeUtc > campaign.EndsAtUtc)
        {
            reasons.Add($"Current time ({evaluationTimeUtc:u}) is outside the campaign sales window ({campaign.StartsAtUtc:u} - {campaign.EndsAtUtc:u}).");
        }

        // 3. Customer Segment Check
        if (criteria.CustomerSegment == CustomerSegmentFilter.NewCustomersOnly && customer.PriorSubscriptionCount > 0)
        {
            reasons.Add("Campaign is restricted to new customers only; customer has prior subscriptions.");
        }
        else if (criteria.CustomerSegment == CustomerSegmentFilter.ExistingCustomersOnly && customer.PriorSubscriptionCount == 0)
        {
            reasons.Add("Campaign is restricted to existing subscribers only; customer has no prior subscription history.");
        }

        // 4. Per-Customer Campaign Subscription Limit
        if (customer.ActiveCampaignSubscriptionCount >= criteria.PerCustomerLimit)
        {
            reasons.Add($"Customer has reached the subscription limit ({criteria.PerCustomerLimit}) for this campaign (currently active: {customer.ActiveCampaignSubscriptionCount}).");
        }

        // 5. Geographic / Delivery Area Check
        if (criteria.AllowedGovernorates is { Count: > 0 })
        {
            if (string.IsNullOrWhiteSpace(customer.DeliveryGovernorate) ||
                !criteria.AllowedGovernorates.Contains(customer.DeliveryGovernorate, StringComparer.OrdinalIgnoreCase))
            {
                reasons.Add($"Customer delivery location ('{customer.DeliveryGovernorate}') is outside the campaign service area.");
            }
        }

        // 6. Duration Bounds Check
        if (customer.RequestedDurationDays.HasValue)
        {
            var requested = customer.RequestedDurationDays.Value;
            if (criteria.MinDurationDays.HasValue && requested < criteria.MinDurationDays.Value)
            {
                reasons.Add($"Requested subscription duration ({requested} days) is below the minimum allowed duration ({criteria.MinDurationDays.Value} days).");
            }
            if (criteria.MaxDurationDays.HasValue && requested > criteria.MaxDurationDays.Value)
            {
                reasons.Add($"Requested subscription duration ({requested} days) exceeds the maximum allowed duration ({criteria.MaxDurationDays.Value} days).");
            }
        }

        return new CampaignEligibilityResult(reasons.Count == 0, reasons);
    }
}
