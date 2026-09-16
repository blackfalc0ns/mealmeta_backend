using MealMate.Domain.Common;
using MealMate.Domain.ValueObjects;

namespace MealMate.Domain.CollaborativeCampaigns;

public sealed class CampaignBundle : AuditableEntity
{
    public Guid CampaignId { get; set; }

    public Campaign Campaign { get; set; } = default!;

    public Guid BundleId { get; set; }

    public ICollection<CampaignBundleDurationPrice> DurationPrices { get; set; } = new List<CampaignBundleDurationPrice>();

    public Result AddDurationPrice(int durationDays, Money referenceCustomerPrice, string source, int version)
    {
        if (DurationPrices.Any(dp => dp.DurationDays == durationDays))
        {
            return Result.Failure($"Duration price for {durationDays} days already exists for this bundle.");
        }

        var createResult = CampaignBundleDurationPrice.Create(Id, durationDays, referenceCustomerPrice, source, version);
        if (createResult.IsFailure)
        {
            return Result.Failure(createResult.Error!);
        }

        DurationPrices.Add(createResult.Value);
        return Result.Success();
    }

    public Result<CampaignBundleDurationPrice> GetDurationPrice(int durationDays)
    {
        var price = DurationPrices.FirstOrDefault(dp => dp.DurationDays == durationDays);
        if (price is null)
        {
            return Result.Failure<CampaignBundleDurationPrice>($"No reference price found for duration of {durationDays} days.");
        }

        return Result.Success(price);
    }
}
