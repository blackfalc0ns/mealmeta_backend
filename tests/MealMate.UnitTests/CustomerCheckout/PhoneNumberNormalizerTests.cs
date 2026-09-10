using FluentAssertions;
using MealMate.Application.Common.Helpers;
using Xunit;

namespace MealMate.UnitTests.CustomerCheckout;

public class PhoneNumberNormalizerTests
{
    [Theory]
    [InlineData(null, null)]
    [InlineData("", null)]
    [InlineData("   ", null)]
    [InlineData("+96550000002", "+96550000002")]
    [InlineData("+965 5000 0002", "+96550000002")]
    [InlineData(" +965-5000-0002 ", "+96550000002")]
    [InlineData("0096550000002", "+96550000002")]
    [InlineData("00965 5000 0002", "+96550000002")]
    [InlineData("50000002", "50000002")]
    public void Normalize_CleansPhoneCorrectly(string? input, string? expected)
    {
        var result = PhoneNumberNormalizer.Normalize(input);
        result.Should().Be(expected);
    }

    [Fact]
    public void GetLookupCandidates_WhenInternationalWithPlus_GeneratesWithoutPlusAndWith00()
    {
        var candidates = PhoneNumberNormalizer.GetLookupCandidates("+96550000002");
        candidates.Should().Contain("+96550000002");
        candidates.Should().Contain("96550000002");
        candidates.Should().Contain("0096550000002");
    }

    [Fact]
    public void GetLookupCandidates_WhenKuwait8Digits_GeneratesInternationalPrefixes()
    {
        var candidates = PhoneNumberNormalizer.GetLookupCandidates("50000002");
        candidates.Should().Contain("50000002");
        candidates.Should().Contain("+50000002");
        candidates.Should().Contain("+96550000002");
        candidates.Should().Contain("96550000002");
        candidates.Should().Contain("0096550000002");
    }

    [Theory]
    [InlineData("50000002", "+96550000002")]
    [InlineData("91234567", "+96591234567")]
    [InlineData("61234567", "+96561234567")]
    [InlineData("+96550000002", "+96550000002")]
    [InlineData("0096550000002", "+96550000002")]
    [InlineData("96550000002", "+96550000002")]
    public void ToInternationalFormat_FormatsCorrectly(string input, string expected)
    {
        var result = PhoneNumberNormalizer.ToInternationalFormat(input);
        result.Should().Be(expected);
    }

    [Theory]
    [InlineData("50000002", "+965", "50000002")]
    [InlineData("+96550000002", "+965", "50000002")]
    [InlineData("0096550000002", "+965", "50000002")]
    [InlineData("+96650000002", "+966", "50000002")]
    [InlineData("+201000000002", "+20", "1000000002")]
    public void ExtractCountryCodeAndNationalNumber_SplitsCorrectly(string input, string expectedCode, string expectedNational)
    {
        var (code, national) = PhoneNumberNormalizer.ExtractCountryCodeAndNationalNumber(input);
        code.Should().Be(expectedCode);
        national.Should().Be(expectedNational);
    }
}

