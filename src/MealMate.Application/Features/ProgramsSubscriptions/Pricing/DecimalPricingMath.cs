using System.Numerics;

namespace MealMate.Application.Features.ProgramsSubscriptions.Pricing;

/// <summary>Bounded decimal arithmetic helpers for pricing curves and cohort statistics.</summary>
public static class DecimalPricingMath
{
    private const int MaxIterations = 256;
    private static readonly BigInteger MaxFractionalExponentDenominator = new(1000);

    public static decimal Sqrt(decimal value)
    {
        if (value < 0m) throw new ArgumentOutOfRangeException(nameof(value), "A square root requires a non-negative value.");
        if (value == 0m) return 0m;

        var estimate = value >= 1m ? (value / 2m) + 0.5m : 1m;
        for (var i = 0; i < MaxIterations; i++)
        {
            var next = (estimate + (value / estimate)) / 2m;
            if (next == estimate || Math.Abs(next - estimate) <= 0.0000000000000000000000000001m)
                return next;
            estimate = next;
        }

        throw new ArithmeticException("Decimal square root did not converge within the supported precision.");
    }

    /// <summary>Computes a non-negative decimal power without converting through binary floating point.</summary>
    public static decimal Pow(decimal value, decimal exponent)
    {
        if (value < 0m) throw new ArgumentOutOfRangeException(nameof(value), "Pricing curve bases must be non-negative.");
        if (value > 1m) throw new ArgumentOutOfRangeException(nameof(value), "Pricing curve bases must be between zero and one.");
        if (exponent < 0m) throw new ArgumentOutOfRangeException(nameof(exponent), "Pricing curve exponents must be non-negative.");
        if (exponent > 100m) throw new ArgumentOutOfRangeException(nameof(exponent), "Pricing curve exponents are bounded to 100.");
        if (exponent == 0m) return 1m;
        if (value == 0m) return 0m;
        if (value == 1m) return 1m;

        var integerPart = (int)decimal.Truncate(exponent);
        var fractionalPart = exponent - integerPart;
        var integerResult = PowInteger(value, integerPart);
        if (fractionalPart == 0m) return integerResult;

        var (numerator, denominator) = ToReducedFraction(fractionalPart);
        if (denominator > MaxFractionalExponentDenominator)
            throw new ArgumentOutOfRangeException(nameof(exponent), "Fractional pricing curve exponents support at most three decimal places.");
        var root = NthRoot(value, denominator);
        var fractionalResult = numerator * 2 > denominator
            ? value / PowInteger(root, denominator - numerator)
            : PowInteger(root, numerator);
        return integerResult * fractionalResult;
    }

    private static decimal NthRoot(decimal value, BigInteger degree)
    {
        if (degree <= BigInteger.One) return value;
        var degreeDecimal = (decimal)degree;
        var degreeMinusOne = degreeDecimal - 1m;
        var estimate = 1m;

        for (var i = 0; i < MaxIterations; i++)
        {
            var powered = PowInteger(estimate, degree - BigInteger.One);
            if (powered == 0m)
                throw new ArithmeticException("Decimal pricing curve root left the representable range.");
            var next = ((degreeMinusOne * estimate) + (value / powered)) / degreeDecimal;
            if (next == estimate || Math.Abs(next - estimate) <= 0.0000000000000000000000000001m)
                return next;
            estimate = next;
        }

        throw new ArithmeticException("Decimal pricing curve power did not converge within the supported precision.");
    }

    private static decimal PowInteger(decimal value, BigInteger exponent)
    {
        var result = 1m;
        var factor = value;
        var power = exponent;
        while (power > BigInteger.Zero)
        {
            if (!power.IsEven) result *= factor;
            power >>= 1;
            if (power > BigInteger.Zero) factor *= factor;
        }
        return result;
    }

    private static (BigInteger Numerator, BigInteger Denominator) ToReducedFraction(decimal value)
    {
        var bits = decimal.GetBits(value);
        var coefficient = (BigInteger)(uint)bits[0]
            | ((BigInteger)(uint)bits[1] << 32)
            | ((BigInteger)(uint)bits[2] << 64);
        var scale = (bits[3] >> 16) & 0x7f;
        var denominator = BigInteger.Pow(10, scale);
        var divisor = BigInteger.GreatestCommonDivisor(coefficient, denominator);
        return (coefficient / divisor, denominator / divisor);
    }
}
