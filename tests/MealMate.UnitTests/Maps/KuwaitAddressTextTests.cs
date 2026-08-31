using MealMate.Application.Common.Geography;

namespace MealMate.UnitTests.Maps;

public sealed class KuwaitAddressTextTests
{
    [Theory]
    [InlineData("شارع 2 جادة 2", "شارع 2", "جادة 2")]
    [InlineData("شارع سالم المبارك جادة 5", "شارع سالم المبارك", "جادة 5")]
    [InlineData("Street 2 Avenue 3", "شارع 2", "جادة 3")]
    [InlineData("شارع 2 جاده 2", "شارع 2", "جادة 2")]
    [InlineData("St. 4 Ave 1", "شارع 4", "جادة 1")]
    public void SplitStreetAndAvenue_separates_and_arabicizes(string input, string street, string avenue)
    {
        var (s, a) = KuwaitAddressText.SplitStreetAndAvenue(input);
        Assert.Equal(street, s);
        Assert.Equal(avenue, a);
    }

    [Theory]
    [InlineData("شارع سالم المبارك", "شارع سالم المبارك")]
    [InlineData("شارع 2", "شارع 2")]
    [InlineData("Street 9", "شارع 9")]
    public void SplitStreetAndAvenue_keeps_street_when_no_avenue(string input, string expectedStreet)
    {
        var (s, a) = KuwaitAddressText.SplitStreetAndAvenue(input);
        Assert.Equal(expectedStreet, s);
        Assert.Null(a);
    }

    [Fact]
    public void SplitStreetAndAvenue_null_or_blank_returns_nulls()
    {
        Assert.Equal((null, null), KuwaitAddressText.SplitStreetAndAvenue(null));
        Assert.Equal((null, null), KuwaitAddressText.SplitStreetAndAvenue("  "));
    }
}
