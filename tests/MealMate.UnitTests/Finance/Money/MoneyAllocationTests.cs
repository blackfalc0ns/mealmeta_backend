using FluentAssertions;
using FluentAssertions.Execution;
using MealMate.Domain.ValueObjects;
using Xunit;

namespace MealMate.UnitTests.Finance;

/// <summary>
/// F01.2 — KWD final quantization (explicit HALF_UP at the fils midpoint) and
/// largest-remainder allocation with a stable, order-independent tie order.
/// </summary>
public sealed class MoneyAllocationTests
{
    [Fact]
    public void HalfUpAndLargestRemainderPreserveTotal()
    {
        // 20.000 KWD split across 26 equal days: 6 x 0.770 + 20 x 0.769 = 20.000
        var items = Enumerable.Range(0, 26)
            .Select(i => (ItemKey: i.ToString("D2", System.Globalization.CultureInfo.InvariantCulture), Weight: 1m))
            .ToArray();

        var shares = MoneyRoundingPolicy.AllocateKwd(20.000m, items);

        using (new AssertionScope())
        {
            shares.Should().HaveCount(26);
            shares.Count(s => s.AmountKwd == 0.770m).Should().Be(6, "the first 6 items by stable key order absorb the 6 remaining fils");
            shares.Count(s => s.AmountKwd == 0.769m).Should().Be(20);
            shares.Sum(s => s.AmountKwd).Should().Be(20.000m, "the exact fils total must be preserved");
            shares.Select(s => s.ItemKey).Should().OnlyHaveUniqueItems();
        }
    }

    [Fact]
    public void HalfFilsRoundsAwayFromZeroOnBothSigns()
    {
        using (new AssertionScope())
        {
            MoneyRoundingPolicy.RoundFinalKwd(1.0005m).Should().Be(1.001m);
            MoneyRoundingPolicy.RoundFinalKwd(-1.0005m).Should().Be(-1.001m);
            MoneyRoundingPolicy.RoundFinalKwd(2.6755m).Should().Be(2.676m);
            MoneyRoundingPolicy.RoundFinalKwd(-0.0005m).Should().Be(-0.001m);
        }
    }

    [Fact]
    public void AllocationIsOrderIndependentForEqualWeights()
    {
        var ordered = Enumerable.Range(0, 6)
            .Select(i => (ItemKey: $"k{i}", Weight: 3m))
            .ToArray();
        var shuffled = ordered.Reverse().ToArray();

        var a = MoneyRoundingPolicy.AllocateKwd(1.000m, ordered);
        var b = MoneyRoundingPolicy.AllocateKwd(1.000m, shuffled);

        using (new AssertionScope())
        {
            // 1.000 KWD over 6 equal weights: 4 links of 0.167 and 2 links of 0.166
            a.Sum(s => s.AmountKwd).Should().Be(1.000m);
            a.AssertSameAllocationAs(b);
        }
    }

    [Fact]
    public void AllocationPreservesExactTotalForMixedWeights()
    {
        var items = new[]
        {
            (ItemKey: "heavy", Weight: 7m),
            (ItemKey: "mid", Weight: 13m),
            (ItemKey: "light", Weight: 2m),
            (ItemKey: "none", Weight: 0m),
        };

        var shares = MoneyRoundingPolicy.AllocateKwd(3.333m, items);

        using (new AssertionScope())
        {
            shares.Single(s => s.ItemKey == "none").AmountKwd.Should().Be(0.000m);
            shares.Sum(s => s.AmountKwd).Should().Be(3.333m);
            // floors: 1060 / 1969 / 303; the single remaining fil goes to the
            // largest residual (11 for both heavy and mid) broken by key order.
            shares.Single(s => s.ItemKey == "heavy").AmountKwd.Should().Be(1.061m);
            shares.Single(s => s.ItemKey == "mid").AmountKwd.Should().Be(1.969m);
            shares.Single(s => s.ItemKey == "light").AmountKwd.Should().Be(0.303m);
        }
    }

    [Fact]
    public void NonPositiveInputsAreRejectedWithoutPartialState()
    {
        using (new AssertionScope())
        {
            FluentActions.Invoking(() => MoneyRoundingPolicy.AllocateKwd(-1m, [("a", 1m)]))
                .Should().Throw<ArgumentOutOfRangeException>();
            FluentActions.Invoking(() => MoneyRoundingPolicy.AllocateKwd(1m, [("a", -1m)]))
                .Should().Throw<ArgumentOutOfRangeException>();
            FluentActions.Invoking(() => MoneyRoundingPolicy.AllocateKwd(1m, []))
                .Should().Throw<ArgumentException>();
            FluentActions.Invoking(() => MoneyRoundingPolicy.AllocateKwd(5m, [("a", 0m)]))
                .Should().Throw<ArgumentException>();
        }
    }

    [Fact]
    public void TinyPositiveWeightsArePreservedWithoutFixedScaleLoss()
    {
        var shares = MoneyRoundingPolicy.AllocateKwd(0.001m, [("a", 0.0000000000000000000000000001m), ("b", 0.0000000000000000000000000001m)]);

        shares.Should().ContainSingle(x => x.ItemKey == "a" && x.AmountKwd == 0.001m);
        shares.Should().ContainSingle(x => x.ItemKey == "b" && x.AmountKwd == 0m);
    }

    [Fact]
    public void LargeTotalsAndWeightsDoNotOverflowIntermediateProducts()
    {
        var shares = MoneyRoundingPolicy.AllocateKwd(999999999999999.999m, [("a", 2m), ("b", 1m)]);

        shares.Sum(x => x.AmountKwd).Should().Be(999999999999999.999m);
        shares.Single(x => x.ItemKey == "a").AmountKwd.Should().Be(666666666666666.666m);
        shares.Single(x => x.ItemKey == "b").AmountKwd.Should().Be(333333333333333.333m);
    }
}

internal static class MoneyAllocationTestExtensions
{
    public static void AssertSameAllocationAs(this IReadOnlyList<AllocatedItem> expected, IReadOnlyList<AllocatedItem> actual)
    {
        expected.Select(x => (x.ItemKey, x.AmountKwd)).Should().BeEquivalentTo(
            actual.Select(x => (x.ItemKey, x.AmountKwd)),
            "shuffling the input must not change who receives what");
    }
}
