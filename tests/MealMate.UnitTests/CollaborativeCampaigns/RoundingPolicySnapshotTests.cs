using FluentAssertions;
using MealMate.Domain.CollaborativeCampaigns.ValueObjects;
using Xunit;

namespace MealMate.UnitTests.CollaborativeCampaigns;

public class RoundingPolicySnapshotTests
{
    [Fact]
    public void DefaultKwd_HasExpectedFrozenProperties()
    {
        var policy = RoundingPolicySnapshot.DefaultKwd();

        policy.CurrencyPrecision.Should().Be(3);
        policy.RoundingMode.Should().Be(MidpointRounding.AwayFromZero);
        policy.RoundingPolicyVersion.Should().Be(1);
    }

    [Theory]
    [InlineData(12.3454, 12.345)]
    [InlineData(12.3455, 12.346)] // AwayFromZero rounds .0005 up
    [InlineData(12.3456, 12.346)]
    [InlineData(-12.3455, -12.346)] // AwayFromZero rounds negative away from zero
    public void Round_KwdDefault_RoundsAccurately(decimal input, decimal expected)
    {
        var policy = RoundingPolicySnapshot.DefaultKwd();
        var result = policy.Round(input);

        result.Should().Be(expected);
    }

    [Fact]
    public void CustomPolicy_RoundsWithCustomPrecision()
    {
        var policy = new RoundingPolicySnapshot(2, MidpointRounding.AwayFromZero, 1);
        var result = policy.Round(10.555m);

        result.Should().Be(10.56m);
    }

    [Theory]
    [InlineData(-1)]
    [InlineData(5)]
    public void Constructor_InvalidPrecision_Throws(int precision)
    {
        var act = () => new RoundingPolicySnapshot(precision, MidpointRounding.AwayFromZero, 1);
        act.Should().Throw<ArgumentOutOfRangeException>();
    }

    [Theory]
    [InlineData(0)]
    [InlineData(-2)]
    public void Constructor_InvalidVersion_Throws(int version)
    {
        var act = () => new RoundingPolicySnapshot(3, MidpointRounding.AwayFromZero, version);
        act.Should().Throw<ArgumentOutOfRangeException>();
    }
}
