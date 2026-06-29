using FluentAssertions;
using MealMate.Domain.Exceptions;
using MealMate.Domain.ValueObjects;

namespace MealMate.UnitTests.Domain;

public class MoneyTests
{
    [Fact]
    public void Add_WithSameCurrency_SumsAmounts()
    {
        var a = new Money(10.500m, "SAR");
        var b = new Money(4.250m, "SAR");

        var result = a.Add(b);

        result.Amount.Should().Be(14.750m);
        result.CurrencyCode.Should().Be("SAR");
    }

    [Fact]
    public void Add_WithDifferentCurrencies_Throws()
    {
        var a = new Money(10m, "SAR");
        var b = new Money(10m, "USD");

        var act = () => a.Add(b);

        act.Should().Throw<DomainException>();
    }

    [Fact]
    public void Equality_IsByValue()
    {
        var a = new Money(5m, "sar");
        var b = new Money(5m, "SAR");

        a.Should().Be(b);
    }
}
