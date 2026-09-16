using MealMate.Domain.Common;
using MealMate.Domain.ValueObjects;

namespace MealMate.Domain.CollaborativeCampaigns;

public sealed class CampaignBundleDurationPrice : BaseEntity
{
    public Guid CampaignBundleId { get; private set; }

    public CampaignBundle CampaignBundle { get; private set; } = default!;

    public int DurationDays { get; private set; }

    public Money ReferenceCustomerPrice { get; private set; } = default!;

    public string Source { get; private set; } = default!;

    public int Version { get; private set; }

    public DateTime CapturedAtUtc { get; private set; }

    private CampaignBundleDurationPrice()
    {
    }

    private CampaignBundleDurationPrice(
        Guid campaignBundleId,
        int durationDays,
        Money referenceCustomerPrice,
        string source,
        int version,
        DateTime capturedAtUtc)
    {
        CampaignBundleId = campaignBundleId;
        DurationDays = durationDays;
        ReferenceCustomerPrice = referenceCustomerPrice;
        Source = source;
        Version = version;
        CapturedAtUtc = capturedAtUtc;
    }

    public static Result<CampaignBundleDurationPrice> Create(
        Guid campaignBundleId,
        int durationDays,
        Money referenceCustomerPrice,
        string source,
        int version,
        DateTime? capturedAtUtc = null)
    {
        if (durationDays <= 0)
        {
            return Result.Failure<CampaignBundleDurationPrice>("Duration days must be greater than zero.");
        }

        if (referenceCustomerPrice == null || referenceCustomerPrice.Amount <= 0m)
        {
            return Result.Failure<CampaignBundleDurationPrice>("Reference customer price must be greater than zero.");
        }

        if (string.IsNullOrWhiteSpace(source))
        {
            return Result.Failure<CampaignBundleDurationPrice>("Price source must be specified.");
        }

        if (version < 1)
        {
            return Result.Failure<CampaignBundleDurationPrice>("Price version must be at least 1.");
        }

        return Result.Success(new CampaignBundleDurationPrice(
            campaignBundleId,
            durationDays,
            referenceCustomerPrice,
            source.Trim(),
            version,
            capturedAtUtc ?? DateTime.UtcNow));
    }
}
