namespace MealMate.Application.Features.ProgramsSubscriptions.Pricing;

/// <summary>
/// C04 — statistical guard against inflated reference box prices within a program+bundle cohort.
/// Hard-rejects at write time so admin does not need a fixed manual ceiling.
/// </summary>
public static class RestaurantReferencePriceGuard
{
    public const int MinSampleForAutoCap = 4;
    public const decimal IqrMultiplier = 1.5m;
    public const decimal SigmaMultiplier = 2m;

    public sealed record AutoCapResult(
        bool CanEnforce,
        decimal? MaxReferencePrice26,
        string Method,
        int SampleSize,
        decimal? MeanDaily,
        decimal? UpperDailyFence);

    public static AutoCapResult Evaluate(IReadOnlyList<decimal> peerReferencePrice26)
    {
        // Caller should already exclude the submitting restaurant's own active row.
        var amounts = peerReferencePrice26
            .Where(x => x > 0m)
            .OrderBy(x => x)
            .ToList();

        if (amounts.Count < MinSampleForAutoCap)
        {
            return new AutoCapResult(false, null, "insufficient_sample", amounts.Count, null, null);
        }

        var daily = amounts.Select(x => x / 26m).OrderBy(x => x).ToList();
        var mean = daily.Average();
        var upperDaily = ResolveUpperFence(daily, out var method);
        if (upperDaily is null)
            return new AutoCapResult(false, null, "no_fence", amounts.Count, mean, null);

        var max26 = Math.Round(upperDaily.Value * 26m, 3, MidpointRounding.AwayFromZero);
        return new AutoCapResult(true, max26, method, amounts.Count, mean, upperDaily);
    }

    public static decimal? ResolveEffectiveCeiling(decimal manualCeiling26, AutoCapResult auto)
    {
        decimal? autoCap = auto.CanEnforce ? auto.MaxReferencePrice26 : null;
        if (manualCeiling26 > 0m && autoCap is decimal a)
            return Math.Min(manualCeiling26, a);
        if (manualCeiling26 > 0m)
            return manualCeiling26;
        return autoCap;
    }

    private static decimal? ResolveUpperFence(IReadOnlyList<decimal> sortedDaily, out string method)
    {
        method = "iqr";
        var n = sortedDaily.Count;
        if (n < 2) return null;

        var q1 = Percentile(sortedDaily, 0.25m);
        var q3 = Percentile(sortedDaily, 0.75m);
        var iqr = q3 - q1;
        if (iqr > 0m)
        {
            method = "iqr_1.5";
            return q3 + (IqrMultiplier * iqr);
        }

        // Degenerate IQR (many identical prices): fall back to mean + 2σ (same spirit as classification outlier).
        var mean = sortedDaily.Average();
        var variance = sortedDaily.Sum(x => (x - mean) * (x - mean)) / n;
        var sd = DecimalPricingMath.Sqrt(variance);
        if (sd <= 0m)
        {
            // All equal — allow a modest headroom (15%) above the cohort so a new higher offer can enter review via pending change.
            method = "flat_cohort_headroom";
            return mean * 1.15m;
        }

        method = "mean_plus_2sd";
        return mean + (SigmaMultiplier * sd);
    }

    private static decimal Percentile(IReadOnlyList<decimal> sorted, decimal percentile)
    {
        if (sorted.Count == 1) return sorted[0];
        var pos = percentile * (sorted.Count - 1);
        var lo = (int)decimal.Floor(pos);
        var hi = (int)decimal.Ceiling(pos);
        if (lo == hi) return sorted[lo];
        var weight = pos - lo;
        return sorted[lo] + (sorted[hi] - sorted[lo]) * weight;
    }
}
