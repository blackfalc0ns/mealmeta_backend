using MealMate.Domain.Common;
using MealMate.Domain.Exceptions;

namespace MealMate.Domain.ValueObjects;

/// <summary>
/// A percentage stored as a decimal fraction (0.15 == 15%). Centralizes the
/// representation used by commissions, cancellation fees, and discounts.
/// </summary>
public sealed class Percentage : ValueObject
{
    private Percentage()
    {
    }

    private Percentage(decimal value) => Value = value;

    /// <summary>Decimal fraction, e.g. 0.15 for 15%.</summary>
    public decimal Value { get; private set; }

    public decimal AsPercent => Value * 100m;

    public static Percentage FromFraction(decimal fraction)
    {
        if (fraction < 0m)
        {
            throw new DomainException(
                "domain.percentage.negative");
        }

        return new Percentage(fraction);
    }

    public static Percentage FromPercent(decimal percent) => FromFraction(percent / 100m);

    public decimal ApplyTo(decimal amount) => amount * Value;

    protected override IEnumerable<object?> GetEqualityComponents()
    {
        yield return Value;
    }

    public override string ToString() => $"{AsPercent:0.##}%";
}
