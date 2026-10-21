using System.Numerics;

namespace MealMate.Domain.ValueObjects;

public static class MoneyRoundingPolicy
{
    public const string KwdCurrencyCode = "KWD";
    public const int FinalScale = 3;

    private const decimal FilsScale = 1_000m;
    public static decimal RoundFinalKwd(decimal value) =>
        decimal.Round(value, FinalScale, MidpointRounding.AwayFromZero);

    public static IReadOnlyList<AllocatedItem> AllocateKwd(decimal totalKwd, IReadOnlyList<(string ItemKey, decimal Weight)> items)
    {
        ArgumentNullException.ThrowIfNull(items);

        var total = RoundFinalKwd(totalKwd);
        if (total < 0m)
        {
            throw new ArgumentOutOfRangeException(nameof(totalKwd), "The allocation total must be non-negative.");
        }

        if (items.Count == 0)
        {
            throw new ArgumentException("At least one allocation item is required.", nameof(items));
        }

        var keys = new string[items.Count];
        var weights = new decimal[items.Count];
        for (var i = 0; i < items.Count; i++)
        {
            if (items[i].Weight < 0m)
            {
                throw new ArgumentOutOfRangeException(nameof(items), "Allocation weights must be non-negative.");
            }

            keys[i] = items[i].ItemKey;
            weights[i] = items[i].Weight;
        }

        if (keys.Distinct(StringComparer.Ordinal).Count() != keys.Length)
        {
            throw new ArgumentException("Allocation item keys must be distinct.", nameof(items));
        }

        var totalFils = ToFils(total);
        if (totalFils == 0L)
        {
            return items.Select(item => new AllocatedItem(item.ItemKey, 0m)).ToArray();
        }

        var commonScale = weights
            .Select(GetScale)
            .DefaultIfEmpty(0)
            .Max();
        var exactWeights = new BigInteger[weights.Length];
        var weightSum = BigInteger.Zero;
        for (var i = 0; i < weights.Length; i++)
        {
            exactWeights[i] = ToUnscaled(weights[i]) * BigInteger.Pow(10, commonScale - GetScale(weights[i]));
            weightSum += exactWeights[i];
        }

        if (weightSum == 0L)
        {
            throw new ArgumentException("The weight sum must be positive when allocating a positive total.", nameof(items));
        }

        var floors = new long[items.Count];
        var residuals = new BigInteger[items.Count];
        long allocatedFils = 0;
        for (var i = 0; i < items.Count; i++)
        {
            var numerator = totalFils * exactWeights[i];
            floors[i] = checked((long)(numerator / weightSum));
            residuals[i] = numerator % weightSum;
            allocatedFils = checked(allocatedFils + floors[i]);
        }

        var remainderFils = checked(totalFils - allocatedFils);
        foreach (var index in Enumerable.Range(0, items.Count)
                     .OrderByDescending(i => residuals[i])
                     .ThenBy(i => keys[i], StringComparer.Ordinal)
                     .Take((int)remainderFils))
        {
            floors[index]++;
        }

        var allocations = new AllocatedItem[items.Count];
        for (var i = 0; i < items.Count; i++)
        {
            allocations[i] = new AllocatedItem(keys[i], FromFils(floors[i]));
        }

        return allocations;
    }

    private static long ToFils(decimal total) => checked((long)(total * FilsScale));

    private static decimal FromFils(long fils) => fils / FilsScale;

    private static int GetScale(decimal value) => (decimal.GetBits(value)[3] >> 16) & 0x7F;

    private static BigInteger ToUnscaled(decimal value)
    {
        var bits = decimal.GetBits(value);
        var magnitude = (BigInteger)(uint)bits[0]
            | ((BigInteger)(uint)bits[1] << 32)
            | ((BigInteger)(uint)bits[2] << 64);
        return (bits[3] & int.MinValue) == 0 ? magnitude : -magnitude;
    }
}

public readonly record struct AllocatedItem(string ItemKey, decimal AmountKwd);
