using MealMate.Domain.Common;

namespace MealMate.Domain.CollaborativeCampaigns.ValueObjects;

public sealed class RoundingPolicySnapshot : ValueObject
{
    public int CurrencyPrecision { get; }
    public MidpointRounding RoundingMode { get; }
    public int RoundingPolicyVersion { get; }

    public RoundingPolicySnapshot(
        int currencyPrecision,
        MidpointRounding roundingMode,
        int roundingPolicyVersion)
    {
        if (currencyPrecision is < 0 or > 4)
        {
            throw new ArgumentOutOfRangeException(
                nameof(currencyPrecision),
                "Currency precision must be between 0 and 4 decimal places.");
        }

        if (roundingPolicyVersion < 1)
        {
            throw new ArgumentOutOfRangeException(
                nameof(roundingPolicyVersion),
                "Rounding policy version must be at least 1.");
        }

        CurrencyPrecision = currencyPrecision;
        RoundingMode = roundingMode;
        RoundingPolicyVersion = roundingPolicyVersion;
    }

    public static RoundingPolicySnapshot DefaultKwd() =>
        new(3, MidpointRounding.AwayFromZero, 1);

    public decimal Round(decimal amount) =>
        Math.Round(amount, CurrencyPrecision, RoundingMode);

    protected override IEnumerable<object?> GetEqualityComponents()
    {
        yield return CurrencyPrecision;
        yield return (int)RoundingMode;
        yield return RoundingPolicyVersion;
    }
}
