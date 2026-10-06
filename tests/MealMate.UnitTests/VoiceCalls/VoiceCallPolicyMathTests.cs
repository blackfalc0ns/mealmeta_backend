using FluentAssertions;
using MealMate.Application.Features.VoiceCalls.Services;
using Xunit;

namespace MealMate.UnitTests.VoiceCalls;

/// <summary>
/// T04 policy math (AC13/AC17): the 3km contact radius is inclusive, and coordinate
/// validation is range-based — (0,0) is a valid point, never a blanket rejection.
/// </summary>
public sealed class VoiceCallPolicyMathTests
{
    [Theory]
    [InlineData(2999, true)]
    [InlineData(3000, true)]
    [InlineData(3001, false)]
    public void ContactRadius_IsInclusive(double meters, bool allowed) =>
        VoiceCallPolicyMath.IsWithinContactRadius(meters, 3000).Should().Be(allowed);

    [Theory]
    [InlineData(double.NaN, false)]
    [InlineData(double.PositiveInfinity, false)]
    [InlineData(-1, false)]
    [InlineData(0, true)]
    public void ContactRadius_RejectsNonFiniteOrNegative(double meters, bool allowed) =>
        VoiceCallPolicyMath.IsWithinContactRadius(meters, 3000).Should().Be(allowed);

    [Fact]
    public void Haversine_ZeroCoordinates_AreGeographicallyValid()
    {
        var distance = VoiceCallPolicyMath.HaversineMeters(0, 0, 0, 0);

        distance.Should().NotBeNull();
        distance!.Value.Should().BeApproximately(0, 0.001);
    }

    [Theory]
    [InlineData(91, 0)]    // latitude out of range
    [InlineData(-91, 0)]
    [InlineData(0, 181)]   // longitude out of range
    [InlineData(0, -181)]
    public void Haversine_OutOfRangeCoordinates_ReturnNull(double lat, double lon) =>
        VoiceCallPolicyMath.HaversineMeters(lat, lon, 0, 0).Should().BeNull();

    [Fact]
    public void Haversine_OneDegreeLatitude_IsApproximately111Km()
    {
        var distance = VoiceCallPolicyMath.HaversineMeters(0, 0, 1, 0);

        distance.Should().NotBeNull();
        distance!.Value.Should().BeInRange(110_000, 112_500);
    }

    [Fact]
    public void Haversine_KnownKuwaitPair_IsReasonable()
    {
        // Kuwait City ~ Salmiya: roughly 20km depending on the exact points used.
        var distance = VoiceCallPolicyMath.HaversineMeters(29.3759, 47.9774, 29.3374, 48.0761);

        distance.Should().NotBeNull();
        distance!.Value.Should().BeInRange(9_000, 13_000);
    }
}
