using MealMate.Domain.Common;
using MealMate.Domain.Exceptions;

namespace MealMate.Domain.ValueObjects;

/// <summary>
/// Money is always stored as <see cref="decimal"/> with an explicit currency.
/// Arithmetic across different currencies is rejected.
/// </summary>
public sealed class Money : ValueObject
{
    private Money()
    {
    }

    public Money(decimal amount, string currencyCode)
    {
        if (string.IsNullOrWhiteSpace(currencyCode))
        {
            throw new DomainException(
                "domain.money.currency_required",
                "Currency code is required.");
        }

        Amount = amount;
        CurrencyCode = currencyCode.ToUpperInvariant();
    }

    public decimal Amount { get; private set; }

    public string CurrencyCode { get; private set; } = default!;

    public static Money Zero(string currencyCode) => new(0m, currencyCode);

    public Money Add(Money other)
    {
        EnsureSameCurrency(other);
        return new Money(Amount + other.Amount, CurrencyCode);
    }

    public Money Subtract(Money other)
    {
        EnsureSameCurrency(other);
        return new Money(Amount - other.Amount, CurrencyCode);
    }

    public Money Multiply(decimal factor) => new(Amount * factor, CurrencyCode);

    private void EnsureSameCurrency(Money other)
    {
        if (CurrencyCode != other.CurrencyCode)
        {
            throw new DomainException(
                "domain.money.currency_mismatch",
                $"Cannot operate on {CurrencyCode} and {other.CurrencyCode}.");
        }
    }

    protected override IEnumerable<object?> GetEqualityComponents()
    {
        yield return Amount;
        yield return CurrencyCode;
    }

    public override string ToString() => $"{Amount:0.###} {CurrencyCode}";
}
