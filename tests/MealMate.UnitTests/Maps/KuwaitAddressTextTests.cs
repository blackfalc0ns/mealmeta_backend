using MealMate.Application.Common.Geography;

namespace MealMate.UnitTests.Maps;

public sealed class KuwaitAddressTextTests
{
    [Theory]
    [InlineData("شارع 2 جادة 2", "شارع 2", "جادة 2")]
    [InlineData("شارع سالم المبارك جادة 5", "شارع سالم المبارك", "جادة 5")]
    [InlineData("Street 2 Avenue 3", "Street 2", "Avenue 3")]
    [InlineData("شارع 2 جاده 2", "شارع 2", "جاده 2")]
    public void SplitStreetAndAvenue_separates_kuwait_route(string input, string street, string avenue)
    {
        var (s, a) = KuwaitAddressText.SplitStreetAndAvenue(input);
        Assert.Equal(street, s);
        Assert.Equal(avenue, a);
    }

    [Theory]
    [InlineData("شارع سالم المبارك")]
    [InlineData("شارع 2")]
    [InlineData(null)]
    [InlineData("")]
    public void SplitStreetAndAvenue_keeps_street_when_no_avenue(string? input)
    {
        var (s, a) = KuwaitAddressText.SplitStreetAndAvenue(input);
        Assert.Null(a);
        if (string.IsNullOrWhiteSpace(input))
        {
            Assert.Null(s);
        }
        else
        {
            Assert.Equal(input.Trim(), s);
        }
    }
}
