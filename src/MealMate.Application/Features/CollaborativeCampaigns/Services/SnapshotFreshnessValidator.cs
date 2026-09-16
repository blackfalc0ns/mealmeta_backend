namespace MealMate.Application.Features.CollaborativeCampaigns.Services;

public sealed record CampaignVersionVector(
    int CampaignVersion,
    int OffersVersion,
    int PricingVersion,
    int CapacityVersion,
    int RoundingPolicyVersion);

public sealed record SnapshotFreshnessResult(
    bool IsFresh,
    IReadOnlyList<string> StaleReasons);

public interface ISnapshotFreshnessValidator
{
    SnapshotFreshnessResult Validate(CampaignVersionVector current, CampaignVersionVector snapshot);
}

public sealed class SnapshotFreshnessValidator : ISnapshotFreshnessValidator
{
    public SnapshotFreshnessResult Validate(CampaignVersionVector current, CampaignVersionVector snapshot)
    {
        var reasons = new List<string>();

        if (current.CampaignVersion != snapshot.CampaignVersion)
        {
            reasons.Add($"CampaignVersion mismatch: current {current.CampaignVersion} vs snapshot {snapshot.CampaignVersion}");
        }

        if (current.OffersVersion != snapshot.OffersVersion)
        {
            reasons.Add($"OffersVersion mismatch: current {current.OffersVersion} vs snapshot {snapshot.OffersVersion}");
        }

        if (current.PricingVersion != snapshot.PricingVersion)
        {
            reasons.Add($"PricingVersion mismatch: current {current.PricingVersion} vs snapshot {snapshot.PricingVersion}");
        }

        if (current.CapacityVersion != snapshot.CapacityVersion)
        {
            reasons.Add($"CapacityVersion mismatch: current {current.CapacityVersion} vs snapshot {snapshot.CapacityVersion}");
        }

        if (current.RoundingPolicyVersion != snapshot.RoundingPolicyVersion)
        {
            reasons.Add($"RoundingPolicyVersion mismatch: current {current.RoundingPolicyVersion} vs snapshot {snapshot.RoundingPolicyVersion}");
        }

        return new SnapshotFreshnessResult(reasons.Count == 0, reasons);
    }
}
