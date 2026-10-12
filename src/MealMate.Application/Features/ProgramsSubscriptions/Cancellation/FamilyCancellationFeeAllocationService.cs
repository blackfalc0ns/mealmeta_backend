namespace MealMate.Application.Features.ProgramsSubscriptions.Cancellation;

/// <summary>Distributes a cancellation fee over proven payer gross without exceeding any payer's entitlement.</summary>
public static class FamilyCancellationFeeAllocationService
{
    public static IReadOnlyDictionary<Guid, decimal> Allocate(
        IReadOnlyDictionary<Guid, decimal> eligibleGrossByPayer,
        decimal totalFee,
        int currencyMinorDigits)
    {
        ArgumentNullException.ThrowIfNull(eligibleGrossByPayer);
        if (currencyMinorDigits is < 0 or > 4 || totalFee < 0m
            || eligibleGrossByPayer.Values.Any(x => x < 0m))
            throw new ArgumentException("Fee and payer gross must be nonnegative valid currency amounts.");

        var scale = (decimal)Math.Pow(10, currencyMinorDigits);
        if (totalFee * scale != decimal.Truncate(totalFee * scale)
            || eligibleGrossByPayer.Values.Any(x => x * scale != decimal.Truncate(x * scale)))
            throw new ArgumentException("Fee and payer gross must match the currency precision.");

        var totalGross = eligibleGrossByPayer.Values.Sum();
        if (totalFee > totalGross)
            throw new ArgumentException("Fee cannot exceed proven payer gross.");

        var grossUnits = eligibleGrossByPayer.ToDictionary(x => x.Key, x => x.Value * scale);
        var feeUnits = totalFee * scale;
        if (totalGross == 0m)
        {
            if (feeUnits != 0m) throw new ArgumentException("A nonzero fee requires proven payer gross.");
            return eligibleGrossByPayer.Keys.ToDictionary(x => x, _ => 0m);
        }

        var shares = new Dictionary<Guid, decimal>(grossUnits.Count);
        var remainders = new List<(Guid PayerId, decimal Remainder)>();
        foreach (var payer in grossUnits)
        {
            var exactShare = feeUnits * payer.Value / (totalGross * scale);
            var wholeUnits = decimal.Floor(exactShare);
            shares.Add(payer.Key, wholeUnits);
            remainders.Add((payer.Key, exactShare - wholeUnits));
        }

        var remainingUnits = feeUnits - shares.Values.Sum();
        if (remainingUnits < 0m || remainingUnits > remainders.Count)
            throw new ArgumentException("Fee allocation could not be represented in currency minor units.");
        foreach (var payer in remainders.OrderByDescending(x => x.Remainder).ThenBy(x => x.PayerId)
                     .Take(decimal.ToInt32(remainingUnits)))
            shares[payer.PayerId]++;

        var result = shares.ToDictionary(x => x.Key, x => x.Value / scale);
        if (result.Values.Sum() != totalFee
            || result.Any(x => x.Value < 0m || x.Value > eligibleGrossByPayer[x.Key]
                || x.Value * scale != decimal.Truncate(x.Value * scale)))
            throw new ArgumentException("Fee allocation violated payer bounds or conservation.");

        return result;
    }
}
